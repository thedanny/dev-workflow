using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;

namespace DevWorkflow.Application.Contracts;

// Responses. Shared by the API and MCP so both return identical shapes.

public sealed record RepositoryDto(
    Guid Id,
    string Key,
    RepositoryProvider Provider,
    string RemoteUrl,
    string DefaultBranch,
    bool Enabled)
{
    public static RepositoryDto From(CodeRepository repository) => new(
        repository.Id,
        repository.Key,
        repository.Provider,
        repository.RemoteUrl,
        repository.DefaultBranch,
        repository.Enabled);
}

public sealed record SignalDto(
    Guid Id,
    string SourceType,
    string SourceReference,
    string IdempotencyKey,
    string? Payload,
    string? PayloadReference,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    RedactionStatus RedactionStatus,
    SignalStatus Status)
{
    public static SignalDto From(Signal signal) => new(
        signal.Id,
        signal.SourceType,
        signal.SourceReference,
        signal.IdempotencyKey,
        signal.Payload,
        signal.PayloadReference,
        signal.ObservedAt,
        signal.ReceivedAt,
        signal.RedactionStatus,
        signal.Status);
}

/// <summary><c>Created</c> is false when the idempotency key matched an existing signal.</summary>
public sealed record SubmitSignalResult(SignalDto Signal, bool Created);

public sealed record WorkItemDto(
    Guid Id,
    string Title,
    string Description,
    Guid? RepositoryId,
    string? Repository,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> SourceReferences,
    IReadOnlyList<Guid> SignalIds,
    WorkItemPriority Priority,
    WorkItemRisk Risk,
    WorkItemStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version)
{
    public static WorkItemDto From(WorkItem item, string? repositoryKey) => new(
        item.Id,
        item.Title,
        item.Description,
        item.RepositoryId,
        repositoryKey,
        item.AcceptanceCriteria,
        item.SourceReferences,
        [.. item.SignalLinks.Select(link => link.SignalId)],
        item.Priority,
        item.Risk,
        item.Status,
        item.CreatedAt,
        item.UpdatedAt,
        item.Version);
}

public sealed record ExecutionDto(
    Guid Id,
    Guid WorkItemId,
    string ExecutorId,
    ExecutionState State,
    DateTimeOffset LeaseExpiresAt,
    DateTimeOffset LastHeartbeatAt,
    string? Branch,
    string? WorktreePath,
    string? Summary,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt)
{
    public static ExecutionDto From(Execution execution) => new(
        execution.Id,
        execution.WorkItemId,
        execution.ExecutorId,
        execution.State,
        execution.LeaseExpiresAt,
        execution.LastHeartbeatAt,
        execution.Branch,
        execution.WorktreePath,
        execution.Summary,
        execution.Error,
        execution.StartedAt,
        execution.EndedAt);
}

public sealed record ExecutionResult(ExecutionDto Execution, WorkItemDto WorkItem);

public sealed record PullRequestDto(
    Guid Id,
    Guid WorkItemId,
    Guid ExecutionId,
    RepositoryProvider Provider,
    string ExternalId,
    Uri Url,
    string HeadBranch,
    string BaseBranch,
    string? ChecksSummary,
    string? ReviewSummary,
    DateTimeOffset ReportedAt)
{
    public static PullRequestDto From(PullRequest pullRequest) => new(
        pullRequest.Id,
        pullRequest.WorkItemId,
        pullRequest.ExecutionId,
        pullRequest.Provider,
        pullRequest.ExternalId,
        pullRequest.Url,
        pullRequest.HeadBranch,
        pullRequest.BaseBranch,
        pullRequest.ChecksSummary,
        pullRequest.ReviewSummary,
        pullRequest.ReportedAt);
}

public sealed record PullRequestResult(PullRequestDto PullRequest, WorkItemDto WorkItem);

// Requests.

public sealed record RegisterRepositoryRequest(
    string Key,
    RepositoryProvider Provider,
    string RemoteUrl,
    string? DefaultBranch = null);

/// <param name="AlreadyRedacted">Set when the listener redacted the payload at the source.</param>
public sealed record SubmitSignalRequest(
    string SourceType,
    string SourceReference,
    string IdempotencyKey,
    string? Payload = null,
    string? PayloadReference = null,
    DateTimeOffset? ObservedAt = null,
    bool AlreadyRedacted = false);

/// <param name="Repository">Repository key or git remote URL. May be left empty and routed later.</param>
public sealed record ProposeWorkItemRequest(
    string Title,
    string? Description = null,
    string? Repository = null,
    IReadOnlyList<Guid>? SignalIds = null,
    IReadOnlyList<string>? AcceptanceCriteria = null,
    WorkItemPriority Priority = WorkItemPriority.Normal,
    WorkItemRisk Risk = WorkItemRisk.Medium,
    IReadOnlyList<string>? SourceReferences = null);

/// <param name="Repository">The executor's repository: its key or git remote URL.</param>
public sealed record ClaimWorkItemRequest(string ExecutorId, string Repository, int? LeaseSeconds = null);

public enum ExecutionUpdateState
{
    InProgress,
    Blocked,
    Failed,
    Completed,
}

public sealed record UpdateExecutionRequest(
    ExecutionUpdateState State,
    string? Summary = null,
    IReadOnlyList<string>? Evidence = null,
    string? Branch = null,
    string? WorktreePath = null);

/// <param name="ExternalId">The provider's PR number. Derived from the URL when omitted.</param>
public sealed record ReportPullRequestRequest(
    Uri Url,
    string Branch,
    RepositoryProvider? Provider = null,
    string? BaseBranch = null,
    string? ExternalId = null,
    string? Checks = null);
