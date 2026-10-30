using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace DevWorkflow.Tests.Providers;

/// <summary>A pull request held by a fake provider. Tests change it to simulate activity on the provider.</summary>
internal sealed class FakePull
{
    public int Number { get; init; }

    public required string Head { get; init; }

    public required string Base { get; init; }

    public string HeadCommit { get; set; } = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    /// <summary>open, merged or closed.</summary>
    public string State { get; set; } = "open";

    public bool Draft { get; init; }

    /// <summary>GitHub check runs as (status, conclusion).</summary>
    public List<(string Status, string? Conclusion)> CheckRuns { get; } = [];

    /// <summary>GitHub commit status states or Bitbucket build status states.</summary>
    public List<string> Statuses { get; } = [];

    /// <summary>GitHub reviews in order, as (login, state).</summary>
    public List<(string User, string State)> Reviews { get; } = [];

    /// <summary>Bitbucket participants as (approved, state).</summary>
    public List<(bool Approved, string? State)> Participants { get; } = [];
}

internal sealed class FakeRepository(string owner, string name, string defaultBranch)
{
    public string Owner { get; } = owner;

    public string Name { get; } = name;

    public string DefaultBranch { get; } = defaultBranch;

    public Dictionary<string, string> Branches { get; } = new(StringComparer.Ordinal) { [defaultBranch] = "0000000000000000000000000000000000000001" };

    public List<FakePull> Pulls { get; } = [];
}

/// <summary>
/// An in-memory stand-in for a provider's REST API. It checks the credential, records every
/// request and answers 404 to anything it does not implement, including any merge endpoint.
/// </summary>
internal abstract class FakeProvider(string expectedAuthorization, string apiPrefix) : HttpMessageHandler
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly List<FakeRepository> _repositories = [];

    public List<(HttpMethod Method, Uri Uri, string? Authorization)> Requests { get; } = [];

    public string ExpectedAuthorization { get; set; } = expectedAuthorization;

    /// <summary>When set, every request fails with this status.</summary>
    public HttpStatusCode? FailWith { get; set; }

    public FakeRepository AddRepository(string owner, string name, string defaultBranch = "main")
    {
        var repository = new FakeRepository(owner, name, defaultBranch);
        _repositories.Add(repository);
        return repository;
    }

    public FakePull AddPullRequest(FakeRepository repository, string head, string? baseBranch = null)
    {
        if (!_repositories.Contains(repository))
        {
            throw new InvalidOperationException("The repository belongs to a different fake provider.");
        }

        repository.Branches.TryAdd(head, "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678");
        var pull = new FakePull { Number = repository.Pulls.Count + 1, Head = head, Base = baseBranch ?? repository.DefaultBranch };
        repository.Pulls.Add(pull);
        return pull;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.Authorization?.ToString();
        Requests.Add((request.Method, request.RequestUri!, authorization));

        if (FailWith is { } failure)
        {
            return new HttpResponseMessage(failure);
        }

        if (authorization != ExpectedAuthorization)
        {
            return Respond(HttpStatusCode.Unauthorized, new { Message = "Bad credentials" });
        }

        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith(apiPrefix, StringComparison.Ordinal))
        {
            return Respond(HttpStatusCode.NotFound, new { Message = "Not Found" });
        }

        var body = request.Content is null
            ? (JsonElement?)null
            : await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var (status, payload) = Handle(request.Method, path[apiPrefix.Length..], body);
        return Respond(status, payload ?? new { Message = "Not Found" });
    }

    protected abstract (HttpStatusCode Status, object? Body) Handle(HttpMethod method, string path, JsonElement? body);

    protected FakeRepository? Find(string owner, string name) =>
        _repositories.SingleOrDefault(r => r.Owner == owner && r.Name == name);

    protected static Match Route(string path, string pattern) => Regex.Match(path, $"^{pattern}$");

    protected static (HttpStatusCode, object?) NotFound => (HttpStatusCode.NotFound, null);

    private static HttpResponseMessage Respond(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body, body.GetType(), options: Json) };
}

internal sealed class FakeGitHub(string token = "github-test-token")
    : FakeProvider($"Bearer {token}", "/")
{
    protected override (HttpStatusCode Status, object? Body) Handle(HttpMethod method, string path, JsonElement? body)
    {
        var repo = Route(path, "repos/([^/]+)/([^/]+)(/.*)?");
        if (!repo.Success || Find(repo.Groups[1].Value, repo.Groups[2].Value) is not { } repository)
        {
            return NotFound;
        }

        var rest = repo.Groups[3].Value;
        var webBase = $"https://github.com/{repository.Owner}/{repository.Name}";
        Match match;

        if (method == HttpMethod.Get && rest.Length == 0)
        {
            return (HttpStatusCode.OK, new { FullName = $"{repository.Owner}/{repository.Name}", repository.DefaultBranch, HtmlUrl = webBase });
        }

        if (method == HttpMethod.Get && (match = Route(rest, "/git/ref/heads/(.+)")).Success)
        {
            var branch = Uri.UnescapeDataString(match.Groups[1].Value);
            return repository.Branches.TryGetValue(branch, out var sha)
                ? (HttpStatusCode.OK, new { Ref = $"refs/heads/{branch}", Object = new { Sha = sha } })
                : NotFound;
        }

        if (method == HttpMethod.Post && rest == "/git/refs")
        {
            var reference = body!.Value.GetProperty("ref").GetString()!;
            var sha = body.Value.GetProperty("sha").GetString()!;
            var branch = reference["refs/heads/".Length..];
            if (!repository.Branches.TryAdd(branch, sha))
            {
                return (HttpStatusCode.UnprocessableEntity, new { Message = "Reference already exists" });
            }

            return (HttpStatusCode.Created, new { Ref = reference, Object = new { Sha = sha } });
        }

        if (method == HttpMethod.Post && rest == "/pulls")
        {
            var head = body!.Value.GetProperty("head").GetString()!;
            if (!repository.Branches.ContainsKey(head))
            {
                return (HttpStatusCode.UnprocessableEntity, new { Message = "Validation Failed" });
            }

            var pull = AddPullRequest(repository, head, body.Value.GetProperty("base").GetString());
            return (HttpStatusCode.Created, PullBody(pull, webBase));
        }

        if (method == HttpMethod.Get && (match = Route(rest, @"/pulls/(\d+)(/reviews)?")).Success)
        {
            var pull = repository.Pulls.SingleOrDefault(p => p.Number == int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            if (pull is null)
            {
                return NotFound;
            }

            return match.Groups[2].Success
                ? (HttpStatusCode.OK, pull.Reviews.Select(r => new { r.State, User = new { Login = r.User } }).ToList())
                : (HttpStatusCode.OK, PullBody(pull, webBase));
        }

        if (method == HttpMethod.Get && (match = Route(rest, "/commits/([^/]+)/(check-runs|status)")).Success)
        {
            var pulls = repository.Pulls.Where(p => p.HeadCommit == match.Groups[1].Value).ToList();
            return match.Groups[2].Value == "check-runs"
                ? (HttpStatusCode.OK, new
                {
                    TotalCount = pulls.Sum(p => p.CheckRuns.Count),
                    CheckRuns = pulls.SelectMany(p => p.CheckRuns).Select(c => new { c.Status, c.Conclusion }).ToList(),
                })
                : (HttpStatusCode.OK, new { State = "pending", Statuses = pulls.SelectMany(p => p.Statuses).Select(s => new { State = s }).ToList() });
        }

        return NotFound;
    }

    private static object PullBody(FakePull pull, string webBase) => new
    {
        pull.Number,
        HtmlUrl = $"{webBase}/pull/{pull.Number}",
        State = pull.State == "open" ? "open" : "closed",
        Merged = pull.State == "merged",
        MergedAt = pull.State == "merged" ? DateTimeOffset.UnixEpoch : (DateTimeOffset?)null,
        pull.Draft,
        Head = new { Ref = pull.Head, Sha = pull.HeadCommit },
        Base = new { Ref = pull.Base, Sha = "0000000000000000000000000000000000000001" },
    };
}

internal sealed class FakeBitbucket(string authorization = "Bearer bitbucket-test-token")
    : FakeProvider(authorization, "/2.0/")
{
    protected override (HttpStatusCode Status, object? Body) Handle(HttpMethod method, string path, JsonElement? body)
    {
        var repo = Route(path, "repositories/([^/]+)/([^/]+)(/.*)?");
        if (!repo.Success || Find(repo.Groups[1].Value, repo.Groups[2].Value) is not { } repository)
        {
            return NotFound;
        }

        var rest = repo.Groups[3].Value;
        var webBase = $"https://bitbucket.org/{repository.Owner}/{repository.Name}";
        Match match;

        if (method == HttpMethod.Get && rest.Length == 0)
        {
            return (HttpStatusCode.OK, new
            {
                FullName = $"{repository.Owner}/{repository.Name}",
                Mainbranch = new { Name = repository.DefaultBranch },
                Links = new { Html = new { Href = webBase } },
            });
        }

        if (method == HttpMethod.Get && (match = Route(rest, "/refs/branches/([^/]+)")).Success)
        {
            var branch = Uri.UnescapeDataString(match.Groups[1].Value);
            return repository.Branches.TryGetValue(branch, out var hash)
                ? (HttpStatusCode.OK, new { Name = branch, Target = new { Hash = hash } })
                : NotFound;
        }

        if (method == HttpMethod.Post && rest == "/refs/branches")
        {
            var name = body!.Value.GetProperty("name").GetString()!;
            var hash = body.Value.GetProperty("target").GetProperty("hash").GetString()!;
            if (!repository.Branches.TryAdd(name, hash))
            {
                return (HttpStatusCode.BadRequest, new { Type = "error", Error = new { Message = "Branch already exists" } });
            }

            return (HttpStatusCode.Created, new { Name = name, Target = new { Hash = hash } });
        }

        if (method == HttpMethod.Post && rest == "/pullrequests")
        {
            var head = body!.Value.GetProperty("source").GetProperty("branch").GetProperty("name").GetString()!;
            if (!repository.Branches.ContainsKey(head))
            {
                return (HttpStatusCode.BadRequest, new { Type = "error", Error = new { Message = "Source branch not found" } });
            }

            var destination = body.Value.GetProperty("destination").GetProperty("branch").GetProperty("name").GetString();
            return (HttpStatusCode.Created, PullBody(AddPullRequest(repository, head, destination), webBase));
        }

        if (method == HttpMethod.Get && (match = Route(rest, @"/pullrequests/(\d+)(/statuses)?")).Success)
        {
            var pull = repository.Pulls.SingleOrDefault(p => p.Number == int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            if (pull is null)
            {
                return NotFound;
            }

            return match.Groups[2].Success
                ? (HttpStatusCode.OK, new { Pagelen = 100, Values = pull.Statuses.Select(s => new { State = s }).ToList() })
                : (HttpStatusCode.OK, PullBody(pull, webBase));
        }

        return NotFound;
    }

    private static object PullBody(FakePull pull, string webBase) => new
    {
        Id = pull.Number,
        State = pull.State switch { "open" => "OPEN", "merged" => "MERGED", _ => "DECLINED" },
        pull.Draft,
        Links = new { Html = new { Href = $"{webBase}/pull-requests/{pull.Number}" } },
        Source = new { Branch = new { Name = pull.Head }, Commit = new { Hash = pull.HeadCommit[..12] } },
        Destination = new { Branch = new { Name = pull.Base }, Commit = new { Hash = "000000000001" } },
        Participants = pull.Participants.Select(p => new { Role = "REVIEWER", p.Approved, p.State }).ToList(),
    };
}

internal static class FakeProviderServices
{
    /// <summary>Routes the adapters' HTTP clients to the fakes, so tests never reach a real provider.</summary>
    public static IServiceCollection UseFakeProviders(this IServiceCollection services, FakeGitHub github, FakeBitbucket bitbucket)
    {
        services.AddHttpClient(RepositoryProviders.HttpClientName(RepositoryProvider.GitHub)).ConfigurePrimaryHttpMessageHandler(() => github);
        services.AddHttpClient(RepositoryProviders.HttpClientName(RepositoryProvider.Bitbucket)).ConfigurePrimaryHttpMessageHandler(() => bitbucket);
        return services;
    }
}
