using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Security;
using DevWorkflow.Domain.Audit;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Services;

/// <summary>JSON conventions shared by the API, MCP tools and audit data: snake_case names and enum values.</summary>
public static class WorkflowJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        // Explicit, because hosts such as the MCP SDK freeze the options and require a resolver.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    });

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

internal static class AuditLog
{
    public static void Record(
        IDevWorkflowDb db,
        string entityType,
        Guid entityId,
        string eventType,
        string actor,
        object? data,
        DateTimeOffset now)
    {
        var json = data is null ? null : JsonSerializer.Serialize(data, WorkflowJson.Options);
        db.AuditEvents.Add(AuditEvent.Record(entityType, entityId, eventType, actor, json, now));
    }
}

internal static class Limits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static int PageSize(int? requested) => Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);
}

/// <summary>Resolves a repository from a registry key or any form of its git remote URL.</summary>
public sealed class RepositoryLookup(IDevWorkflowDb db)
{
    public async Task<CodeRepository> ResolveAsync(string repository, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw WorkflowException.Validation("repository_required", "A repository key or git remote URL is required.");
        }

        var value = repository.Trim();
        CodeRepository? match;

        if (LooksLikeRemote(value))
        {
            var remote = RemoteUrl.Normalize(value);
            match = await db.Repositories.SingleOrDefaultAsync(r => r.RemoteUrl == remote, cancellationToken);
        }
        else
        {
            var key = value.ToLowerInvariant();
            match = await db.Repositories.SingleOrDefaultAsync(r => r.Key == key, cancellationToken);
        }

        return match ?? throw WorkflowException.NotFound(
            "repository_not_registered",
            $"Repository '{value}' is not registered.");
    }

    public async Task<string?> KeyForAsync(Guid? repositoryId, CancellationToken cancellationToken) =>
        repositoryId is null
            ? null
            : await db.Repositories.Where(r => r.Id == repositoryId).Select(r => r.Key).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> KeysForAsync(
        IEnumerable<WorkItem> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Where(i => i.RepositoryId is not null).Select(i => i.RepositoryId!.Value).Distinct().ToList();
        return await db.Repositories.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Key, cancellationToken);
    }

    private static bool LooksLikeRemote(string value) =>
        value.Contains('/', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal);
}

internal static class Actors
{
    public const string LeaseSweeper = "system:lease-sweeper";

    public const string PullRequestSync = "system:pull-request-sync";

    public static string Of(Caller caller) => caller.Name;
}
