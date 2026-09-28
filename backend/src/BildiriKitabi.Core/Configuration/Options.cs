using System.ComponentModel.DataAnnotations;

namespace BildiriKitabi.Core.Configuration;

/// <summary><c>Books</c> section.</summary>
public sealed class BookOptions
{
    public const string SectionName = "Books";

    public const int DefaultRequiredPaperCount = 10;

    [Range(1, 100)]
    public int RequiredPaperCount { get; set; } = DefaultRequiredPaperCount;
}

/// <summary><c>Upload</c> section: limits applied to every uploaded .docx before it is parsed.</summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    public const long DefaultMaxFileSizeBytes = 10L * 1024 * 1024;

    /// <summary>Also the base of the upload endpoint's request size limit (see <c>BooksController</c>).</summary>
    public const long DefaultMaxTotalSizeBytes = 60L * 1024 * 1024;

    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    [Range(1, long.MaxValue)]
    public long MaxTotalSizeBytes { get; set; } = DefaultMaxTotalSizeBytes;

    [Range(1, 100_000)]
    public int MaxArchiveEntries { get; set; } = 500;

    [Range(1, long.MaxValue)]
    public long MaxUncompressedBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Folder for files while a request is validated; empty means the system temp folder.</summary>
    public string TempPath { get; set; } = string.Empty;
}

/// <summary><c>Generation</c> section: background PDF generation.</summary>
public sealed class GenerationOptions
{
    public const string SectionName = "Generation";

    [Range(1, 64)]
    public int MaxConcurrency { get; set; } = 2;

    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 120;

    [Range(1, 100_000)]
    public int QueueCapacity { get; set; } = 100;

    /// <summary>How often the sweeper looks for books that have been waiting in the queue for too long.</summary>
    [Range(1, 86_400)]
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>A queued book older than this is enqueued again (its message may have been lost).</summary>
    [Range(1, 86_400)]
    public int RequeueStaleAfterSeconds { get; set; } = 60;
}

/// <summary><c>Storage</c> section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder of stored files, relative to the content root unless absolute. Never under wwwroot.</summary>
    [Required(AllowEmptyStrings = false)]
    public string RootPath { get; set; } = "App_Data/storage";
}

/// <summary><c>Queue</c> section: which generation queue the application uses.</summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";
    public const string InMemory = "InMemory";
    public const string RabbitMq = "RabbitMq";

    /// <summary><c>InMemory</c> (one process, the default) or <c>RabbitMq</c> (a durable broker).</summary>
    [Required(AllowEmptyStrings = false)]
    [AllowedValues(InMemory, RabbitMq, ErrorMessage = "Queue:Provider must be 'InMemory' or 'RabbitMq'.")]
    public string Provider { get; set; } = InMemory;

    public bool UsesRabbitMq => Provider == RabbitMq;
}
