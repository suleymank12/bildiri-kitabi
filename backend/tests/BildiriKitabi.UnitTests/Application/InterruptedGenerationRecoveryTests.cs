using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BildiriKitabi.UnitTests.Application;

/// <summary>Recovery of books stuck in <c>Processing</c>, with a fake clock.</summary>
public sealed class InterruptedGenerationRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 7, 0, 0, TimeSpan.Zero);

    // Defaults: 120 s time limit + 60 s requeue delay.
    private static readonly TimeSpan StuckAfter = QueueSweepService.StuckAfter(new GenerationOptions());

    [Fact]
    public void The_stuck_threshold_is_the_time_limit_plus_the_requeue_delay()
    {
        StuckAfter.ShouldBe(TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void A_run_that_has_not_expired_is_left_alone()
    {
        var clock = new FakeTimeProvider(Start);
        var book = Processing(clock);

        clock.Advance(TimeSpan.FromSeconds(179));

        IsStuck(book, clock).ShouldBeFalse();
    }

    [Fact]
    public void An_expired_run_is_queued_again_the_first_time()
    {
        var clock = new FakeTimeProvider(Start);
        var book = Processing(clock);

        clock.Advance(TimeSpan.FromSeconds(180));

        IsStuck(book, clock).ShouldBeTrue();
        InterruptedGenerationRecovery.Decide(book).ShouldBe(RecoveryAction.Requeue);
        book.ReturnToQueue(clock.GetUtcNow().UtcDateTime);
        book.Status.ShouldBe(BookStatus.Queued);
        book.WasInterrupted.ShouldBeTrue();
    }

    [Fact]
    public void A_run_that_gets_stuck_again_fails_as_interrupted()
    {
        var clock = new FakeTimeProvider(Start);
        var book = Processing(clock);
        clock.Advance(TimeSpan.FromSeconds(180));
        book.ReturnToQueue(clock.GetUtcNow().UtcDateTime);
        book.MarkProcessing(clock.GetUtcNow().UtcDateTime);

        clock.Advance(TimeSpan.FromSeconds(179));
        IsStuck(book, clock).ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));

        IsStuck(book, clock).ShouldBeTrue();
        InterruptedGenerationRecovery.Decide(book).ShouldBe(RecoveryAction.Fail);
    }

    [Fact]
    public void A_retry_by_the_user_gets_a_new_extra_run()
    {
        var clock = new FakeTimeProvider(Start);
        var book = Processing(clock);
        book.ReturnToQueue(clock.GetUtcNow().UtcDateTime);
        book.MarkProcessing(clock.GetUtcNow().UtcDateTime);
        book.MarkFailed(BookErrorCodes.GenerationInterrupted, InterruptedGenerationRecovery.InterruptedMessage, clock.GetUtcNow().UtcDateTime);

        clock.Advance(TimeSpan.FromMinutes(5));
        book.MarkQueued(clock.GetUtcNow().UtcDateTime);
        book.MarkProcessing(clock.GetUtcNow().UtcDateTime);

        InterruptedGenerationRecovery.Decide(book).ShouldBe(RecoveryAction.Requeue);
    }

    [Theory]
    [InlineData(BookStatus.Uploaded)]
    [InlineData(BookStatus.Queued)]
    [InlineData(BookStatus.Completed)]
    [InlineData(BookStatus.Failed)]
    public void Only_processing_books_are_ever_stuck(BookStatus status)
    {
        InterruptedGenerationRecovery.IsStuck(status, Start.UtcDateTime, Start.UtcDateTime.AddHours(1), StuckAfter).ShouldBeFalse();
    }

    [Fact]
    public async Task Writing_the_failure_does_not_throw_when_the_database_is_unreachable()
    {
        // Port 1 on the loopback address refuses at once, so every query fails with a SqlException.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=Yok;User Id=x;Password=y;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=True")
            .Options;
        await using var db = new AppDbContext(options);
        var handler = new BookGenerationHandler(
            db,
            new NoStorage(),
            null!,
            Options.Create(new GenerationOptions()),
            new FakeTimeProvider(Start),
            NullLogger<BookGenerationHandler>.Instance);

        await Should.NotThrowAsync(() => handler.FailAsync(Guid.NewGuid(), BookErrorCodes.InternalError, BookGenerationHandler.UnexpectedErrorMessage));
    }

    private static bool IsStuck(Book book, TimeProvider clock) =>
        InterruptedGenerationRecovery.IsStuck(book.Status, book.ProcessingStartedAt, clock.GetUtcNow().UtcDateTime, StuckAfter);

    private static Book Processing(TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var book = new Book("Örnek Bilim Kongresi 2026", now);
        book.MarkQueued(now);
        book.MarkProcessing(now);
        return book;
    }

    private sealed class NoStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new FileNotFoundException(key);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
