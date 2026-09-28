using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using BildiriKitabi.Api.Problems;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Api.Http;

/// <summary><c>RateLimiting</c> section: requests per minute and client IP.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int UploadPermitsPerMinute { get; set; } = 10;

    [Range(1, 100_000)]
    public int GeneratePermitsPerMinute { get; set; } = 20;

    /// <summary>Renaming, reordering, deleting and restoring books.</summary>
    [Range(1, 100_000)]
    public int EditPermitsPerMinute { get; set; } = 60;
}

public static class RateLimiting
{
    public const string UploadPolicy = "upload";
    public const string GeneratePolicy = "generate";
    public const string EditPolicy = "edit";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(UploadPolicy, context => PerClient(context, o => o.UploadPermitsPerMinute));
            options.AddPolicy(GeneratePolicy, context => PerClient(context, o => o.GeneratePermitsPerMinute));
            options.AddPolicy(EditPolicy, context => PerClient(context, o => o.EditPermitsPerMinute));
            options.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await ApiProblem.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    ApiErrorCodes.RateLimited,
                    "Çok fazla istek.",
                    "Kısa sürede çok fazla istek gönderildi. Lütfen bir dakika bekleyip tekrar deneyin.").ConfigureAwait(false);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, Func<RateLimitingOptions, int> permits)
    {
        var limit = permits(context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value);
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
