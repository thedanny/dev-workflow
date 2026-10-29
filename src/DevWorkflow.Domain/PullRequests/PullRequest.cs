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
}
