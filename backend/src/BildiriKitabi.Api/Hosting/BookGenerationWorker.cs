using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Api.Hosting;

/// <summary>
/// Consumes the generation queue with up to <c>Generation:MaxConcurrency</c> jobs at once, each in its own DI scope
/// (its own DbContext). What happens to a message whose job throws is up to the queue provider.
/// </summary>
public sealed class BookGenerationWorker(
    IBookGenerationQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<GenerationOptions> options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        queue.ConsumeAsync(HandleAsync, options.Value.MaxConcurrency, stoppingToken);

    private async Task HandleAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var handler = scope.ServiceProvider.GetRequiredService<BookGenerationHandler>();
            await handler.HandleAsync(bookUid, cancellationToken).ConfigureAwait(false);
        }
    }
}
