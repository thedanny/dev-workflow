using DevWorkflow.Domain.Audit;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevWorkflow.Tests.Persistence;

[Collection(PostgresTestGroup.Name)]
public class PersistenceTests(PostgresFixture database)
{
    private const string UniqueViolation = "23505";
    private const string RestrictViolation = "23001";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Migrations_match_the_model()
    {
        await using var db = database.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges(), "Run 'dotnet ef migrations add' for the model changes.");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task A_work_item_round_trips_with_routing_criteria_and_signal_links()
    {
        var repository = TestData.RegisterRepository();
        var signal = TestData.ReceiveSignal();
        var item = TestData.ProposeWorkItem(repository.Id, [signal.Id]);

        await using (var db = database.CreateContext())
        {
            db.AddRange(repository, signal, item);
            await db.SaveChangesAsync();
        }

        // A fresh context stands in for a restart: everything comes back from the database.
        await using (var db = database.CreateContext())
        {
            var loaded = await db.WorkItems.Include(w => w.SignalLinks).SingleAsync(w => w.Id == item.Id);

            Assert.Equal(repository.Id, loaded.RepositoryId);
            Assert.Equal(item.AcceptanceCriteria, loaded.AcceptanceCriteria);
            Assert.Equal(["PROJ-123"], loaded.SourceReferences);
            Assert.Equal(signal.Id, Assert.Single(loaded.SignalLinks).SignalId);
            Assert.Equal(WorkItemPriority.High, loaded.Priority);
            Assert.Equal(1, loaded.Version);
        }
    }

    [Fact]
    public async Task The_same_idempotency_key_from_the_same_source_is_stored_once()
    {
        var key = $"PROJ-{Guid.NewGuid():N}";

        await using (var db = database.CreateContext())
        {
            db.Signals.Add(TestData.ReceiveSignal("jira", key));
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            db.Signals.Add(TestData.ReceiveSignal("jira", key));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }

        // The same key from a different source is a different signal.
        await using (var db = database.CreateContext())
        {
            db.Signals.Add(TestData.ReceiveSignal("email", key));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task The_database_allows_only_one_active_execution_per_work_item()
    {
        var itemId = await SeedApprovedWorkItemAsync();

        var first = Execution.Start(itemId, "executor-1", "test-client", Lease, TestData.Now);
        await using (var db = database.CreateContext())
        {
            db.Executions.Add(first);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            db.Executions.Add(Execution.Start(itemId, "executor-2", "test-client", Lease, TestData.Now));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }

        // Once the first claim is released, a new claim can be recorded.
        await using (var db = database.CreateContext())
        {
            var held = await db.Executions.SingleAsync(e => e.Id == first.Id);
            held.Release("handing off", TestData.Now.AddMinutes(1));
            db.Executions.Add(Execution.Start(itemId, "executor-2", "test-client", Lease, TestData.Now.AddMinutes(1)));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_stale_work_item_update_is_rejected()
    {
        var itemId = await SeedApprovedWorkItemAsync();

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var copyA = await first.WorkItems.SingleAsync(w => w.Id == itemId);
        var copyB = await second.WorkItems.SingleAsync(w => w.Id == itemId);

        copyA.MarkClaimed(TestData.Now);
        await first.SaveChangesAsync();

        copyB.MarkClaimed(TestData.Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Audit_events_cannot_be_updated_or_deleted()
    {
        var auditEvent = AuditEvent.Record("work_item", Guid.NewGuid(), "work_item.approved", "reviewer", """{"reason":"ok"}""", TestData.Now);

        await using var db = database.CreateContext();
        db.AuditEvents.Add(auditEvent);
        await db.SaveChangesAsync();
        Assert.True(auditEvent.Id > 0);

        var update = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlAsync($"UPDATE audit_events SET actor = 'someone-else' WHERE id = {auditEvent.Id}"));
        var delete = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlAsync($"DELETE FROM audit_events WHERE id = {auditEvent.Id}"));

        Assert.Equal(RestrictViolation, update.SqlState);
        Assert.Equal(RestrictViolation, delete.SqlState);
    }

    [Fact]
    public async Task Signal_raw_data_is_immutable_but_its_status_can_change()
    {
        var signal = TestData.ReceiveSignal("slack");

        await using (var db = database.CreateContext())
        {
            db.Signals.Add(signal);
            await db.SaveChangesAsync();

            var edit = await Assert.ThrowsAsync<PostgresException>(
                () => db.Database.ExecuteSqlAsync($"UPDATE signals SET payload = 'tampered' WHERE id = {signal.Id}"));
            var delete = await Assert.ThrowsAsync<PostgresException>(
                () => db.Database.ExecuteSqlAsync($"DELETE FROM signals WHERE id = {signal.Id}"));

            Assert.Equal(RestrictViolation, edit.SqlState);
            Assert.Equal(RestrictViolation, delete.SqlState);

            signal.MarkEvaluated();
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var loaded = await db.Signals.SingleAsync(s => s.Id == signal.Id);
            Assert.Equal(SignalStatus.Evaluated, loaded.Status);
            Assert.Equal(signal.Payload, loaded.Payload);
        }
    }

    private async Task<Guid> SeedApprovedWorkItemAsync()
    {
        var repository = TestData.RegisterRepository();
        var item = TestData.ProposeWorkItem(repository.Id);
        var authorization = item.Approve("reviewer", "development-through-pr", "Confirmed defect", TestData.Now);

        await using var db = database.CreateContext();
        db.AddRange(repository, item, authorization);
        await db.SaveChangesAsync();

        return item.Id;
    }
}
