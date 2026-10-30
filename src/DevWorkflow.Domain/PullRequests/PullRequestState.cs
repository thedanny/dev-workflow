namespace DevWorkflow.Domain.PullRequests;

/// <summary>A pull request's state as last reported by its provider.</summary>
public enum PullRequestState
{
    Open,
    Merged,
    Closed,
}
