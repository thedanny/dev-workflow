namespace DevWorkflow.Domain.WorkItems;

public enum WorkItemStatus
{
    Proposed,
    Approved,
    Rejected,
    Claimed,
    InProgress,
    PRReady,
    Completed,
    Blocked,
    Failed,
    Cancelled,
}

public enum WorkItemPriority
{
    Low,
    Normal,
    High,
    Critical,
}

public enum WorkItemRisk
{
    Low,
    Medium,
    High,
}

/// <summary>
/// The allowed lifecycle transitions. Requeue (back to Approved) covers released claims,
/// expired leases and unblocked work.
/// </summary>
public static class WorkItemTransitions
{
    private static readonly Dictionary<WorkItemStatus, WorkItemStatus[]> Allowed = new()
    {
        [WorkItemStatus.Proposed] = [WorkItemStatus.Approved, WorkItemStatus.Rejected, WorkItemStatus.Cancelled],
        [WorkItemStatus.Approved] = [WorkItemStatus.Claimed, WorkItemStatus.Cancelled],
        [WorkItemStatus.Claimed] =
        [
            WorkItemStatus.InProgress, WorkItemStatus.Approved, WorkItemStatus.Blocked,
            WorkItemStatus.Failed, WorkItemStatus.Cancelled,
        ],
        [WorkItemStatus.InProgress] =
        [
            WorkItemStatus.PRReady, WorkItemStatus.Approved, WorkItemStatus.Blocked,
            WorkItemStatus.Failed, WorkItemStatus.Cancelled,
        ],
        [WorkItemStatus.PRReady] =
        [
            WorkItemStatus.Completed, WorkItemStatus.InProgress, WorkItemStatus.Failed, WorkItemStatus.Cancelled,
        ],
        [WorkItemStatus.Blocked] = [WorkItemStatus.Approved, WorkItemStatus.Cancelled],
        [WorkItemStatus.Rejected] = [],
        [WorkItemStatus.Completed] = [],
        [WorkItemStatus.Failed] = [],
        [WorkItemStatus.Cancelled] = [],
    };

    public static bool CanMove(WorkItemStatus from, WorkItemStatus to) => Allowed[from].Contains(to);

    public static bool IsTerminal(WorkItemStatus status) => Allowed[status].Length == 0;
}
