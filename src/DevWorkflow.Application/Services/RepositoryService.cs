using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Providers;
using DevWorkflow.Application.Security;
using DevWorkflow.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Services;

public sealed class RepositoryService(IDevWorkflowDb db, IRepositoryClientFactory clients, TimeProvider clock)
{
    public async Task<RepositoryDto> RegisterAsync(
        Caller caller,
        RegisterRepositoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        caller.Require(CallerRole.Approver);

        var now = clock.GetUtcNow();
        var repository = CodeRepository.Register(
            request.Key,
            request.Provider,
            request.RemoteUrl,
            string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch,
            now);

        var duplicate = await db.Repositories.AnyAsync(
            r => r.Key == repository.Key || r.RemoteUrl == repository.RemoteUrl,
            cancellationToken);
        if (duplicate)
        {
            throw WorkflowException.Conflict(
                "repository_already_registered",
                $"A repository with key '{repository.Key}' or remote '{repository.RemoteUrl}' is already registered.");
        }

        // With a credential configured, the provider must confirm the repository exists.
        if (clients.Create(repository) is { } client)
        {
            var remote = await client.GetRepositoryAsync(cancellationToken)
                ?? throw WorkflowException.Validation(
                    "repository_not_found_on_provider",
                    $"{repository.Provider} has no repository at {repository.RemoteUrl} that the configured credential can see.");
            if (string.IsNullOrWhiteSpace(request.DefaultBranch))
            {
                repository.ChangeDefaultBranch(remote.DefaultBranch);
            }
        }

        db.Repositories.Add(repository);
        AuditLog.Record(db, "repository", repository.Id, "repository.registered", Actors.Of(caller), RepositoryDto.From(repository), now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.DiscardChanges();
            throw WorkflowException.Conflict("repository_already_registered", "The repository was registered concurrently.");
        }

        return RepositoryDto.From(repository);
    }

    public async Task<IReadOnlyList<RepositoryDto>> ListAsync(Caller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Require(CallerRole.Evaluator, CallerRole.Executor, CallerRole.Approver);

        var repositories = await db.Repositories.AsNoTracking().OrderBy(r => r.Key).ToListAsync(cancellationToken);
        return [.. repositories.Select(RepositoryDto.From)];
    }
}
