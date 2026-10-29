using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Security;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Services;

/// <summary>Proposing, routing and authorizing work. Executors use <see cref="ExecutionService"/>.</summary>
public sealed class WorkItemService(IDevWorkflowDb db, RepositoryLookup repositories, TimeProvider clock)
{
    public const string DefaultApprovalScope = "development-through-pr";

    public async Task<WorkItemDto> ProposeAsync(
        Caller caller,
        ProposeWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        caller.Require(CallerRole.Evaluator);

        var repository = string.IsNullOrWhiteSpace(request.Repository)
            ? null
            : await repositories.ResolveAsync(request.Repository, cancellationToken);

        var signalIds = (request.SignalIds ?? []).Distinct().ToList();
        if (signalIds.Count > 0)
        {
            var found = await db.Signals.Where(s => signalIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken);
            var missing = signalIds.Except(found).ToList();
            if (missing.Count > 0)
            {
                throw WorkflowException.NotFound("signal_not_found", $"Unknown signal IDs: {string.Join(", ", missing)}.");
            }
        }

        var now = clock.GetUtcNow();
        var item = WorkItem.Propose(
            request.Title,
            request.Description ?? string.Empty,
            request.AcceptanceCriteria ?? [],
            request.Priority,
            request.Risk,
            repository?.Id,
            signalIds,
            request.SourceReferences ?? [],
            now);

        db.WorkItems.Add(item);
        AuditLog.Record(db, "work_item", item.Id, "work_item.proposed", Actors.Of(caller), new
        {
            item.Title,
            Repository = repository?.Key,
            SignalIds = signalIds,
        }, now);
        await db.SaveChangesAsync(cancellationToken);

        return WorkItemDto.From(item, repository?.Key);
    }

    public async Task<WorkItemDto> RouteAsync(
        Caller caller,
        Guid workItemId,
        string repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Evaluator, CallerRole.Approver);

        var target = await repositories.ResolveAsync(repository, cancellationToken);
        var item = await LoadAsync(workItemId, cancellationToken);
        var now = clock.GetUtcNow();

        item.AssignRepository(target.Id, now);
        AuditLog.Record(db, "work_item", item.Id, "work_item.routed", Actors.Of(caller), new { Repository = target.Key }, now);
        await db.SaveChangesAsync(cancellationToken);

        return WorkItemDto.From(item, target.Key);
    }

    public async Task<WorkItemDto> ApproveAsync(
        Caller caller,
        Guid workItemId,
        string reason,
        string? scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Approver);

        var item = await LoadAsync(workItemId, cancellationToken);
        if (item.RepositoryId is not null)
        {
            var enabled = await db.Repositories.Where(r => r.Id == item.RepositoryId).Select(r => r.Enabled).SingleAsync(cancellationToken);
            if (!enabled)
            {
                throw WorkflowException.Conflict("repository_disabled", "The target repository is disabled.");
            }
        }

        var now = clock.GetUtcNow();
        var authorization = item.Approve(Actors.Of(caller), string.IsNullOrWhiteSpace(scope) ? DefaultApprovalScope : scope, reason, now);
        db.Authorizations.Add(authorization);
        AuditLog.Record(db, "work_item", item.Id, "work_item.approved", Actors.Of(caller), new { authorization.Scope, authorization.Reason }, now);
        await db.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(item, cancellationToken);
    }

    public async Task<WorkItemDto> RejectAsync(
        Caller caller,
        Guid workItemId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Approver);

        var item = await LoadAsync(workItemId, cancellationToken);
        var now = clock.GetUtcNow();

        var authorization = item.Reject(Actors.Of(caller), reason, now);
        db.Authorizations.Add(authorization);
        AuditLog.Record(db, "work_item", item.Id, "work_item.rejected", Actors.Of(caller), new { authorization.Reason }, now);
        await db.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(item, cancellationToken);
    }

    /// <summary>Returns blocked work to the executable queue once the blocker is resolved.</summary>
    public async Task<WorkItemDto> RequeueAsync(
        Caller caller,
        Guid workItemId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Approver);

        var item = await LoadAsync(workItemId, cancellationToken);
        if (item.Status != WorkItemStatus.Blocked)
        {
            throw WorkflowException.Conflict("not_blocked", $"Only Blocked work can be requeued; this work item is {item.Status}.");
        }

        var now = clock.GetUtcNow();
        item.Requeue(now);
        AuditLog.Record(db, "work_item", item.Id, "work_item.requeued", Actors.Of(caller), new { Reason = reason }, now);
        await db.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(item, cancellationToken);
    }

    public async Task<WorkItemDto> CancelAsync(
        Caller caller,
        Guid workItemId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Approver);

        var item = await LoadAsync(workItemId, cancellationToken);
        var now = clock.GetUtcNow();

        var active = await db.Executions.SingleOrDefaultAsync(
            e => e.WorkItemId == item.Id && e.State == ExecutionState.Active,
            cancellationToken);
        if (active is not null && !active.IsLeaseExpired(now))
        {
            active.Release($"work item cancelled: {reason}", now);
        }

        item.Cancel(now);
        AuditLog.Record(db, "work_item", item.Id, "work_item.cancelled", Actors.Of(caller), new { Reason = reason }, now);
        await db.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(item, cancellationToken);
    }

    /// <summary>
    /// Executors must name their repository and only see work routed to it.
    /// Evaluators and approvers can see any work item.
    /// </summary>
    public async Task<WorkItemDto> GetAsync(
        Caller caller,
        Guid workItemId,
        string? repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Evaluator, CallerRole.Approver, CallerRole.Executor);

        var item = await db.WorkItems.AsNoTracking().Include(w => w.SignalLinks).SingleOrDefaultAsync(w => w.Id == workItemId, cancellationToken)
            ?? throw NotFound(workItemId);

        if (caller.Role == CallerRole.Executor)
        {
            var own = await repositories.ResolveAsync(repository ?? string.Empty, cancellationToken);
            if (item.RepositoryId != own.Id)
            {
                throw RepositoryMismatch(own.Key);
            }
        }

        return await ToDtoAsync(item, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItemDto>> ListAsync(
        Caller caller,
        WorkItemStatus? status,
        string? repository,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Evaluator, CallerRole.Approver);

        var query = db.WorkItems.AsNoTracking().Include(w => w.SignalLinks).AsQueryable();
        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(repository))
        {
            var target = await repositories.ResolveAsync(repository, cancellationToken);
            query = query.Where(w => w.RepositoryId == target.Id);
        }

        var items = await query
            .OrderByDescending(w => w.UpdatedAt)
            .Take(Limits.PageSize(limit))
            .ToListAsync(cancellationToken);
        var keys = await repositories.KeysForAsync(items, cancellationToken);

        return [.. items.Select(i => WorkItemDto.From(i, i.RepositoryId is { } id ? keys.GetValueOrDefault(id) : null))];
    }

    internal static WorkflowException NotFound(Guid workItemId) =>
        WorkflowException.NotFound("work_item_not_found", $"Work item {workItemId} does not exist.");

    internal static WorkflowException RepositoryMismatch(string executorRepository) =>
        WorkflowException.Forbidden(
            "repository_mismatch",
            $"This work item is not routed to repository '{executorRepository}'. Executors can only access work for their own repository.");

    private async Task<WorkItem> LoadAsync(Guid workItemId, CancellationToken cancellationToken) =>
        await db.WorkItems.Include(w => w.SignalLinks).SingleOrDefaultAsync(w => w.Id == workItemId, cancellationToken)
        ?? throw NotFound(workItemId);

    private async Task<WorkItemDto> ToDtoAsync(WorkItem item, CancellationToken cancellationToken) =>
        WorkItemDto.From(item, await repositories.KeyForAsync(item.RepositoryId, cancellationToken));
}
