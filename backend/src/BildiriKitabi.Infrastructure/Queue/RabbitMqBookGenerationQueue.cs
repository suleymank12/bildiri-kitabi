using System.Diagnostics.CodeAnalysis;
using BildiriKitabi.Core.Books;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>
/// Generation queue on RabbitMQ. Publishing: persistent JSON message to the work queue through the default exchange,
/// on a dedicated channel with publisher confirms; <see cref="EnqueueAsync"/> returns once the broker confirmed.
/// Consuming: manual acknowledgement, prefetch = concurrency; a message is acknowledged only after the handler
/// returned, rejected to the dead-letter queue when the handler throws or the body is invalid, and left
/// unacknowledged (so it is delivered again) when the application stops mid-job.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "It is a queue; see IBookGenerationQueue.")]
public sealed partial class RabbitMqBookGenerationQueue(
    RabbitMqConnection connection,
    ILogger<RabbitMqBookGenerationQueue> logger) : IBookGenerationQueue, IAsyncDisposable
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private IChannel? _publishChannel;

    public async ValueTask EnqueueAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        connection.EnsureStarted();
        var current = connection.Current
            ?? throw new InvalidOperationException("RabbitMQ is not connected; the book stays queued and is sent by the sweeper.");

        var options = connection.Options;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.PublishConfirmTimeoutSeconds));

        await _publishLock.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            if (_publishChannel is not { IsOpen: true })
            {
                if (_publishChannel is not null)
                {
                    await _publishChannel.DisposeAsync().ConfigureAwait(false);
                }

                _publishChannel = await current.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    timeout.Token).ConfigureAwait(false);
            }

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = GenerationMessage.ContentType,
                MessageId = Guid.NewGuid().ToString("D"),
                CorrelationId = bookId.ToString("D"),
            };

            // With confirmation tracking the call completes only when the broker has taken the message (or throws).
            await _publishChannel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: options.QueueName,
                mandatory: true,
                basicProperties: properties,
                body: GenerationMessage.Serialize(bookId),
                cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async Task ConsumeAsync(Func<Guid, CancellationToken, Task> handler, int maxConcurrency, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        IConnection current;
        try
        {
            current = await connection.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var concurrency = (ushort)Math.Min(maxConcurrency, ushort.MaxValue);
        var channel = await current.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: concurrency),
            stoppingToken).ConfigureAwait(false);
        await using (channel.ConfigureAwait(false))
        {
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: concurrency, global: false, stoppingToken).ConfigureAwait(false);

            var inFlight = new InFlight();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                inFlight.Enter();
                try
                {
                    await HandleDeliveryAsync(channel, delivery, handler, stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    inFlight.Exit();
                }
            };

            var consumerTag = await channel.BasicConsumeAsync(connection.Options.QueueName, autoAck: false, consumer, stoppingToken)
                .ConfigureAwait(false);
            LogConsuming(logger, connection.Options.QueueName, concurrency);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopping: take no new messages, let running jobs see the cancellation, acknowledge nothing more.
            }

            try
            {
                await channel.BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                await inFlight.WaitIdleAsync(DrainTimeout).ConfigureAwait(false);
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The channel may already be gone (broker down); unacknowledged messages are redelivered either way.
                LogStopFailed(logger, ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_publishChannel is not null)
        {
            try
            {
                await _publishChannel.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogStopFailed(logger, ex);
            }

            await _publishChannel.DisposeAsync().ConfigureAwait(false);
        }

        _publishLock.Dispose();
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        Func<Guid, CancellationToken, Task> handler,
        CancellationToken stoppingToken)
    {
        if (!GenerationMessage.TryParse(delivery.Body.Span, out var bookId))
        {
            LogInvalidMessage(logger, delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            await handler(bookId, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogHandlerFailed(logger, ex, bookId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // A job interrupted by shutdown returns normally; without an ack the broker hands the message out again.
        // Acknowledgements use no token: once the job is done, the ack must not be skipped by a late cancellation.
        if (!stoppingToken.IsCancellationRequested)
        {
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Counts running deliveries so the channel is closed only after they finished.</summary>
    private sealed class InFlight
    {
        private int _count;

        public void Enter() => Interlocked.Increment(ref _count);

        public void Exit() => Interlocked.Decrement(ref _count);

        public async Task WaitIdleAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Volatile.Read(ref _count) > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming RabbitMQ queue {Queue} with {Concurrency} parallel job(s)")]
    private static partial void LogConsuming(ILogger logger, string queue, int concurrency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} has an invalid body; sent to the dead-letter queue")]
    private static partial void LogInvalidMessage(ILogger logger, string? messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation job for book {BookId} crashed; message sent to the dead-letter queue")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, Guid bookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ consumer did not stop cleanly")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);
}
