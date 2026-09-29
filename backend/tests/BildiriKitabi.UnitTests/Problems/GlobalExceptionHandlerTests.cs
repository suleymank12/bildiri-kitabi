using System.Text.Json;
using BildiriKitabi.Api.Problems;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.UnitTests.Problems;

/// <summary>
/// A request the client cancelled is not a server error, whatever the exception: SqlClient reports a cancelled
/// query as a SqlException ("Operation cancelled by user"), not as an OperationCanceledException.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    // Stands in for the SqlException SqlClient throws when the request's token cancels a running query.
    private static readonly InvalidOperationException CancelledQuery = new("Operation cancelled by user.");

    [Fact]
    public async Task Any_exception_of_a_request_the_client_cancelled_is_not_logged_or_answered()
    {
        var (handler, logger) = Handler();
        var context = Context(aborted: true);

        (await handler.TryHandleAsync(context, CancelledQuery, TestContext.Current.CancellationToken)).ShouldBeTrue();

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Response.Body.Length.ShouldBe(0);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_cancellation_of_a_request_the_client_cancelled_is_not_logged_or_answered()
    {
        var (handler, logger) = Handler();
        var context = Context(aborted: true);

        (await handler.TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken)).ShouldBeTrue();

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Response.Body.Length.ShouldBe(0);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task The_same_exception_while_the_client_waits_is_logged_as_an_error_and_answered_with_500()
    {
        var (handler, logger) = Handler();
        var context = Context(aborted: false);

        (await handler.TryHandleAsync(context, CancelledQuery, TestContext.Current.CancellationToken)).ShouldBeTrue();

        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        var problem = Body(context);
        problem.GetProperty("status").GetInt32().ShouldBe(500);
        problem.GetProperty("code").GetString().ShouldBe(ApiErrorCodes.InternalError);
        problem.GetRawText().ShouldNotContain("Operation cancelled");
        var error = logger.Entries.ShouldHaveSingleItem();
        error.Level.ShouldBe(LogLevel.Error);
        error.Exception.ShouldBeSameAs(CancelledQuery);
    }

    [Fact]
    public async Task A_request_over_the_size_limit_is_answered_with_413()
    {
        var (handler, logger) = Handler();
        var context = Context(aborted: false);

        (await handler.TryHandleAsync(context, new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge), TestContext.Current.CancellationToken)).ShouldBeTrue();

        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        Body(context).GetProperty("code").GetString().ShouldBe(ApiErrorCodes.RequestTooLarge);
        logger.Entries.ShouldBeEmpty();
    }

    private static (GlobalExceptionHandler Handler, ListLogger Logger) Handler()
    {
        var logger = new ListLogger();
        return (new GlobalExceptionHandler(logger), logger);
    }

    private static DefaultHttpContext Context(bool aborted)
    {
        var services = new ServiceCollection();
        services.AddApiProblemDetails();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            RequestAborted = new CancellationToken(canceled: aborted),
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/books";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonElement Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement.Clone();
    }

    private sealed class ListLogger : ILogger<GlobalExceptionHandler>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}
