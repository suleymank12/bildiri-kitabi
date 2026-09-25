using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Core.Sanitization;
using BildiriKitabi.Core.Titles;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Core.Uploads;

/// <summary>
/// Validates a create-book request. Every problem is collected, so the user sees all of them in one response.
/// Files are streamed to a per-request temporary folder while their SHA-256 is computed; nothing is kept in memory.
/// Papers that pass are read, sanitized and given a detected title.
/// </summary>
public sealed class BookUploadValidator(IDocxReader reader, IOptions<UploadOptions> uploadOptions, IOptions<BookOptions> bookOptions)
{
    private const string DocxExtension = ".docx";
    private const int CopyBufferSize = 81920;
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly UploadOptions _upload = uploadOptions.Value;
    private readonly BookOptions _books = bookOptions.Value;

    public async Task<UploadValidation> ValidateAsync(string? bookName, IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var errors = new List<UploadError>();
        var name = BookNameValidator.Validate(bookName);
        if (!name.IsValid)
        {
            errors.Add(new UploadError(name.ErrorCode!, name.ErrorMessage!, Field: "name"));
        }

        if (files.Count != _books.RequiredPaperCount)
        {
            errors.Add(new UploadError(
                UploadErrorCodes.PaperCountInvalid,
                $"Tam olarak {_books.RequiredPaperCount} bildiri dosyası yüklenmelidir; {files.Count} dosya seçildi.",
                Field: "files"));
        }

        var totalSize = files.Sum(f => Math.Max(0, f.Length));
        if (totalSize > _upload.MaxTotalSizeBytes)
        {
            errors.Add(new UploadError(
                UploadErrorCodes.TotalSizeTooLarge,
                $"Dosyaların toplam boyutu {FormatMegabytes(_upload.MaxTotalSizeBytes)} sınırını aşıyor.",
                Field: "files"));
        }

        var tempDirectory = Path.Combine(TempRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var papers = new List<ValidatedPaper>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var paper = await ValidateFileAsync(files[i], i, tempDirectory, errors, cancellationToken).ConfigureAwait(false);
                if (paper is not null)
                {
                    papers.Add(paper);
                }
            }

            AddDuplicateErrors(papers, errors);
        }
        catch
        {
            new UploadValidation(name.Name, [], errors, tempDirectory).Dispose();
            throw;
        }

        return new UploadValidation(name.Name, papers, errors, tempDirectory);
    }

    /// <summary>Only the last path segment, without control characters, at most 255 characters. Never used as a path.</summary>
    public static string ToDisplayName(string? fileName, int index)
    {
        var name = fileName ?? string.Empty;
        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > Books.Paper.FileNameMaxLength)
        {
            name = name[..Books.Paper.FileNameMaxLength];
        }

        return name.Length > 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{index + 1}. dosya");
    }

    private async Task<ValidatedPaper?> ValidateFileAsync(
        UploadedFile file, int index, string tempDirectory, List<UploadError> errors, CancellationToken cancellationToken)
    {
        var name = ToDisplayName(file.FileName, index);

        void Fail(string code, string message) => errors.Add(new UploadError(code, message, FileName: name));

        if (!string.Equals(Path.GetExtension(name), DocxExtension, StringComparison.OrdinalIgnoreCase))
        {
            Fail(UploadErrorCodes.FileExtensionInvalid, $"{name} yüklenemez; yalnızca .docx uzantılı Word belgeleri kabul edilir.");
            return null;
        }

        if (file.Length <= 0)
        {
            Fail(UploadErrorCodes.FileEmpty, $"{name} boş bir dosya.");
            return null;
        }

        if (file.Length > _upload.MaxFileSizeBytes)
        {
            Fail(UploadErrorCodes.FileTooLarge, $"{name} {FormatMegabytes(_upload.MaxFileSizeBytes)} dosya boyutu sınırını aşıyor.");
            return null;
        }

        var tempPath = Path.Combine(tempDirectory, string.Create(CultureInfo.InvariantCulture, $"{index + 1:D2}.docx"));
        var (size, sha256) = await CopyWithHashAsync(file, tempPath, cancellationToken).ConfigureAwait(false);
        if (size == 0)
        {
            Fail(UploadErrorCodes.FileEmpty, $"{name} boş bir dosya.");
            return null;
        }

        if (size > _upload.MaxFileSizeBytes)
        {
            Fail(UploadErrorCodes.FileTooLarge, $"{name} {FormatMegabytes(_upload.MaxFileSizeBytes)} dosya boyutu sınırını aşıyor.");
            return null;
        }

        var notDocx = $"{name} geçerli bir Word (.docx) belgesi değil.";
        if (!await HasZipSignatureAsync(tempPath, cancellationToken).ConfigureAwait(false))
        {
            Fail(UploadErrorCodes.FileNotDocx, notDocx);
            return null;
        }

        switch (CheckArchive(tempPath))
        {
            case ArchiveCheck.Unreadable:
                Fail(UploadErrorCodes.FileNotDocx, notDocx);
                return null;
            case ArchiveCheck.Unsafe:
                Fail(
                    UploadErrorCodes.FileUnsafeArchive,
                    $"{name} güvenli olmayan bir arşiv yapısı içeriyor (çok fazla girdi, çok büyük açılmış boyut veya geçersiz yol).");
                return null;
        }

        SourceDocument document;
        try
        {
            await using var stream = File.OpenRead(tempPath);
            document = reader.Read(stream);
        }
        catch (InvalidDocumentException)
        {
            Fail(UploadErrorCodes.FileNotDocx, notDocx);
            return null;
        }

        if (!document.Paragraphs.Any(p => !p.IsBlank))
        {
            Fail(UploadErrorCodes.FileNoContent, $"{name} metin içeren hiçbir paragraf içermiyor.");
            return null;
        }

        var sanitized = ContactInfoSanitizer.Sanitize(document);
        var title = TitleDetector.Detect(sanitized.Document, name);
        return new ValidatedPaper(name, tempPath, size, sha256, title);
    }

    /// <summary>Streams the upload to disk, hashing as it goes; stops one byte past the size limit.</summary>
    private async Task<(long Size, byte[] Sha256)> CopyWithHashAsync(UploadedFile file, string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferSize];
        long size = 0;
        await using var source = file.OpenRead();
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            size += read;
            if (size > _upload.MaxFileSizeBytes)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return (size, hash.GetHashAndReset());
    }

    private static async Task<bool> HasZipSignatureAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[ZipSignature.Length];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return read == header.Length && header.AsSpan().SequenceEqual(ZipSignature);
    }

    /// <summary>
    /// Zip bomb and path checks on the central directory. Declared sizes can be trusted as an upper bound because
    /// <see cref="ZipArchive"/> refuses to inflate an entry past its declared length when the document is read.
    /// </summary>
    private ArchiveCheck CheckArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > _upload.MaxArchiveEntries)
            {
                return ArchiveCheck.Unsafe;
            }

            long uncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                uncompressed += entry.Length;
                if (uncompressed > _upload.MaxUncompressedBytes || !IsSafeEntryName(entry.FullName))
                {
                    return ArchiveCheck.Unsafe;
                }
            }

            return ArchiveCheck.Safe;
        }
        catch (InvalidDataException)
        {
            return ArchiveCheck.Unreadable;
        }
    }

    private static bool IsSafeEntryName(string name) =>
        name.Length > 0
        && name[0] is not '/' and not '\\'
        && !name.Contains(':', StringComparison.Ordinal)
        && !name.Split('/', '\\').Contains("..");

    private static void AddDuplicateErrors(IReadOnlyList<ValidatedPaper> papers, List<UploadError> errors)
    {
        foreach (var group in papers.GroupBy(p => Convert.ToHexString(p.Sha256)).Where(g => g.Count() > 1))
        {
            var first = group.First();
            foreach (var duplicate in group.Skip(1))
            {
                errors.Add(new UploadError(
                    UploadErrorCodes.FileDuplicate,
                    $"{duplicate.FileName} ile {first.FileName} aynı içeriğe sahip; aynı bildiri iki kez yüklenemez.",
                    FileName: duplicate.FileName));
            }
        }
    }

    private string TempRoot() => string.IsNullOrWhiteSpace(_upload.TempPath)
        ? Path.Combine(Path.GetTempPath(), "bildiri-kitabi-uploads")
        : _upload.TempPath;

    private static string FormatMegabytes(long bytes) =>
        string.Create(Turkish, $"{bytes / (1024d * 1024d):0.##} MB");

    private enum ArchiveCheck
    {
        Safe,
        Unsafe,
        Unreadable,
    }
}
