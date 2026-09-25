using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>
/// In-process queue on a bounded channel; a full queue makes the producer wait. Messages are lost when the process
/// stops, which is safe because the startup recovery queues every <c>Queued</c>/<c>Processing</c> book again.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "It is a queue; see IBookGenerationQueue.")]
public sealed class InMemoryBookGenerationQueue(IOptions<GenerationOptions> options) : IBookGenerationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    public ValueTask EnqueueAsync(Guid bookId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(bookId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
