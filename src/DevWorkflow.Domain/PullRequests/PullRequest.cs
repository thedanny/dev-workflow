using DevWorkflow.Domain.Repositories;

namespace DevWorkflow.Domain.PullRequests;

/// <summary>A pull request reported by an executor for a work item.</summary>
public sealed class PullRequest
{
    private PullRequest()
    {
        ExternalId = string.Empty;
        HeadBranch = string.Empty;
        BaseBranch = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid WorkItemId { get; private set; }

    public Guid ExecutionId { get; private set; }

    public Guid RepositoryId { get; private set; }

    public RepositoryProvider Provider { get; private set; }

    /// <summary>The provider's identifier, such as the PR number.</summary>
    public string ExternalId { get; private set; }

    public Uri Url { get; private set; } = null!;

    public string HeadBranch { get; private set; }

    public string BaseBranch { get; private set; }

    public string? ChecksSummary { get; private set; }

    public string? ReviewSummary { get; private set; }

    public PullRequestState State { get; private set; }

    /// <summary>The head commit the provider last reported.</summary>
    public string? HeadCommit { get; private set; }

    /// <summary>When the provider last confirmed this pull request. Null if it was never checked.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PullRequest Report(
        Guid workItemId,
        Guid executionId,
        Guid repositoryId,
        RepositoryProvider provider,
        string externalId,
        Uri url,
        string headBranch,
        string baseBranch,
        string? checksSummary,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(headBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseBranch);

        return new PullRequest
        {
            Id = Guid.CreateVersion7(now),
            WorkItemId = workItemId,
            ExecutionId = executionId,
            RepositoryId = repositoryId,
            Provider = provider,
            ExternalId = externalId.Trim(),
            Url = url,
            HeadBranch = headBranch.Trim(),
            BaseBranch = baseBranch.Trim(),
            ChecksSummary = checksSummary,
            State = PullRequestState.Open,
            ReportedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateStatus(string? checksSummary, string? reviewSummary, DateTimeOffset now)
    {
        ChecksSummary = checksSummary;
        ReviewSummary = reviewSummary;
        UpdatedAt = now;
    }

    /// <summary>Records the provider's view of the pull request. Returns true when its state changed.</summary>
    public bool RecordProviderStatus(
        PullRequestState state,
        string? headCommit,
        string? checksSummary,
        string? reviewSummary,
        DateTimeOffset now)
    {
        var changed = state != State;
        State = state;
        HeadCommit = headCommit;
        ChecksSummary = checksSummary;
        ReviewSummary = reviewSummary;
        VerifiedAt = now;
        UpdatedAt = now;
        return changed;
    }
}
