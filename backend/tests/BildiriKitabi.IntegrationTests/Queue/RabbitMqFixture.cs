using System.Globalization;
using System.Net;
using System.Net.Sockets;
using BildiriKitabi.Infrastructure.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(BildiriKitabi.IntegrationTests.Queue.RabbitMqFixture))]

namespace BildiriKitabi.IntegrationTests.Queue;

/// <summary>
/// One real RabbitMQ broker (Testcontainers) for the run; every test uses its own queue name. Without Docker the
/// RabbitMQ tests are skipped with a clear message instead of failing.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public const string Image = "rabbitmq:4-management";
    public const string Username = "kitap";
    public const string Password = "test-parolasi";

    private RabbitMqContainer? _container;
    private string? _unavailableReason;

    public RabbitMqContainer Container => _container ?? throw new InvalidOperationException(_unavailableReason);

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = NewBuilder().Build();
            await _container.StartAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _unavailableReason =
                $"RabbitMQ testleri atlandı: Docker veya RabbitMQ konteyneri ({Image}) kullanılamıyor. Ayrıntı: {ex.GetType().Name}: {ex.Message}";
            _container = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public void EnsureAvailable()
    {
        if (_unavailableReason is not null)
        {
            Assert.Skip(_unavailableReason);
        }
    }

    public static RabbitMqBuilder NewBuilder() => new RabbitMqBuilder(Image).WithUsername(Username).WithPassword(Password);

    /// <summary>A broker of its own on a fixed host port, so it can be stopped and started again at the same address.</summary>
    public static async Task<RabbitMqContainer> StartDedicatedAsync(int hostPort)
    {
        var container = NewBuilder().WithPortBinding(hostPort, 5672).Build();
        await container.StartAsync();
        return container;
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public RabbitMqOptions Options(string queueName) => new()
    {
        Host = Container.Hostname,
        Port = Container.GetMappedPublicPort(5672),
        Username = Username,
        Password = Password,
        QueueName = queueName,
        PublishConfirmTimeoutSeconds = 5,
    };

    public static string NewQueueName() => $"test.{Guid.NewGuid():N}";

    public static RabbitMqConnection Connection(RabbitMqOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, NullLogger<RabbitMqConnection>.Instance);

    public static RabbitMqBookGenerationQueue Queue(RabbitMqConnection connection) =>
        new(connection, NullLogger<RabbitMqBookGenerationQueue>.Instance);

    /// <summary>A plain client connection for looking at the broker from the outside.</summary>
    public Task<IConnection> RawConnectionAsync() => new ConnectionFactory
    {
        HostName = Container.Hostname,
        Port = Container.GetMappedPublicPort(5672),
        UserName = Username,
        Password = Password,
    }.CreateConnectionAsync(TestContext.Current.CancellationToken);

    /// <summary>Ready and unacknowledged message counts straight from the broker (no statistics delay).</summary>
    public async Task<(int Ready, int Unacked)> CountsAsync(string queue)
    {
        var result = await Container.ExecAsync(
            ["rabbitmqctl", "list_queues", "-q", "--no-table-headers", "name", "messages_ready", "messages_unacknowledged"],
            TestContext.Current.CancellationToken);
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length == 3 && parts[0] == queue)
            {
                return (int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture));
            }
        }

        throw new InvalidOperationException($"Queue {queue} not found: {result.Stdout} {result.Stderr}");
    }

    public async Task<string> CtlAsync(params string[] arguments)
    {
        var result = await Container.ExecAsync(["rabbitmqctl", "-q", .. arguments], TestContext.Current.CancellationToken);
        return result.Stdout;
    }

    /// <summary>Polls until the condition holds or the time limit passes.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, string what, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{what}: {timeoutSeconds} sn içinde gerçekleşmedi.");
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}
