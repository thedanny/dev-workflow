using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevWorkflow.Application.Errors;
using DevWorkflow.Domain.Repositories;

namespace DevWorkflow.Infrastructure.Providers;

/// <summary>The owner (GitHub) or workspace (Bitbucket) and name of a repository, from its normalized remote.</summary>
internal sealed record RepositoryCoordinates(string Host, string Owner, string Name)
{
    public static RepositoryCoordinates From(CodeRepository repository)
    {
        var parts = repository.RemoteUrl.Split('/');
        if (parts.Length != 3)
        {
            throw WorkflowException.Validation(
                "unsupported_remote_url",
                $"Remote {repository.RemoteUrl} is not in host/owner/name form, which the {repository.Provider} adapter needs.");
        }

        return new RepositoryCoordinates(parts[0], parts[1], parts[2]);
    }
}

/// <summary>
/// JSON over HTTP for the provider adapters. A 404 reads as null. Other failures become
/// <see cref="ErrorKind.Unavailable"/> or validation errors that never echo the credential.
/// </summary>
internal sealed class ProviderHttp(HttpClient http, RepositoryProvider provider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int MaxProviderMessageLength = 200;

    public static string Segment(string value) => Uri.EscapeDataString(value);

    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        return await SendAsync<T>(request, cancellationToken);
    }

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body, body.GetType(), options: Json),
        };
        return await SendAsync<T>(request, cancellationToken)
            ?? throw Unavailable("provider_unexpected_response", "returned no resource for a create request");
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw Unavailable("provider_unavailable", "could not be reached");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable("provider_unavailable", "did not respond in time");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw Unavailable("provider_access_denied", "refused the configured credential");
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                var message = await ProviderMessageAsync(response, cancellationToken);
                throw WorkflowException.Validation("provider_rejected", $"{provider} rejected the request: {message}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable("provider_unavailable", $"returned HTTP {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
            }
            catch (JsonException)
            {
                throw Unavailable("provider_unexpected_response", "returned a response the adapter could not read");
            }
        }
    }

    private WorkflowException Unavailable(string code, string problem) =>
        WorkflowException.Unavailable(code, $"{provider} {problem}.");

    /// <summary>Reads the provider's own error message: <c>message</c> on GitHub, <c>error.message</c> on Bitbucket.</summary>
    private static async Task<string> ProviderMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            var message =
                root.TryGetProperty("message", out var direct) && direct.ValueKind == JsonValueKind.String ? direct.GetString()
                : root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var nested) && nested.ValueKind == JsonValueKind.String ? nested.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message.Length <= MaxProviderMessageLength ? message : message[..MaxProviderMessageLength];
            }
        }
        catch (JsonException)
        {
        }

        return $"HTTP {(int)response.StatusCode}";
    }
}

internal enum CheckOutcome
{
    Passed,
    Failed,
    Pending,
    Skipped,
}

/// <summary>Short, provider-neutral summaries such as "2 passed, 1 failed" and "1 approved".</summary>
internal static class StatusSummary
{
    public static string? Checks(IEnumerable<CheckOutcome> outcomes)
    {
        var counts = outcomes.GroupBy(o => o).ToDictionary(g => g.Key, g => g.Count());
        var parts = new List<string>();
        Add(parts, counts, CheckOutcome.Passed, "passed");
        Add(parts, counts, CheckOutcome.Failed, "failed");
        Add(parts, counts, CheckOutcome.Pending, "pending");
        Add(parts, counts, CheckOutcome.Skipped, "skipped");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public static string? Reviews(int approved, int changesRequested)
    {
        var parts = new List<string>();
        if (approved > 0)
        {
            parts.Add($"{approved} approved");
        }

        if (changesRequested > 0)
        {
            parts.Add($"{changesRequested} changes requested");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static void Add(List<string> parts, Dictionary<CheckOutcome, int> counts, CheckOutcome outcome, string label)
    {
        if (counts.TryGetValue(outcome, out var count))
        {
            parts.Add($"{count} {label}");
        }
    }
}
