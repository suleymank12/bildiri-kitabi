using BildiriKitabi.Core.Books;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.IntegrationTests.Books;

/// <summary>Generates the book from the ten sample papers once and shares it between the end-to-end tests.</summary>
public sealed class SampleBookFixture
{
    public const string BookName = "Örnek Bilim Kongresi 2026";

    public SampleBookFixture()
    {
        PaperFiles = TestPaths.PaperFiles;
        Book = CreateGenerator().Generate(
            BookName,
            PaperFiles.Select(path => new PaperSource(Path.GetFileName(path), () => File.OpenRead(path))).ToList());

        var artifacts = Path.Combine(TestPaths.RepositoryRoot, "artifacts");
        Directory.CreateDirectory(artifacts);
        File.WriteAllBytes(Path.Combine(artifacts, "ornek-kitap.pdf"), Book.Pdf);
    }

    public IReadOnlyList<string> PaperFiles { get; }

    public GeneratedBook Book { get; }

    public static BookGenerator CreateGenerator() =>
        new(
            new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance),
            new QuestPdfBookRenderer(),
            new PdfPigLeakScanner(),
            TimeProvider.System,
            NullLogger<BookGenerator>.Instance);
}
