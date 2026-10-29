namespace DevWorkflow.Domain.WorkItems;

public enum AuthorizationDecision
{
    Approved,
    Rejected,
}

/// <summary>
/// A recorded decision to approve or reject a work item. Actionability is not authorization:
/// only an Approved authorization lets a work item enter the executable queue.
/// </summary>
public sealed class Authorization
{
    private Authorization()
    {
        Actor = string.Empty;
        Scope = string.Empty;
        Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid WorkItemId { get; private set; }

    public AuthorizationDecision Decision { get; private set; }

    /// <summary>Who made the decision: a person, or a named policy for auto-approved low-risk defects.</summary>
    public string Actor { get; private set; }

    /// <summary>What the decision covers, for example <c>development-through-pr</c>.</summary>
    public string Scope { get; private set; }

    public string Reason { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    internal static Authorization Record(
        Guid workItemId,
        AuthorizationDecision decision,
        string actor,
        string scope,
        string reason,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new Authorization
        {
            Id = Guid.CreateVersion7(now),
            WorkItemId = workItemId,
            Decision = decision,
            Actor = actor.Trim(),
            Scope = scope.Trim(),
            Reason = reason.Trim(),
            DecidedAt = now,
        };
    }
}
