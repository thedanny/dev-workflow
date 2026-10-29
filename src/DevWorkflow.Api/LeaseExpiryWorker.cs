using DevWorkflow.Application.Services;
using Microsoft.Extensions.Options;

namespace DevWorkflow.Api;

public sealed class LeaseExpiryOptions
{
    public const string SectionName = "Leases";

    /// <summary>How often to requeue work whose lease ran out. Zero disables the sweep.</summary>
    public int SweepIntervalSeconds { get; set; } = 30;
}

/// <summary>
/// Periodically expires stale leases so abandoned work returns to the queue. Claims also
/// expire a stale lease inline, so the sweep only bounds how long work stays stuck.
/// </summary>
internal sealed partial class LeaseExpiryWorker(
    IServiceScopeFactory scopes,
    IOptions<LeaseExpiryOptions> options,
    ILogger<LeaseExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SweepIntervalSeconds;
        if (interval <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<ExecutionService>().ExpireLeasesAsync(cancellationToken: stoppingToken);
                if (expired > 0)
                {
                    LogExpired(logger, expired);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSweepFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} stale execution leases and requeued their work.")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lease expiry sweep failed; retrying on the next tick.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
