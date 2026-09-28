using System.Linq.Expressions;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Core.Application;

public enum ReorderOutcome
{
    Reordered,
    NotFound,
    Locked,
    InvalidList,
}

public enum StartGenerationOutcome
{
    Started,
    NotFound,
    AlreadyInProgress,
    AlreadyCompleted,
}

public enum DeleteBookOutcome
{
    Deleted,
    NotFound,
    InProgress,
}

public enum RestoreBookOutcome
{
    Restored,
    NotFound,
}

/// <summary>
/// State-changing commands on an existing book. Each one is a single conditional SQL statement, so two concurrent
/// requests can never both succeed.
/// </summary>
public sealed partial class BookCommandService(
    IAppDbContext db,
    IBookGenerationQueue queue,
    TimeProvider timeProvider,
    ILogger<BookCommandService> logger)
{
    /// <summary>
    /// Sets the order of all papers of a book in one <c>UPDATE … SET SiraNo = CASE Uid …</c>. SQL Server checks the
    /// unique (KitapId, SiraNo) index at the end of the statement, so swapping numbers needs no temporary values.
    /// The papers are reached through <c>Books</c>, so a deleted book's papers are out of reach like the book.
    /// </summary>
    public async Task<ReorderOutcome> ReorderAsync(Guid bookUid, IReadOnlyList<Guid> paperUids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paperUids);

        var book = await db.Books.AsNoTracking()
            .Where(b => b.Uid == bookUid)
            .Select(b => new { b.Id, b.Status, PaperUids = b.Papers.Select(p => p.Uid).ToList() })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (book is null)
        {
            return ReorderOutcome.NotFound;
        }

        if (book.Status is not (BookStatus.Uploaded or BookStatus.Failed))
        {
            return ReorderOutcome.Locked;
        }

        if (paperUids.Count != book.PaperUids.Count || paperUids.Distinct().Count() != paperUids.Count || !paperUids.All(book.PaperUids.Contains))
        {
            return ReorderOutcome.InvalidList;
        }

        var newOrder = OrderExpression(paperUids);
        var updated = await db.Books
            .Where(b => b.Id == book.Id && (b.Status == BookStatus.Uploaded || b.Status == BookStatus.Failed))
            .SelectMany(b => b.Papers)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Order, newOrder), cancellationToken)
            .ConfigureAwait(false);

        // Zero rows: generation started (or the book was deleted) between the read above and the update.
        return updated == paperUids.Count ? ReorderOutcome.Reordered : ReorderOutcome.Locked;
    }

    /// <summary>
    /// Queues generation with one conditional update (<c>Uploaded</c>/<c>Failed</c> → <c>Queued</c>), then enqueues
    /// the id. If enqueueing fails the book stays <c>Queued</c> and the queued-book sweeper enqueues it again later.
    /// </summary>
    public async Task<StartGenerationOutcome> StartGenerationAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var queued = await db.Books
            .Where(b => b.Uid == bookUid && (b.Status == BookStatus.Uploaded || b.Status == BookStatus.Failed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.Status, BookStatus.Queued)
                    .SetProperty(b => b.QueuedAt, now)
                    .SetProperty(b => b.Stage, (GenerationStage?)null)
                    .SetProperty(b => b.ProgressPercent, (byte)0)
                    .SetProperty(b => b.ErrorCode, (string?)null)
                    .SetProperty(b => b.ErrorMessage, (string?)null)
                    .SetProperty(b => b.ProcessingStartedAt, (DateTime?)null)
                    .SetProperty(b => b.ProcessingFinishedAt, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);

        if (queued == 0)
        {
            var status = await db.Books.AsNoTracking()
                .Where(b => b.Uid == bookUid)
                .Select(b => (BookStatus?)b.Status)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return status switch
            {
                null => StartGenerationOutcome.NotFound,
                BookStatus.Completed => StartGenerationOutcome.AlreadyCompleted,
                _ => StartGenerationOutcome.AlreadyInProgress,
            };
        }

        try
        {
            await queue.EnqueueAsync(bookUid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEnqueueFailed(logger, ex, bookUid);
        }

        LogQueued(logger, bookUid);
        return StartGenerationOutcome.Started;
    }

    /// <summary>
    /// Deletes the book softly: <c>AktifMi = 0</c> and <c>SilinmeZamani</c> are set; the row, its papers and every stored
    /// file stay, and the global query filter hides the book from then on, so a second delete finds nothing.
    /// </summary>
    public async Task<DeleteBookOutcome> DeleteAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var deleted = await db.Books
            .Where(b => b.Uid == bookUid && b.Status != BookStatus.Queued && b.Status != BookStatus.Processing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.IsActive, false)
                    .SetProperty(b => b.DeletedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
        if (deleted == 0)
        {
            var exists = await db.Books.AnyAsync(b => b.Uid == bookUid, cancellationToken).ConfigureAwait(false);
            return exists ? DeleteBookOutcome.InProgress : DeleteBookOutcome.NotFound;
        }

        LogDeleted(logger, bookUid);
        return DeleteBookOutcome.Deleted;
    }

    /// <summary>Brings a deleted book back as it was when it was deleted; an active or unknown book is not found.</summary>
    public async Task<RestoreBookOutcome> RestoreAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var restored = await db.Books.Deleted()
            .Where(b => b.Uid == bookUid)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.IsActive, true)
                    .SetProperty(b => b.DeletedAt, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);
        if (restored == 0)
        {
            return RestoreBookOutcome.NotFound;
        }

        LogRestored(logger, bookUid);
        return RestoreBookOutcome.Restored;
    }

    /// <summary>Builds <c>p =&gt; p.Uid == uid1 ? 1 : p.Uid == uid2 ? 2 : … : p.Order</c>, translated to a SQL CASE.</summary>
    private static Expression<Func<Paper, int>> OrderExpression(IReadOnlyList<Guid> paperUids)
    {
        var paper = Expression.Parameter(typeof(Paper), "p");
        var uid = Expression.Property(paper, nameof(Paper.Uid));
        Expression body = Expression.Property(paper, nameof(Paper.Order));
        for (var i = paperUids.Count - 1; i >= 0; i--)
        {
            body = Expression.Condition(Expression.Equal(uid, Expression.Constant(paperUids[i])), Expression.Constant(i + 1), body);
        }

        return Expression.Lambda<Func<Paper, int>>(body, paper);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} queued for generation")]
    private static partial void LogQueued(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Book {BookUid} could not be enqueued; it stays queued and the queued-book sweeper sends it later")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} deleted (kept as inactive)")]
    private static partial void LogDeleted(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} restored")]
    private static partial void LogRestored(ILogger logger, Guid bookUid);
}
