using System.Net;
using System.Text.Json;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Infrastructure.Queue;
using BildiriKitabi.IntegrationTests.Api;
using RabbitMQ.Client;

namespace BildiriKitabi.IntegrationTests.Queue;

/// <summary>The whole API on SQL Server and RabbitMQ containers with <c>Queue:Provider=RabbitMq</c>.</summary>
public sealed class RabbitMqApiTests(SqlServerFixture sql, RabbitMqFixture rabbit) : IAsyncDisposable
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
    public async Task A_book_is_generated_through_rabbitmq_and_a_duplicate_message_does_not_generate_it_again()
    {
        sql.EnsureAvailable();
        rabbit.EnsureAvailable();
        var options = rabbit.Options(RabbitMqFixture.NewQueueName());
        var api = Host(options);
        var book = await api.CreateSampleBookAsync();

        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        var completed = await api.WaitForFinalStatusAsync(book.Id);
        completed.Status.ShouldBe(BookStatus.Completed, completed.Error?.Message);
        using var client = api.Client();
        var pdf = await client.GetByteArrayAsync(new Uri(completed.PdfUrl!, UriKind.Relative), TestContext.Current.CancellationToken);
        pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8).ShouldBeTrue();
        var before = await StoredResultAsync(api, book.Id);

        // At-least-once delivery: the same book id arrives again.
        var connection = await rabbit.RawConnectionAsync();
        _disposables.Add(connection);
        var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        _disposables.Add(channel);
        await channel.BasicPublishAsync(string.Empty, options.QueueName, GenerationMessage.Serialize(book.Id), TestContext.Current.CancellationToken);
        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.CountsAsync(options.QueueName) == (0, 0), "ikinci mesaj işlenip onaylandı");

        (await StoredResultAsync(api, book.Id)).ShouldBe(before);
        (await rabbit.CountsAsync(options.DeadLetterQueue)).Ready.ShouldBe(0);

        var health = await HealthAsync(api);
        health.Status.ShouldBe(HttpStatusCode.OK);
        health.Checks["rabbitmq"].ShouldBe("Healthy");
    }

    [Fact]
    public async Task A_book_queued_while_the_broker_is_down_is_generated_after_it_comes_back()
    {
        sql.EnsureAvailable();
        rabbit.EnsureAvailable();
        var port = RabbitMqFixture.FreePort();
        var broker = await RabbitMqFixture.StartDedicatedAsync(port);
        _disposables.Add(broker);
        var options = new RabbitMqOptions
        {
            Host = broker.Hostname,
            Port = port,
            Username = RabbitMqFixture.Username,
            Password = RabbitMqFixture.Password,
            QueueName = RabbitMqFixture.NewQueueName(),
            PublishConfirmTimeoutSeconds = 3,
        };
        var api = Host(options, new Dictionary<string, string>
        {
            ["Generation:SweepIntervalSeconds"] = "1",
            ["Generation:RequeueStaleAfterSeconds"] = "2",
        });
        var book = await api.CreateSampleBookAsync();
        await RabbitMqFixture.EventuallyAsync(async () => (await HealthAsync(api)).Status == HttpStatusCode.OK, "broker bağlantısı");

        await broker.StopAsync(TestContext.Current.CancellationToken);
        await RabbitMqFixture.EventuallyAsync(async () => (await HealthAsync(api)).Status == HttpStatusCode.ServiceUnavailable, "broker durunca Unhealthy");
        (await HealthAsync(api)).Checks["rabbitmq"].ShouldBe("Unhealthy");
        (await HealthAsync(api)).Checks["database"].ShouldBe("Healthy");

        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        (await api.QueryAsync("SELECT Durum FROM Kitaplar WHERE Uid = @id", r => r.GetString(0), ("@id", book.Id))).ShouldBe(["Queued"]);

        await broker.StartAsync(TestContext.Current.CancellationToken);
        var completed = await api.WaitForFinalStatusAsync(book.Id, timeoutSeconds: 120);

        completed.Status.ShouldBe(BookStatus.Completed, completed.Error?.Message);
        (await HealthAsync(api)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_health_report_has_no_rabbitmq_check_with_the_in_memory_queue()
    {
        sql.EnsureAvailable();
        var api = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot());
        _disposables.Add(api);

        var health = await HealthAsync(api);

        health.Status.ShouldBe(HttpStatusCode.OK);
        health.Checks.Keys.ShouldBe(["database"]);
    }

    private ApiHost Host(RabbitMqOptions options, Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string>
        {
            ["Queue:Provider"] = "RabbitMq",
            ["RabbitMq:Host"] = options.Host,
            ["RabbitMq:Port"] = options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = options.Username,
            ["RabbitMq:Password"] = options.Password,
            ["RabbitMq:QueueName"] = options.QueueName,
            ["RabbitMq:PublishConfirmTimeoutSeconds"] = options.PublishConfirmTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        var host = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot(), settings: settings);
        _disposables.Add(host);
        return host;
    }

    private static async Task<(int Pages, DateTime FinishedAt)> StoredResultAsync(ApiHost api, Guid id) =>
        (await api.QueryAsync(
            "SELECT SayfaSayisi, IslemBitisZamani FROM Kitaplar WHERE Uid = @id",
            r => (r.GetInt32(0), r.GetDateTime(1)),
            ("@id", id))).Single();

    private static async Task<(HttpStatusCode Status, Dictionary<string, string> Checks)> HealthAsync(ApiHost api)
    {
        using var client = api.Client();
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var checks = json.RootElement.GetProperty("checks").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetProperty("status").GetString() ?? string.Empty);
        return (response.StatusCode, checks);
    }
}
