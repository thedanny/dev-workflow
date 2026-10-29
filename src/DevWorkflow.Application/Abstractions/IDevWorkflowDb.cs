using DevWorkflow.Domain.Audit;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Abstractions;

/// <summary>The unit of work the application services use. Implemented by the EF Core context.</summary>
public interface IDevWorkflowDb
{
    DbSet<CodeRepository> Repositories { get; }

    DbSet<Signal> Signals { get; }

    DbSet<WorkItem> WorkItems { get; }

    DbSet<Authorization> Authorizations { get; }

    DbSet<Execution> Executions { get; }

    DbSet<PullRequest> PullRequests { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets tracked changes after a failed save so the unit of work can be reused.</summary>
    void DiscardChanges();
}
