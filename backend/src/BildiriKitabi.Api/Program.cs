using System.Text.Json;
using System.Text.Json.Serialization;
using BildiriKitabi.Api.Hosting;
using BildiriKitabi.Api.Http;
using BildiriKitabi.Api.Problems;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Infrastructure;
using BildiriKitabi.Infrastructure.Persistence;
using BildiriKitabi.Infrastructure.Queue;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

AddValidatedOptions<StorageOptions>(StorageOptions.SectionName);
AddValidatedOptions<UploadOptions>(UploadOptions.SectionName);
AddValidatedOptions<GenerationOptions>(GenerationOptions.SectionName);
AddValidatedOptions<BookOptions>(BookOptions.SectionName);
AddValidatedOptions<RateLimitingOptions>(RateLimitingOptions.SectionName);
AddValidatedOptions<QueueOptions>(QueueOptions.SectionName);
AddValidatedOptions<RabbitMqOptions>(RabbitMqOptions.SectionName);

builder.Services.AddBookApplication(builder.Environment.ContentRootPath);

// JSON is camelCase with enums as strings and numbers as plain numbers, both for MVC and for the OpenAPI
// document (which reads the minimal-API options), so generated client types match the wire format.
builder.Services
    .AddControllers(options =>
    {
        options.OutputFormatters.RemoveType<StringOutputFormatter>();
        options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>().Single().SupportedMediaTypes.Remove("text/json");
        options.InputFormatters.OfType<SystemTextJsonInputFormatter>().Single().SupportedMediaTypes.Remove("text/json");
    })
    .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
builder.Services.AddApiProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddApiRateLimiting();
builder.Services.AddApiForwardedHeaders();
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(
        HeaderNames.ContentDisposition,
        HeaderNames.ContentLength,
        HeaderNames.ContentRange,
        HeaderNames.AcceptRanges,
        HeaderNames.ETag,
        HeaderNames.Location)));

// Startup recovery must run before the worker starts reading the queue; hosted services start in this order.
builder.Services.AddHostedService<GenerationRecoveryService>();
builder.Services.AddHostedService<BookGenerationWorker>();
builder.Services.AddHostedService<QueuedBookSweeper>();

var app = builder.Build();

if (ForwardedHeadersSetup.IsEnabled(app.Configuration))
{
    // First, so that everything after it (HTTPS redirection, rate limiting, logging) sees the real client.
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
if (app.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Bildiri Kitabı API"));
}

app.MapControllers();
// The RabbitMQ check only exists when RabbitMQ is the queue provider.
var usesRabbitMq = app.Services.GetRequiredService<IOptions<QueueOptions>>().Value.UsesRabbitMq;
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Name != "rabbitmq" || usesRabbitMq,
    ResponseWriter = HealthResponse.WriteAsync,
});

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

    // End-to-end test runs start from an empty database. Honoured only in Development, never in production.
    if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:ResetOnStartup", false))
    {
        await database.EnsureDeletedAsync();
    }

    // SQL Server may still be starting (container or machine restart): wait for it for up to a minute.
    var retry = new StartupRetry(app.Services.GetRequiredService<TimeProvider>(), app.Logger);
    await DatabaseStartup.MigrateAsync(database, retry, app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter());
    options.NumberHandling = JsonNumberHandling.Strict;
}

void AddValidatedOptions<TOptions>(string section)
    where TOptions : class =>
    builder.Services.AddOptions<TOptions>().Bind(builder.Configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
