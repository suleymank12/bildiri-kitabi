using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Infrastructure.Persistence;

/// <summary>
/// Bounded retry with exponential back-off for work that has to succeed once while the process starts, such as the
/// startup migration while SQL Server is still opening its databases. Only errors that <c>transientReason</c>
/// describes are retried; any other error, or the last error once <see cref="Budget"/> is used up, is thrown.
/// </summary>
public sealed partial class StartupRetry(TimeProvider timeProvider, ILogger logger)
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    /// <summary>Total time allowed, including the attempts themselves; no wait is started that would exceed it.</summary>
    public TimeSpan Budget { get; init; } = DefaultBudget;

    /// <summary>1, 2, 4, 8, then 10 seconds before each further attempt.</summary>
    public static TimeSpan DelayAfterFailedAttempt(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        var seconds = InitialDelay.TotalSeconds * Math.Pow(2, Math.Min(failedAttempts - 1, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }

    /// <param name="operationName">Short name for the log line, for example "Database migration".</param>
    /// <param name="operation">The work; it runs again from the start after a transient error.</param>
    /// <param name="transientReason">
    /// A short, secret-free description of a transient error (it is logged), or <c>null</c> for a permanent one.
    /// </param>
    public async Task RunAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        Func<Exception, string?> transientReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(transientReason);

        var started = timeProvider.GetTimestamp();
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan delay;
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                && transientReason(ex) is { } reason
                && timeProvider.GetElapsedTime(started) + DelayAfterFailedAttempt(attempt) < Budget)
            {
                delay = DelayAfterFailedAttempt(attempt);
                LogRetrying(logger, operationName, attempt, reason, delay.TotalSeconds);
            }

            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with a transient error (attempt {Attempt}, {Reason}); retrying in {DelaySeconds} s")]
    private static partial void LogRetrying(ILogger logger, string operation, int attempt, string reason, double delaySeconds);
}
