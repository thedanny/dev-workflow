using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;

namespace DevWorkflow.Tests;

/// <summary>Fictional sample data. Unique values per call keep shared-database tests independent.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static CodeRepository RegisterRepository()
    {
        var name = $"sample-{Guid.NewGuid():N}";
        return CodeRepository.Register(name, RepositoryProvider.GitHub, $"git@github.com:example-org/{name}.git", "main", Now);
    }

    public static Signal ReceiveSignal(string sourceType = "jira", string? idempotencyKey = null)
    {
        var key = idempotencyKey ?? $"PROJ-{Guid.NewGuid():N}";
        return Signal.Receive(
            sourceType,
            sourceReference: key,
            idempotencyKey: key,
            payload: """{"summary":"Checkout button does nothing on mobile"}""",
            payloadReference: null,
            observedAt: Now,
            RedactionStatus.Clean,
            Now);
    }

    public static WorkItem ProposeWorkItem(Guid? repositoryId, IEnumerable<Guid>? signalIds = null) =>
        WorkItem.Propose(
            "Fix checkout button on mobile",
            "The checkout button does not respond on small screens.",
            ["Checkout works on a 375px-wide viewport", "A regression test covers the fix"],
            WorkItemPriority.High,
            WorkItemRisk.Low,
            repositoryId,
            signalIds ?? [],
            ["PROJ-123"],
            Now);
}
