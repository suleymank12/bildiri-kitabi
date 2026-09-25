using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
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
        var processing = await AddAsync(db, b =>
        {
            b.MarkQueued(now.AddSeconds(-600));
            b.MarkProcessing(now.AddSeconds(-590));
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

        var queuedAt = await db.Books.AsNoTracking().Where(b => b.Id == id).Select(b => b.QueuedAt).SingleAsync(TestContext.Current.CancellationToken);
        queuedAt.ShouldBe(Start.UtcDateTime.AddMinutes(-5));
        var queue = new RecordingQueue();
        (await Sweep(db, queue, clock)).ShouldBe(1);
        queue.Ids.ShouldBe([id]);
    }

    private static Task<int> Sweep(AppDbContext db, RecordingQueue queue, TimeProvider clock) =>
        new QueueSweepService(
            db,
            queue,
            Options.Create(new GenerationOptions { RequeueStaleAfterSeconds = 60 }),
            clock,
            NullLogger<QueueSweepService>.Instance).SweepAsync(TestContext.Current.CancellationToken);

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
        return book.Id;
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
