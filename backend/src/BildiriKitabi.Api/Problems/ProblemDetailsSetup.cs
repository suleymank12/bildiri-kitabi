using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BildiriKitabi.Api.Problems;

/// <summary>
/// Every error response is RFC 9457 ProblemDetails with a Turkish <c>detail</c>, a machine-readable <c>code</c>
/// and the <c>traceId</c>. Responses produced by the framework (404, 405, 413, 415, model binding) get the same shape.
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? StatusCodes.Status500InternalServerError;
            problem.Extensions.TryAdd("code", DefaultCode(status));
            problem.Detail ??= DefaultDetail(status);
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .SelectMany(entry => entry.Value!.Errors.Select(_ => new ApiError(
                        ApiErrorCodes.RequestInvalid,
                        $"'{entry.Key}' alanının değeri geçersiz.",
                        Field: entry.Key)))
                    .ToList();
                return ApiProblem.Create(
                    context.HttpContext,
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.RequestInvalid,
                    "İstek geçersiz.",
                    "İstekteki bazı alanlar okunamadı veya geçersiz.",
                    errors);
            };
        });

        return services;
    }

    private static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ApiErrorCodes.RequestInvalid,
        StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ApiErrorCodes.MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => ApiErrorCodes.RequestTooLarge,
        StatusCodes.Status415UnsupportedMediaType => ApiErrorCodes.UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => ApiErrorCodes.RateLimited,
        _ => ApiErrorCodes.InternalError,
    };

    private static string DefaultDetail(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "İstek geçersiz.",
        StatusCodes.Status404NotFound => "İstenen kaynak bulunamadı.",
        StatusCodes.Status405MethodNotAllowed => "Bu işlem bu adreste desteklenmiyor.",
        StatusCodes.Status413PayloadTooLarge => "İstek boyutu izin verilen sınırı aşıyor.",
        StatusCodes.Status415UnsupportedMediaType => "İsteğin içerik türü desteklenmiyor.",
        StatusCodes.Status429TooManyRequests => "Çok fazla istek gönderildi. Lütfen biraz bekleyip tekrar deneyin.",
        _ => "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.",
    };
}
