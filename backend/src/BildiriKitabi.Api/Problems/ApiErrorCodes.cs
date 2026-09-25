using System.Text.Json.Serialization;

namespace BildiriKitabi.Api.Problems;

/// <summary>Machine-readable <c>code</c> values of API errors that are not upload validation errors.</summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string RequestInvalid = "REQUEST_INVALID";
    public const string BookNotFound = "BOOK_NOT_FOUND";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string RequestTooLarge = "REQUEST_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string GenerationAlreadyInProgress = "GENERATION_ALREADY_IN_PROGRESS";
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
    public const string BookNotCompleted = "BOOK_NOT_COMPLETED";
    public const string PdfNotFound = "PDF_NOT_FOUND";
    public const string PaperOrderLocked = "PAPER_ORDER_LOCKED";
    public const string PaperOrderInvalid = "PAPER_ORDER_INVALID";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}

/// <summary>One entry of the <c>errors</c> array of a problem response.</summary>
public sealed record ApiError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FileName = null);
