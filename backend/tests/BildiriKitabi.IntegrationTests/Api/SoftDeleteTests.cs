using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>
/// Deleting a book only deactivates it (<c>AktifMi = 0</c>): its row, papers and files stay, every normal endpoint
/// answers 404, it is listed among the deleted books and can be restored.
/// </summary>
public sealed class SoftDeleteTests(SqlServerFixture sql) : IAsyncDisposable
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
    public void Papers_have_no_set_of_their_own_so_every_query_passes_the_book_filter()
    {
        typeof(IAppDbContext).GetProperties().Select(p => p.PropertyType).ShouldNotContain(t => t.IsGenericType && t.GetGenericArguments()[0] == typeof(Paper));
    }

    [Fact]
    public async Task A_deleted_book_is_hidden_keeps_its_rows_and_files_and_comes_back_when_restored()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync("Silinecek Kitap");
        var other = await api.CreateSampleBookAsync("Kalacak Kitap");
        (await api.GenerateAsync(book.Id)).ShouldBe(HttpStatusCode.Accepted);
        var completed = await api.WaitForFinalStatusAsync(book.Id);
        completed.Status.ShouldBe(BookStatus.Completed);
        using var client = api.Client();
        var files = api.StoredFiles(book.Id);
        files.Count.ShouldBe(11);

        using (var deleted = await client.DeleteAsync(BookUri(book.Id), TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // The row and its papers stay, deactivated; the files stay on disk.
        var row = await api.QueryAsync(
            "SELECT AktifMi, SilinmeZamani, Durum, PdfDepolamaAnahtari FROM Kitaplar WHERE Uid = @id",
            r => (Active: r.GetBoolean(0), DeletedAt: r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1), Status: r.GetString(2), Pdf: r.GetString(3)),
            ("@id", book.Id));
        row.ShouldHaveSingleItem().Active.ShouldBeFalse();
        row[0].DeletedAt.ShouldNotBeNull();
        row[0].Status.ShouldBe("Completed");
        (await api.QueryAsync("SELECT COUNT(*) FROM Bildiriler WHERE KitapId = (SELECT Id FROM Kitaplar WHERE Uid = @id)", r => r.GetInt32(0), ("@id", book.Id))).ShouldBe([10]);
        api.StoredFiles(book.Id).ShouldBe(files, ignoreOrder: true);

        // Out of the book list, in the deleted list with its deletion time.
        var list = (await client.GetFromJsonAsync<PagedResult<BookSummaryDto>>(new Uri("/api/books", UriKind.Relative), ApiHost.Json, TestContext.Current.CancellationToken))!;
        list.Items.Select(b => b.Id).ShouldBe([other.Id]);
        list.TotalCount.ShouldBe(1);
        var trash = (await client.GetFromJsonAsync<PagedResult<DeletedBookSummaryDto>>(new Uri("/api/books/deleted", UriKind.Relative), ApiHost.Json, TestContext.Current.CancellationToken))!;
        var item = trash.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(book.Id);
        item.Name.ShouldBe("Silinecek Kitap");
        item.Status.ShouldBe(BookStatus.Completed);
        item.PaperCount.ShouldBe(10);
        item.PageCount.ShouldBe(22);
        item.PdfUrl.ShouldBeNull();
        item.DeletedAt.ShouldBe(row[0].DeletedAt!.Value, TimeSpan.FromMilliseconds(1));
        item.DeletedAt.Kind.ShouldBe(DateTimeKind.Utc);

        // Deleted again or restored twice: not found.
        using (var again = await client.DeleteAsync(BookUri(book.Id), TestContext.Current.CancellationToken))
        {
            await ShouldBeProblemAsync(again, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        using (var restored = await client.PostAsync(new Uri($"/api/books/{book.Id}/restore", UriKind.Relative), null, TestContext.Current.CancellationToken))
        {
            restored.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var twice = await client.PostAsync(new Uri($"/api/books/{book.Id}/restore", UriKind.Relative), null, TestContext.Current.CancellationToken))
        {
            await ShouldBeProblemAsync(twice, HttpStatusCode.NotFound, "DELETED_BOOK_NOT_FOUND");
        }

        // Back in the list as it was, and its PDF downloads again.
        (await api.QueryAsync("SELECT AktifMi, SilinmeZamani FROM Kitaplar WHERE Uid = @id", r => (r.GetBoolean(0), r.IsDBNull(1)), ("@id", book.Id))).ShouldBe([(true, true)]);
        var back = await api.GetBookAsync(book.Id);
        back.Status.ShouldBe(BookStatus.Completed);
        back.PdfUrl.ShouldBe(completed.PdfUrl);
        back.Papers.Select(p => p.StartPage).ShouldBe(completed.Papers.Select(p => p.StartPage));
        (await client.GetFromJsonAsync<PagedResult<DeletedBookSummaryDto>>(new Uri("/api/books/deleted", UriKind.Relative), ApiHost.Json, TestContext.Current.CancellationToken))!
            .Items.ShouldBeEmpty();
        using var pdf = await client.GetAsync(new Uri(back.PdfUrl!, UriKind.Relative), TestContext.Current.CancellationToken);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).LongLength.ShouldBe(completed.PdfSizeBytes!.Value);
    }

    [Fact]
    public async Task Every_endpoint_of_a_deleted_book_answers_404_and_changes_nothing()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();
        using (var deleted = await client.DeleteAsync(BookUri(book.Id), TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var before = await RowAsync(api, book.Id);
        var reversed = book.Papers.Select(p => p.Id).Reverse().ToList();

        using (var detail = await client.GetAsync(BookUri(book.Id), TestContext.Current.CancellationToken))
        {
            await ShouldBeProblemAsync(detail, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        using (var pdf = await client.GetAsync(new Uri($"/api/books/{book.Id}/pdf", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            await ShouldBeProblemAsync(pdf, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        using (var generate = await client.PostAsync(new Uri($"/api/books/{book.Id}/generate", UriKind.Relative), null, TestContext.Current.CancellationToken))
        {
            await ShouldBeProblemAsync(generate, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        using (var order = await client.PutAsJsonAsync(
            new Uri($"/api/books/{book.Id}/paper-order", UriKind.Relative),
            new PaperOrderRequest(reversed),
            ApiHost.Json,
            TestContext.Current.CancellationToken))
        {
            await ShouldBeProblemAsync(order, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        (await RowAsync(api, book.Id)).ShouldBe(before);
        (await api.QueryAsync(
            "SELECT SiraNo FROM Bildiriler WHERE KitapId = (SELECT Id FROM Kitaplar WHERE Uid = @id) ORDER BY YuklemeSirasi",
            r => r.GetInt32(0),
            ("@id", book.Id))).ShouldBe(Enumerable.Range(1, 10));
    }

    [Fact]
    public async Task A_queued_or_processing_book_cannot_be_deleted_and_an_unknown_book_cannot_be_restored()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();

        foreach (var status in new[] { "Queued", "Processing" })
        {
            await api.ExecuteAsync($"UPDATE Kitaplar SET Durum = '{status}' WHERE Uid = @id", ("@id", book.Id));
            using var busy = await client.DeleteAsync(BookUri(book.Id), TestContext.Current.CancellationToken);
            await ShouldBeProblemAsync(busy, HttpStatusCode.Conflict, "GENERATION_ALREADY_IN_PROGRESS");
        }

        (await api.QueryAsync("SELECT AktifMi FROM Kitaplar WHERE Uid = @id", r => r.GetBoolean(0), ("@id", book.Id))).ShouldBe([true]);

        using var unknown = await client.PostAsync(new Uri($"/api/books/{Guid.NewGuid()}/restore", UriKind.Relative), null, TestContext.Current.CancellationToken);
        await ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "DELETED_BOOK_NOT_FOUND");
        using var active = await client.PostAsync(new Uri($"/api/books/{book.Id}/restore", UriKind.Relative), null, TestContext.Current.CancellationToken);
        await ShouldBeProblemAsync(active, HttpStatusCode.NotFound, "DELETED_BOOK_NOT_FOUND");
    }

    [Fact]
    public async Task The_worker_the_sweeper_and_the_startup_recovery_leave_deleted_books_alone()
    {
        var connectionString = sql.NewDatabase();
        var dataRoot = ApiHost.NewDataRoot();
        var first = Host(connectionString, dataRoot);
        var queued = await first.CreateSampleBookAsync("Kuyrukta Silinmiş");
        var processing = await first.CreateSampleBookAsync("İşlenirken Silinmiş");

        // Rows that cannot be produced through the API (deleting a busy book is refused), as a worst case.
        const string Deleted = "AktifMi = 0, SilinmeZamani = DATEADD(MINUTE, -30, SYSUTCDATETIME())";
        await first.ExecuteAsync(
            $"UPDATE Kitaplar SET Durum = 'Queued', KuyrugaAlinmaZamani = DATEADD(HOUR, -1, SYSUTCDATETIME()), {Deleted} WHERE Uid = @id",
            ("@id", queued.Id));
        await first.ExecuteAsync(
            $"UPDATE Kitaplar SET Durum = 'Processing', IslemBaslangicZamani = DATEADD(HOUR, -1, SYSUTCDATETIME()), {Deleted} WHERE Uid = @id",
            ("@id", processing.Id));
        await first.DisposeAsync();
        _hosts.Remove(first);

        // Startup recovery runs while the new host starts.
        var restarted = Host(connectionString, dataRoot);
        using (restarted.Client())
        {
            var before = new[] { await RowAsync(restarted, queued.Id), await RowAsync(restarted, processing.Id) };

            await using (var scope = restarted.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<BookGenerationHandler>().HandleAsync(queued.Id, TestContext.Current.CancellationToken);
                await scope.ServiceProvider.GetRequiredService<BookGenerationHandler>().HandleAsync(processing.Id, TestContext.Current.CancellationToken);
            }

            await using (var scope = restarted.Services.CreateAsyncScope())
            {
                (await scope.ServiceProvider.GetRequiredService<QueueSweepService>().SweepAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
            }

            new[] { await RowAsync(restarted, queued.Id), await RowAsync(restarted, processing.Id) }.ShouldBe(before);
            before[0].Status.ShouldBe("Queued");
            before[1].Status.ShouldBe("Processing");
            restarted.StoredFiles(queued.Id).ShouldAllBe(f => f.EndsWith(".docx", StringComparison.Ordinal));
            restarted.StoredFiles(processing.Id).ShouldAllBe(f => f.EndsWith(".docx", StringComparison.Ordinal));
        }
    }

    internal static Uri BookUri(Guid uid) => new($"/api/books/{uid}", UriKind.Relative);

    internal static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json", body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
        problem.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>The columns the background work would change, and the row version that any update changes.</summary>
    private static async Task<(string Status, string? Stage, int Progress, bool Active, string RowVersion)> RowAsync(ApiHost api, Guid uid) =>
        (await api.QueryAsync(
            "SELECT Durum, Asama, IlerlemeYuzdesi, AktifMi, CONVERT(varchar(20), SatirVersiyonu, 1) FROM Kitaplar WHERE Uid = @id",
            r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), (int)r.GetByte(2), r.GetBoolean(3), r.GetString(4)),
            ("@id", uid))).Single();

    private ApiHost Host(string? connectionString = null, string? dataRoot = null)
    {
        var host = new ApiHost(connectionString ?? sql.NewDatabase(), dataRoot ?? ApiHost.NewDataRoot());
        _hosts.Add(host);
        return host;
    }
}
