using System.Text.Json.Serialization;
using BildiriKitabi.Api.Hosting;
using BildiriKitabi.Api.Http;
using BildiriKitabi.Api.Problems;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Infrastructure;
using BildiriKitabi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

AddValidatedOptions<StorageOptions>(StorageOptions.SectionName);
AddValidatedOptions<UploadOptions>(UploadOptions.SectionName);
AddValidatedOptions<GenerationOptions>(GenerationOptions.SectionName);
AddValidatedOptions<BookOptions>(BookOptions.SectionName);
AddValidatedOptions<RateLimitingOptions>(RateLimitingOptions.SectionName);

builder.Services.AddBookApplication(builder.Environment.ContentRootPath);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddApiProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddApiRateLimiting();
builder.Services.AddApiForwardedHeaders();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

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
app.MapHealthChecks("/health");

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

void AddValidatedOptions<TOptions>(string section)
    where TOptions : class =>
    builder.Services.AddOptions<TOptions>().Bind(builder.Configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
