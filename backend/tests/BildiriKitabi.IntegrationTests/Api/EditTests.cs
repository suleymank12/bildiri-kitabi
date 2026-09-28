using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Infrastructure.Persistence;
using BildiriKitabi.IntegrationTests.Books;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>Renaming a book and reordering its papers, also after the PDF was generated.</summary>
public sealed class EditTests(SqlServerFixture sql) : IAsyncDisposable
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

    [Theory]
    [InlineData("ab", "BOOK_NAME_INVALID", "Kitap adı 3–150 karakter olmalıdır.")]
    [InlineData("   ", "BOOK_NAME_INVALID", "Kitap adı 3–150 karakter olmalıdır.")]
    [InlineData("Satır\nsonu", "BOOK_NAME_INVALID", "Kitap adı satır sonu veya kontrol karakteri içeremez.")]
    [InlineData("Kongre 2026 😀", "BOOK_NAME_UNSUPPORTED_CHARACTER", "Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.")]
    public async Task An_invalid_name_is_rejected_like_on_upload(string name, string code, string message)
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();

        using var response = await RenameAsync(client, book.Uid, name);

        await SoftDeleteTests.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, code);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().ShouldBe(message);
        var error = problem.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("field").GetString().ShouldBe("name");
        error.GetProperty("code").GetString().ShouldBe(code);
        (await api.GetBookAsync(book.Uid)).Name.ShouldBe(book.Name);

        var tooLong = new string('a', 151);
        using var longName = await RenameAsync(client, book.Uid, tooLong);
        await SoftDeleteTests.ShouldBeProblemAsync(longName, HttpStatusCode.BadRequest, "BOOK_NAME_INVALID");
    }

    [Fact]
    public async Task Renaming_an_uploaded_book_keeps_its_state()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();

        using var response = await RenameAsync(client, book.Uid, "  Yeni Ad 2026  ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renamed = (await response.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
        renamed.Name.ShouldBe("Yeni Ad 2026");
        renamed.Status.ShouldBe(BookStatus.Uploaded);
        (await api.QueryAsync("SELECT Ad FROM Kitaplar WHERE Uid = @id", r => r.GetString(0), ("@id", book.Uid))).ShouldBe(["Yeni Ad 2026"]);
    }

    [Fact]
    public async Task Renaming_a_completed_book_reopens_it_deletes_the_pdf_and_it_can_be_generated_again()
    {
        var api = Host();
        var book = await CompletedBookAsync(api);
        using var client = api.Client();
        var pdfPath = PdfPath(api, book.Uid);
        File.Exists(pdfPath).ShouldBeTrue();

        // The same name (after trimming) changes nothing.
        var before = await RowVersionAsync(api, book.Uid);
        using (var same = await RenameAsync(client, book.Uid, $" {book.Name} "))
        {
            same.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await same.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!.Status.ShouldBe(BookStatus.Completed);
        }

        (await RowVersionAsync(api, book.Uid)).ShouldBe(before);
        File.Exists(pdfPath).ShouldBeTrue();

        using (var response = await RenameAsync(client, book.Uid, "Düzeltilmiş Kongre Kitabı"))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var reopened = (await response.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
            ShouldBeReopened(reopened);
            reopened.Name.ShouldBe("Düzeltilmiş Kongre Kitabı");
        }

        await ShouldBeReopenedInDatabaseAsync(api, book.Uid);
        File.Exists(pdfPath).ShouldBeFalse();
        api.StoredFiles(book.Uid).Count.ShouldBe(10);
        using (var pdf = await client.GetAsync(new Uri($"/api/books/{book.Uid}/pdf", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(pdf, HttpStatusCode.Conflict, "BOOK_NOT_COMPLETED");
        }

        (await api.GenerateAsync(book.Uid)).ShouldBe(HttpStatusCode.Accepted);
        var regenerated = await api.WaitForFinalStatusAsync(book.Uid);
        regenerated.Status.ShouldBe(BookStatus.Completed);
        regenerated.Papers.Select(p => p.StartPage).ShouldBe([3, 5, 7, 9, 11, 13, 15, 17, 19, 21]);
        var bytes = await client.GetByteArrayAsync(new Uri(regenerated.PdfUrl!, UriKind.Relative), TestContext.Current.CancellationToken);
        new BookPdf(bytes).Page(1).Text.ShouldContain("Düzeltilmiş Kongre Kitabı");
    }

    [Fact]
    public async Task Reordering_a_completed_book_reopens_it_and_the_same_order_changes_nothing()
    {
        var api = Host();
        var book = await CompletedBookAsync(api);
        using var client = api.Client();
        var pdfPath = PdfPath(api, book.Uid);
        var order = book.Papers.Select(p => p.Uid).ToList();

        var before = await RowVersionAsync(api, book.Uid);
        using (var same = await ReorderAsync(client, book.Uid, order))
        {
            same.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await same.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!.Status.ShouldBe(BookStatus.Completed);
        }

        (await RowVersionAsync(api, book.Uid)).ShouldBe(before);
        File.Exists(pdfPath).ShouldBeTrue();

        var swapped = order.ToList();
        (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
        using (var response = await ReorderAsync(client, book.Uid, swapped))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var reopened = (await response.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
            ShouldBeReopened(reopened);
            reopened.Papers.Select(p => p.Uid).ShouldBe(swapped);
        }

        await ShouldBeReopenedInDatabaseAsync(api, book.Uid);
        File.Exists(pdfPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Processing")]
    public async Task A_queued_or_processing_book_cannot_be_edited(string status)
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();
        await api.ExecuteAsync($"UPDATE Kitaplar SET Durum = '{status}' WHERE Uid = @id", ("@id", book.Uid));

        using (var rename = await RenameAsync(client, book.Uid, "Başka Bir Ad"))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(rename, HttpStatusCode.Conflict, "GENERATION_ALREADY_IN_PROGRESS");
        }

        using (var reorder = await ReorderAsync(client, book.Uid, book.Papers.Select(p => p.Uid).Reverse().ToList()))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(reorder, HttpStatusCode.Conflict, "PAPER_ORDER_LOCKED");
        }

        (await api.QueryAsync("SELECT Ad FROM Kitaplar WHERE Uid = @id", r => r.GetString(0), ("@id", book.Uid))).ShouldBe([book.Name]);
    }

    [Fact]
    public async Task A_deleted_or_unknown_book_cannot_be_renamed()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();
        using (var deleted = await client.DeleteAsync(SoftDeleteTests.BookUri(book.Uid), TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var rename = await RenameAsync(client, book.Uid, "Başka Bir Ad"))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(rename, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        using (var unknown = await RenameAsync(client, Guid.NewGuid(), "Başka Bir Ad"))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        (await api.QueryAsync("SELECT Ad FROM Kitaplar WHERE Uid = @id", r => r.GetString(0), ("@id", book.Uid))).ShouldBe([book.Name]);
    }

    [Fact]
    public async Task An_edit_that_overlaps_another_change_of_the_book_gets_a_409()
    {
        var interceptor = new ConcurrentChange();
        var api = Host(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor)));
        var book = await api.CreateSampleBookAsync();
        interceptor.ConnectionString = api.ConnectionString;
        using var client = api.Client();

        // Another request writes the book after this one read it and before it saves.
        interceptor.Arm(book.Uid);
        using (var rename = await RenameAsync(client, book.Uid, "Çakışan Ad"))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(rename, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        }

        interceptor.Arm(book.Uid);
        using (var reorder = await ReorderAsync(client, book.Uid, book.Papers.Select(p => p.Uid).Reverse().ToList()))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(reorder, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        }

        var unchanged = await api.GetBookAsync(book.Uid);
        unchanged.Name.ShouldBe(book.Name);
        unchanged.Papers.Select(p => p.Uid).ShouldBe(book.Papers.Select(p => p.Uid));

        // Without an overlapping change the same edits go through.
        using var ok = await RenameAsync(client, book.Uid, "Çakışmayan Ad");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static void ShouldBeReopened(BookDetailDto book)
    {
        book.Status.ShouldBe(BookStatus.Uploaded);
        book.ProgressPercent.ShouldBe(0);
        book.PdfUrl.ShouldBeNull();
        book.PageCount.ShouldBeNull();
        book.PdfSizeBytes.ShouldBeNull();
        book.ProcessingStartedAt.ShouldBeNull();
        book.ProcessingFinishedAt.ShouldBeNull();
        book.Papers.ShouldAllBe(p => p.StartPage == null && p.EndPage == null && p.RemovedEmailCount == 0 && p.RemovedPhoneCount == 0);
    }

    private static async Task ShouldBeReopenedInDatabaseAsync(ApiHost api, Guid uid)
    {
        (await api.QueryAsync(
            "SELECT Durum, PdfDepolamaAnahtari, PdfBoyutuBayt, SayfaSayisi FROM Kitaplar WHERE Uid = @id",
            r => (r.GetString(0), r.IsDBNull(1), r.IsDBNull(2), r.IsDBNull(3)),
            ("@id", uid))).ShouldBe([("Uploaded", true, true, true)]);
        (await api.QueryAsync(
            """
            SELECT COUNT(*) FROM Bildiriler
            WHERE KitapId = (SELECT Id FROM Kitaplar WHERE Uid = @id)
              AND (BaslangicSayfasi IS NOT NULL OR BitisSayfasi IS NOT NULL OR SilinenEpostaSayisi <> 0 OR SilinenTelefonSayisi <> 0)
            """,
            r => r.GetInt32(0),
            ("@id", uid))).ShouldBe([0]);
    }

    private static async Task<BookDetailDto> CompletedBookAsync(ApiHost api)
    {
        var book = await api.CreateSampleBookAsync();
        (await api.GenerateAsync(book.Uid)).ShouldBe(HttpStatusCode.Accepted);
        var completed = await api.WaitForFinalStatusAsync(book.Uid);
        completed.Status.ShouldBe(BookStatus.Completed);
        completed.Papers.Sum(p => p.RemovedEmailCount).ShouldBe(13);
        return completed;
    }

    private static string PdfPath(ApiHost api, Guid uid) => Path.Combine(api.StorageRoot, "books", uid.ToString("D"), "output", "book.pdf");

    private static async Task<string> RowVersionAsync(ApiHost api, Guid uid) =>
        (await api.QueryAsync("SELECT CONVERT(varchar(20), SatirVersiyonu, 1) FROM Kitaplar WHERE Uid = @id", r => r.GetString(0), ("@id", uid))).Single();

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid uid, string name) =>
        client.PutAsJsonAsync(new Uri($"/api/books/{uid}", UriKind.Relative), new RenameBookRequest(name), ApiHost.Json, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ReorderAsync(HttpClient client, Guid uid, List<Guid> paperUids) =>
        client.PutAsJsonAsync(
            new Uri($"/api/books/{uid}/paper-order", UriKind.Relative),
            new PaperOrderRequest(paperUids),
            ApiHost.Json,
            TestContext.Current.CancellationToken);

    private ApiHost Host(Action<IServiceCollection>? configureServices = null)
    {
        var host = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot(), configureServices);
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// Once armed, writes the book on its own connection just before the application's next <c>UPDATE [Kitaplar]</c>,
    /// as another request would; that gives the row a new version the application has not read.
    /// </summary>
    private sealed class ConcurrentChange : DbCommandInterceptor
    {
        private Guid? _armedFor;

        public string ConnectionString { get; set; } = string.Empty;

        public void Arm(Guid uid) => _armedFor = uid;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (_armedFor is { } uid && command.CommandText.Contains("UPDATE [Kitaplar]", StringComparison.Ordinal))
            {
                _armedFor = null;
                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var touch = new SqlCommand("UPDATE Kitaplar SET Ad = Ad WHERE Uid = @uid", connection);
                touch.Parameters.AddWithValue("@uid", uid);
                await touch.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
