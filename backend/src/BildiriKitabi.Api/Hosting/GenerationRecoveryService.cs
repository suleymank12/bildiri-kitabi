using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BildiriKitabi.Api.Hosting;

/// <summary>
/// Startup recovery, registered before the worker: books left <c>Processing</c> by a stopped process go back to
/// <c>Queued</c>, then every queued book is enqueued again, oldest first. Safe because generation is idempotent.
/// Enqueueing runs in the background so a queue smaller than the backlog cannot block startup.
/// </summary>
public sealed partial class GenerationRecoveryService(
    IServiceScopeFactory scopeFactory,
    IBookGenerationQueue queue,
    TimeProvider timeProvider,
    ILogger<GenerationRecoveryService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _enqueueing = Task.CompletedTask;
    private bool _disposed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        List<Guid> queued;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var interrupted = await db.Books.Where(b => b.Status == BookStatus.Processing).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var book in interrupted)
            {
                book.ReturnToQueue(timeProvider.GetUtcNow().UtcDateTime);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            queued = await db.Books.AsNoTracking()
                .Where(b => b.Status == BookStatus.Queued)
                .OrderBy(b => b.CreatedAt)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            LogRecovered(logger, interrupted.Count, queued.Count);
        }

        _enqueueing = EnqueueAsync(queued, _stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // A host that is disposed and then stopped again (test hosts do this) must not fail here.
        if (_disposed)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _enqueueing.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping; the rest stays Queued in the database.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _stopping.Dispose();
    }

    private async Task EnqueueAsync(List<Guid> bookIds, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            foreach (var id in bookIds)
            {
                await queue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The broker may not be reachable yet; the queued-book sweeper sends these books later.
            LogEnqueueFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup recovery could not enqueue every queued book; the sweeper will retry")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup recovery: {InterruptedCount} interrupted book(s) returned to the queue, {QueuedCount} queued book(s) enqueued")]
    private static partial void LogRecovered(ILogger logger, int interruptedCount, int queuedCount);
}
