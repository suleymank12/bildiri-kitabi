using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>
/// The single RabbitMQ connection of the process. It is opened in the background with exponential back-off
/// (1, 2, 4 … at most 30 s) so the application starts even when the broker is down; once open, the client's
/// automatic connection and topology recovery keeps it alive. The durable topology is declared on every
/// (re)connect and is idempotent:
/// queue <c>{name}</c> (dead-letters to <c>{name}.dlx</c>) and fanout exchange <c>{name}.dlx</c> → queue <c>{name}.dlq</c>.
/// </summary>
public sealed partial class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly RabbitMqOptions _options = options.Value;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<IConnection> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _startLock = new();
    private Task? _connecting;
    private IConnection? _connection;

    public RabbitMqOptions Options => _options;

    /// <summary>The open connection, or null while the broker cannot be reached.</summary>
    public IConnection? Current => _connection is { IsOpen: true } connection ? connection : null;

    /// <summary>Starts connecting in the background (once); returns immediately.</summary>
    public void EnsureStarted()
    {
        lock (_startLock)
        {
            _connecting ??= Task.Run(() => ConnectWithRetryAsync(_stopping.Token));
        }
    }

    /// <summary>Waits until the first connection is open.</summary>
    public Task<IConnection> WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        return _connected.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_connecting is not null)
        {
            try
            {
                await _connecting.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopped while still trying to connect.
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogCloseFailed(logger, ex);
            }

            _connection.Dispose();
        }

        _connected.TrySetCanceled();
        _stopping.Dispose();
    }

    private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            VirtualHost = _options.VirtualHost,
            UserName = _options.Username,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = "bildiri-kitabi-api",
        };

        var delay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                await DeclareTopologyAsync(connection, cancellationToken).ConfigureAwait(false);
                connection.RecoverySucceededAsync += async (_, _) =>
                {
                    // Topology recovery replays what this connection declared; declaring again keeps it certain.
                    await DeclareTopologyAsync(connection, CancellationToken.None).ConfigureAwait(false);
                    LogRecovered(logger);
                };
                _connection = connection;
                _connected.TrySetResult(connection);
                LogConnected(logger, _options.Host, _options.Port);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogConnectFailed(logger, ex, _options.Host, _options.Port, delay.TotalSeconds);
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    private async Task DeclareTopologyAsync(IConnection connection, CancellationToken cancellationToken)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (channel.ConfigureAwait(false))
        {
            await channel.ExchangeDeclareAsync(
                _options.DeadLetterExchange,
                ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueDeclareAsync(
                _options.DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(_options.DeadLetterQueue, _options.DeadLetterExchange, string.Empty, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await channel.QueueDeclareAsync(
                _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = _options.DeadLetterExchange },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ at {Host}:{Port}")]
    private static partial void LogConnected(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection recovered")]
    private static partial void LogRecovered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ at {Host}:{Port} is not reachable; retrying in {DelaySeconds} s")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception, string host, int port, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ connection did not close cleanly")]
    private static partial void LogCloseFailed(ILogger logger, Exception exception);
}
