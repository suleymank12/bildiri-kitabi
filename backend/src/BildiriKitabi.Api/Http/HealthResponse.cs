using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BildiriKitabi.Api.Http;

/// <summary><c>/health</c> as JSON: the overall status and one entry per check (no exception details).</summary>
public static class HealthResponse
{
    // Turkish letters are written as they are (the output is JSON, never embedded in HTML).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        };
        return JsonSerializer.SerializeAsync(context.Response.Body, body, Json, context.RequestAborted);
    }
}
