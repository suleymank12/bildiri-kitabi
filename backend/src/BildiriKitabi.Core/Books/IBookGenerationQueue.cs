using System.Diagnostics.CodeAnalysis;

namespace BildiriKitabi.Core.Books;

/// <summary>
/// Carries book ids to the background worker: the external <see cref="Book.Uid"/>, never the database id. A message
/// holds nothing but that uid; the database stays the single
/// source of truth, so a message may be delivered more than once (at-least-once brokers such as RabbitMQ).
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "It is a queue; the name is part of the documented design.")]
public interface IBookGenerationQueue
{
    ValueTask EnqueueAsync(Guid bookUid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <paramref name="handler"/> for each message, at most <paramref name="maxConcurrency"/> at a time, until
    /// <paramref name="stoppingToken"/> is cancelled. A message is acknowledged only after the handler returned; one
    /// that is still being handled when the application stops is not acknowledged, so a broker delivers it again.
    /// </summary>
    Task ConsumeAsync(Func<Guid, CancellationToken, Task> handler, int maxConcurrency, CancellationToken stoppingToken);
}
