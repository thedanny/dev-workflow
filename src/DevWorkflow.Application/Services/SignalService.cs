using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Signals;
using DevWorkflow.Domain.Signals;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Services;

/// <summary>Source-neutral intake: every listener submits raw signals the same way.</summary>
public sealed class SignalService(IDevWorkflowDb db, ISignalRedactor redactor, TimeProvider clock)
{
    public async Task<SubmitSignalResult> SubmitAsync(
        Caller caller,
        SubmitSignalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        caller.Require(CallerRole.Listener);

        var existing = await FindAsync(request.SourceType, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new SubmitSignalResult(SignalDto.From(existing), Created: false);
        }

        var now = clock.GetUtcNow();
        var (payload, payloadRedacted) = redactor.Redact(request.Payload);
        var (reference, referenceRedacted) = redactor.Redact(request.SourceReference);
        var redaction = request.AlreadyRedacted || payloadRedacted || referenceRedacted
            ? RedactionStatus.Redacted
            : RedactionStatus.Clean;

        var signal = Signal.Receive(
            request.SourceType,
            reference ?? request.SourceReference,
            request.IdempotencyKey,
            payload,
            request.PayloadReference,
            request.ObservedAt ?? now,
            redaction,
            now);

        db.Signals.Add(signal);
        AuditLog.Record(db, "signal", signal.Id, "signal.received", Actors.Of(caller), new
        {
            signal.SourceType,
            signal.SourceReference,
            RedactionStatus = signal.RedactionStatus,
        }, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another submission with the same key won the race; return that signal.
            db.DiscardChanges();
            var winner = await FindAsync(request.SourceType, request.IdempotencyKey, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new SubmitSignalResult(SignalDto.From(winner), Created: false);
        }

        return new SubmitSignalResult(SignalDto.From(signal), Created: true);
    }

    public async Task<IReadOnlyList<SignalDto>> ListAsync(
        Caller caller,
        SignalStatus? status,
        string? sourceType,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Evaluator, CallerRole.Approver);

        var query = db.Signals.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            var normalized = sourceType.Trim().ToLowerInvariant();
            query = query.Where(s => s.SourceType == normalized);
        }

        var signals = await query.OrderBy(s => s.ReceivedAt).Take(Limits.PageSize(limit)).ToListAsync(cancellationToken);
        return [.. signals.Select(SignalDto.From)];
    }

    /// <summary>Marks a signal as evaluated or discarded once the evaluator has dealt with it.</summary>
    public async Task<SignalDto> ResolveAsync(
        Caller caller,
        Guid signalId,
        SignalStatus status,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Evaluator);

        var signal = await db.Signals.SingleOrDefaultAsync(s => s.Id == signalId, cancellationToken)
            ?? throw WorkflowException.NotFound("signal_not_found", $"Signal {signalId} does not exist.");

        switch (status)
        {
            case SignalStatus.Evaluated:
                signal.MarkEvaluated();
                break;
            case SignalStatus.Discarded:
                signal.Discard();
                break;
            default:
                throw WorkflowException.Validation("invalid_signal_status", "A signal can be resolved as evaluated or discarded.");
        }

        var now = clock.GetUtcNow();
        AuditLog.Record(db, "signal", signal.Id, $"signal.{status.ToString().ToLowerInvariant()}", Actors.Of(caller), new { Reason = reason }, now);
        await db.SaveChangesAsync(cancellationToken);

        return SignalDto.From(signal);
    }

    private Task<Signal?> FindAsync(string sourceType, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw WorkflowException.Validation("invalid_signal", "Source type and idempotency key are required.");
        }

        var normalizedSource = sourceType.Trim().ToLowerInvariant();
        var normalizedKey = idempotencyKey.Trim();
        return db.Signals.AsNoTracking()
            .SingleOrDefaultAsync(s => s.SourceType == normalizedSource && s.IdempotencyKey == normalizedKey, cancellationToken);
    }
}
