using DevWorkflow.Domain.Audit;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Infrastructure.Persistence;

public sealed class DevWorkflowDbContext(DbContextOptions<DevWorkflowDbContext> options) : DbContext(options)
{
    public DbSet<CodeRepository> Repositories => Set<CodeRepository>();

    public DbSet<Signal> Signals => Set<Signal>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<Authorization> Authorizations => Set<Authorization>();

    public DbSet<Execution> Executions => Set<Execution>();

    public DbSet<PullRequest> PullRequests => Set<PullRequest>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums by name so the database stays readable and reordering members is safe.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevWorkflowDbContext).Assembly);
    }
}
