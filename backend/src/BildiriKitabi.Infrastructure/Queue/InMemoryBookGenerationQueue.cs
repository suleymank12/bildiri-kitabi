using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>
/// In-process queue on a bounded channel; a full queue makes the producer wait. Messages are lost when the process
/// stops, which is safe because the startup recovery and the queued-book sweeper enqueue every waiting book again.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "It is a queue; see IBookGenerationQueue.")]
public sealed partial class InMemoryBookGenerationQueue(
    IOptions<GenerationOptions> options,
    ILogger<InMemoryBookGenerationQueue> logger) : IBookGenerationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    public ValueTask EnqueueAsync(Guid bookUid, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(bookUid, cancellationToken);

    public async Task ConsumeAsync(Func<Guid, CancellationToken, Task> handler, int maxConcurrency, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = stoppingToken };
        try
        {
            await Parallel.ForEachAsync(_channel.Reader.ReadAllAsync(stoppingToken), parallel, async (bookUid, cancellationToken) =>
            {
                try
                {
                    await handler(bookUid, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Shutting down; the book is recovered on the next start.
                }
                catch (Exception ex)
                {
                    // There is no broker to hand the message back to: log it and keep consuming.
                    LogHandlerFailed(logger, ex, bookUid);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation job for book {BookUid} crashed")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, Guid bookUid);
}
