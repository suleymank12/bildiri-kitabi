using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Titles;
using BildiriKitabi.IntegrationTests.Books;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary><c>PUT /api/books/{uid}/papers/{paperUid}/title</c>: a title typed by the user replaces the detected one.</summary>
public sealed class PaperTitleTests(SqlServerFixture sql) : IAsyncDisposable
{
    private const string NewTitle = "SULAMADA SENSÖR VERİSİYLE SU TASARRUFU";

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
    [InlineData("   ", "PAPER_TITLE_INVALID", "Bildiri başlığı boş olamaz.")]
    [InlineData("Akıllı Sulama elif.kaya@example.org", "PAPER_TITLE_CONTACT_INFO", "Başlıkta e-posta adresi veya telefon numarası bulunamaz.")]
    [InlineData("Akıllı Sulama 0312 555 12 34", "PAPER_TITLE_CONTACT_INFO", "Başlıkta e-posta adresi veya telefon numarası bulunamaz.")]
    [InlineData("Akıllı Sulama 😀", "PAPER_TITLE_UNSUPPORTED_CHARACTER", "Başlıktaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.")]
    public async Task An_invalid_title_is_rejected_with_its_code(string title, string code, string message)
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();

        using var response = await SetTitleAsync(client, book.Uid, book.Papers[0].Uid, title);

        await SoftDeleteTests.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, code);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().ShouldBe(message);
        problem.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem().GetProperty("field").GetString().ShouldBe("title");

        using var tooLong = await SetTitleAsync(client, book.Uid, book.Papers[0].Uid, new string('a', 501));
        await SoftDeleteTests.ShouldBeProblemAsync(tooLong, HttpStatusCode.BadRequest, "PAPER_TITLE_INVALID");
        (await api.GetBookAsync(book.Uid)).Papers[0].Title.ShouldBe(book.Papers[0].Title);
    }

    [Fact]
    public async Task A_paper_of_another_book_an_unknown_paper_and_a_deleted_book_are_not_found()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync("Birinci Kitap");
        var other = await api.CreateSampleBookAsync("İkinci Kitap");
        using var client = api.Client();

        using (var foreign = await SetTitleAsync(client, book.Uid, other.Papers[0].Uid, NewTitle))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(foreign, HttpStatusCode.NotFound, "PAPER_NOT_FOUND");
        }

        using (var unknown = await SetTitleAsync(client, book.Uid, Guid.NewGuid(), NewTitle))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "PAPER_NOT_FOUND");
        }

        using (var deleted = await client.DeleteAsync(SoftDeleteTests.BookUri(book.Uid), TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var ofDeleted = await SetTitleAsync(client, book.Uid, book.Papers[0].Uid, NewTitle))
        {
            await SoftDeleteTests.ShouldBeProblemAsync(ofDeleted, HttpStatusCode.NotFound, "BOOK_NOT_FOUND");
        }

        (await api.GetBookAsync(other.Uid)).Papers[0].Title.ShouldBe(other.Papers[0].Title);
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Processing")]
    public async Task A_queued_or_processing_book_keeps_its_titles(string status)
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();
        await api.ExecuteAsync($"UPDATE Kitaplar SET Durum = '{status}' WHERE Uid = @id", ("@id", book.Uid));

        using var response = await SetTitleAsync(client, book.Uid, book.Papers[0].Uid, NewTitle);

        await SoftDeleteTests.ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "GENERATION_ALREADY_IN_PROGRESS");
        (await api.QueryAsync("SELECT Baslik FROM Bildiriler WHERE Uid = @id", r => r.GetString(0), ("@id", book.Papers[0].Uid))).ShouldBe([book.Papers[0].Title]);
    }

    [Fact]
    public async Task A_new_title_reopens_a_completed_book_and_the_regenerated_pdf_prints_only_the_new_title()
    {
        var api = Host();
        var book = await api.CreateSampleBookAsync();
        using var client = api.Client();
        (await api.GenerateAsync(book.Uid)).ShouldBe(HttpStatusCode.Accepted);
        (await api.WaitForFinalStatusAsync(book.Uid)).Status.ShouldBe(BookStatus.Completed);
        var paper = book.Papers[0];
        var oldTitle = paper.Title;
        var pdfPath = Path.Combine(api.StorageRoot, "books", book.Uid.ToString("D"), "output", "book.pdf");
        File.Exists(pdfPath).ShouldBeTrue();

        // The same title (after trimming) changes nothing.
        using (var same = await SetTitleAsync(client, book.Uid, paper.Uid, $"  {oldTitle}\n"))
        {
            same.StatusCode.ShouldBe(HttpStatusCode.OK);
            var unchanged = (await same.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
            unchanged.Status.ShouldBe(BookStatus.Completed);
            unchanged.Papers[0].TitleSource.ShouldBe(TitleSource.TitleStyle);
        }

        File.Exists(pdfPath).ShouldBeTrue();

        using (var response = await SetTitleAsync(client, book.Uid, paper.Uid, $"  {NewTitle}  "))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var reopened = (await response.Content.ReadFromJsonAsync<BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
            reopened.Status.ShouldBe(BookStatus.Uploaded);
            reopened.PdfUrl.ShouldBeNull();
            reopened.Papers[0].Title.ShouldBe(NewTitle);
            reopened.Papers[0].TitleSource.ShouldBe(TitleSource.Manual);
            reopened.Papers.Skip(1).ShouldAllBe(p => p.TitleSource == TitleSource.TitleStyle);
        }

        File.Exists(pdfPath).ShouldBeFalse();
        (await api.QueryAsync(
            "SELECT Baslik, BaslikKaynagi FROM Bildiriler WHERE Uid = @id",
            r => (r.GetString(0), r.GetString(1)),
            ("@id", paper.Uid))).ShouldBe([(NewTitle, "Manual")]);

        (await api.GenerateAsync(book.Uid)).ShouldBe(HttpStatusCode.Accepted);
        var regenerated = await api.WaitForFinalStatusAsync(book.Uid);
        regenerated.Status.ShouldBe(BookStatus.Completed);
        regenerated.Papers[0].Title.ShouldBe(NewTitle);

        // The PDF is read on its own (PdfPig), not through the generator's data.
        var pdf = new BookPdf(await client.GetByteArrayAsync(new Uri(regenerated.PdfUrl!, UriKind.Relative), TestContext.Current.CancellationToken));
        pdf.Page(2).BodyText.ShouldContain(NewTitle);
        var firstPage = pdf.Page(regenerated.Papers[0].StartPage!.Value);
        firstPage.BodyText.ShouldStartWith(NewTitle);
        firstPage.Text.ShouldContain(NewTitle);
        pdf.AllText.ShouldNotContain(oldTitle);
        pdf.AllText.ShouldNotContain("KENTSEL TARIMDA");
    }

    private static Task<HttpResponseMessage> SetTitleAsync(HttpClient client, Guid bookUid, Guid paperUid, string title) =>
        client.PutAsJsonAsync(
            new Uri($"/api/books/{bookUid}/papers/{paperUid}/title", UriKind.Relative),
            new PaperTitleRequest(title),
            ApiHost.Json,
            TestContext.Current.CancellationToken);

    private ApiHost Host()
    {
        var host = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot());
        _hosts.Add(host);
        return host;
    }
}
