using System.Net;
using System.Net.Http.Headers;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Core.Books;
using BildiriKitabi.IntegrationTests.Books;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>Uploads the sample papers once, generates the book and checks the result through the API and SQL.</summary>
public sealed class GenerationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private BookDetailDto _book = null!;

    public async ValueTask InitializeAsync()
    {
        if (!sql.IsAvailable)
        {
            return;
        }

        _api = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot());
        _book = await _api.CreateSampleBookAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
            SchemaAndUploadTests.TryDelete(_api.DataRoot);
        }
    }

    [Fact]
    public async Task Generation_runs_in_the_background_and_stores_page_numbers()
    {
        sql.EnsureAvailable();
        using var client = _api.Client();
        using var response = await client.PostAsync(new Uri($"/api/books/{_book.Id}/generate", UriKind.Relative), null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/books/{_book.Id}");

        var book = await _api.WaitForFinalStatusAsync(_book.Id);

        book.Status.ShouldBe(BookStatus.Completed, book.Error?.Message);
        book.ProgressPercent.ShouldBe(100);
        book.Stage.ShouldBeNull();
        book.Error.ShouldBeNull();
        book.PdfUrl.ShouldBe($"/api/books/{_book.Id}/pdf");
        book.PageCount.ShouldBe(22);
        book.Papers.Select(p => p.StartPage).ShouldBe([3, 5, 7, 9, 11, 13, 15, 17, 19, 21]);
        book.Papers.Sum(p => p.RemovedEmailCount).ShouldBe(13);
        book.Papers.Sum(p => p.RemovedPhoneCount).ShouldBe(13);
        book.ProcessingStartedAt.ShouldNotBeNull().Kind.ShouldBe(DateTimeKind.Utc);
        book.ProcessingFinishedAt.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(book.ProcessingStartedAt!.Value);

        using var pdf = await client.GetAsync(new Uri(book.PdfUrl!, UriKind.Relative), TestContext.Current.CancellationToken);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8).ShouldBeTrue();
        new BookPdf(bytes).Pages.Count.ShouldBe(22);

        var stored = await _api.QueryAsync(
            "SELECT Durum, SayfaSayisi, PdfBoyutuBayt, PdfDepolamaAnahtari, Asama FROM Kitaplar WHERE Uid = @id",
            r => (Status: r.GetString(0), Pages: r.GetInt32(1), Size: r.GetInt64(2), Key: r.GetString(3), StageIsNull: r.IsDBNull(4)),
            ("@id", _book.Id));
        stored.ShouldBe([("Completed", 22, bytes.LongLength, $"books/{_book.Id}/output/book.pdf", true)]);
        (await _api.QueryAsync("SELECT KuyrugaAlinmaZamani FROM Kitaplar WHERE Uid = @id", r => r.IsDBNull(0), ("@id", _book.Id)))
            .ShouldBe([false]);
        var pages = await _api.QueryAsync(
            "SELECT BaslangicSayfasi, BitisSayfasi FROM Bildiriler WHERE KitapId = (SELECT Id FROM Kitaplar WHERE Uid = @id) ORDER BY SiraNo",
            r => (r.GetInt32(0), r.GetInt32(1)),
            ("@id", _book.Id));
        pages.ShouldBe(Enumerable.Range(0, 10).Select(i => (3 + (2 * i), 4 + (2 * i))).ToList());

        // A completed book cannot be generated again or reordered.
        (await _api.GenerateAsync(_book.Id)).ShouldBe(HttpStatusCode.Conflict);

        // Range and download headers.
        using var range = new HttpRequestMessage(HttpMethod.Get, new Uri(book.PdfUrl!, UriKind.Relative));
        range.Headers.Range = new RangeHeaderValue(0, 99);
        using var partial = await client.SendAsync(range, TestContext.Current.CancellationToken);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        partial.Content.Headers.ContentRange!.ToString().ShouldBe($"bytes 0-99/{bytes.Length}");
        (await partial.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(bytes[..100]);

        using var download = await client.GetAsync(new Uri($"{book.PdfUrl}?download=true", UriKind.Relative), TestContext.Current.CancellationToken);
        download.Content.Headers.GetValues("Content-Disposition").ShouldBe(
            ["attachment; filename=\"bildiri-kitabi-ornek-bilim-kongresi-2026.pdf\"; filename*=UTF-8''%C3%96rnek%20Bilim%20Kongresi%202026.pdf"]);
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("Örnek Bilim Kongresi 2026.pdf");
        pdf.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");

        using var conditional = new HttpRequestMessage(HttpMethod.Get, new Uri(book.PdfUrl!, UriKind.Relative));
        conditional.Headers.IfNoneMatch.Add(pdf.Headers.ETag!);
        using var notModified = await client.SendAsync(conditional, TestContext.Current.CancellationToken);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Two_concurrent_generate_requests_start_one_generation()
    {
        sql.EnsureAvailable();
        var results = await Task.WhenAll(_api.GenerateAsync(_book.Id), _api.GenerateAsync(_book.Id));

        results.Order().ShouldBe([HttpStatusCode.Accepted, HttpStatusCode.Conflict]);
        (await _api.WaitForFinalStatusAsync(_book.Id)).Status.ShouldBe(BookStatus.Completed);
    }

    [Fact]
    public async Task The_pdf_of_an_unfinished_book_is_a_conflict()
    {
        sql.EnsureAvailable();
        using var client = _api.Client();
        using var response = await client.GetAsync(new Uri($"/api/books/{_book.Id}/pdf", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("BOOK_NOT_COMPLETED");
    }
}
