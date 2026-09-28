using System.Linq.Expressions;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Fonts;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Core.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Core.Application;

public enum EditBookOutcome
{
    /// <summary>The change was saved; a completed book is back to <c>Uploaded</c> and its PDF is deleted.</summary>
    Edited,

    /// <summary>The same name or the same order: nothing was written.</summary>
    Unchanged,
    NotFound,

    /// <summary>The book is queued or being generated.</summary>
    Busy,

    /// <summary>Another request changed the book between reading and saving (row version check).</summary>
    Conflict,
    InvalidName,
    InvalidList,

    /// <summary>The book exists but has no paper with that uid.</summary>
    PaperNotFound,
    InvalidTitle,
}

public sealed record EditBookResult(EditBookOutcome Outcome, BookNameValidation? Name = null, PaperTitleValidation? Title = null);

/// <summary>
/// Renames a book, reorders its papers and changes paper titles, also after it was generated: a completed book is reopened through
/// <see cref="Book.ReopenForEditing"/> and its PDF is deleted once the change is saved. The book row is always written
/// with a row version check (<c>SatirVersiyonu</c>), so of two edits that overlap only the first one succeeds.
/// </summary>
public sealed partial class BookEditService(
    IAppDbContext db,
    IFileStorage storage,
    IGlyphCoverage glyphCoverage,
    ILogger<BookEditService> logger)
{
    /// <summary>Validates the name like an upload does, then renames the book.</summary>
    public async Task<EditBookResult> RenameAsync(Guid bookUid, string? name, CancellationToken cancellationToken)
    {
        var book = await db.Books.Include(b => b.Papers).FirstOrDefaultAsync(b => b.Uid == bookUid, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return new EditBookResult(EditBookOutcome.NotFound);
        }

        var validation = BookNameValidator.Validate(name, glyphCoverage);
        if (!validation.IsValid)
        {
            return new EditBookResult(EditBookOutcome.InvalidName, validation);
        }

        if (!book.IsEditable)
        {
            return new EditBookResult(EditBookOutcome.Busy);
        }

        var pdf = book.PdfStorageKey;
        if (!book.Rename(validation.Name))
        {
            return new EditBookResult(EditBookOutcome.Unchanged);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EditBookResult(EditBookOutcome.Conflict);
        }

        await DeleteObsoletePdfAsync(book, pdf).ConfigureAwait(false);
        LogRenamed(logger, bookUid);
        return new EditBookResult(EditBookOutcome.Edited);
    }

    /// <summary>
    /// Replaces a paper title with one the user typed (<see cref="PaperTitleValidator"/>); the paper becomes
    /// <see cref="Titles.TitleSource.Manual"/>. The same title changes nothing.
    /// </summary>
    public async Task<EditBookResult> UpdatePaperTitleAsync(Guid bookUid, Guid paperUid, string? title, CancellationToken cancellationToken)
    {
        var book = await db.Books.Include(b => b.Papers).FirstOrDefaultAsync(b => b.Uid == bookUid, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return new EditBookResult(EditBookOutcome.NotFound);
        }

        var paper = book.Papers.FirstOrDefault(p => p.Uid == paperUid);
        if (paper is null)
        {
            return new EditBookResult(EditBookOutcome.PaperNotFound);
        }

        var validation = PaperTitleValidator.Validate(title, glyphCoverage);
        if (!validation.IsValid)
        {
            return new EditBookResult(EditBookOutcome.InvalidTitle, Title: validation);
        }

        if (!book.IsEditable)
        {
            return new EditBookResult(EditBookOutcome.Busy);
        }

        var pdf = book.PdfStorageKey;
        if (!book.UpdatePaperTitle(paper, validation.Title))
        {
            return new EditBookResult(EditBookOutcome.Unchanged);
        }

        // Only the paper row changes for an uploaded book; the book row is written too so the row version check
        // catches an overlapping edit.
        db.Books.Entry(book).Property(b => b.Name).IsModified = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EditBookResult(EditBookOutcome.Conflict);
        }

        await DeleteObsoletePdfAsync(book, pdf).ConfigureAwait(false);
        LogPaperRetitled(logger, bookUid, paperUid);
        return new EditBookResult(EditBookOutcome.Edited);
    }

    /// <summary>
    /// Sets the order of all papers. The book row is written first (reopened, or at least its row version renewed) and
    /// then the papers in one <c>UPDATE … SET SiraNo = CASE Uid …</c>, both in one transaction. SQL Server checks the
    /// unique (KitapId, SiraNo) index at the end of that statement, so swapping numbers needs no temporary values.
    /// The papers are reached through <c>Books</c>, so a deleted book's papers are out of reach like the book.
    /// </summary>
    public async Task<EditBookResult> ReorderAsync(Guid bookUid, IReadOnlyList<Guid> paperUids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paperUids);

        var book = await db.Books.Include(b => b.Papers).FirstOrDefaultAsync(b => b.Uid == bookUid, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return new EditBookResult(EditBookOutcome.NotFound);
        }

        if (!book.IsEditable)
        {
            return new EditBookResult(EditBookOutcome.Busy);
        }

        var current = book.Papers.OrderBy(p => p.Order).Select(p => p.Uid).ToList();
        if (paperUids.Count != current.Count || paperUids.Distinct().Count() != paperUids.Count || !paperUids.All(current.Contains))
        {
            return new EditBookResult(EditBookOutcome.InvalidList);
        }

        if (paperUids.SequenceEqual(current))
        {
            return new EditBookResult(EditBookOutcome.Unchanged);
        }

        var pdf = book.PdfStorageKey;
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            book.ReopenForEditing();

            // Written even when reopening changed nothing (an uploaded book): the row version check and the new row
            // version are what make an overlapping edit of the same book fail.
            db.Books.Entry(book).Property(b => b.Name).IsModified = true;
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new EditBookResult(EditBookOutcome.Conflict);
            }

            await db.Books
                .Where(b => b.Id == book.Id)
                .SelectMany(b => b.Papers)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Order, OrderExpression(paperUids)), cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await DeleteObsoletePdfAsync(book, pdf).ConfigureAwait(false);
        LogReordered(logger, bookUid);
        return new EditBookResult(EditBookOutcome.Edited);
    }

    /// <summary>Deletes the PDF of a book that was reopened. A file left behind is never served and is replaced by the next PDF.</summary>
    private async Task DeleteObsoletePdfAsync(Book book, string? pdf)
    {
        if (pdf is null || book.PdfStorageKey is not null)
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(pdf, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            LogPdfDeleteFailed(logger, ex, book.Uid);
        }

        LogReopened(logger, book.Uid);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} renamed")]
    private static partial void LogRenamed(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Title of paper {PaperUid} of book {BookUid} changed")]
    private static partial void LogPaperRetitled(ILogger logger, Guid bookUid, Guid paperUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Papers of book {BookUid} reordered")]
    private static partial void LogReordered(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed book {BookUid} reopened for editing; its PDF was removed")]
    private static partial void LogReopened(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Obsolete PDF of book {BookUid} could not be deleted")]
    private static partial void LogPdfDeleteFailed(ILogger logger, Exception exception, Guid bookUid);
}
