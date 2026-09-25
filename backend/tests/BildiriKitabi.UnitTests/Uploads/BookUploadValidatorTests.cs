using System.IO.Compression;
using System.Text;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Titles;
using BildiriKitabi.Core.Uploads;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.Tests.Shared;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.UnitTests.Uploads;

public sealed class BookUploadValidatorTests : IDisposable
{
    private const string BookName = "Örnek Bilim Kongresi 2026";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "bk-upload-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task The_ten_sample_papers_pass_and_get_their_titles()
    {
        var files = TestPaths.PaperFiles.Select(path => File(Path.GetFileName(path), System.IO.File.ReadAllBytes(path))).ToList();

        using var result = await Validate(BookName, files);

        result.Errors.ShouldBeEmpty();
        result.IsValid.ShouldBeTrue();
        result.BookName.ShouldBe(BookName);
        result.Papers.Select(p => p.FileName).ShouldBe(TestPaths.PaperFiles.Select(Path.GetFileName).ToList()!);
        result.Papers.ShouldAllBe(p => p.Title.Source == TitleSource.TitleStyle && p.Sha256.Length == 32);
        result.Papers[0].Title.Text.ShouldBe("KENTSEL TARIMDA AKILLI SULAMA SİSTEMLERİNİN SU TÜKETİMİNE ETKİSİ");
    }

    [Fact]
    public async Task Nine_files_are_rejected()
    {
        using var result = await Validate(BookName, ValidFiles(9));

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(UploadErrorCodes.PaperCountInvalid);
        result.Errors[0].Field.ShouldBe("files");
        result.Errors[0].Message.ShouldContain("9 dosya");
    }

    [Theory]
    [InlineData("06_Bildiri.doc")]
    [InlineData("06_Bildiri.docm")]
    [InlineData("06_Bildiri.dotx")]
    [InlineData("06_Bildiri.pdf")]
    [InlineData("06_Bildiri")]
    public async Task Other_extensions_are_rejected(string fileName)
    {
        var files = ValidFiles(10);
        files[5] = files[5] with { FileName = fileName };

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileExtensionInvalid, fileName);
    }

    [Fact]
    public async Task Extension_check_ignores_case()
    {
        var files = ValidFiles(10);
        files[0] = files[0] with { FileName = "01_BILDIRI.DOCX" };

        using var result = await Validate(BookName, files);

        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_renamed_pdf_is_not_a_word_document()
    {
        var files = ValidFiles(10);
        files[2] = File("03_Dijital_Arsiv.docx", Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF"));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileNotDocx, "03_Dijital_Arsiv.docx");
        result.Errors[0].Message.ShouldBe("03_Dijital_Arsiv.docx geçerli bir Word (.docx) belgesi değil.");
    }

    [Fact]
    public async Task A_zip_that_is_not_a_word_package_is_rejected()
    {
        var files = ValidFiles(10);
        files[2] = File("03_Arsiv.docx", Zip(("readme.txt", 10)));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileNotDocx, "03_Arsiv.docx");
    }

    [Theory]
    [InlineData(WordprocessingDocumentType.MacroEnabledDocument)]
    [InlineData(WordprocessingDocumentType.Template)]
    [InlineData(WordprocessingDocumentType.MacroEnabledTemplate)]
    public async Task Macro_enabled_documents_and_templates_renamed_to_docx_are_rejected(WordprocessingDocumentType type)
    {
        var files = ValidFiles(10);
        files[4] = File("05_Makrolu.docx", Package(type, TestDocx.Paragraph("MAKROLU BİLDİRİ")));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileNotDocx, "05_Makrolu.docx");
    }

    [Fact]
    public async Task A_zip_bomb_is_rejected_by_its_uncompressed_size()
    {
        var files = ValidFiles(10);
        files[7] = File("08_Bomba.docx", Zip(("word/document.xml", 51L * 1024 * 1024)));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileUnsafeArchive, "08_Bomba.docx");
    }

    [Fact]
    public async Task An_archive_with_too_many_entries_is_rejected()
    {
        var files = ValidFiles(10);
        files[7] = File("08_Cok_Girdi.docx", Zip(Enumerable.Range(0, 501).Select(i => ($"x/{i}.xml", 1L)).ToArray()));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileUnsafeArchive, "08_Cok_Girdi.docx");
    }

    [Theory]
    [InlineData("../evil.xml")]
    [InlineData("word/../../evil.xml")]
    [InlineData("/etc/evil.xml")]
    [InlineData("C:/evil.xml")]
    public async Task Entries_with_traversal_or_absolute_paths_are_rejected(string entryName)
    {
        var files = ValidFiles(10);
        files[7] = File("08_Yol.docx", Zip((entryName, 1L)));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileUnsafeArchive, "08_Yol.docx");
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        var files = ValidFiles(10);
        files[1] = File("02_Bos.docx", []);

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileEmpty, "02_Bos.docx");
    }

    [Fact]
    public async Task A_document_without_text_is_rejected()
    {
        var files = ValidFiles(10);
        files[1] = File("02_Metinsiz.docx", Docx("<w:p/><w:p><w:r><w:t xml:space=\"preserve\">   </w:t></w:r></w:p>"));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileNoContent, "02_Metinsiz.docx");
    }

    [Fact]
    public async Task The_same_content_twice_is_rejected_with_both_names()
    {
        var files = ValidFiles(10);
        files[9] = files[3] with { FileName = "10_Kopya.docx" };

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileDuplicate, "10_Kopya.docx");
        result.Errors[0].Message.ShouldContain("10_Kopya.docx");
        result.Errors[0].Message.ShouldContain(files[3].FileName);
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_rejected()
    {
        var files = ValidFiles(10);
        var big = Docx(TestDocx.Paragraph(new string('a', 40_000)));
        files[6] = File("07_Buyuk.docx", big);

        using var result = await Validate(BookName, files, o => o.MaxFileSizeBytes = big.Length - 1);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileTooLarge, "07_Buyuk.docx");
    }

    [Fact]
    public async Task A_stream_longer_than_its_declared_length_is_still_stopped_at_the_limit()
    {
        var files = ValidFiles(10);
        var big = Docx(TestDocx.Paragraph(new string('a', 40_000)));
        files[6] = new UploadedFile("07_Yalanci.docx", 10, () => new MemoryStream(big));

        using var result = await Validate(BookName, files, o => o.MaxFileSizeBytes = big.Length - 1);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileTooLarge, "07_Yalanci.docx");
    }

    [Fact]
    public async Task Total_size_over_the_limit_is_rejected()
    {
        var files = ValidFiles(10);
        var total = files.Sum(f => f.Length);

        using var result = await Validate(BookName, files, o => o.MaxTotalSizeBytes = total - 1);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(UploadErrorCodes.TotalSizeTooLarge);
    }

    [Fact]
    public async Task Every_problem_is_reported_at_once()
    {
        var files = ValidFiles(9);
        files[0] = files[0] with { FileName = "01.doc" };
        files[1] = File("02_Bos.docx", []);
        files[8] = files[3] with { FileName = "09_Kopya.docx" };

        using var result = await Validate("ab", files);

        result.Errors.Select(e => e.Code).ShouldBe(
            [
                UploadErrorCodes.BookNameInvalid,
                UploadErrorCodes.PaperCountInvalid,
                UploadErrorCodes.FileExtensionInvalid,
                UploadErrorCodes.FileEmpty,
                UploadErrorCodes.FileDuplicate,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Contact_details_in_the_book_name_are_allowed()
    {
        using var result = await Validate("Kongre 0312 555 12 34 iletisim@example.org", ValidFiles(10));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task A_book_name_with_a_character_missing_from_the_fonts_is_rejected()
    {
        using var result = await Validate("Kongre 2026 😀", ValidFiles(10));

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(UploadErrorCodes.BookNameUnsupportedCharacter);
        error.Field.ShouldBe("name");
        error.Message.ShouldBe("Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.");
    }

    [Fact]
    public async Task Academic_symbols_in_the_book_name_and_papers_are_accepted()
    {
        var files = ValidFiles(10);
        files[0] = File("01_Sembol.docx", Docx(TestDocx.Paragraph("α β χ² ± ≤ ≥ ∑ √ → × ∀ ∈ ⇒")));

        using var result = await Validate("Kongre: α ≤ β → ∑", files);

        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_paper_with_a_character_missing_from_the_fonts_is_rejected_by_file()
    {
        var files = ValidFiles(10);
        files[2] = File("03_Sembol.docx", Docx(TestDocx.Paragraph("BİLDİRİ 3", "<w:b/>") + TestDocx.Paragraph("Küme 𝒜 tanımı.")));

        using var result = await Validate(BookName, files);

        ShouldHaveOnlyFileError(result, UploadErrorCodes.FileUnsupportedCharacter, "03_Sembol.docx");
        result.Errors[0].Message.ShouldBe("03_Sembol.docx içinde PDF yazı tipinde bulunmayan '𝒜' karakteri var; lütfen kaldırıp dosyayı yeniden yükleyin.");
    }

    [Fact]
    public async Task The_temporary_folder_is_removed_on_dispose()
    {
        var result = await Validate(BookName, ValidFiles(10));
        var folder = Path.GetDirectoryName(result.Papers[0].TempFilePath)!;
        System.IO.File.Exists(result.Papers[0].TempFilePath).ShouldBeTrue();

        result.Dispose();

        Directory.Exists(folder).ShouldBeFalse();
    }

    [Theory]
    [InlineData("C:\\fakepath\\01_Bildiri.docx", "01_Bildiri.docx")]
    [InlineData("../../etc/01_Bildiri.docx", "01_Bildiri.docx")]
    [InlineData("01_\u0000Bil\u001Fdiri.docx", "01_Bildiri.docx")]
    [InlineData("   ", "4. dosya")]
    public void Display_names_are_only_the_last_segment_without_control_characters(string fileName, string expected)
    {
        BookUploadValidator.ToDisplayName(fileName, 3).ShouldBe(expected);
    }

    [Fact]
    public void Display_names_are_cut_to_255_characters()
    {
        BookUploadValidator.ToDisplayName(new string('a', 300) + ".docx", 0).Length.ShouldBe(255);
    }

    private static void ShouldHaveOnlyFileError(UploadValidation result, string code, string fileName)
    {
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.FileName.ShouldBe(fileName);
        error.Message.ShouldContain(fileName);
        result.IsValid.ShouldBeFalse();
    }

    private Task<UploadValidation> Validate(string? name, IReadOnlyList<UploadedFile> files, Action<UploadOptions>? configure = null)
    {
        var options = new UploadOptions { TempPath = _tempRoot };
        configure?.Invoke(options);
        var validator = new BookUploadValidator(
            new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance),
            FontGlyphCoverage.Instance,
            Options.Create(options),
            Options.Create(new BookOptions()));
        return validator.ValidateAsync(name, files, TestContext.Current.CancellationToken);
    }

    private static List<UploadedFile> ValidFiles(int count) =>
        Enumerable.Range(1, count)
            .Select(i => File($"{i:D2}_Bildiri.docx", Docx(TestDocx.Paragraph($"BİLDİRİ {i}", "<w:b/>", "<w:jc w:val=\"center\"/>"))))
            .ToList();

    private static UploadedFile File(string name, byte[] content) => new(name, content.Length, () => new MemoryStream(content));

    private static byte[] Docx(string bodyXml)
    {
        using var stream = TestDocx.Create(bodyXml);
        return stream.ToArray();
    }

    private static byte[] Package(WordprocessingDocumentType type, string bodyXml)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, type))
        {
            var main = document.AddMainDocumentPart();
            using var xml = new MemoryStream(Encoding.UTF8.GetBytes(
                $"<w:document xmlns:w=\"{TestDocx.WordNamespace}\"><w:body>{bodyXml}</w:body></w:document>"));
            main.FeedData(xml);
        }

        return stream.ToArray();
    }

    /// <summary>A zip whose entries hold the given number of zero bytes (they compress to almost nothing).</summary>
    private static byte[] Zip(params (string Name, long Size)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var zeros = new byte[1024 * 1024];
            foreach (var (name, size) in entries)
            {
                using var entry = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                for (var written = 0L; written < size; written += zeros.Length)
                {
                    entry.Write(zeros, 0, (int)Math.Min(zeros.Length, size - written));
                }
            }
        }

        return stream.ToArray();
    }
}
