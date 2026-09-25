using System.Linq.Expressions;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
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

/// <summary>
/// State-changing commands on an existing book. Each one is a single conditional SQL statement, so two concurrent
/// requests can never both succeed.
/// </summary>
public sealed partial class BookCommandService(
    IAppDbContext db,
    IFileStorage storage,
    IBookGenerationQueue queue,
    TimeProvider timeProvider,
    ILogger<BookCommandService> logger)
{
    /// <summary>
    /// Sets the order of all papers of a book in one <c>UPDATE … SET SiraNo = CASE Id …</c>. SQL Server checks the
    /// unique (KitapId, SiraNo) index at the end of the statement, so swapping numbers needs no temporary values.
    /// </summary>
    public async Task<ReorderOutcome> ReorderAsync(Guid bookId, IReadOnlyList<Guid> paperIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paperIds);

        var book = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new { b.Status, PaperIds = b.Papers.Select(p => p.Id).ToList() })
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

        if (paperIds.Count != book.PaperIds.Count || paperIds.Distinct().Count() != paperIds.Count || !paperIds.All(book.PaperIds.Contains))
        {
            return ReorderOutcome.InvalidList;
        }

        var newOrder = OrderExpression(paperIds);
        var updated = await db.Papers
            .Where(p => p.BookId == bookId && (p.Book.Status == BookStatus.Uploaded || p.Book.Status == BookStatus.Failed))
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Order, newOrder), cancellationToken)
            .ConfigureAwait(false);

        // Zero rows: generation started (or the book was deleted) between the read above and the update.
        return updated == paperIds.Count ? ReorderOutcome.Reordered : ReorderOutcome.Locked;
    }

    /// <summary>
    /// Queues generation with one conditional update (<c>Uploaded</c>/<c>Failed</c> → <c>Queued</c>), then enqueues
    /// the id. If enqueueing fails the book stays <c>Queued</c> and the queued-book sweeper enqueues it again later.
    /// </summary>
    public async Task<StartGenerationOutcome> StartGenerationAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var queued = await db.Books
            .Where(b => b.Id == bookId && (b.Status == BookStatus.Uploaded || b.Status == BookStatus.Failed))
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
                .Where(b => b.Id == bookId)
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
            await queue.EnqueueAsync(bookId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEnqueueFailed(logger, ex, bookId);
        }

        LogQueued(logger, bookId);
        return StartGenerationOutcome.Started;
    }

    /// <summary>Deletes the book (papers cascade) and every stored file under <c>books/{id}/</c>.</summary>
    public async Task<DeleteBookOutcome> DeleteAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var deleted = await db.Books
            .Where(b => b.Id == bookId && b.Status != BookStatus.Queued && b.Status != BookStatus.Processing)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (deleted == 0)
        {
            var exists = await db.Books.AnyAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false);
            return exists ? DeleteBookOutcome.InProgress : DeleteBookOutcome.NotFound;
        }

        await storage.DeletePrefixAsync(StorageKeys.BookPrefix(bookId), CancellationToken.None).ConfigureAwait(false);
        LogDeleted(logger, bookId);
        return DeleteBookOutcome.Deleted;
    }

    /// <summary>Builds <c>p =&gt; p.Id == id1 ? 1 : p.Id == id2 ? 2 : … : p.Order</c>, translated to a SQL CASE.</summary>
    private static Expression<Func<Paper, int>> OrderExpression(IReadOnlyList<Guid> paperIds)
    {
        var paper = Expression.Parameter(typeof(Paper), "p");
        var id = Expression.Property(paper, nameof(Paper.Id));
        Expression body = Expression.Property(paper, nameof(Paper.Order));
        for (var i = paperIds.Count - 1; i >= 0; i--)
        {
            body = Expression.Condition(Expression.Equal(id, Expression.Constant(paperIds[i])), Expression.Constant(i + 1), body);
        }

        return Expression.Lambda<Func<Paper, int>>(body, paper);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId} queued for generation")]
    private static partial void LogQueued(ILogger logger, Guid bookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Book {BookId} could not be enqueued; it stays queued for the startup recovery")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, Guid bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId} deleted")]
    private static partial void LogDeleted(ILogger logger, Guid bookId);
}
