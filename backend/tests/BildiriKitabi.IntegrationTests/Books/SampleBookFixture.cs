using BildiriKitabi.Core.Books;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.IntegrationTests.Books;

/// <summary>
/// Generates the book from the ten sample papers once and shares it between the end-to-end tests. Without the
/// sample papers nothing is generated and every test using the fixture is skipped.
/// </summary>
public sealed class SampleBookFixture
{
    public const string BookName = "Örnek Bilim Kongresi 2026";

    private readonly IReadOnlyList<string> _paperFiles = [];
    private readonly GeneratedBook? _book;

    public SampleBookFixture()
    {
        if (!TestPaths.PapersAvailable)
        {
            return;
        }

        _paperFiles = TestPaths.PaperFiles;
        _book = CreateGenerator().Generate(
            BookName,
            _paperFiles.Select(path => new PaperSource(Path.GetFileName(path), () => File.OpenRead(path))).ToList());

        var artifacts = Path.Combine(TestPaths.RepositoryRoot, "artifacts");
        Directory.CreateDirectory(artifacts);
        File.WriteAllBytes(Path.Combine(artifacts, "ornek-kitap.pdf"), _book.Pdf);
    }

    public IReadOnlyList<string> PaperFiles
    {
        get
        {
            TestPaths.EnsurePapersAvailable();
            return _paperFiles;
        }
    }

    public GeneratedBook Book
    {
        get
        {
            TestPaths.EnsurePapersAvailable();
            return _book!;
        }
    }

    public static BookGenerator CreateGenerator() =>
        new(
            new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance),
            new QuestPdfBookRenderer(),
            new PdfPigLeakScanner(),
            FontGlyphCoverage.Instance,
            TimeProvider.System,
            NullLogger<BookGenerator>.Instance);
}
