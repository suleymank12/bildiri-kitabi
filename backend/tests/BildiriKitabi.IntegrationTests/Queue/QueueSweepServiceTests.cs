using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Infrastructure.Persistence;
using BildiriKitabi.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BildiriKitabi.IntegrationTests.Queue;

/// <summary>The sweeper against a real SQL Server database with a fake clock.</summary>
public sealed class QueueSweepServiceTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueues_only_queued_books_that_waited_long_enough_and_restarts_their_wait()
    {
        sql.EnsureAvailable();
        var clock = new FakeTimeProvider(Start);
        await using var db = await NewDatabaseAsync();
        var now = Start.UtcDateTime;
        var fresh = await AddAsync(db, b => b.MarkQueued(now.AddSeconds(-30)));
        var stale = await AddAsync(db, b => b.MarkQueued(now.AddSeconds(-120)));
        // Running for 10 s; it only counts as stuck after the time limit plus the requeue delay (180 s).
        var processing = await AddAsync(db, b =>
        {
            b.MarkQueued(now.AddSeconds(-20));
            b.MarkProcessing(now.AddSeconds(-10));
        });
        var uploaded = await AddAsync(db, _ => { });
        var queue = new RecordingQueue();

        (await Sweep(db, queue, clock)).ShouldBe(1);
        queue.Ids.ShouldBe([stale]);

        // 31 s later the fresh book has waited 61 s; the stale one was re-enqueued at the first sweep and waits again.
        clock.Advance(TimeSpan.FromSeconds(31));
        (await Sweep(db, queue, clock)).ShouldBe(1);
        queue.Ids.ShouldBe([stale, fresh]);

        clock.Advance(TimeSpan.FromSeconds(60));
        (await Sweep(db, queue, clock)).ShouldBe(2);
        queue.Ids.ShouldNotContain(processing);
        queue.Ids.ShouldNotContain(uploaded);
    }

    [Fact]
    public async Task Leaves_the_books_waiting_when_the_queue_is_unavailable()
    {
        sql.EnsureAvailable();
        var clock = new FakeTimeProvider(Start);
        await using var db = await NewDatabaseAsync();
        var id = await AddAsync(db, b => b.MarkQueued(Start.UtcDateTime.AddMinutes(-5)));

        (await Sweep(db, new RecordingQueue { Fail = true }, clock)).ShouldBe(0);

        var queuedAt = await db.Books.AsNoTracking().Where(b => b.Uid == id).Select(b => b.QueuedAt).SingleAsync(TestContext.Current.CancellationToken);
        queuedAt.ShouldBe(Start.UtcDateTime.AddMinutes(-5));
        var queue = new RecordingQueue();
        (await Sweep(db, queue, clock)).ShouldBe(1);
        queue.Ids.ShouldBe([id]);
    }

    [Fact]
    public async Task A_stuck_processing_book_is_queued_once_more_and_then_fails_as_interrupted()
    {
        sql.EnsureAvailable();
        var clock = new FakeTimeProvider(Start);
        await using var db = await NewDatabaseAsync();
        var now = Start.UtcDateTime;
        var running = await AddAsync(db, b =>
        {
            b.MarkQueued(now.AddSeconds(-200));
            b.MarkProcessing(now.AddSeconds(-179));
        });
        var stuck = await AddAsync(db, b =>
        {
            b.MarkQueued(now.AddSeconds(-200));
            b.MarkProcessing(now.AddSeconds(-180));
        });
        var queue = new RecordingQueue();

        (await Sweep(db, queue, clock)).ShouldBe(1);
        queue.Ids.ShouldBe([stuck]);
        (await LoadAsync(db, running)).Status.ShouldBe(BookStatus.Processing);
        var requeued = await LoadAsync(db, stuck);
        requeued.Status.ShouldBe(BookStatus.Queued);
        requeued.QueuedAt.ShouldBe(now);
        requeued.ProcessingStartedAt.ShouldBeNull();

        // The handler claims it again (as TryClaimAsync does) and the second run gets stuck too.
        await db.Books.Where(b => b.Uid == stuck).ExecuteUpdateAsync(
            setters => setters.SetProperty(b => b.Status, BookStatus.Processing).SetProperty(b => b.ProcessingStartedAt, now),
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(180));

        // The other book has now run for 359 s: its first recovery queues it again, the second one fails the book.
        (await Sweep(db, queue, clock)).ShouldBe(1);
        queue.Ids.ShouldBe([stuck, running]);
        (await LoadAsync(db, running)).Status.ShouldBe(BookStatus.Queued);
        var failed = await LoadAsync(db, stuck);
        failed.Status.ShouldBe(BookStatus.Failed);
        failed.ErrorCode.ShouldBe(BookErrorCodes.GenerationInterrupted);
        failed.ErrorMessage.ShouldBe(InterruptedGenerationRecovery.InterruptedMessage);
    }

    private static async Task<Book> LoadAsync(AppDbContext db, Guid id)
    {
        db.ChangeTracker.Clear();
        return await db.Books.AsNoTracking().SingleAsync(b => b.Uid == id, TestContext.Current.CancellationToken);
    }

    private static Task<int> Sweep(AppDbContext db, RecordingQueue queue, TimeProvider clock)
    {
        var options = Options.Create(new GenerationOptions { TimeoutSeconds = 120, RequeueStaleAfterSeconds = 60 });
        var recovery = new InterruptedGenerationRecovery(db, new NoStorage(), clock, NullLogger<InterruptedGenerationRecovery>.Instance);
        return new QueueSweepService(db, queue, recovery, options, clock, NullLogger<QueueSweepService>.Instance)
            .SweepAsync(TestContext.Current.CancellationToken);
    }

    private async Task<AppDbContext> NewDatabaseAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(sql.NewDatabase()).Options);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static async Task<Guid> AddAsync(AppDbContext db, Action<Book> arrange)
    {
        var book = new Book("Süpürücü Denemesi", Start.UtcDateTime.AddHours(-1));
        arrange(book);
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return book.Uid;
    }

    private sealed class NoStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new FileNotFoundException(key);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingQueue : IBookGenerationQueue
    {
        public List<Guid> Ids { get; } = [];

        public bool Fail { get; init; }

        public ValueTask EnqueueAsync(Guid bookId, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Kuyruk kullanılamıyor.");
            }

            Ids.Add(bookId);
            return ValueTask.CompletedTask;
        }

        public Task ConsumeAsync(Func<Guid, CancellationToken, Task> handler, int maxConcurrency, CancellationToken stoppingToken) =>
            Task.CompletedTask;
    }
}
