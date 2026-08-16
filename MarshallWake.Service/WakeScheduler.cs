using Microsoft.Extensions.Options;

namespace MarshallWake.Service;

public sealed class WakeScheduler(
    WakeCoordinator coordinator,
    IOptions<MarshallWakeOptions> options,
    TimeProvider timeProvider,
    ILogger<WakeScheduler> logger) : BackgroundService
{
    private readonly MarshallWakeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Automatic wake scheduling is disabled");
            return;
        }

        if (_options.InitialDelaySeconds > 0)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_options.InitialDelaySeconds),
                timeProvider,
                stoppingToken);
        }

        await RunWakeAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(_options.IntervalMinutes),
            timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunWakeAsync(stoppingToken);
    }

    private async Task RunWakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.WakeAsync(null, "scheduled", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled Marshall wake cycle failed");
        }
    }
}
