using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Api.Hosting;

/// <summary>
/// Reads book ids from the queue and runs up to <c>Generation:MaxConcurrency</c> jobs at once, each in its own
/// DI scope (its own DbContext).
/// </summary>
public sealed partial class BookGenerationWorker(
    IBookGenerationQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<GenerationOptions> options,
    ILogger<BookGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.MaxConcurrency,
            CancellationToken = stoppingToken,
        };

        try
        {
            await Parallel.ForEachAsync(queue.ReadAllAsync(stoppingToken), parallel, async (bookId, cancellationToken) =>
            {
                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using (scope.ConfigureAwait(false))
                    {
                        var handler = scope.ServiceProvider.GetRequiredService<BookGenerationHandler>();
                        await handler.HandleAsync(bookId, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Shutting down; the book is recovered on the next start.
                }
                catch (Exception ex)
                {
                    // Keep the worker alive whatever one job does.
                    LogJobCrashed(logger, ex, bookId);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation job for book {BookId} crashed")]
    private static partial void LogJobCrashed(ILogger logger, Exception exception, Guid bookId);
}
