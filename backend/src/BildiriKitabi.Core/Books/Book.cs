using BildiriKitabi.Core.Storage;
using BildiriKitabi.Core.Titles;

namespace BildiriKitabi.Core.Books;

/// <summary>
/// A book of papers and the state of its PDF generation. State changes only through the methods below; an
/// invalid transition throws <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class Book
{
    public const int NameMaxLength = 150;
    public const int ErrorCodeMaxLength = 50;
    public const int ErrorMessageMaxLength = 500;
    public const int StorageKeyMaxLength = 260;

    private readonly List<Paper> _papers = [];

    public Book(string name, DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Status = BookStatus.Uploaded;
        CreatedAt = createdAtUtc;
    }

    private Book()
    {
        Name = string.Empty;
    }

    /// <summary>
    /// Database identity (<c>Kitaplar.Id</c>, <c>int IDENTITY</c>), assigned by SQL Server on insert. Used only for keys
    /// and joins inside the database; it is guessable, so it never leaves the application.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// External identity (<c>Kitaplar.Uid</c>), assigned by EF Core's sequential GUID generator when the book is added to
    /// the context. API addresses and bodies, queue messages, logs and storage keys use only this value.
    /// </summary>
    public Guid Uid { get; private set; }

    public string Name { get; private set; }

    public BookStatus Status { get; private set; }

    public GenerationStage? Stage { get; private set; }

    public byte ProgressPercent { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? PdfStorageKey { get; private set; }

    public long? PdfSizeBytes { get; private set; }

    public int? PageCount { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ProcessingStartedAt { get; private set; }

    /// <summary>When the book was last put in the queue; the sweeper enqueues it again if it waits too long.</summary>
    public DateTime? QueuedAt { get; private set; }

    /// <summary>
    /// When the last run ended. While the book is <c>Queued</c> or <c>Processing</c> it is set only if an earlier run
    /// of the same attempt was interrupted (see <see cref="ReturnToQueue"/>); queueing by the user clears it.
    /// </summary>
    public DateTime? ProcessingFinishedAt { get; private set; }

    /// <summary>True when a run of the current attempt was already interrupted and the book got its one more run.</summary>
    public bool WasInterrupted => Status is BookStatus.Queued or BookStatus.Processing && ProcessingFinishedAt is not null;

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<Paper> Papers => _papers;

    /// <summary>True while the paper order may change and generation may start.</summary>
    public bool IsEditable => Status is BookStatus.Uploaded or BookStatus.Failed;

    /// <summary>True while a worker owns the book or is about to.</summary>
    public bool IsBusy => Status is BookStatus.Queued or BookStatus.Processing;

    public Paper AddPaper(string fileName, long sizeBytes, byte[] sha256, string title, TitleSource titleSource, DateTime uploadedAtUtc)
    {
        EnsureStatus(nameof(AddPaper), BookStatus.Uploaded);
        var paper = new Paper(this, _papers.Count + 1, fileName, sizeBytes, sha256, title, titleSource, uploadedAtUtc);
        _papers.Add(paper);
        return paper;
    }

    /// <summary>Fills the storage keys once the context has assigned the book and paper uids.</summary>
    public void AssignStorageKeys()
    {
        if (Uid == Guid.Empty || _papers.Any(p => p.Uid == Guid.Empty))
        {
            throw new InvalidOperationException("Storage keys need the uids; add the book to the context first.");
        }

        foreach (var paper in _papers)
        {
            paper.AssignStorageKey(StorageKeys.Source(Uid, paper.Uid));
        }
    }

    public void MarkQueued(DateTime queuedAtUtc)
    {
        EnsureStatus(nameof(MarkQueued), BookStatus.Uploaded, BookStatus.Failed);
        Status = BookStatus.Queued;
        QueuedAt = queuedAtUtc;
        Stage = null;
        ProgressPercent = 0;
        ErrorCode = null;
        ErrorMessage = null;
        ProcessingStartedAt = null;
        ProcessingFinishedAt = null;
    }

    public void MarkProcessing(DateTime startedAtUtc)
    {
        EnsureStatus(nameof(MarkProcessing), BookStatus.Queued);
        Status = BookStatus.Processing;
        Stage = null;
        ProgressPercent = 0;
        ProcessingStartedAt = startedAtUtc;
    }

    /// <summary>
    /// Puts an interrupted run back in the queue and records when it ended, which marks the book as
    /// <see cref="WasInterrupted"/>.
    /// </summary>
    public void ReturnToQueue(DateTime queuedAtUtc)
    {
        EnsureStatus(nameof(ReturnToQueue), BookStatus.Processing);
        Status = BookStatus.Queued;
        QueuedAt = queuedAtUtc;
        Stage = null;
        ProgressPercent = 0;
        ProcessingStartedAt = null;
        ProcessingFinishedAt = queuedAtUtc;
    }

    /// <summary>Progress never goes backwards and stays below 100 until the book is completed.</summary>
    public void ReportProgress(GenerationStage stage, int percent)
    {
        EnsureStatus(nameof(ReportProgress), BookStatus.Processing);
        Stage = stage;
        ProgressPercent = (byte)Math.Clamp(percent, ProgressPercent, 99);
    }

    public void MarkCompleted(string pdfStorageKey, long pdfSizeBytes, int pageCount, DateTime finishedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfStorageKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pdfSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        EnsureStatus(nameof(MarkCompleted), BookStatus.Processing);
        Status = BookStatus.Completed;
        Stage = null;
        ProgressPercent = 100;
        PdfStorageKey = pdfStorageKey;
        PdfSizeBytes = pdfSizeBytes;
        PageCount = pageCount;
        ProcessingFinishedAt = finishedAtUtc;
    }

    public void MarkFailed(string errorCode, string errorMessage, DateTime finishedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        EnsureStatus(nameof(MarkFailed), BookStatus.Queued, BookStatus.Processing);
        Status = BookStatus.Failed;
        ErrorCode = Truncate(errorCode, ErrorCodeMaxLength);
        ErrorMessage = Truncate(errorMessage, ErrorMessageMaxLength);
        ProcessingFinishedAt = finishedAtUtc;
    }

    private void EnsureStatus(string operation, params BookStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"{operation} is not allowed while the book is {Status}.");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}
