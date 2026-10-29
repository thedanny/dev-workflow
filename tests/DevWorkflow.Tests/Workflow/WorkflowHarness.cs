using DevWorkflow.Application;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.WorkItems;
using DevWorkflow.Infrastructure.Persistence;
using DevWorkflow.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DevWorkflow.Tests.Workflow;

/// <summary>
/// Real application services on the shared Postgres container, with a controllable clock.
/// Each operation runs in its own DI scope, like a separate request.
/// </summary>
internal sealed class WorkflowHarness : IAsyncDisposable
{
    public static readonly Caller Listener = new("listener-1", CallerRole.Listener);
    public static readonly Caller Evaluator = new("evaluator-1", CallerRole.Evaluator);
    public static readonly Caller Approver = new("approver-1", CallerRole.Approver);
    public static readonly Caller ExecutorA = new("executor-client-a", CallerRole.Executor);
    public static readonly Caller ExecutorB = new("executor-client-b", CallerRole.Executor);

    private readonly ServiceProvider _provider;

    public WorkflowHarness(PostgresFixture database)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddDevWorkflowPersistence(database.ConnectionString);
        services.AddDevWorkflowApplication();
        _provider = services.BuildServiceProvider();
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<RepositoryDto> RegisterRepositoryAsync(RepositoryProvider provider = RepositoryProvider.GitHub)
    {
        var key = $"sample-{Guid.NewGuid():N}"[..20];
        var remote = provider == RepositoryProvider.GitHub
            ? $"git@github.com:example-org/{key}.git"
            : $"git@bitbucket.org:example-team/{key}.git";
        return Run<RepositoryService, RepositoryDto>(s => s.RegisterAsync(Approver, new RegisterRepositoryRequest(key, provider, remote)));
    }

    public Task<WorkItemDto> ProposeAsync(string? repository, IReadOnlyList<Guid>? signalIds = null, WorkItemPriority priority = WorkItemPriority.Normal) =>
        Run<WorkItemService, WorkItemDto>(s => s.ProposeAsync(Evaluator, new ProposeWorkItemRequest(
            "Fix checkout button on mobile",
            "The checkout button does not respond on small screens.",
            repository,
            signalIds,
            ["Checkout works on a 375px-wide viewport"],
            priority,
            WorkItemRisk.Low)));

    public async Task<WorkItemDto> ProposeApprovedAsync(string repository, WorkItemPriority priority = WorkItemPriority.Normal)
    {
        var item = await ProposeAsync(repository, priority: priority);
        return await Run<WorkItemService, WorkItemDto>(s => s.ApproveAsync(Approver, item.Id, "Confirmed defect", null));
    }

    public Task<ExecutionResult> ClaimAsync(Caller executor, Guid workItemId, string repository, string executorId = "agent-1", int? leaseSeconds = null) =>
        Run<ExecutionService, ExecutionResult>(s => s.ClaimAsync(executor, workItemId, new ClaimWorkItemRequest(executorId, repository, leaseSeconds)));

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
