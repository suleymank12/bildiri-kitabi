using System.Diagnostics.CodeAnalysis;

namespace BildiriKitabi.Core.Books;

/// <summary>
/// Carries book ids to the background worker. A message holds nothing but the id; the database stays the single
/// source of truth, so a message may be delivered more than once (at-least-once brokers such as RabbitMQ).
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "It is a queue; the name is part of the documented design.")]
public interface IBookGenerationQueue
{
    ValueTask EnqueueAsync(Guid bookId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default);
}
