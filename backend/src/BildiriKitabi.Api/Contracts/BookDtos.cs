using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace BildiriKitabi.Api.Contracts;

public sealed record BookErrorDto(string Code, string Message);

public sealed record PaperDto(
    Guid Id,
    int Order,
    int UploadOrder,
    string FileName,
    string Title,
    TitleSource TitleSource,
    int? StartPage,
    int? EndPage,
    int RemovedEmailCount,
    int RemovedPhoneCount,
    long SizeBytes);

public sealed record BookDetailDto(
    Guid Id,
    string Name,
    BookStatus Status,
    GenerationStage? Stage,
    int ProgressPercent,
    BookErrorDto? Error,
    DateTime CreatedAt,
    DateTime? ProcessingStartedAt,
    DateTime? ProcessingFinishedAt,
    int? PageCount,
    long? PdfSizeBytes,
    string? PdfUrl,
    IReadOnlyList<PaperDto> Papers)
{
    public static BookDetailDto From(Book book) => new(
        book.Id,
        book.Name,
        book.Status,
        book.Stage,
        book.ProgressPercent,
        ErrorOf(book),
        book.CreatedAt,
        book.ProcessingStartedAt,
        book.ProcessingFinishedAt,
        book.PageCount,
        book.PdfSizeBytes,
        PdfUrlOf(book),
        book.Papers
            .OrderBy(p => p.Order)
            .Select(p => new PaperDto(
                p.Id,
                p.Order,
                p.UploadOrder,
                p.OriginalFileName,
                p.Title,
                p.TitleSource,
                p.StartPage,
                p.EndPage,
                p.RemovedEmailCount,
                p.RemovedPhoneCount,
                p.SizeBytes))
            .ToList());

    internal static BookErrorDto? ErrorOf(Book book) =>
        book.Status == BookStatus.Failed ? new BookErrorDto(book.ErrorCode ?? BookErrorCodes.InternalError, book.ErrorMessage ?? string.Empty) : null;

    internal static string? PdfUrlOf(Book book) => book.Status == BookStatus.Completed ? $"/api/books/{book.Id:D}/pdf" : null;
}

public sealed record BookSummaryDto(
    Guid Id,
    string Name,
    BookStatus Status,
    GenerationStage? Stage,
    int ProgressPercent,
    int PaperCount,
    int? PageCount,
    BookErrorDto? Error,
    DateTime CreatedAt,
    DateTime? ProcessingFinishedAt,
    string? PdfUrl);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record PaperOrderRequest(IReadOnlyList<Guid> PaperIds);

/// <summary>multipart/form-data body of <c>POST /api/books</c>.</summary>
public sealed class CreateBookForm
{
    /// <summary>Book name, 3–150 characters.</summary>
    [FromForm(Name = "name")]
    public string? Name { get; set; }

    /// <summary>Exactly ten .docx files, in book order.</summary>
    [FromForm(Name = "files")]
    public IReadOnlyList<IFormFile>? Files { get; set; }
}
