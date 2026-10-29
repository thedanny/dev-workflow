namespace DevWorkflow.Domain.Signals;

public enum SignalStatus
{
    Received,
    Evaluated,
    Discarded,
}

public enum RedactionStatus
{
    /// <summary>The payload was scanned and nothing needed redacting.</summary>
    Clean,

    /// <summary>Sensitive values were removed before storage.</summary>
    Redacted,
}

/// <summary>
/// Raw inbound data from any source, stored before evaluation. The payload is never edited;
/// only the processing status changes.
/// </summary>
public sealed class Signal
{
    public const int MaxInlinePayloadLength = 64 * 1024;

    private Signal()
    {
        SourceType = string.Empty;
        SourceReference = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Open value such as <c>jira</c>, <c>cloudwatch</c>, <c>email</c> or <c>slack</c>.</summary>
    public string SourceType { get; private set; }

    /// <summary>Identifier or link to the item in the source system.</summary>
    public string SourceReference { get; private set; }

    /// <summary>Unique per source type; resubmitting the same key returns the existing signal.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Inline payload, already redacted. Null when the payload is stored by reference.</summary>
    public string? Payload { get; private set; }

    /// <summary>Location of a large payload stored outside the database.</summary>
    public string? PayloadReference { get; private set; }

    public DateTimeOffset ObservedAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public RedactionStatus RedactionStatus { get; private set; }

    public SignalStatus Status { get; private set; }

    public static Signal Receive(
        string sourceType,
        string sourceReference,
        string idempotencyKey,
        string? payload,
        string? payloadReference,
        DateTimeOffset observedAt,
        RedactionStatus redactionStatus,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (payload is null && payloadReference is null)
        {
            throw new DomainException("signal_payload_missing", "A signal needs an inline payload or a payload reference.");
        }

        if (payload is { Length: > MaxInlinePayloadLength })
        {
            throw new DomainException(
                "signal_payload_too_large",
                $"Inline payloads are limited to {MaxInlinePayloadLength} characters; store larger payloads by reference.");
        }

        return new Signal
        {
            Id = Guid.CreateVersion7(now),
            SourceType = sourceType.Trim().ToLowerInvariant(),
            SourceReference = sourceReference.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            Payload = payload,
            PayloadReference = payloadReference,
            ObservedAt = observedAt,
            ReceivedAt = now,
            RedactionStatus = redactionStatus,
            Status = SignalStatus.Received,
        };
    }

    public void MarkEvaluated() => MoveFromReceived(SignalStatus.Evaluated);

    public void Discard() => MoveFromReceived(SignalStatus.Discarded);

    private void MoveFromReceived(SignalStatus target)
    {
        if (Status != SignalStatus.Received)
        {
            throw new DomainException("invalid_signal_transition", $"Signal is already {Status}.");
        }

        Status = target;
    }
}
