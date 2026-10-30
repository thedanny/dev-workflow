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
    ILogger<LeaseExpiryWorker> logger) : PeriodicWorker(scopes, logger)
{
    protected override int IntervalSeconds => options.Value.SweepIntervalSeconds;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var expired = await services.GetRequiredService<ExecutionService>().ExpireLeasesAsync(cancellationToken: cancellationToken);
        if (expired > 0)
        {
            LogExpired(logger, expired);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} stale execution leases and requeued their work.")]
    private static partial void LogExpired(ILogger logger, int count);
}
