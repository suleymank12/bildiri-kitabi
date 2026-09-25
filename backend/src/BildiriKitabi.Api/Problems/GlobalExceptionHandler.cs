using Microsoft.AspNetCore.Diagnostics;

namespace BildiriKitabi.Api.Problems;

/// <summary>Turns unhandled exceptions into ProblemDetails; no exception text or stack trace reaches the client.</summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is BadHttpRequestException badRequest)
        {
            // Kestrel limits (request body too large, malformed multipart) — the client's fault, not ours.
            var status = badRequest.StatusCode;
            var tooLarge = status == StatusCodes.Status413PayloadTooLarge;
            await ApiProblem.WriteAsync(
                httpContext,
                status,
                tooLarge ? ApiErrorCodes.RequestTooLarge : ApiErrorCodes.RequestInvalid,
                tooLarge ? "İstek çok büyük." : "İstek geçersiz.",
                tooLarge ? "İstek boyutu izin verilen sınırı aşıyor." : "İstek okunamadı.").ConfigureAwait(false);
            return true;
        }

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            return true;
        }

        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        await ApiProblem.WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalError,
            "Sunucu hatası.",
            "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.").ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
