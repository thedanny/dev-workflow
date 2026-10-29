using System.Globalization;
using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Security;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Services;

/// <summary>
/// The executor's side of the queue. Every operation is scoped to the executor's repository,
/// and every execution can only be acted on by the client that claimed it.
/// </summary>
public sealed class ExecutionService(IDevWorkflowDb db, RepositoryLookup repositories, TimeProvider clock)
{
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<WorkItemDto>> ListApprovedWorkAsync(
        Caller caller,
        string repository,
        int? limit,
        WorkItemPriority? minimumPriority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Executor);

        var own = await repositories.ResolveAsync(repository, cancellationToken);
        var query = db.WorkItems.AsNoTracking()
            .Include(w => w.SignalLinks)
            .Where(w => w.RepositoryId == own.Id && w.Status == WorkItemStatus.Approved);
        if (minimumPriority is not null)
        {
            query = query.Where(w => w.Priority >= minimumPriority);
        }

        var items = await query
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.CreatedAt)
            .Take(Limits.PageSize(limit))
            .ToListAsync(cancellationToken);

        return [.. items.Select(i => WorkItemDto.From(i, own.Key))];
    }

    public async Task<ExecutionResult> ClaimAsync(
        Caller caller,
        Guid workItemId,
        ClaimWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        caller.Require(CallerRole.Executor);
        if (string.IsNullOrWhiteSpace(request.ExecutorId))
        {
            throw WorkflowException.Validation("executor_id_required", "An executor ID is required.");
        }

        var now = clock.GetUtcNow();
        var lease = LeaseFrom(request.LeaseSeconds);
        var own = await repositories.ResolveAsync(request.Repository, cancellationToken);
        var item = await db.WorkItems.Include(w => w.SignalLinks).SingleOrDefaultAsync(w => w.Id == workItemId, cancellationToken)
            ?? throw WorkItemService.NotFound(workItemId);

        if (item.RepositoryId != own.Id)
        {
            throw WorkItemService.RepositoryMismatch(own.Key);
        }

        var active = await db.Executions.SingleOrDefaultAsync(
            e => e.WorkItemId == item.Id && e.State == ExecutionState.Active,
            cancellationToken);
        if (active is not null)
        {
            if (!active.IsLeaseExpired(now))
            {
                // A retried claim from the same executor gets its existing execution back.
                if (active.ClaimedBy == caller.Name && active.ExecutorId == request.ExecutorId.Trim())
                {
                    return new ExecutionResult(ExecutionDto.From(active), WorkItemDto.From(item, own.Key));
                }

                throw AlreadyClaimed();
            }

            ExpireInto(active, item, now);
        }

        if (item.Status != WorkItemStatus.Approved)
        {
            throw WorkflowException.Conflict("not_claimable", $"Only Approved work can be claimed; this work item is {item.Status}.");
        }

        var execution = Execution.Start(item.Id, request.ExecutorId, caller.Name, lease, now);
        item.MarkClaimed(now);
        db.Executions.Add(execution);
        AuditLog.Record(db, "work_item", item.Id, "work_item.claimed", Actors.Of(caller), new
        {
            ExecutionId = execution.Id,
            execution.ExecutorId,
            execution.LeaseExpiresAt,
        }, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race: the work item version changed, or the one-active-execution index fired.
            db.DiscardChanges();
            throw AlreadyClaimed();
        }

        return new ExecutionResult(ExecutionDto.From(execution), WorkItemDto.From(item, own.Key));
    }

    public async Task<ExecutionDto> HeartbeatAsync(
        Caller caller,
        Guid executionId,
        int? leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        var (execution, _) = await LoadOwnedAsync(caller, executionId, cancellationToken);

        execution.Heartbeat(LeaseFrom(leaseSeconds), clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return ExecutionDto.From(execution);
    }

    public async Task<ExecutionResult> UpdateAsync(
        Caller caller,
        Guid executionId,
        UpdateExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (execution, item) = await LoadOwnedAsync(caller, executionId, cancellationToken);
        var now = clock.GetUtcNow();

        if (request.Summary is not null)
        {
            execution.RecordSummary(request.Summary, now);
        }

        if (request.Branch is not null || request.WorktreePath is not null)
        {
            execution.RecordWorkspace(request.Branch, request.WorktreePath, now);
        }

        switch (request.State)
        {
            case ExecutionUpdateState.InProgress:
                if (item.Status != WorkItemStatus.InProgress)
                {
                    item.MarkInProgress(now);
                }

                break;
            case ExecutionUpdateState.Blocked:
                item.Block(now);
                execution.Release($"blocked: {request.Summary ?? "no summary"}", now);
                break;
            case ExecutionUpdateState.Failed:
                item.Fail(now);
                execution.Fail(request.Summary ?? "failed", now);
                break;
            case ExecutionUpdateState.Completed:
                item.Complete(now);
                execution.Complete(now);
                break;
            default:
                throw WorkflowException.Validation("invalid_state", $"Unsupported execution state {request.State}.");
        }

        AuditLog.Record(db, "execution", execution.Id, "execution.updated", Actors.Of(caller), new
        {
            WorkItemId = item.Id,
            request.State,
            request.Summary,
            Evidence = request.Evidence ?? [],
        }, now);
        await db.SaveChangesAsync(cancellationToken);

        return new ExecutionResult(ExecutionDto.From(execution), await ToDtoAsync(item, cancellationToken));
    }

    public async Task<PullRequestResult> ReportPullRequestAsync(
        Caller caller,
        Guid executionId,
        ReportPullRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (execution, item) = await LoadOwnedAsync(caller, executionId, cancellationToken);
        var repository = await db.Repositories.SingleAsync(r => r.Id == item.RepositoryId, cancellationToken);
        var now = clock.GetUtcNow();

        if (request.Provider is { } provider && provider != repository.Provider)
        {
            throw WorkflowException.Validation(
                "provider_mismatch",
                $"The repository is hosted on {repository.Provider}, not {provider}.");
        }

        ValidatePullRequestUrl(request.Url, repository);
        var externalId = string.IsNullOrWhiteSpace(request.ExternalId) ? PullRequestNumberFrom(request.Url) : request.ExternalId.Trim();

        var pullRequest = await db.PullRequests.SingleOrDefaultAsync(
            p => p.RepositoryId == repository.Id && p.ExternalId == externalId,
            cancellationToken);
        if (pullRequest is null)
        {
            pullRequest = PullRequest.Report(
                item.Id,
                execution.Id,
                repository.Id,
                repository.Provider,
                externalId,
                request.Url,
                request.Branch,
                string.IsNullOrWhiteSpace(request.BaseBranch) ? repository.DefaultBranch : request.BaseBranch,
                request.Checks,
                now);
            db.PullRequests.Add(pullRequest);
        }
        else if (pullRequest.WorkItemId != item.Id)
        {
            throw WorkflowException.Conflict("pull_request_belongs_to_other_work", "This pull request was reported for a different work item.");
        }
        else
        {
            pullRequest.UpdateStatus(request.Checks, pullRequest.ReviewSummary, now);
        }

        execution.RecordWorkspace(request.Branch, null, now);
        if (item.Status == WorkItemStatus.Claimed)
        {
            item.MarkInProgress(now);
        }

        if (item.Status != WorkItemStatus.PRReady)
        {
            item.MarkPullRequestReady(now);
        }

        AuditLog.Record(db, "work_item", item.Id, "work_item.pull_request_reported", Actors.Of(caller), new
        {
            ExecutionId = execution.Id,
            ExternalId = externalId,
            Url = request.Url,
            request.Checks,
        }, now);
        await db.SaveChangesAsync(cancellationToken);

        return new PullRequestResult(PullRequestDto.From(pullRequest), WorkItemDto.From(item, repository.Key));
    }

    public async Task<ExecutionResult> ReleaseAsync(
        Caller caller,
        Guid executionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var (execution, item) = await LoadOwnedAsync(caller, executionId, cancellationToken);
        if (item.Status == WorkItemStatus.PRReady)
        {
            throw WorkflowException.Conflict(
                "pull_request_open",
                "A pull request is already reported. Complete or fail the execution instead of releasing it.");
        }

        var now = clock.GetUtcNow();
        execution.Release(string.IsNullOrWhiteSpace(reason) ? "released" : reason, now);
        item.Requeue(now);
        AuditLog.Record(db, "execution", execution.Id, "execution.released", Actors.Of(caller), new { WorkItemId = item.Id, Reason = reason }, now);
        await db.SaveChangesAsync(cancellationToken);

        return new ExecutionResult(ExecutionDto.From(execution), await ToDtoAsync(item, cancellationToken));
    }

    /// <summary>
    /// Ends executions whose lease ran out and requeues their work. Safe to run from several
    /// processes at once: a concurrent change makes that one execution be skipped until next time.
    /// </summary>
    public async Task<int> ExpireLeasesAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var expiredIds = await db.Executions
            .Where(e => e.State == ExecutionState.Active && e.LeaseExpiresAt <= now)
            .OrderBy(e => e.LeaseExpiresAt)
            .Select(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var id in expiredIds)
        {
            var execution = await db.Executions.SingleAsync(e => e.Id == id, cancellationToken);
            var item = await db.WorkItems.SingleAsync(w => w.Id == execution.WorkItemId, cancellationToken);
            if (!execution.IsLeaseExpired(now))
            {
                continue;
            }

            ExpireInto(execution, item, now);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                expired++;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.DiscardChanges();
            }
        }

        return expired;
    }

    private void ExpireInto(Execution execution, WorkItem item, DateTimeOffset now)
    {
        execution.Expire(now);
        if (item.Status is WorkItemStatus.Claimed or WorkItemStatus.InProgress)
        {
            item.Requeue(now);
        }

        AuditLog.Record(db, "execution", execution.Id, "execution.lease_expired", Actors.LeaseSweeper, new
        {
            WorkItemId = item.Id,
            execution.ExecutorId,
            execution.LeaseExpiresAt,
            WorkItemStatus = item.Status,
        }, now);
    }

    private async Task<(Execution Execution, WorkItem Item)> LoadOwnedAsync(
        Caller caller,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Executor);

        var execution = await db.Executions.SingleOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            ?? throw WorkflowException.NotFound("execution_not_found", $"Execution {executionId} does not exist.");
        if (execution.ClaimedBy != caller.Name)
        {
            throw WorkflowException.Forbidden("not_execution_owner", "Only the client that claimed this execution can act on it.");
        }

        var item = await db.WorkItems.Include(w => w.SignalLinks).SingleAsync(w => w.Id == execution.WorkItemId, cancellationToken);
        return (execution, item);
    }

    private async Task<WorkItemDto> ToDtoAsync(WorkItem item, CancellationToken cancellationToken) =>
        WorkItemDto.From(item, await repositories.KeyForAsync(item.RepositoryId, cancellationToken));

    private static TimeSpan LeaseFrom(int? seconds) => seconds is null ? DefaultLease : TimeSpan.FromSeconds(seconds.Value);

    private static WorkflowException AlreadyClaimed() =>
        WorkflowException.Conflict("already_claimed", "Another executor holds this work item.");

    /// <summary>The PR URL must point into the work item's repository, which catches PRs opened in the wrong place.</summary>
    private static void ValidatePullRequestUrl(Uri url, CodeRepository repository)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            throw WorkflowException.Validation("invalid_pull_request_url", "The pull request URL must be an absolute http(s) URL.");
        }

        var location = $"{url.Host}{url.AbsolutePath}".ToLowerInvariant();
        if (!location.StartsWith(repository.RemoteUrl + "/", StringComparison.Ordinal))
        {
            throw WorkflowException.Validation(
                "pull_request_url_mismatch",
                $"The pull request URL is not in repository {repository.RemoteUrl}.");
        }
    }

    private static string PullRequestNumberFrom(Uri url)
    {
        var last = url.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault();
        if (last is null || !int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw WorkflowException.Validation(
                "pull_request_number_required",
                "Could not read the pull request number from the URL; pass external_id.");
        }

        return last;
    }
}
