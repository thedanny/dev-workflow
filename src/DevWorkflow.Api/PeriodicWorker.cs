namespace DevWorkflow.Api;

/// <summary>
/// Runs a job in its own DI scope on a fixed interval. A failed run is logged and retried on
/// the next tick. A zero or negative interval disables the job.
/// </summary>
internal abstract partial class PeriodicWorker(IServiceScopeFactory scopes, ILogger logger) : BackgroundService
{
    protected abstract int IntervalSeconds { get; }

    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (IntervalSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRunFailed(logger, GetType().Name, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Worker} failed; retrying on the next tick.")]
    private static partial void LogRunFailed(ILogger logger, string worker, Exception exception);
}
