using DevWorkflow.Application.Services;
using Microsoft.Extensions.Options;

namespace DevWorkflow.Api;

public sealed class PullRequestSyncOptions
{
    public const string SectionName = "PullRequests";

    /// <summary>How often to refresh open pull requests from their provider. Zero disables the sync.</summary>
    public int SyncIntervalSeconds { get; set; } = 300;
}

/// <summary>Refreshes open pull requests' state, checks and reviews from repositories with a configured credential.</summary>
internal sealed partial class PullRequestSyncWorker(
    IServiceScopeFactory scopes,
    IOptions<PullRequestSyncOptions> options,
    ILogger<PullRequestSyncWorker> logger) : PeriodicWorker(scopes, logger)
{
    protected override int IntervalSeconds => options.Value.SyncIntervalSeconds;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var refreshed = await services.GetRequiredService<PullRequestService>().RefreshOpenAsync(
            TimeSpan.FromSeconds(IntervalSeconds),
            cancellationToken: cancellationToken);
        if (refreshed > 0)
        {
            LogRefreshed(logger, refreshed);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refreshed {Count} open pull requests from their providers.")]
    private static partial void LogRefreshed(ILogger logger, int count);
}
