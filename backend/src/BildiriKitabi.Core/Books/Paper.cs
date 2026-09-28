using BildiriKitabi.Core.Titles;

namespace BildiriKitabi.Core.Books;

/// <summary>One uploaded paper of a book. The original file name is only shown, never used as a path.</summary>
public sealed class Paper
{
    public const int FileNameMaxLength = 255;
    public const int TitleMaxLength = 500;
    public const int Sha256Length = 32;

    internal Paper(Book book, int order, string fileName, long sizeBytes, byte[] sha256, string title, TitleSource titleSource, DateTime uploadedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(order, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(sha256);
        if (sha256.Length != Sha256Length)
        {
            throw new ArgumentException("A SHA-256 hash is 32 bytes long.", nameof(sha256));
        }

        Book = book;
        Order = order;
        UploadOrder = order;
        OriginalFileName = fileName.Length <= FileNameMaxLength ? fileName : fileName[..FileNameMaxLength];
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Title = title.Length <= TitleMaxLength ? title : string.Concat(title.AsSpan(0, TitleMaxLength - 1), "…");
        TitleSource = titleSource;
        UploadedAt = uploadedAtUtc;
    }

    private Paper()
    {
        Book = null!;
        OriginalFileName = string.Empty;
        Sha256 = [];
        Title = string.Empty;
    }

    /// <summary>Database identity (<c>Bildiriler.Id</c>, <c>int IDENTITY</c>); never leaves the application.</summary>
    public int Id { get; private set; }

    /// <summary>External identity (<c>Bildiriler.Uid</c>): the paper id in API bodies and in its storage key.</summary>
    public Guid Uid { get; private set; }

    /// <summary>Database identity of the book (<c>Bildiriler.KitapId</c> → <c>Kitaplar.Id</c>).</summary>
    public int BookId { get; private set; }

    public Book Book { get; private set; }

    public int Order { get; private set; }

    /// <summary>Position in the upload; never changes, so the client can tell whether the order was edited.</summary>
    public int UploadOrder { get; private set; }

    public string OriginalFileName { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public byte[] Sha256 { get; private set; }

    public string Title { get; private set; }

    public TitleSource TitleSource { get; private set; }

    public int? StartPage { get; private set; }

    public int? EndPage { get; private set; }

    public int RemovedEmailCount { get; private set; }

    public int RemovedPhoneCount { get; private set; }

    public DateTime UploadedAt { get; private set; }

    /// <summary>Stores what the last successful generation found for this paper.</summary>
    public void RecordGeneration(int startPage, int endPage, int removedEmailCount, int removedPhoneCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPage, startPage);
        ArgumentOutOfRangeException.ThrowIfNegative(removedEmailCount);
        ArgumentOutOfRangeException.ThrowIfNegative(removedPhoneCount);
        StartPage = startPage;
        EndPage = endPage;
        RemovedEmailCount = removedEmailCount;
        RemovedPhoneCount = removedPhoneCount;
    }

    /// <summary>Replaces the detected title with one the user typed (already validated).</summary>
    internal void SetManualTitle(string title)
    {
        Title = title;
        TitleSource = TitleSource.Manual;
    }

    /// <summary>Forgets the last generation when the book is reopened for editing.</summary>
    internal void ClearGeneration()
    {
        StartPage = null;
        EndPage = null;
        RemovedEmailCount = 0;
        RemovedPhoneCount = 0;
    }

    internal void AssignStorageKey(string key) => StorageKey = key;
}
