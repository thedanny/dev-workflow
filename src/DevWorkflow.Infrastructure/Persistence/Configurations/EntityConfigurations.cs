using DevWorkflow.Domain.Audit;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevWorkflow.Infrastructure.Persistence.Configurations;

internal sealed class CodeRepositoryConfiguration : IEntityTypeConfiguration<CodeRepository>
{
    public void Configure(EntityTypeBuilder<CodeRepository> builder)
    {
        builder.ToTable("repositories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Key).HasMaxLength(100);
        builder.Property(r => r.RemoteUrl).HasMaxLength(500);
        builder.Property(r => r.DefaultBranch).HasMaxLength(200);
        builder.HasIndex(r => r.Key).IsUnique();
        builder.HasIndex(r => r.RemoteUrl).IsUnique();
    }
}

internal sealed class SignalConfiguration : IEntityTypeConfiguration<Signal>
{
    public void Configure(EntityTypeBuilder<Signal> builder)
    {
        builder.ToTable("signals");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SourceType).HasMaxLength(64);
        builder.Property(s => s.SourceReference).HasMaxLength(1000);
        builder.Property(s => s.IdempotencyKey).HasMaxLength(300);
        builder.Property(s => s.PayloadReference).HasMaxLength(1000);

        // Resubmitting the same key from the same source must not create a second signal.
        builder.HasIndex(s => new { s.SourceType, s.IdempotencyKey }).IsUnique();
        builder.HasIndex(s => new { s.Status, s.ReceivedAt });
    }
}

internal sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("work_items");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Title).HasMaxLength(300);
        builder.Property(w => w.Version).IsConcurrencyToken();

        // Stored as a number, unlike other enums, so the queue can order by priority.
        builder.Property(w => w.Priority).HasConversion<int>();

        builder.PrimitiveCollection(w => w.AcceptanceCriteria)
            .HasField("_acceptanceCriteria")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.PrimitiveCollection(w => w.SourceReferences)
            .HasField("_sourceReferences")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<CodeRepository>()
            .WithMany()
            .HasForeignKey(w => w.RepositoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(w => w.SignalLinks)
            .WithOne()
            .HasForeignKey(link => link.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.SignalLinks)
            .HasField("_signalLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Serves the executable queue: approved work for one repository.
        builder.HasIndex(w => new { w.RepositoryId, w.Status, w.Priority });
    }
}

internal sealed class WorkItemSignalConfiguration : IEntityTypeConfiguration<WorkItemSignal>
{
    public void Configure(EntityTypeBuilder<WorkItemSignal> builder)
    {
        builder.ToTable("work_item_signals");
        builder.HasKey(link => new { link.WorkItemId, link.SignalId });
        builder.HasOne<Signal>()
            .WithMany()
            .HasForeignKey(link => link.SignalId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(link => link.SignalId);
    }
}

internal sealed class AuthorizationConfiguration : IEntityTypeConfiguration<Authorization>
{
    public void Configure(EntityTypeBuilder<Authorization> builder)
    {
        builder.ToTable("authorizations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Actor).HasMaxLength(200);
        builder.Property(a => a.Scope).HasMaxLength(100);
        builder.HasOne<WorkItem>()
            .WithMany()
            .HasForeignKey(a => a.WorkItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.WorkItemId, a.DecidedAt });
    }
}

internal sealed class ExecutionConfiguration : IEntityTypeConfiguration<Execution>
{
    public void Configure(EntityTypeBuilder<Execution> builder)
    {
        builder.ToTable("executions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ExecutorId).HasMaxLength(200);
        builder.Property(e => e.ClaimedBy).HasMaxLength(200);
        builder.Property(e => e.Branch).HasMaxLength(300);
        builder.Property(e => e.WorktreePath).HasMaxLength(1000);
        builder.Property(e => e.Version).IsConcurrencyToken();
        builder.HasOne<WorkItem>()
            .WithMany()
            .HasForeignKey(e => e.WorkItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // The database, not just application code, guarantees one active claim per work item.
        builder.HasIndex(e => e.WorkItemId)
            .IsUnique()
            .HasFilter("state = 'Active'")
            .HasDatabaseName("ix_executions_one_active_per_work_item");

        // Serves lease-expiry sweeps.
        builder.HasIndex(e => new { e.State, e.LeaseExpiresAt });
    }
}

internal sealed class PullRequestConfiguration : IEntityTypeConfiguration<PullRequest>
{
    public void Configure(EntityTypeBuilder<PullRequest> builder)
    {
        builder.ToTable("pull_requests");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ExternalId).HasMaxLength(100);
        builder.Property(p => p.Url).HasMaxLength(1000);
        builder.Property(p => p.HeadBranch).HasMaxLength(300);
        builder.Property(p => p.BaseBranch).HasMaxLength(300);
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(p => p.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Execution>().WithMany().HasForeignKey(p => p.ExecutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CodeRepository>().WithMany().HasForeignKey(p => p.RepositoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.RepositoryId, p.ExternalId }).IsUnique();
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.EntityType).HasMaxLength(64);
        builder.Property(a => a.EventType).HasMaxLength(100);
        builder.Property(a => a.Actor).HasMaxLength(200);
        builder.Property(a => a.Data).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.Id });
    }
}
