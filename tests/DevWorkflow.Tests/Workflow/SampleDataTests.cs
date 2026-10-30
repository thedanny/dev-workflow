using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Samples;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using DevWorkflow.Infrastructure.Hosting;
using DevWorkflow.Tests.Persistence;
using static DevWorkflow.Tests.Workflow.WorkflowHarness;

namespace DevWorkflow.Tests.Workflow;

[Collection(PostgresTestGroup.Name)]
public sealed class SampleDataTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly WorkflowHarness _harness = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Seeding_creates_three_approved_tasks_across_two_repositories_linked_to_signals_once()
    {
        var first = await _harness.Run<SampleDataSeeder, bool>(s => s.SeedAsync());
        var second = await _harness.Run<SampleDataSeeder, bool>(s => s.SeedAsync());

        var storefront = await QueueAsync(SampleDataSeeder.StorefrontKey);
        var billing = await QueueAsync(SampleDataSeeder.BillingKey);
        var signals = await _harness.Run<SignalService, IReadOnlyList<SignalDto>>(s => s.ListAsync(Evaluator, SignalStatus.Evaluated, null, 100));
        var seededSignals = signals.Where(s => s.IdempotencyKey.StartsWith("sample:", StringComparison.Ordinal)).ToList();
        var items = storefront.Concat(billing).ToList();

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(2, storefront.Count);
        Assert.Single(billing);
        Assert.All(items, i => Assert.Equal(WorkItemStatus.Approved, i.Status));
        Assert.All(items, i => Assert.Contains(Assert.Single(i.SignalIds), seededSignals.Select(s => s.Id)));
        Assert.Equal(3, seededSignals.Select(s => s.SourceType).Distinct().Count());
        Assert.Contains(seededSignals, s => s.RedactionStatus == RedactionStatus.Redacted && s.Payload!.Contains("[REDACTED_EMAIL]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new string[0], null, new string[0])]
    [InlineData(new[] { "migrate" }, "migrate", new string[0])]
    [InlineData(new[] { "seed", "--urls", "http://localhost:6000" }, "seed", new[] { "--urls", "http://localhost:6000" })]
    [InlineData(new[] { "--urls", "http://localhost:6000" }, null, new[] { "--urls", "http://localhost:6000" })]
    public void A_leading_command_is_split_from_the_host_arguments(string[] args, string? command, string[] hostArgs)
    {
        var parsed = HostCommands.Parse(args);

        Assert.Equal(command, parsed.Command);
        Assert.Equal(hostArgs, parsed.HostArgs);
    }

    private Task<IReadOnlyList<WorkItemDto>> QueueAsync(string repository) =>
        _harness.Run<ExecutionService, IReadOnlyList<WorkItemDto>>(s => s.ListApprovedWorkAsync(ExecutorA, repository, null, null));
}
