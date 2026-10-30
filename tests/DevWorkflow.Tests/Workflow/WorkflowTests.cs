using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using DevWorkflow.Infrastructure.Persistence;
using DevWorkflow.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using static DevWorkflow.Tests.Workflow.WorkflowHarness;

namespace DevWorkflow.Tests.Workflow;

/// <summary>The Phase 1 acceptance criteria, exercised through the application services.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class WorkflowTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly WorkflowHarness _harness = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_signal_becomes_approved_work_that_an_agent_claims_reports_and_completes()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var signal = await _harness.Run<SignalService, SubmitSignalResult>(s => s.SubmitAsync(Listener, new SubmitSignalRequest(
            "jira", "PROJ-101", $"PROJ-101-{Guid.NewGuid():N}", """{"summary":"Checkout broken on mobile"}""")));
        var proposed = await _harness.ProposeAsync(repository.Key, [signal.Signal.Id]);
        await _harness.Run<WorkItemService, WorkItemDto>(s => s.ApproveAsync(Approver, proposed.Id, "Confirmed defect", null));

        var queue = await _harness.Run<ExecutionService, IReadOnlyList<WorkItemDto>>(s => s.ListApprovedWorkAsync(ExecutorA, repository.RemoteUrl, null, null));
        var claimed = await _harness.ClaimAsync(ExecutorA, Assert.Single(queue).Id, $"https://{repository.RemoteUrl}.git");
        var executionId = claimed.Execution.Id;

        await _harness.Run<ExecutionService, ExecutionDto>(s => s.HeartbeatAsync(ExecutorA, executionId, 600));
        await _harness.Run<ExecutionService, ExecutionResult>(s => s.UpdateAsync(ExecutorA, executionId, new UpdateExecutionRequest(
            ExecutionUpdateState.InProgress, "Reproduced and fixing", ["unit tests pass"], "fix/checkout-mobile", "/work/trees/checkout")));
        var reported = await _harness.Run<ExecutionService, PullRequestResult>(s => s.ReportPullRequestAsync(ExecutorA, executionId, new ReportPullRequestRequest(
            new Uri($"https://{repository.RemoteUrl}/pull/42"), "fix/checkout-mobile", Checks: "build and tests green")));
        var completed = await _harness.Run<ExecutionService, ExecutionResult>(s => s.UpdateAsync(ExecutorA, executionId, new UpdateExecutionRequest(
            ExecutionUpdateState.Completed, "PR opened for review")));

        Assert.Equal(signal.Signal.Id, Assert.Single(proposed.SignalIds));
        Assert.Equal("42", reported.PullRequest.ExternalId);
        Assert.Equal("main", reported.PullRequest.BaseBranch);
        Assert.Equal(WorkItemStatus.PRReady, reported.WorkItem.Status);
        Assert.Equal(WorkItemStatus.Completed, completed.WorkItem.Status);
        Assert.Equal(ExecutionState.Completed, completed.Execution.State);
        Assert.Equal("fix/checkout-mobile", completed.Execution.Branch);

        var events = await AuditTrailAsync(proposed.Id, executionId);
        Assert.Equal(
            ["work_item.proposed", "work_item.approved", "work_item.claimed", "execution.updated", "work_item.pull_request_reported", "execution.updated"],
            events);
    }

    [Fact]
    public async Task Only_approved_work_for_the_executors_repository_appears_in_its_queue()
    {
        var repoA = await _harness.RegisterRepositoryAsync();
        var repoB = await _harness.RegisterRepositoryAsync();
        var approvedA = await _harness.ProposeApprovedAsync(repoA.Key, WorkItemPriority.Normal);
        var urgentA = await _harness.ProposeApprovedAsync(repoA.Key, WorkItemPriority.Critical);
        await _harness.ProposeAsync(repoA.Key);
        await _harness.ProposeApprovedAsync(repoB.Key);

        var queueA = await _harness.Run<ExecutionService, IReadOnlyList<WorkItemDto>>(s => s.ListApprovedWorkAsync(ExecutorA, repoA.Key, null, null));

        Assert.Equal([urgentA.Id, approvedA.Id], queueA.Select(w => w.Id));
    }

    [Fact]
    public async Task Concurrent_claims_cannot_acquire_the_same_work_item()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);

        var attempts = Enumerable.Range(0, 8).Select(async i =>
        {
            try
            {
                await _harness.ClaimAsync(i % 2 == 0 ? ExecutorA : ExecutorB, item.Id, repository.Key, $"agent-{i}");
                return "claimed";
            }
            catch (WorkflowException error)
            {
                return error.Code;
            }
        });
        var outcomes = await Task.WhenAll(attempts);

        Assert.Single(outcomes, o => o == "claimed");
        Assert.All(outcomes.Where(o => o != "claimed"), o => Assert.Equal("already_claimed", o));

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Executions.CountAsync(e => e.WorkItemId == item.Id));
    }

    [Fact]
    public async Task An_executor_cannot_see_or_claim_work_for_another_repository()
    {
        var repoA = await _harness.RegisterRepositoryAsync();
        var repoB = await _harness.RegisterRepositoryAsync();
        var itemA = await _harness.ProposeApprovedAsync(repoA.Key);

        var queueB = await _harness.Run<ExecutionService, IReadOnlyList<WorkItemDto>>(s => s.ListApprovedWorkAsync(ExecutorB, repoB.Key, null, null));
        var claim = await Assert.ThrowsAsync<WorkflowException>(() => _harness.ClaimAsync(ExecutorB, itemA.Id, repoB.Key));
        var read = await Assert.ThrowsAsync<WorkflowException>(() =>
            _harness.Run<WorkItemService, WorkItemDto>(s => s.GetAsync(ExecutorB, itemA.Id, repoB.Key)));

        Assert.DoesNotContain(queueB, w => w.Id == itemA.Id);
        Assert.Equal(("repository_mismatch", ErrorKind.Forbidden), (claim.Code, claim.Kind));
        Assert.Equal("repository_mismatch", read.Code);
    }

    [Fact]
    public async Task A_work_item_without_a_target_repository_cannot_be_approved_until_routed()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeAsync(repository: null);

        var error = await ErrorCodeAsync(() =>
            _harness.Run<WorkItemService, WorkItemDto>(s => s.ApproveAsync(Approver, item.Id, "ok", null)));
        await _harness.Run<WorkItemService, WorkItemDto>(s => s.RouteAsync(Evaluator, item.Id, repository.Key));
        var approved = await _harness.Run<WorkItemService, WorkItemDto>(s => s.ApproveAsync(Approver, item.Id, "ok", null));

        Assert.Equal("repository_required", error);
        Assert.Equal(WorkItemStatus.Approved, approved.Status);
        Assert.Equal(repository.Key, approved.Repository);
    }

    [Fact]
    public async Task Resubmitting_a_signal_returns_the_original_and_secrets_are_redacted()
    {
        var key = $"cw-{Guid.NewGuid():N}";
        var request = new SubmitSignalRequest(
            "CloudWatch",
            "arn:aws:logs:example",
            key,
            "ERROR login failed for jane@example.com password=hunter2 key AKIAABCDEFGHIJKLMNOP");

        var first = await _harness.Run<SignalService, SubmitSignalResult>(s => s.SubmitAsync(Listener, request));
        var second = await _harness.Run<SignalService, SubmitSignalResult>(s => s.SubmitAsync(Listener, request));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Signal.Id, second.Signal.Id);
        Assert.Equal("cloudwatch", first.Signal.SourceType);
        Assert.Equal(RedactionStatus.Redacted, first.Signal.RedactionStatus);
        Assert.Equal("ERROR login failed for [REDACTED_EMAIL] password=[REDACTED] key [REDACTED]", first.Signal.Payload);
    }

    [Fact]
    public async Task Expired_leases_are_recovered_and_the_work_can_be_claimed_again()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);
        var first = await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key, leaseSeconds: 60);

        _harness.Clock.Advance(TimeSpan.FromSeconds(61));
        var expired = await _harness.Run<ExecutionService, int>(s => s.ExpireLeasesAsync());
        var heartbeat = await ErrorCodeAsync(() =>
            _harness.Run<ExecutionService, ExecutionDto>(s => s.HeartbeatAsync(ExecutorA, first.Execution.Id, null)));
        var second = await _harness.ClaimAsync(ExecutorB, item.Id, repository.Key, "agent-2");

        Assert.True(expired >= 1);
        Assert.Equal("execution_not_active", heartbeat);
        Assert.Equal(WorkItemStatus.Claimed, second.WorkItem.Status);
    }

    [Fact]
    public async Task A_claim_takes_over_a_stale_lease_without_waiting_for_the_sweep()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);
        await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key, leaseSeconds: 60);

        _harness.Clock.Advance(TimeSpan.FromMinutes(2));
        var takeover = await _harness.ClaimAsync(ExecutorB, item.Id, repository.Key, "agent-2");

        Assert.Equal("agent-2", takeover.Execution.ExecutorId);
    }

    [Fact]
    public async Task A_retried_claim_from_the_same_executor_returns_the_same_execution()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);

        var first = await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key, "agent-1");
        var retry = await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key, "agent-1");

        Assert.Equal(first.Execution.Id, retry.Execution.Id);
    }

    [Fact]
    public async Task An_executor_lists_only_its_own_live_executions_in_its_repository_to_resume_them()
    {
        var repoA = await _harness.RegisterRepositoryAsync();
        var repoB = await _harness.RegisterRepositoryAsync();
        var held = await _harness.ClaimAsync(ExecutorA, (await _harness.ProposeApprovedAsync(repoA.Key)).Id, repoA.Key, leaseSeconds: 600);
        var released = await _harness.ClaimAsync(ExecutorA, (await _harness.ProposeApprovedAsync(repoA.Key)).Id, repoA.Key);
        await _harness.ClaimAsync(ExecutorA, (await _harness.ProposeApprovedAsync(repoA.Key)).Id, repoA.Key, leaseSeconds: 60);
        await _harness.ClaimAsync(ExecutorA, (await _harness.ProposeApprovedAsync(repoB.Key)).Id, repoB.Key);
        await _harness.ClaimAsync(ExecutorB, (await _harness.ProposeApprovedAsync(repoA.Key)).Id, repoA.Key);
        await _harness.Run<ExecutionService, ExecutionResult>(s => s.ReleaseAsync(ExecutorA, released.Execution.Id, "not now"));
        _harness.Clock.Advance(TimeSpan.FromSeconds(61));

        var mine = await _harness.Run<ExecutionService, IReadOnlyList<ExecutionResult>>(s => s.ListActiveAsync(ExecutorA, $"https://{repoA.RemoteUrl}.git"));

        var only = Assert.Single(mine);
        Assert.Equal(held.Execution.Id, only.Execution.Id);
        Assert.Equal(held.WorkItem.Id, only.WorkItem.Id);
        Assert.Equal(repoA.Key, only.WorkItem.Repository);
    }

    [Fact]
    public async Task Roles_and_execution_ownership_are_enforced()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);

        var listenerClaim = await Assert.ThrowsAsync<WorkflowException>(() => _harness.ClaimAsync(Listener, item.Id, repository.Key));
        var executorApproval = await Assert.ThrowsAsync<WorkflowException>(() =>
            _harness.Run<WorkItemService, WorkItemDto>(s => s.ApproveAsync(ExecutorA, item.Id, "self-approval", null)));
        var claimed = await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key);
        var foreignRelease = await Assert.ThrowsAsync<WorkflowException>(() =>
            _harness.Run<ExecutionService, ExecutionResult>(s => s.ReleaseAsync(ExecutorB, claimed.Execution.Id, "not mine")));

        Assert.Equal("role_not_permitted", listenerClaim.Code);
        Assert.Equal("role_not_permitted", executorApproval.Code);
        Assert.Equal("not_execution_owner", foreignRelease.Code);
    }

    [Fact]
    public async Task Blocked_work_returns_to_the_queue_only_when_an_approver_requeues_it()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);
        var claimed = await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key);

        var blocked = await _harness.Run<ExecutionService, ExecutionResult>(s => s.UpdateAsync(ExecutorA, claimed.Execution.Id,
            new UpdateExecutionRequest(ExecutionUpdateState.Blocked, "Needs a product decision")));
        var queueWhileBlocked = await _harness.Run<ExecutionService, IReadOnlyList<WorkItemDto>>(s => s.ListApprovedWorkAsync(ExecutorA, repository.Key, null, null));
        await _harness.Run<WorkItemService, WorkItemDto>(s => s.RequeueAsync(Approver, item.Id, "Decision made"));
        var queueAfter = await _harness.Run<ExecutionService, IReadOnlyList<WorkItemDto>>(s => s.ListApprovedWorkAsync(ExecutorA, repository.Key, null, null));

        Assert.Equal(WorkItemStatus.Blocked, blocked.WorkItem.Status);
        Assert.Equal(ExecutionState.Released, blocked.Execution.State);
        Assert.DoesNotContain(queueWhileBlocked, w => w.Id == item.Id);
        Assert.Contains(queueAfter, w => w.Id == item.Id);
    }

    [Fact]
    public async Task A_pull_request_must_be_in_the_work_items_repository()
    {
        var repository = await _harness.RegisterRepositoryAsync();
        var item = await _harness.ProposeApprovedAsync(repository.Key);
        var claimed = await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key);

        var error = await Assert.ThrowsAsync<WorkflowException>(() =>
            _harness.Run<ExecutionService, PullRequestResult>(s => s.ReportPullRequestAsync(ExecutorA, claimed.Execution.Id,
                new ReportPullRequestRequest(new Uri("https://github.com/example-org/some-other-repo/pull/7"), "fix/x"))));

        Assert.Equal(("pull_request_url_mismatch", ErrorKind.Validation), (error.Code, error.Kind));
    }

    private async Task<string[]> AuditTrailAsync(Guid workItemId, Guid executionId)
    {
        await using var db = database.CreateContext();
        return await db.AuditEvents
            .Where(a => a.EntityId == workItemId || a.EntityId == executionId)
            .OrderBy(a => a.Id)
            .Select(a => a.EventType)
            .ToArrayAsync();
    }

    /// <summary>Runs an operation that must fail with an expected error, and returns its code.</summary>
    private static async Task<string> ErrorCodeAsync(Func<Task> operation)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(operation);
        var error = WorkflowError.From(exception);
        Assert.NotNull(error);
        return error.Code;
    }
}
