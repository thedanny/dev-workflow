using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Infrastructure.Providers;
using DevWorkflow.Tests.Persistence;
using DevWorkflow.Tests.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static DevWorkflow.Tests.Workflow.WorkflowHarness;

namespace DevWorkflow.Tests.Providers;

/// <summary>The queue checked against fake GitHub and Bitbucket remotes, on the shared Postgres container.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ProviderVerificationTests : IAsyncLifetime
{
    private readonly FakeGitHub _github = new();
    private readonly FakeBitbucket _bitbucket = new();
    private readonly WorkflowHarness _harness;
    private readonly WorkflowHarness _unconnected;

    public ProviderVerificationTests(PostgresFixture database)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RepositoryProviders:GitHub:Token"] = "github-test-token",
                ["RepositoryProviders:Bitbucket:Token"] = "bitbucket-test-token",
            })
            .Build();
        _harness = new WorkflowHarness(database, services => services
            .AddDevWorkflowRepositoryProviders(configuration)
            .UseFakeProviders(_github, _bitbucket));
        _unconnected = new WorkflowHarness(database);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _unconnected.DisposeAsync();
        _github.Dispose();
        _bitbucket.Dispose();
    }

    [Fact]
    public async Task Registering_a_repository_confirms_it_on_the_provider_and_takes_its_default_branch()
    {
        var key = NewKey();
        _github.AddRepository("example-org", key, "trunk");

        var registered = await Register(RepositoryProvider.GitHub, key);

        Assert.Equal("trunk", registered.DefaultBranch);
    }

    [Fact]
    public async Task A_repository_the_provider_cannot_see_is_not_registered()
    {
        var error = await Assert.ThrowsAsync<WorkflowException>(() => Register(RepositoryProvider.GitHub, NewKey()));

        Assert.Equal(("repository_not_found_on_provider", ErrorKind.Validation), (error.Code, error.Kind));
    }

    [Fact]
    public async Task A_reported_pull_request_is_verified_and_takes_the_providers_status()
    {
        var (repository, remote, executionId) = await ClaimedGitHubWorkAsync();
        var pull = _github.AddPullRequest(remote, "fix/checkout-mobile");
        pull.CheckRuns.AddRange([("completed", "success"), ("completed", "success")]);
        pull.Reviews.Add(("reviewer-a", "APPROVED"));

        var reported = await Report(executionId, new Uri($"https://{repository.RemoteUrl}/pull/{pull.Number}"), "fix/checkout-mobile", "self-reported: green");

        Assert.Equal(_harness.Clock.GetUtcNow(), reported.PullRequest.VerifiedAt);
        Assert.Equal(PullRequestState.Open, reported.PullRequest.State);
        Assert.Equal(pull.HeadCommit, reported.PullRequest.HeadCommit);
        Assert.Equal("trunk", reported.PullRequest.BaseBranch);
        Assert.Equal("2 passed", reported.PullRequest.ChecksSummary);
        Assert.Equal("1 approved", reported.PullRequest.ReviewSummary);
        Assert.DoesNotContain(_github.Requests, r => r.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task A_pull_request_that_does_not_match_the_provider_is_rejected()
    {
        var (repository, remote, executionId) = await ClaimedGitHubWorkAsync();
        var pull = _github.AddPullRequest(remote, "fix/checkout-mobile");
        var closed = _github.AddPullRequest(remote, "fix/old-attempt");
        closed.State = "closed";
        Uri Url(int number) => new($"https://{repository.RemoteUrl}/pull/{number}");

        var wrongBranch = await Assert.ThrowsAsync<WorkflowException>(() => Report(executionId, Url(pull.Number), "fix/something-else"));
        var wrongBase = await Assert.ThrowsAsync<WorkflowException>(() => Report(executionId, Url(pull.Number), "fix/checkout-mobile", baseBranch: "release"));
        var missing = await Assert.ThrowsAsync<WorkflowException>(() => Report(executionId, Url(404), "fix/checkout-mobile"));
        var declined = await Assert.ThrowsAsync<WorkflowException>(() => Report(executionId, Url(closed.Number), "fix/old-attempt"));

        Assert.Equal("pull_request_branch_mismatch", wrongBranch.Code);
        Assert.Equal("pull_request_base_mismatch", wrongBase.Code);
        Assert.Equal("pull_request_not_found", missing.Code);
        Assert.Equal("pull_request_closed", declined.Code);
    }

    [Fact]
    public async Task A_bitbucket_pull_request_is_verified_from_its_build_statuses()
    {
        var key = NewKey();
        var remote = _bitbucket.AddRepository("example-team", key);
        var repository = await Register(RepositoryProvider.Bitbucket, key);
        var executionId = await ClaimAsync(repository);
        var pull = _bitbucket.AddPullRequest(remote, "bugfix/invoice-rounding");
        pull.Statuses.AddRange(["SUCCESSFUL", "INPROGRESS"]);

        var reported = await Report(executionId, new Uri($"https://{repository.RemoteUrl}/pull-requests/{pull.Number}"), "bugfix/invoice-rounding");

        Assert.NotNull(reported.PullRequest.VerifiedAt);
        Assert.Equal("1 passed, 1 pending", reported.PullRequest.ChecksSummary);
    }

    [Fact]
    public async Task The_sync_records_a_merge_made_by_a_human_on_the_provider()
    {
        var (repository, remote, executionId) = await ClaimedGitHubWorkAsync();
        var pull = _github.AddPullRequest(remote, "fix/checkout-mobile");
        var reported = await Report(executionId, new Uri($"https://{repository.RemoteUrl}/pull/{pull.Number}"), "fix/checkout-mobile");

        pull.State = "merged";
        pull.Reviews.Add(("reviewer-a", "APPROVED"));
        await RefreshAsync();
        var beforeDue = await LoadAsync(reported.PullRequest.Id);
        _harness.Clock.Advance(TimeSpan.FromMinutes(10));
        var refreshed = await RefreshAsync();
        await RefreshAsync();

        var stored = await LoadAsync(reported.PullRequest.Id);
        var events = await _harness.Run<IDevWorkflowDb, List<string>>(db => db.AuditEvents
            .Where(a => a.EntityId == reported.PullRequest.Id)
            .Select(a => a.EventType)
            .ToListAsync());
        Assert.Equal(PullRequestState.Open, beforeDue.State);
        Assert.True(refreshed >= 1);
        Assert.Equal(PullRequestState.Merged, stored.State);
        Assert.Equal("1 approved", stored.ReviewSummary);
        Assert.Equal(["pull_request.state_changed"], events);
        Assert.DoesNotContain(_github.Requests, r => r.Uri.AbsolutePath.EndsWith("/merge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_credential_a_pull_request_is_recorded_unverified()
    {
        var repository = await _unconnected.RegisterRepositoryAsync();
        var item = await _unconnected.ProposeApprovedAsync(repository.Key);
        var claimed = await _unconnected.ClaimAsync(ExecutorA, item.Id, repository.Key);

        var reported = await _unconnected.Run<ExecutionService, PullRequestResult>(s => s.ReportPullRequestAsync(ExecutorA, claimed.Execution.Id,
            new ReportPullRequestRequest(new Uri($"https://{repository.RemoteUrl}/pull/5"), "fix/checkout-mobile", Checks: "tests green")));

        Assert.Null(reported.PullRequest.VerifiedAt);
        Assert.Equal("tests green", reported.PullRequest.ChecksSummary);
        Assert.Equal(PullRequestState.Open, reported.PullRequest.State);
    }

    private static string NewKey() => $"sample-{Guid.NewGuid():N}"[..20];

    private Task<RepositoryDto> Register(RepositoryProvider provider, string key)
    {
        var remote = provider == RepositoryProvider.GitHub
            ? $"git@github.com:example-org/{key}.git"
            : $"git@bitbucket.org:example-team/{key}.git";
        return _harness.Run<RepositoryService, RepositoryDto>(s => s.RegisterAsync(Approver, new RegisterRepositoryRequest(key, provider, remote)));
    }

    private async Task<(RepositoryDto Repository, FakeRepository Remote, Guid ExecutionId)> ClaimedGitHubWorkAsync()
    {
        var key = NewKey();
        var remote = _github.AddRepository("example-org", key, "trunk");
        var repository = await Register(RepositoryProvider.GitHub, key);
        return (repository, remote, await ClaimAsync(repository));
    }

    private async Task<Guid> ClaimAsync(RepositoryDto repository)
    {
        var item = await _harness.ProposeApprovedAsync(repository.Key);
        return (await _harness.ClaimAsync(ExecutorA, item.Id, repository.Key)).Execution.Id;
    }

    private Task<PullRequestResult> Report(Guid executionId, Uri url, string branch, string? checks = null, string? baseBranch = null) =>
        _harness.Run<ExecutionService, PullRequestResult>(s => s.ReportPullRequestAsync(ExecutorA, executionId,
            new ReportPullRequestRequest(url, branch, BaseBranch: baseBranch, Checks: checks)));

    // Other tests share the database, so the batch is large enough to always reach this test's pull request.
    private Task<int> RefreshAsync() =>
        _harness.Run<PullRequestService, int>(s => s.RefreshOpenAsync(TimeSpan.FromMinutes(5), batchSize: 10_000));

    private Task<PullRequest> LoadAsync(Guid id) =>
        _harness.Run<IDevWorkflowDb, PullRequest>(db => db.PullRequests.AsNoTracking().SingleAsync(p => p.Id == id));
}
