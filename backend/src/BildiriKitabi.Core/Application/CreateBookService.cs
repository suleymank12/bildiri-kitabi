using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Core.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Core.Application;

public sealed record CreateBookResult(Book? Book, IReadOnlyList<UploadError> Errors)
{
    public bool Succeeded => Book is not null;
}

/// <summary>
/// Creates a book from an upload, all or nothing: validation → files copied to their permanent keys → one
/// <c>SaveChanges</c> (a single transaction for the book and its papers). If anything fails, the stored files are
/// deleted and nothing is written to the database; the request's temporary folder is always removed.
/// </summary>
public sealed partial class CreateBookService(
    IAppDbContext db,
    IFileStorage storage,
    BookUploadValidator validator,
    TimeProvider timeProvider,
    ILogger<CreateBookService> logger)
{
    public async Task<CreateBookResult> CreateAsync(string? name, IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken)
    {
        using var validation = await validator.ValidateAsync(name, files, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            LogRejected(logger, validation.Errors.Count, files.Count);
            return new CreateBookResult(null, validation.Errors);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var book = new Book(validation.BookName, now);
        foreach (var paper in validation.Papers)
        {
            book.AddPaper(paper.FileName, paper.SizeBytes, paper.Sha256, paper.Title.Text, paper.Title.Source, now);
        }

        // Adding the book assigns the sequential uids, which the storage keys are built from.
        db.Books.Add(book);
        book.AssignStorageKeys();
        try
        {
            for (var i = 0; i < validation.Papers.Count; i++)
            {
                var source = File.OpenRead(validation.Papers[i].TempFilePath);
                await using (source.ConfigureAwait(false))
                {
                    await storage.SaveAsync(book.Papers[i].StorageKey, source, cancellationToken).ConfigureAwait(false);
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The context belongs to this request and tracks nothing else.
            db.ChangeTracker.Clear();

            await storage.DeletePrefixAsync(StorageKeys.BookPrefix(book.Uid), CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        LogCreated(logger, book.Uid, book.Papers.Count);
        return new CreateBookResult(book, []);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} created with {PaperCount} papers")]
    private static partial void LogCreated(ILogger logger, Guid bookUid, int paperCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book upload rejected with {ErrorCount} validation error(s) for {FileCount} file(s)")]
    private static partial void LogRejected(ILogger logger, int errorCount, int fileCount);
}
