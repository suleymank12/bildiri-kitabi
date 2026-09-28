using System.ComponentModel.DataAnnotations;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Api.Http;
using BildiriKitabi.Api.Problems;
using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Core.Uploads;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace BildiriKitabi.Api.Controllers;

/// <summary>
/// Books: upload ten papers, order them, generate the PDF and download it. There is deliberately no endpoint that
/// returns the original .docx files: they still contain the contact details that the book removes.
/// </summary>
[ApiController]
[Route("api/books")]
public sealed class BooksController(IAppDbContext db, IFileStorage storage) : ControllerBase
{
    private const long MaxRequestBytes = 70L * 1024 * 1024;

    /// <summary>Creates a book from its name and exactly ten .docx files (in book order).</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [EnableRateLimiting(RateLimiting.UploadPolicy)]
    [ProducesResponseType<BookDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Create([FromForm] CreateBookForm form, [FromServices] CreateBookService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(service);

        var files = (form.Files ?? []).Select(f => new UploadedFile(f.FileName, f.Length, f.OpenReadStream)).ToList();
        var result = await service.CreateAsync(form.Name, files, cancellationToken);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => new ApiError(e.Code, e.Message, e.Field, e.FileName)).ToList();
            return ApiProblem.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                errors.Select(e => e.Code).Distinct().Count() == 1 ? errors[0].Code : ApiErrorCodes.ValidationFailed,
                "Yükleme doğrulanamadı.",
                errors.Count == 1 ? errors[0].Message : $"Yüklemede {errors.Count} sorun bulundu; ayrıntılar 'errors' listesinde.",
                errors);
        }

        var book = result.Book!;
        return CreatedAtAction(nameof(Get), new { uid = book.Uid }, BookDetailDto.From(book));
    }

    /// <summary>Lists the books that are not deleted, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BookSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<BookSummaryDto>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        PageAsync(
            db.Books.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id),
            page,
            pageSize,
            BookSummaryDto.From,
            cancellationToken);

    /// <summary>Lists the deleted books, most recently deleted first; they can be restored.</summary>
    [HttpGet("deleted")]
    [ProducesResponseType<PagedResult<DeletedBookSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<DeletedBookSummaryDto>> ListDeleted(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        PageAsync(
            db.Books.Deleted().OrderByDescending(b => b.DeletedAt).ThenByDescending(b => b.Id),
            page,
            pageSize,
            DeletedBookSummaryDto.From,
            cancellationToken);

    /// <summary>Status, progress, detected titles, page ranges and errors of one book.</summary>
    [HttpGet("{uid:guid}")]
    [ProducesResponseType<BookDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid uid, CancellationToken cancellationToken)
    {
        var book = await db.Books.AsNoTracking().Include(b => b.Papers).FirstOrDefaultAsync(b => b.Uid == uid, cancellationToken);
        return book is null ? BookNotFound() : Ok(BookDetailDto.From(book));
    }

    /// <summary>
    /// Renames the book (same rules as on upload). A completed book goes back to <c>Uploaded</c> and its PDF is
    /// deleted; generate it again. The same name changes nothing. Not allowed while the book is queued or generating.
    /// </summary>
    [HttpPut("{uid:guid}")]
    [EnableRateLimiting(RateLimiting.EditPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<BookDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Rename(
        Guid uid,
        [FromBody] RenameBookRequest request,
        [FromServices] BookEditService edits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(edits);

        var result = await edits.RenameAsync(uid, request.Name, cancellationToken);
        if (result is { Outcome: EditBookOutcome.InvalidName, Name: { } name })
        {
            return ApiProblem.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                name.ErrorCode!,
                "Kitap adı geçersiz.",
                name.ErrorMessage!,
                [new ApiError(name.ErrorCode!, name.ErrorMessage!, Field: "name")]);
        }

        return await EditResultAsync(uid, result.Outcome, "Kitap adı değiştirilemez.", cancellationToken);
    }

    /// <summary>
    /// Sets the order of the papers. A completed book goes back to <c>Uploaded</c> and its PDF is deleted; generate it
    /// again. The same order changes nothing. Not allowed while the book is queued or generating.
    /// </summary>
    [HttpPut("{uid:guid}/paper-order")]
    [EnableRateLimiting(RateLimiting.EditPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<BookDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> SetPaperOrder(
        Guid uid,
        [FromBody] PaperOrderRequest request,
        [FromServices] BookEditService edits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(edits);

        var result = await edits.ReorderAsync(uid, request.PaperUids, cancellationToken);
        if (result.Outcome == EditBookOutcome.InvalidList)
        {
            return ApiProblem.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.PaperOrderInvalid,
                "Sıralama geçersiz.",
                "Liste kitabın tüm bildirilerini tam olarak birer kez içermelidir.");
        }

        if (result.Outcome == EditBookOutcome.Busy)
        {
            return ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.PaperOrderLocked,
                "Sıralama değiştirilemez.",
                "Kitap kuyrukta veya oluşturuluyor; bildiri sırası işlem bitince değiştirilebilir.");
        }

        return await EditResultAsync(uid, result.Outcome, "Sıralama değiştirilemez.", cancellationToken);
    }

    /// <summary>Starts generating the PDF in the background; poll <c>GET /api/books/{uid}</c> for progress.</summary>
    [HttpPost("{uid:guid}/generate")]
    [EnableRateLimiting(RateLimiting.GeneratePolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Generate(Guid uid, [FromServices] BookCommandService commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var outcome = await commands.StartGenerationAsync(uid, cancellationToken);
        return outcome switch
        {
            StartGenerationOutcome.Started => AcceptedAtAction(nameof(Get), new { uid }, null),
            StartGenerationOutcome.NotFound => BookNotFound(),
            StartGenerationOutcome.AlreadyCompleted => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.AlreadyCompleted,
                "Kitap zaten oluşturuldu.",
                "Bu kitabın PDF'i zaten oluşturuldu."),
            _ => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.GenerationAlreadyInProgress,
                "Oluşturma sürüyor.",
                "Bu kitap zaten kuyrukta veya oluşturuluyor."),
        };
    }

    /// <summary>
    /// The finished PDF. Supports range requests (206) and conditional requests (ETag / 304);
    /// <c>download=true</c> asks the browser to save it instead of showing it.
    /// </summary>
    [HttpGet("{uid:guid}/pdf")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status206PartialContent, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Pdf(Guid uid, [FromQuery] bool download = false, CancellationToken cancellationToken = default)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Uid == uid, cancellationToken);
        if (book is null)
        {
            return BookNotFound();
        }

        if (book.Status != BookStatus.Completed || book.PdfStorageKey is null)
        {
            return ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.BookNotCompleted,
                "PDF hazır değil.",
                "Kitabın PDF'i henüz oluşturulmadı.");
        }

        Stream stream;
        try
        {
            stream = await storage.OpenReadAsync(book.PdfStorageKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return ApiProblem.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PdfNotFound,
                "PDF bulunamadı.",
                "Kitabın PDF dosyası bulunamadı. Kitabı silip yeniden oluşturabilirsiniz.");
        }

        Response.Headers.ContentDisposition = PdfFileName.ContentDisposition(book.Name, download);
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(book.RowVersion)}\"");
        DateTimeOffset? lastModified = book.ProcessingFinishedAt is { } finished ? new DateTimeOffset(finished, TimeSpan.Zero) : null;
        return File(stream, "application/pdf", lastModified, etag, enableRangeProcessing: true);
    }

    /// <summary>
    /// Deletes the book: it moves to the deleted books and every other endpoint answers 404 for it; its papers and files
    /// are kept so it can be restored. Not allowed while it is queued or generating.
    /// </summary>
    [HttpDelete("{uid:guid}")]
    [EnableRateLimiting(RateLimiting.EditPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid uid, [FromServices] BookCommandService commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var outcome = await commands.DeleteAsync(uid, cancellationToken);
        return outcome switch
        {
            DeleteBookOutcome.Deleted => NoContent(),
            DeleteBookOutcome.NotFound => BookNotFound(),
            _ => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.GenerationAlreadyInProgress,
                "Kitap silinemez.",
                "Kitap kuyrukta veya oluşturuluyor; işlem bitince tekrar deneyin."),
        };
    }

    /// <summary>Restores a deleted book to the book list, in the state it was deleted in.</summary>
    [HttpPost("{uid:guid}/restore")]
    [EnableRateLimiting(RateLimiting.EditPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Restore(Guid uid, [FromServices] BookCommandService commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var outcome = await commands.RestoreAsync(uid, cancellationToken);
        return outcome == RestoreBookOutcome.Restored
            ? NoContent()
            : ApiProblem.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.DeletedBookNotFound,
                "Silinmiş kitap bulunamadı.",
                "Geri alınacak kitap silinenler arasında bulunamadı; zaten geri alınmış olabilir.");
    }

    /// <summary>The book after an edit, or the problem shared by both edit endpoints.</summary>
    private async Task<IActionResult> EditResultAsync(Guid uid, EditBookOutcome outcome, string title, CancellationToken cancellationToken) =>
        outcome switch
        {
            EditBookOutcome.Edited or EditBookOutcome.Unchanged => await Get(uid, cancellationToken),
            EditBookOutcome.NotFound => BookNotFound(),
            EditBookOutcome.Busy => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.GenerationAlreadyInProgress,
                title,
                "Kitap kuyrukta veya oluşturuluyor; işlem bitince tekrar deneyin."),
            _ => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.EditConflict,
                title,
                "Kitap bu sırada başka bir istekle değiştirildi. Sayfayı yenileyip tekrar deneyin."),
        };

    /// <summary>One page of <paramref name="books"/> (already ordered) with the paper count of each book.</summary>
    private static async Task<PagedResult<T>> PageAsync<T>(
        IQueryable<Book> books,
        int page,
        int pageSize,
        Func<Book, int, T> map,
        CancellationToken cancellationToken)
    {
        var total = await books.CountAsync(cancellationToken);
        var rows = await books.AsNoTracking()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new { Book = b, PaperCount = b.Papers.Count })
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(rows.Select(x => map(x.Book, x.PaperCount)).ToList(), page, pageSize, total);
    }

    private ObjectResult BookNotFound() => ApiProblem.Create(
        HttpContext,
        StatusCodes.Status404NotFound,
        ApiErrorCodes.BookNotFound,
        "Kitap bulunamadı.",
        "İstenen kitap bulunamadı; silinmiş olabilir.");
}
