using BildiriKitabi.Core.Titles;

namespace BildiriKitabi.Core.Uploads;

/// <summary>A file as received from the client; <see cref="FileName"/> is untrusted and only ever displayed.</summary>
public sealed record UploadedFile(string FileName, long Length, Func<Stream> OpenRead);

/// <summary>One validation problem; <see cref="Field"/> or <see cref="FileName"/> says what it is about.</summary>
public sealed record UploadError(string Code, string Message, string? Field = null, string? FileName = null);

/// <summary>A paper that passed every check, waiting in the request's temporary folder.</summary>
public sealed record ValidatedPaper(string FileName, string TempFilePath, long SizeBytes, byte[] Sha256, DetectedTitle Title);

/// <summary>
/// Result of validating a create-book request. Owns the temporary folder of the request and deletes it on dispose,
/// whatever the outcome.
/// </summary>
public sealed class UploadValidation : IDisposable
{
    private readonly string _tempDirectory;

    internal UploadValidation(string bookName, IReadOnlyList<ValidatedPaper> papers, IReadOnlyList<UploadError> errors, string tempDirectory)
    {
        BookName = bookName;
        Papers = papers;
        Errors = errors;
        _tempDirectory = tempDirectory;
    }

    public string BookName { get; }

    public IReadOnlyList<ValidatedPaper> Papers { get; }

    public IReadOnlyList<UploadError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the folder only holds copies of this request's uploads and never becomes reachable.
        }
    }
}
