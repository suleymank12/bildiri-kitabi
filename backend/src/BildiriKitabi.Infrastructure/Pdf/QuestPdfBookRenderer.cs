using System.Globalization;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Documents;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BildiriKitabi.Infrastructure.Pdf;

/// <summary>
/// Lays out the book: cover, table of contents and one page set per paper. Page numbers are physical page
/// positions (the cover is page 1 but shows no number), so the table of contents matches the viewer's page box.
/// </summary>
public sealed class QuestPdfBookRenderer : IBookRenderer
{
    public const string ProducerName = "Bildiri Kitabı Oluşturucu";

    private const float MarginCm = 2.5f;
    private const float BodyFontSize = 11f;
    private const float RunningHeadFontSize = 9f;

    // Natural single-line height of Liberation Serif/Sans relative to the font size (ascent + descent + gap).
    private const float SingleLineFactor = 1.15f;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly string RuleColor = Colors.Grey.Medium;
    private static readonly string MutedColor = Colors.Grey.Darken2;

    public QuestPdfBookRenderer()
    {
        QuestPdfSetup.EnsureInitialized();
    }

    public RenderedBook Render(BookContent book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var document = Document
            .Create(container =>
            {
                ComposeCover(container, book);
                ComposeTableOfContents(container, book);
                for (var i = 0; i < book.Papers.Count; i++)
                {
                    ComposePaper(container, book, i);
                }
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = book.Title,
                Subject = "Bildiri Kitabı",
                Creator = ProducerName,
                Producer = ProducerName,
                Language = "tr-TR",
                CreationDate = book.CreatedAt,
                ModifiedDate = book.CreatedAt,
            })
            .WithSettings(new DocumentSettings { CompressDocument = true });

        byte[] pdf;
        try
        {
            pdf = document.GeneratePdf();
        }
        catch (Exception ex) when (ex is DocumentLayoutException or DocumentDrawingException or DocumentComposeException)
        {
            throw new BookGenerationException(
                BookErrorCodes.RenderFailed,
                "PDF dizgisi oluşturulamadı; belgelerden biri desteklenmeyen bir içerik (ör. yazı tipinde bulunmayan bir karakter) içeriyor olabilir.",
                ex);
        }

        var layout = PdfLayoutReader.Read(pdf, book.Papers.Count);
        return new RenderedBook(pdf, layout.PageCount, layout.PaperPages);
    }

    internal static string SectionName(int paperIndex) => $"paper-{paperIndex + 1}";

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(MarginCm, Unit.Centimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(style => style.FontFamily(QuestPdfSetup.SerifFamily).FontSize(BodyFontSize).FontColor(Colors.Black));
    }

    private static void ComposeCover(IDocumentContainer container, BookContent book)
    {
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Content().PaddingTop(5, Unit.Centimetre).Column(column =>
            {
                column.Item().Text(book.Title).FontSize(26).Bold().LineHeight(1.2f).AlignCenter();
                column.Item().PaddingVertical(20).AlignCenter().Width(90).LineHorizontal(1).LineColor(RuleColor);
                column.Item().Text("Bildiri Kitabı").FontFamily(QuestPdfSetup.SansFamily).FontSize(14).LetterSpacing(0.05f).AlignCenter();
                column.Item().PaddingTop(6).Text($"{book.Papers.Count} bildiri")
                    .FontFamily(QuestPdfSetup.SansFamily).FontSize(11).FontColor(MutedColor).AlignCenter();
            });
            page.Footer().Text(book.CreatedAt.ToString("d MMMM yyyy", Turkish))
                .FontFamily(QuestPdfSetup.SansFamily).FontSize(10).FontColor(MutedColor).AlignCenter();
        });
    }

    private static void ComposeTableOfContents(IDocumentContainer container, BookContent book)
    {
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Content().Column(column =>
            {
                column.Item().PaddingBottom(18).Text("İçindekiler").FontSize(18).Bold();
                for (var i = 0; i < book.Papers.Count; i++)
                {
                    var section = SectionName(i);
                    column.Item().SectionLink(section).PaddingVertical(4).Row(row =>
                    {
                        row.ConstantItem(24).Text($"{i + 1}.");
                        row.RelativeItem().Text(book.Papers[i].Title).LineHeight(1.25f);
                        row.ConstantItem(64).AlignBottom().Row(leader =>
                        {
                            leader.RelativeItem().PaddingHorizontal(4).PaddingBottom(3)
                                .LineHorizontal(0.75f).LineColor(RuleColor).LineDashPattern([1f, 2.5f]);
                            leader.ConstantItem(22).AlignRight().Text(text => text.BeginPageNumberOfSection(section));
                        });
                    });
                }
            });
            ComposePageNumber(page);
        });
    }

    private static void ComposePaper(IDocumentContainer container, BookContent book, int index)
    {
        var paper = book.Papers[index];
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Header().Column(header =>
            {
                header.Item().Row(row =>
                {
                    row.RelativeItem(2).Text(book.Title).ClampLines(1, "…").FontSize(RunningHeadFontSize)
                        .FontFamily(QuestPdfSetup.SansFamily).FontColor(MutedColor);
                    row.ConstantItem(16);
                    row.RelativeItem(3).Text(paper.Title).ClampLines(1, "…").FontSize(RunningHeadFontSize)
                        .FontFamily(QuestPdfSetup.SansFamily).FontColor(MutedColor).AlignRight();
                });
                header.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(RuleColor);
                header.Item().Height(14);
            });
            page.Content().Section(SectionName(index)).Column(column =>
            {
                ComposeBlocks(column, paper.Document.Blocks, allowPageBreaks: true);
                ComposeFootnotes(column, paper.Document.Footnotes);
            });
            ComposePageNumber(page);
        });
    }

    private static void ComposePageNumber(PageDescriptor page)
    {
        page.Footer().PaddingTop(12).AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(style => style.FontFamily(QuestPdfSetup.SansFamily).FontSize(RunningHeadFontSize).FontColor(MutedColor));
            text.CurrentPageNumber();
        });
    }

    private static void ComposeBlocks(ColumnDescriptor column, IReadOnlyList<Block> blocks, bool allowPageBreaks)
    {
        var i = 0;
        while (i < blocks.Count)
        {
            if (blocks[i] is Paragraph { KeepWithNext: true })
            {
                // Paragraphs marked "keep with next" move to the next page together with the block that follows them.
                var end = i;
                while (end < blocks.Count && blocks[end] is Paragraph { KeepWithNext: true })
                {
                    end++;
                }

                if (end < blocks.Count && blocks[end] is Paragraph)
                {
                    end++;
                }

                var group = blocks.Skip(i).Take(end - i).ToList();
                column.Item().PreventPageBreak().Column(inner =>
                {
                    foreach (var block in group)
                    {
                        ComposeBlock(inner, block, allowPageBreaks);
                    }
                });
                i = end;
                continue;
            }

            ComposeBlock(column, blocks[i], allowPageBreaks);
            i++;
        }
    }

    private static void ComposeBlock(ColumnDescriptor column, Block block, bool allowPageBreaks)
    {
        switch (block)
        {
            case Paragraph paragraph:
                ComposeParagraph(column.Item(), paragraph);
                break;
            case PageBreak when allowPageBreaks:
                column.Item().PageBreak();
                break;
            case Table table:
                ComposeTable(column.Item(), table);
                break;
        }
    }

    private static void ComposeParagraph(IContainer container, Paragraph paragraph, IReadOnlyList<Core.Documents.Run>? prefix = null)
    {
        var runs = prefix is null ? paragraph.Runs : [.. prefix, .. paragraph.Runs];
        var box = container
            .PaddingTop(paragraph.SpacingBeforePt)
            .PaddingBottom(paragraph.SpacingAfterPt)
            .PaddingLeft(Math.Max(0, paragraph.LeftIndentPt))
            .PaddingRight(Math.Max(0, paragraph.RightIndentPt));

        var largestFont = runs.Count == 0 ? BodyFontSize : runs.Max(r => r.FontSizePt);
        if (runs.All(r => r.Text.Length == 0))
        {
            // An empty paragraph still occupies one line in Word.
            box.Height(largestFont * LineHeightFactor(paragraph.LineSpacing, largestFont));
            return;
        }

        box.Text(text =>
        {
            switch (paragraph.Alignment)
            {
                case ParagraphAlignment.Center:
                    text.AlignCenter();
                    break;
                case ParagraphAlignment.Right:
                    text.AlignRight();
                    break;
                case ParagraphAlignment.Justify:
                    text.Justify();
                    break;
                default:
                    text.AlignLeft();
                    break;
            }

            if (paragraph.FirstLineIndentPt > 0)
            {
                text.ParagraphFirstLineIndentation(paragraph.FirstLineIndentPt);
            }

            var lineHeight = LineHeightFactor(paragraph.LineSpacing, largestFont);
            foreach (var run in runs)
            {
                var content = run.Text.Replace("\t", "    ", StringComparison.Ordinal);
                var span = run.Hyperlink is { } url && IsExternalLink(url) ? text.Hyperlink(content, url) : text.Span(content);
                span.FontFamily(run.FontFamilyKind == FontFamilyKind.Sans ? QuestPdfSetup.SansFamily : QuestPdfSetup.SerifFamily)
                    .FontSize(run.FontSizePt)
                    .LineHeight(lineHeight);
                if (run.Bold)
                {
                    span.Bold();
                }

                if (run.Italic)
                {
                    span.Italic();
                }

                if (run.Underline)
                {
                    span.Underline();
                }

                if (run.VerticalPosition == VerticalPosition.Superscript)
                {
                    span.Superscript();
                }
                else if (run.VerticalPosition == VerticalPosition.Subscript)
                {
                    span.Subscript();
                }
            }
        });
    }

    private static void ComposeTable(IContainer container, Table table)
    {
        var columnCount = table.ColumnWidthsPt.Count;
        container.PaddingVertical(4).Table(descriptor =>
        {
            descriptor.ColumnsDefinition(columns =>
            {
                foreach (var width in table.ColumnWidthsPt)
                {
                    columns.RelativeColumn(width);
                }
            });

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var columnIndex = 0;
                foreach (var cell in table.Rows[r].Cells)
                {
                    if (columnIndex >= columnCount)
                    {
                        break;
                    }

                    var span = Math.Min(cell.ColumnSpan, columnCount - columnIndex);
                    descriptor.Cell()
                        .Row((uint)(r + 1))
                        .Column((uint)(columnIndex + 1))
                        .ColumnSpan((uint)span)
                        .Border(0.5f)
                        .BorderColor(RuleColor)
                        .Padding(3)
                        .Column(inner => ComposeBlocks(inner, cell.Blocks, allowPageBreaks: false));
                    columnIndex += span;
                }
            }
        });
    }

    private static void ComposeFootnotes(ColumnDescriptor column, IReadOnlyList<Footnote> footnotes)
    {
        if (footnotes.Count == 0)
        {
            return;
        }

        column.Item().PaddingTop(16).Width(120).LineHorizontal(0.5f).LineColor(RuleColor);
        column.Item().PaddingTop(6).PaddingBottom(4).Text("Dipnotlar").Bold().FontSize(10);
        foreach (var footnote in footnotes)
        {
            for (var p = 0; p < footnote.Paragraphs.Count; p++)
            {
                var paragraph = footnote.Paragraphs[p];
                IReadOnlyList<Core.Documents.Run>? marker = p == 0
                    ? [new Core.Documents.Run { Text = $"{footnote.Number.ToString(CultureInfo.InvariantCulture)} ", FontSizePt = 9f, VerticalPosition = VerticalPosition.Superscript }]
                    : null;
                ComposeParagraph(column.Item(), paragraph with { KeepWithNext = false }, marker);
            }
        }
    }

    private static float LineHeightFactor(LineSpacing spacing, float fontSize) => spacing.Rule switch
    {
        LineSpacingRule.Exact => Math.Max(0.5f, spacing.Value / fontSize),
        LineSpacingRule.AtLeast => Math.Max(SingleLineFactor, spacing.Value / fontSize),
        _ => SingleLineFactor * Math.Max(0.5f, spacing.Value),
    };

    private static bool IsExternalLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
