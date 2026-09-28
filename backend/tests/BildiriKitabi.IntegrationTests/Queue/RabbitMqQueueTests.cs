using System.Text;
using BildiriKitabi.Infrastructure.Queue;
using RabbitMQ.Client;

namespace BildiriKitabi.IntegrationTests.Queue;

/// <summary>The RabbitMQ provider against a real broker: topology, publishing, acknowledgement and dead-lettering.</summary>
public sealed class RabbitMqQueueTests(RabbitMqFixture rabbit) : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            await _disposables[i].DisposeAsync();
        }
    }

    [Fact]
    public async Task The_durable_topology_with_dead_lettering_is_declared_on_connect()
    {
        var (_, _, name) = await StartAsync();

        // name → (durable, arguments); RabbitMQ 4 adds its own x-queue-type argument, so only ours is checked.
        var queues = (await rabbit.CtlAsync("list_queues", "--no-table-headers", "name", "durable", "arguments"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .ToDictionary(parts => parts[0], parts => (Durable: parts[1], Arguments: parts[2]));
        queues[name].Durable.ShouldBe("true");
        queues[name].Arguments.ShouldContain($"{{\"x-dead-letter-exchange\",\"{name}.dlx\"}}");
        queues[$"{name}.dlq"].Durable.ShouldBe("true");
        var exchanges = await rabbit.CtlAsync("list_exchanges", "--no-table-headers", "name", "type", "durable");
        exchanges.ShouldContain($"{name}.dlx\tfanout\ttrue");
        var bindings = await rabbit.CtlAsync("list_bindings", "--no-table-headers", "source_name", "destination_name");
        bindings.ShouldContain($"{name}.dlx\t{name}.dlq");
    }

    [Fact]
    public async Task A_published_message_is_persistent_json_with_the_book_as_correlation_id()
    {
        var (_, queue, name) = await StartAsync();
        var bookUid = Guid.NewGuid();

        await queue.EnqueueAsync(bookUid, TestContext.Current.CancellationToken);

        var raw = await RawChannelAsync();
        var message = await raw.BasicGetAsync(name, autoAck: true, TestContext.Current.CancellationToken);
        message.ShouldNotBeNull();
        message.BasicProperties.Persistent.ShouldBeTrue();
        message.BasicProperties.ContentType.ShouldBe("application/json");
        message.BasicProperties.CorrelationId.ShouldBe(bookUid.ToString("D"));
        Guid.TryParse(message.BasicProperties.MessageId, out _).ShouldBeTrue();
        Encoding.UTF8.GetString(message.Body.Span).ShouldBe($$"""{"bookUid":"{{bookUid:D}}","version":2}""");
    }

    [Fact]
    public async Task The_message_is_acknowledged_only_after_the_handler_returned()
    {
        var (_, queue, name) = await StartAsync();
        var bookUid = Guid.NewGuid();
        var started = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopping = new CancellationTokenSource();
        var consuming = queue.ConsumeAsync(
            async (uid, _) =>
            {
                started.TrySetResult(uid);
                await release.Task;
            },
            2,
            stopping.Token);

        await queue.EnqueueAsync(bookUid, TestContext.Current.CancellationToken);
        (await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).ShouldBe(bookUid);

        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.CountsAsync(name) == (0, 1), "handler çalışırken 1 onaysız mesaj");
        release.SetResult();
        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.CountsAsync(name) == (0, 0), "handler bitince onay");

        await stopping.CancelAsync();
        await consuming;
    }

    [Fact]
    public async Task A_handler_exception_sends_the_message_to_the_dead_letter_queue()
    {
        var (_, queue, name) = await StartAsync();
        using var stopping = new CancellationTokenSource();
        var consuming = queue.ConsumeAsync((_, _) => throw new InvalidOperationException("Beklenmeyen hata"), 1, stopping.Token);

        await queue.EnqueueAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await RabbitMqFixture.EventuallyAsync(async () => (await rabbit.CountsAsync($"{name}.dlq")).Ready == 1, "DLQ'da 1 mesaj");
        (await rabbit.CountsAsync(name)).ShouldBe((0, 0));
        await stopping.CancelAsync();
        await consuming;
    }

    [Fact]
    public async Task An_invalid_body_goes_to_the_dead_letter_queue_without_calling_the_handler()
    {
        var (_, queue, name) = await StartAsync();
        var calls = 0;
        using var stopping = new CancellationTokenSource();
        var consuming = queue.ConsumeAsync(
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            },
            1,
            stopping.Token);

        var raw = await RawChannelAsync();
        await raw.BasicPublishAsync(string.Empty, name, Encoding.UTF8.GetBytes("bu bir JSON değil"), TestContext.Current.CancellationToken);

        await RabbitMqFixture.EventuallyAsync(async () => (await rabbit.CountsAsync($"{name}.dlq")).Ready == 1, "DLQ'da geçersiz mesaj");
        calls.ShouldBe(0);
        await stopping.CancelAsync();
        await consuming;
    }

    [Fact]
    public async Task A_message_left_unacknowledged_by_a_stopped_consumer_is_delivered_to_the_next_one()
    {
        var (_, queue, name) = await StartAsync();
        var bookUid = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var stoppingFirst = new CancellationTokenSource())
        {
            var first = queue.ConsumeAsync(
                async (_, token) =>
                {
                    firstStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                },
                1,
                stoppingFirst.Token);

            await queue.EnqueueAsync(bookUid, TestContext.Current.CancellationToken);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await stoppingFirst.CancelAsync();
            await first;
        }

        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.CountsAsync(name) == (1, 0), "onaysız mesaj kuyruğa geri döndü");

        var handled = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stoppingSecond = new CancellationTokenSource();
        var second = queue.ConsumeAsync(
            (uid, _) =>
            {
                handled.TrySetResult(uid);
                return Task.CompletedTask;
            },
            1,
            stoppingSecond.Token);

        (await handled.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).ShouldBe(bookUid);
        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.CountsAsync(name) == (0, 0), "ikinci tüketici onayladı");
        await stoppingSecond.CancelAsync();
        await second;
    }

    private async Task<(RabbitMqConnection Connection, RabbitMqBookGenerationQueue Queue, string Name)> StartAsync()
    {
        rabbit.EnsureAvailable();
        var name = RabbitMqFixture.NewQueueName();
        var connection = RabbitMqFixture.Connection(rabbit.Options(name));
        var queue = RabbitMqFixture.Queue(connection);
        _disposables.Add(connection);
        _disposables.Add(queue);
        await connection.WaitForConnectionAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30));
        return (connection, queue, name);
    }

    private async Task<IChannel> RawChannelAsync()
    {
        var connection = await rabbit.RawConnectionAsync();
        var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        _disposables.Add(connection);
        _disposables.Add(channel);
        return channel;
    }
}
