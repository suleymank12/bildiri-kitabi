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
        return CreatedAtAction(nameof(Get), new { id = book.Id }, BookDetailDto.From(book));
    }

    /// <summary>Lists books, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BookSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<BookSummaryDto>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var total = await db.Books.CountAsync(cancellationToken);
        var books = await db.Books.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new
            {
                Book = b,
                PaperCount = b.Papers.Count,
            })
            .ToListAsync(cancellationToken);

        var items = books
            .Select(x => new BookSummaryDto(
                x.Book.Id,
                x.Book.Name,
                x.Book.Status,
                x.Book.Stage,
                x.Book.ProgressPercent,
                x.PaperCount,
                x.Book.PageCount,
                BookDetailDto.ErrorOf(x.Book),
                x.Book.CreatedAt,
                x.Book.ProcessingFinishedAt,
                BookDetailDto.PdfUrlOf(x.Book)))
            .ToList();
        return new PagedResult<BookSummaryDto>(items, page, pageSize, total);
    }

    /// <summary>Status, progress, detected titles, page ranges and errors of one book.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<BookDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var book = await db.Books.AsNoTracking().Include(b => b.Papers).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        return book is null ? BookNotFound() : Ok(BookDetailDto.From(book));
    }

    /// <summary>Sets the order of the papers; allowed before generation or after a failed one.</summary>
    [HttpPut("{id:guid}/paper-order")]
    [Consumes("application/json")]
    [ProducesResponseType<BookDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> SetPaperOrder(
        Guid id,
        [FromBody] PaperOrderRequest request,
        [FromServices] BookCommandService commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(commands);

        var outcome = await commands.ReorderAsync(id, request.PaperIds, cancellationToken);
        return outcome switch
        {
            ReorderOutcome.Reordered => await Get(id, cancellationToken),
            ReorderOutcome.NotFound => BookNotFound(),
            ReorderOutcome.InvalidList => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.PaperOrderInvalid,
                "Sıralama geçersiz.",
                "Liste kitabın tüm bildirilerini tam olarak birer kez içermelidir."),
            _ => ApiProblem.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.PaperOrderLocked,
                "Sıralama değiştirilemez.",
                "Bildiri sırası yalnızca kitap oluşturulmadan önce veya başarısız bir denemeden sonra değiştirilebilir."),
        };
    }

    /// <summary>Starts generating the PDF in the background; poll <c>GET /api/books/{id}</c> for progress.</summary>
    [HttpPost("{id:guid}/generate")]
    [EnableRateLimiting(RateLimiting.GeneratePolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Generate(Guid id, [FromServices] BookCommandService commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var outcome = await commands.StartGenerationAsync(id, cancellationToken);
        return outcome switch
        {
            StartGenerationOutcome.Started => AcceptedAtAction(nameof(Get), new { id }, null),
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
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status206PartialContent, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Pdf(Guid id, [FromQuery] bool download = false, CancellationToken cancellationToken = default)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
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

    /// <summary>Deletes the book, its papers and every stored file; not allowed while it is queued or generating.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, [FromServices] BookCommandService commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var outcome = await commands.DeleteAsync(id, cancellationToken);
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

    private ObjectResult BookNotFound() => ApiProblem.Create(
        HttpContext,
        StatusCodes.Status404NotFound,
        ApiErrorCodes.BookNotFound,
        "Kitap bulunamadı.",
        "İstenen kitap bulunamadı; silinmiş olabilir.");
}
