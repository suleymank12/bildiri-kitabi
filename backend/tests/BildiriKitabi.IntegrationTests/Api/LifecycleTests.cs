using System.Net;
using System.Net.Http.Json;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.IntegrationTests.Books;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BildiriKitabi.IntegrationTests.Api;

public sealed class LifecycleTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly List<ApiHost> _hosts = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
            SchemaAndUploadTests.TryDelete(host.DataRoot);
        }
    }

    [Fact]
    public async Task A_renderer_failure_marks_the_book_failed_and_a_retry_completes_it()
    {
        var renderer = new SwitchableRenderer { Fail = true };
        var api = Host(services =>
        {
            services.RemoveAll<IBookRenderer>();
            services.AddSingleton<IBookRenderer>(renderer);
        });
        var book = await api.CreateSampleBookAsync();

        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        var failed = await api.WaitForFinalStatusAsync(book.Id);

        failed.Status.ShouldBe(BookStatus.Failed);
        failed.Error.ShouldBe(new BookErrorDto(BookErrorCodes.InternalError, BookGenerationHandler.UnexpectedErrorMessage));
        failed.PdfUrl.ShouldBeNull();
        var row = await api.QueryAsync(
            "SELECT Durum, HataMesaji, HataKodu FROM Kitaplar WHERE Id = @id",
            r => (Status: r.GetString(0), Message: r.IsDBNull(1) ? null : r.GetString(1), Code: r.GetString(2)),
            ("@id", book.Id));
        row.ShouldHaveSingleItem().Status.ShouldBe("Failed");
        row[0].Message.ShouldNotBeNullOrWhiteSpace();
        row[0].Code.ShouldBe("INTERNAL_ERROR");
        row[0].Message!.ShouldNotContain("Sahte");

        renderer.Fail = false;
        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        var completed = await api.WaitForFinalStatusAsync(book.Id);

        completed.Status.ShouldBe(BookStatus.Completed);
        completed.Error.ShouldBeNull();
        (await api.QueryAsync("SELECT HataMesaji FROM Kitaplar WHERE Id = @id", r => r.IsDBNull(0), ("@id", book.Id))).ShouldBe([true]);
    }

    [Fact]
    public async Task Reordered_papers_are_generated_in_the_new_order()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        var reversed = book.Papers.Select(p => p.Id).Reverse().ToList();
        using var client = api.Client();

        using (var invalid = await client.PutAsJsonAsync(
            new Uri($"/api/books/{book.Id}/paper-order", UriKind.Relative),
            new PaperOrderRequest(reversed.Take(9).ToList()),
            ApiHost.Json,
            TestContext.Current.CancellationToken))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("PAPER_ORDER_INVALID");
        }

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/books/{book.Id}/paper-order", UriKind.Relative),
            new PaperOrderRequest(reversed),
            ApiHost.Json,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reordered = (await response.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
        reordered.Papers.Select(p => p.Id).ShouldBe(reversed);
        reordered.Papers.Select(p => p.Order).ShouldBe(Enumerable.Range(1, 10));

        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        var completed = await api.WaitForFinalStatusAsync(book.Id);
        completed.Status.ShouldBe(BookStatus.Completed);
        completed.Papers[0].FileName.ShouldBe("10_Muze_Deneyimi.docx");
        completed.Papers.Select(p => p.StartPage).ShouldBe([3, 5, 7, 9, 11, 13, 15, 17, 19, 21]);

        var pdf = new BookPdf(await client.GetByteArrayAsync(new Uri(completed.PdfUrl!, UriKind.Relative), TestContext.Current.CancellationToken));
        var contents = pdf.Page(2).BodyText;
        var positions = completed.Papers.Select(p => contents.IndexOf(p.Title.Split(' ')[0] + " " + p.Title.Split(' ')[1], StringComparison.Ordinal)).ToList();
        positions.ShouldAllBe(p => p >= 0);
        positions.ShouldBeInOrder();
        pdf.Page(3).BodyText.ShouldStartWith(completed.Papers[0].Title);

        using var locked = await client.PutAsJsonAsync(
            new Uri($"/api/books/{book.Id}/paper-order", UriKind.Relative),
            new PaperOrderRequest(book.Papers.Select(p => p.Id).ToList()),
            ApiHost.Json,
            TestContext.Current.CancellationToken);
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Startup_recovery_processes_queued_and_interrupted_books()
    {
        var connectionString = sql.NewDatabase();
        var dataRoot = ApiHost.NewDataRoot();
        var first = Host(connectionString: connectionString, dataRoot: dataRoot);
        var queued = await first.CreateSampleBookAsync("Kuyrukta Kalan Kitap");
        var interrupted = await first.CreateSampleBookAsync("Yarıda Kalan Kitap");
        await first.ExecuteAsync("UPDATE Kitaplar SET Durum = 'Queued' WHERE Id = @id", ("@id", queued.Id));
        await first.ExecuteAsync(
            "UPDATE Kitaplar SET Durum = 'Processing', Asama = 'Rendering', IlerlemeYuzdesi = 45, IslemBaslangicZamani = SYSUTCDATETIME() WHERE Id = @id",
            ("@id", interrupted.Id));
        await first.DisposeAsync();
        _hosts.Remove(first);

        var restarted = Host(connectionString: connectionString, dataRoot: dataRoot);
        using (restarted.Client())
        {
            (await restarted.WaitForFinalStatusAsync(queued.Id)).Status.ShouldBe(BookStatus.Completed);
            (await restarted.WaitForFinalStatusAsync(interrupted.Id)).Status.ShouldBe(BookStatus.Completed);
        }
    }

    [Fact]
    public async Task Delete_removes_the_book_papers_and_files_but_not_while_it_is_processing()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        var other = await api.CreateSampleBookAsync("Kalacak Kitap");
        using var client = api.Client();
        var uri = new Uri($"/api/books/{book.Id}", UriKind.Relative);

        await api.ExecuteAsync("UPDATE Kitaplar SET Durum = 'Processing' WHERE Id = @id", ("@id", book.Id));
        using (var busy = await client.DeleteAsync(uri, TestContext.Current.CancellationToken))
        {
            busy.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        api.StoredFiles(book.Id).Count.ShouldBe(10);
        await api.ExecuteAsync("UPDATE Kitaplar SET Durum = 'Uploaded' WHERE Id = @id", ("@id", book.Id));
        using (var deleted = await client.DeleteAsync(uri, TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await api.QueryAsync("SELECT COUNT(*) FROM Kitaplar WHERE Id = @id", r => r.GetInt32(0), ("@id", book.Id))).ShouldBe([0]);
        (await api.QueryAsync("SELECT COUNT(*) FROM Bildiriler WHERE KitapId = @id", r => r.GetInt32(0), ("@id", book.Id))).ShouldBe([0]);
        api.StoredFiles(book.Id).ShouldBeEmpty();
        Directory.Exists(Path.Combine(api.StorageRoot, "books", book.Id.ToString("D"))).ShouldBeFalse();
        api.StoredFiles(other.Id).Count.ShouldBe(10);
        (await api.QueryAsync("SELECT COUNT(*) FROM Bildiriler WHERE KitapId = @id", r => r.GetInt32(0), ("@id", other.Id))).ShouldBe([10]);

        using var again = await client.DeleteAsync(uri, TestContext.Current.CancellationToken);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_generation_that_exceeds_the_time_limit_fails_with_a_timeout_code()
    {
        var api = Host(
            services =>
            {
                services.RemoveAll<IBookRenderer>();
                services.AddSingleton<IBookRenderer>(new SlowRenderer(TimeSpan.FromSeconds(4)));
            },
            settings: new Dictionary<string, string> { ["Generation:TimeoutSeconds"] = "1" });
        var book = await api.CreateSampleBookAsync();

        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        var failed = await api.WaitForFinalStatusAsync(book.Id);

        failed.Status.ShouldBe(BookStatus.Failed);
        failed.Error!.Code.ShouldBe(BookErrorCodes.GenerationTimeout);
        failed.Error.Message.ShouldContain("1 saniyelik süre sınırını aştı");
    }

    [Fact]
    public async Task Too_many_uploads_from_one_client_get_a_429_problem()
    {
        var api = Host(settings: new Dictionary<string, string> { ["RateLimiting:UploadPermitsPerMinute"] = "1" });
        using var client = api.Client();

        using (var first = new MultipartFormDataContent { { new StringContent("Deneme"), "name" } })
        using (var response = await client.PostAsync(new Uri("/api/books", UriKind.Relative), first, TestContext.Current.CancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var second = new MultipartFormDataContent { { new StringContent("Deneme"), "name" } };
        using var limited = await client.PostAsync(new Uri("/api/books", UriKind.Relative), second, TestContext.Current.CancellationToken);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await limited.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("RATE_LIMITED");
        body.ShouldContain("bir dakika bekleyip");
    }

    private ApiHost Host(
        Action<IServiceCollection>? configureServices = null,
        string? connectionString = null,
        string? dataRoot = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var host = new ApiHost(connectionString ?? sql.NewDatabase(), dataRoot ?? ApiHost.NewDataRoot(), configureServices, settings);
        _hosts.Add(host);
        return host;
    }

    /// <summary>Takes longer than the configured time limit before rendering.</summary>
    private sealed class SlowRenderer(TimeSpan delay) : IBookRenderer
    {
        private readonly QuestPdfBookRenderer _inner = new();

        public RenderedBook Render(BookContent book)
        {
            Thread.Sleep(delay);
            return _inner.Render(book);
        }
    }

    /// <summary>The real renderer, or a failure while <see cref="Fail"/> is set.</summary>
    private sealed class SwitchableRenderer : IBookRenderer
    {
        private readonly QuestPdfBookRenderer _inner = new();
        private volatile bool _fail;

        public bool Fail
        {
            get => _fail;
            set => _fail = value;
        }

        public RenderedBook Render(BookContent book) =>
            Fail ? throw new InvalidOperationException("Sahte dizgi hatası.") : _inner.Render(book);
    }
}
