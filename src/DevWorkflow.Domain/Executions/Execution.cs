namespace DevWorkflow.Domain.Executions;

public enum ExecutionState
{
    /// <summary>Holds the lease. At most one active execution exists per work item.</summary>
    Active,
    Released,
    Expired,
    Completed,
    Failed,
}

/// <summary>
/// One executor's claim on a work item, held through a renewable lease.
/// </summary>
public sealed class Execution
{
    public static readonly TimeSpan MinimumLease = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumLease = TimeSpan.FromHours(4);

    private Execution()
    {
        ExecutorId = string.Empty;
        ClaimedBy = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid WorkItemId { get; private set; }

    /// <summary>The executor's self-reported identity, such as <c>laptop-claude-1</c>.</summary>
    public string ExecutorId { get; private set; }

    /// <summary>The authenticated client that made the claim. Only this client may act on the execution.</summary>
    public string ClaimedBy { get; private set; }

    public ExecutionState State { get; private set; }

    public DateTimeOffset LeaseExpiresAt { get; private set; }

    public DateTimeOffset LastHeartbeatAt { get; private set; }

    public string? Branch { get; private set; }

    public string? WorktreePath { get; private set; }

    public string? Summary { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public int Version { get; private set; }

    public static Execution Start(Guid workItemId, string executorId, string claimedBy, TimeSpan lease, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);
        ValidateLease(lease);

        return new Execution
        {
            Id = Guid.CreateVersion7(now),
            WorkItemId = workItemId,
            ExecutorId = executorId.Trim(),
            ClaimedBy = claimedBy,
            State = ExecutionState.Active,
            StartedAt = now,
            LastHeartbeatAt = now,
            LeaseExpiresAt = now + lease,
            Version = 1,
        };
    }

    public bool IsLeaseExpired(DateTimeOffset now) => State == ExecutionState.Active && now >= LeaseExpiresAt;

    public void Heartbeat(TimeSpan lease, DateTimeOffset now)
    {
        ValidateLease(lease);
        EnsureActive(now);

        LastHeartbeatAt = now;
        LeaseExpiresAt = now + lease;
        Version++;
    }

    /// <summary>Records the branch and/or worktree in use. Blank values leave the current value unchanged.</summary>
    public void RecordWorkspace(string? branch, string? worktreePath, DateTimeOffset now)
    {
        EnsureActive(now);

        if (!string.IsNullOrWhiteSpace(branch))
        {
            Branch = branch.Trim();
        }

        if (!string.IsNullOrWhiteSpace(worktreePath))
        {
            WorktreePath = worktreePath.Trim();
        }

        Version++;
    }

    public void RecordSummary(string summary, DateTimeOffset now)
    {
        EnsureActive(now);

        Summary = summary;
        Version++;
    }

    public void Release(string reason, DateTimeOffset now) => End(ExecutionState.Released, reason, now);

    public void Complete(DateTimeOffset now) => End(ExecutionState.Completed, null, now);

    public void Fail(string error, DateTimeOffset now) => End(ExecutionState.Failed, error, now);

    /// <summary>Ends an execution whose lease ran out, so the work item can be requeued.</summary>
    public void Expire(DateTimeOffset now)
    {
        if (!IsLeaseExpired(now))
        {
            throw new DomainException("lease_not_expired", $"Execution {Id} still holds a valid lease.");
        }

        State = ExecutionState.Expired;
        EndedAt = now;
        Version++;
    }

    private void End(ExecutionState state, string? error, DateTimeOffset now)
    {
        EnsureActive(now);

        State = state;
        Error = error;
        EndedAt = now;
        Version++;
    }

    private void EnsureActive(DateTimeOffset now)
    {
        if (State != ExecutionState.Active)
        {
            throw new DomainException("execution_not_active", $"Execution {Id} is {State}.");
        }

        if (now >= LeaseExpiresAt)
        {
            throw new DomainException("lease_expired", $"The lease for execution {Id} expired at {LeaseExpiresAt:O}.");
        }
    }

    private static void ValidateLease(TimeSpan lease)
    {
        if (lease < MinimumLease || lease > MaximumLease)
        {
            throw new DomainException(
                "invalid_lease",
                $"Lease must be between {MinimumLease.TotalSeconds} and {MaximumLease.TotalSeconds} seconds.");
        }
    }
}
