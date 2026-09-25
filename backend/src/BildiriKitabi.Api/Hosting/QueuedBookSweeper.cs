using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Api.Hosting;

/// <summary>Runs <see cref="QueueSweepService"/> every <c>Generation:SweepIntervalSeconds</c>, for either queue provider.</summary>
public sealed partial class QueuedBookSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<GenerationOptions> options,
    TimeProvider timeProvider,
    ILogger<QueuedBookSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using (scope.ConfigureAwait(false))
                    {
                        await scope.ServiceProvider.GetRequiredService<QueueSweepService>().SweepAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A database hiccup must not stop the sweeper; the next tick tries again.
                    LogSweepFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Queued-book sweep failed")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
