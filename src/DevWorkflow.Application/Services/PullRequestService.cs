using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Providers;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Services;

/// <summary>
/// Keeps reported pull requests in step with their provider: state, head commit, checks and
/// reviews. It only reads from providers; merging stays a human decision on the provider.
/// </summary>
public sealed class PullRequestService(IDevWorkflowDb db, IRepositoryClientFactory clients, TimeProvider clock)
{
    /// <summary>
    /// Refreshes open pull requests not updated within <paramref name="minimumAge"/>, oldest first.
    /// Repositories without a credential are skipped, and a provider failure skips only that one.
    /// </summary>
    public async Task<int> RefreshOpenAsync(
        TimeSpan minimumAge,
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var staleBefore = now - minimumAge;

        // The registry is small. Picking connected repositories first keeps unconnected ones from
        // filling every batch.
        var connected = (await db.Repositories.ToListAsync(cancellationToken))
            .Select(r => (r.Id, Client: TryCreateClient(r)))
            .Where(r => r.Client is not null)
            .ToDictionary(r => r.Id, r => r.Client!);
        if (connected.Count == 0)
        {
            return 0;
        }

        var connectedIds = connected.Keys.ToList();
        var candidates = await db.PullRequests
            .Where(p => p.State == PullRequestState.Open && p.UpdatedAt <= staleBefore && connectedIds.Contains(p.RepositoryId))
            .OrderBy(p => p.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var refreshed = 0;
        foreach (var pullRequest in candidates)
        {
            var client = connected[pullRequest.RepositoryId];
            RemotePullRequest? remote;
            try
            {
                remote = await client.GetPullRequestAsync(pullRequest.ExternalId, cancellationToken);
            }
            catch (WorkflowException exception) when (exception.Kind == ErrorKind.Unavailable)
            {
                continue;
            }

            if (remote is null)
            {
                continue;
            }

            var previous = pullRequest.State;
            if (pullRequest.RecordProviderStatus(remote.State, remote.HeadCommit, remote.ChecksSummary, remote.ReviewSummary, now))
            {
                AuditLog.Record(db, "pull_request", pullRequest.Id, "pull_request.state_changed", Actors.PullRequestSync, new
                {
                    pullRequest.WorkItemId,
                    pullRequest.ExternalId,
                    From = previous,
                    To = remote.State,
                }, now);
            }

            refreshed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return refreshed;
    }

    /// <summary>A repository registered before credentials existed may have a remote no adapter supports.</summary>
    private IRepositoryClient? TryCreateClient(CodeRepository repository)
    {
        try
        {
            return clients.Create(repository);
        }
        catch (WorkflowException)
        {
            return null;
        }
    }
}
