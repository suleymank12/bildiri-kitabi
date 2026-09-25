using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BildiriKitabi.Api.Problems;

public static class ApiProblem
{
    /// <summary>A ProblemDetails result with <c>code</c>, optional <c>errors</c> and the standard type/traceId.</summary>
    public static ObjectResult Create(
        HttpContext httpContext,
        int status,
        string code,
        string title,
        string detail,
        IReadOnlyList<ApiError>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, status, title, type: null, detail);
        problem.Extensions["code"] = code;
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>Writes a problem outside MVC (rate limiter, exception handler).</summary>
    public static async Task WriteAsync(HttpContext httpContext, int status, string code, string title, string detail)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = status;
        var service = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        await service.WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }).ConfigureAwait(false);
    }
}
