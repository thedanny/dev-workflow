namespace DevWorkflow.Domain.Audit;

/// <summary>
/// An append-only record of a state change or decision. The database rejects updates and deletes.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent()
    {
        EntityType = string.Empty;
        EventType = string.Empty;
        Actor = string.Empty;
        Data = "{}";
    }

    /// <summary>Database-assigned sequence; gives a total order of events.</summary>
    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>For example <c>work_item</c>, <c>execution</c> or <c>signal</c>.</summary>
    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    /// <summary>For example <c>work_item.approved</c> or <c>execution.claimed</c>.</summary>
    public string EventType { get; private set; }

    public string Actor { get; private set; }

    /// <summary>Event details as a JSON object. Must not contain secrets.</summary>
    public string Data { get; private set; }

    public static AuditEvent Record(
        string entityType,
        Guid entityId,
        string eventType,
        string actor,
        string? data,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        return new AuditEvent
        {
            OccurredAt = now,
            EntityType = entityType,
            EntityId = entityId,
            EventType = eventType,
            Actor = actor,
            Data = string.IsNullOrWhiteSpace(data) ? "{}" : data,
        };
    }
}
