using System.IO.Packaging;
using System.Text;
using BildiriKitabi.Core.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using CoreFootnote = BildiriKitabi.Core.Documents.Footnote;
using CoreParagraph = BildiriKitabi.Core.Documents.Paragraph;
using CoreRun = BildiriKitabi.Core.Documents.Run;
using CoreTable = BildiriKitabi.Core.Documents.Table;
using CoreTableCell = BildiriKitabi.Core.Documents.TableCell;
using CoreTableRow = BildiriKitabi.Core.Documents.TableRow;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace BildiriKitabi.Infrastructure.Docx;

/// <summary>
/// Reads the main body of a .docx into the format-independent <see cref="SourceDocument"/> model.
/// Headers, footers and comments of the source are not read; the book has its own running header.
/// Package metadata (docProps) is never read.
/// </summary>
public sealed partial class OpenXmlDocxReader(ILogger<OpenXmlDocxReader> logger) : IDocxReader
{
    private const string WordMainContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    public SourceDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffered = CopyToMemory(stream);
        using var document = Open(buffered);

        if (document.DocumentType != WordprocessingDocumentType.Document)
        {
            throw new InvalidDocumentException("Dosya bir Word belgesi (.docx) değil; şablon veya makro içeren belgeler desteklenmez.");
        }

        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDocumentException("Belgenin ana içerik bölümü bulunamadı.");

        // A renamed .xlsx or .pptx is a valid package too; only a Word main part is a document.
        if (!string.Equals(mainPart.ContentType, WordMainContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDocumentException("Dosya bir Word belgesi (.docx) değil.");
        }

        var body = LoadBody(mainPart)
            ?? throw new InvalidDocumentException("Belgenin gövdesi bulunamadı.");

        var context = new ReadContext(mainPart, new StyleResolver(mainPart.StyleDefinitionsPart, mainPart.ThemePart));
        var blocks = new List<Block>();
        ReadBlocks(body.ChildElements, blocks, context);
        var footnotes = ReadFootnotes(context);
        return new SourceDocument(blocks, footnotes);
    }

    private static Body? LoadBody(MainDocumentPart mainPart)
    {
        try
        {
            return mainPart.Document?.Body;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            throw new InvalidDocumentException("Belgenin ana içeriği okunamadı; geçerli bir Word (.docx) belgesi değil veya bozuk.", ex);
        }
    }

    private static MemoryStream CopyToMemory(Stream stream)
    {
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    private static WordprocessingDocument Open(Stream stream)
    {
        try
        {
            return WordprocessingDocument.Open(stream, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException or InvalidOperationException)
        {
            throw new InvalidDocumentException("Dosya açılamadı; geçerli bir Word (.docx) belgesi değil veya bozuk.", ex);
        }
    }

    private void ReadBlocks(IEnumerable<OpenXmlElement> elements, List<Block> output, ReadContext context)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case W.Paragraph paragraph:
                    ReadParagraph(paragraph, output, context);
                    break;
                case W.Table table:
                    output.Add(ReadTable(table, context));
                    break;
                case SdtBlock sdt:
                    ReadBlocks(sdt.SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>(), output, context);
                    break;
                case CustomXmlBlock customXml:
                    ReadBlocks(customXml.ChildElements, output, context);
                    break;
                case SectionProperties or BookmarkStart or BookmarkEnd or ProofError or PermStart or PermEnd
                    or CommentRangeStart or CommentRangeEnd or SdtProperties or SdtEndCharProperties:
                    break;
                default:
                    Ignore(element, context);
                    break;
            }
        }
    }

    private void ReadParagraph(W.Paragraph paragraph, List<Block> output, ReadContext context)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        var format = context.Styles.ResolveParagraph(styleId, properties);

        if (format.PageBreakBefore && output.Count > 0)
        {
            output.Add(new PageBreak());
        }

        var builder = new ParagraphBuilder(format, context.Styles.GetParagraphStyleName(styleId), output);
        var trailingBlocks = new List<Block>();
        ReadInline(paragraph.ChildElements, builder, trailingBlocks, styleId, hyperlink: null, context);
        builder.Complete();
        output.AddRange(trailingBlocks);
    }

    private void ReadInline(
        IEnumerable<OpenXmlElement> elements,
        ParagraphBuilder builder,
        List<Block> trailingBlocks,
        string? paragraphStyleId,
        string? hyperlink,
        ReadContext context)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case W.Run run:
                    ReadRun(run, builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case Hyperlink link:
                    ReadInline(link.ChildElements, builder, trailingBlocks, paragraphStyleId, ResolveHyperlink(link, context), context);
                    break;
                case SimpleField field:
                    ReadInline(field.ChildElements, builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case InsertedRun inserted:
                    ReadInline(inserted.ChildElements, builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case MoveToRun moveTo:
                    ReadInline(moveTo.ChildElements, builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case SdtRun sdt:
                    ReadInline(sdt.SdtContentRun?.ChildElements ?? Enumerable.Empty<OpenXmlElement>(), builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case CustomXmlRun customXml:
                    ReadInline(customXml.ChildElements, builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case OpenXmlElement smartTag when smartTag.LocalName == "smartTag":
                    ReadInline(smartTag.ChildElements, builder, trailingBlocks, paragraphStyleId, hyperlink, context);
                    break;
                case DeletedRun or MoveFromRun or W.ParagraphProperties or BookmarkStart or BookmarkEnd or ProofError
                    or PermStart or PermEnd or CommentRangeStart or CommentRangeEnd or MoveFromRangeStart or MoveFromRangeEnd
                    or MoveToRangeStart or MoveToRangeEnd or SdtProperties or SdtEndCharProperties or CustomXmlProperties:
                    break;
                case OpenXmlElement smartTagProperties when smartTagProperties.LocalName == "smartTagPr":
                    break;
                default:
                    Ignore(element, context);
                    break;
            }
        }
    }

    private void ReadRun(
        W.Run run,
        ParagraphBuilder builder,
        List<Block> trailingBlocks,
        string? paragraphStyleId,
        string? hyperlink,
        ReadContext context)
    {
        var format = context.Styles.ResolveRun(paragraphStyleId, run.RunProperties);
        if (format.Hidden)
        {
            return;
        }

        foreach (var element in run.ChildElements)
        {
            switch (element)
            {
                case Text text:
                    builder.Append(text.Text, format, hyperlink);
                    break;
                case TabChar or PositionalTab:
                    builder.Append("\t", format, hyperlink);
                    break;
                case Break lineBreak when lineBreak.Type?.Value == BreakValues.Page || lineBreak.Type?.Value == BreakValues.Column:
                    // A column break in a single-column section behaves like a page break.
                    builder.PageBreak();
                    break;
                case Break or CarriageReturn:
                    builder.Append("\n", format, hyperlink);
                    break;
                case NoBreakHyphen:
                    builder.Append("-", format, hyperlink);
                    break;
                case FootnoteReference reference when reference.Id?.Value is { } id:
                    var number = context.ReferenceFootnote(id);
                    builder.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture), format with { VerticalPosition = VerticalPosition.Superscript }, hyperlink: null);
                    break;
                case Drawing or Picture or AlternateContent or EmbeddedObject:
                    var textBoxes = FindTextBoxes(element).ToList();
                    if (textBoxes.Count == 0)
                    {
                        Ignore(element, context);
                    }

                    foreach (var textBox in textBoxes)
                    {
                        ReadBlocks(textBox.ChildElements, trailingBlocks, context);
                    }

                    break;
                case W.RunProperties or FieldChar or FieldCode or DeletedText or DeletedFieldCode or LastRenderedPageBreak
                    or SoftHyphen or FootnoteReferenceMark or EndnoteReferenceMark or AnnotationReferenceMark or CommentReference:
                    break;
                default:
                    Ignore(element, context);
                    break;
            }
        }
    }

    private CoreTable ReadTable(W.Table table, ReadContext context)
    {
        var rows = new List<CoreTableRow>();
        foreach (var row in table.Elements<W.TableRow>())
        {
            var cells = new List<CoreTableCell>();
            foreach (var cell in row.Elements<W.TableCell>())
            {
                var blocks = new List<Block>();
                ReadBlocks(cell.ChildElements, blocks, context);
                var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                cells.Add(new CoreTableCell(Math.Max(1, span), blocks));
            }

            rows.Add(new CoreTableRow(cells));
        }

        var columnCount = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Cells.Sum(c => c.ColumnSpan)));
        var widths = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>()
            .Select(c => Twips.ToPoints(c.Width?.Value) ?? 0f)
            .ToList() ?? [];
        if (widths.Count != columnCount || widths.Any(w => w <= 0))
        {
            widths = Enumerable.Repeat(1f, columnCount).ToList();
        }

        return new CoreTable(widths, rows);
    }

    private List<CoreFootnote> ReadFootnotes(ReadContext context)
    {
        var footnotes = new List<CoreFootnote>();
        var source = context.MainPart.FootnotesPart?.Footnotes;
        if (source is null)
        {
            return footnotes;
        }

        var byId = source.Elements<W.Footnote>()
            .Where(f => f.Id?.Value is not null)
            .GroupBy(f => f.Id!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (id, number) in context.ReferencedFootnotes)
        {
            if (!byId.TryGetValue(id, out var footnote))
            {
                continue;
            }

            var blocks = new List<Block>();
            ReadBlocks(footnote.ChildElements, blocks, context);
            footnotes.Add(new CoreFootnote(number, blocks.OfType<CoreParagraph>().ToList()));
        }

        return footnotes;
    }

    private static string? ResolveHyperlink(Hyperlink link, ReadContext context)
    {
        if (link.Id?.Value is not { } relationshipId)
        {
            return null;
        }

        return context.MainPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == relationshipId)?.Uri.OriginalString;
    }

    /// <summary>
    /// Text boxes are stored twice inside mc:AlternateContent (DrawingML choice and VML fallback); only one copy is read.
    /// </summary>
    private static IEnumerable<TextBoxContent> FindTextBoxes(OpenXmlElement element)
    {
        switch (element)
        {
            case TextBoxContent textBox:
                yield return textBox;
                yield break;
            case AlternateContent alternate:
                var chosen = (OpenXmlElement?)alternate.GetFirstChild<AlternateContentChoice>()
                    ?? alternate.GetFirstChild<AlternateContentFallback>();
                if (chosen is not null)
                {
                    foreach (var textBox in FindTextBoxes(chosen))
                    {
                        yield return textBox;
                    }
                }

                yield break;
        }

        foreach (var child in element.ChildElements)
        {
            foreach (var textBox in FindTextBoxes(child))
            {
                yield return textBox;
            }
        }
    }

    private void Ignore(OpenXmlElement element, ReadContext context)
    {
        var name = string.IsNullOrEmpty(element.Prefix) ? element.LocalName : $"{element.Prefix}:{element.LocalName}";
        if (context.IgnoredElements.Add(name))
        {
            LogIgnoredElement(logger, name);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored unsupported document element {ElementName}")]
    private static partial void LogIgnoredElement(ILogger logger, string elementName);

    private sealed class ReadContext(MainDocumentPart mainPart, StyleResolver styles)
    {
        private readonly Dictionary<long, int> _footnoteNumbers = [];

        public MainDocumentPart MainPart { get; } = mainPart;

        public StyleResolver Styles { get; } = styles;

        public HashSet<string> IgnoredElements { get; } = new(StringComparer.Ordinal);

        public IEnumerable<(long Id, int Number)> ReferencedFootnotes => _footnoteNumbers.Select(p => (p.Key, p.Value));

        public int ReferenceFootnote(long id)
        {
            if (!_footnoteNumbers.TryGetValue(id, out var number))
            {
                number = _footnoteNumbers.Count + 1;
                _footnoteNumbers[id] = number;
            }

            return number;
        }
    }

    /// <summary>
    /// Collects runs for one source paragraph; an explicit page break splits it into a paragraph, a
    /// <see cref="PageBreak"/> block and the paragraph's remainder.
    /// </summary>
    private sealed class ParagraphBuilder(ParagraphFormat format, string? styleName, List<Block> output)
    {
        private readonly List<CoreRun> _runs = [];
        private readonly StringBuilder _pending = new();
        private RunFormat? _pendingFormat;
        private string? _pendingHyperlink;
        private bool _hadPageBreak;

        public void Append(string text, RunFormat runFormat, string? hyperlink)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (_pendingFormat is not null && (_pendingFormat != runFormat || _pendingHyperlink != hyperlink))
            {
                FlushRun();
            }

            _pendingFormat = runFormat;
            _pendingHyperlink = hyperlink;
            _pending.Append(text);
        }

        public void PageBreak()
        {
            FlushRun();
            if (_runs.Count > 0)
            {
                output.Add(CreateParagraph());
            }

            output.Add(new PageBreak());
            _hadPageBreak = true;
        }

        public void Complete()
        {
            FlushRun();

            // An empty paragraph from the source is kept; the empty remainder after a page break is not.
            if (_runs.Count > 0 || !_hadPageBreak)
            {
                output.Add(CreateParagraph());
            }
        }

        private void FlushRun()
        {
            if (_pendingFormat is null || _pending.Length == 0)
            {
                return;
            }

            _runs.Add(new CoreRun
            {
                Text = _pending.ToString(),
                Bold = _pendingFormat.Bold,
                Italic = _pendingFormat.Italic,
                Underline = _pendingFormat.Underline,
                FontSizePt = _pendingFormat.FontSizePt,
                FontFamilyKind = FontClassifier.Classify(_pendingFormat.FontName),
                VerticalPosition = _pendingFormat.VerticalPosition,
                Hyperlink = _pendingHyperlink,
            });
            _pending.Clear();
            _pendingFormat = null;
            _pendingHyperlink = null;
        }

        private CoreParagraph CreateParagraph()
        {
            var paragraph = new CoreParagraph
            {
                Style = styleName,
                Alignment = format.Alignment,
                SpacingBeforePt = format.SpacingBeforePt,
                SpacingAfterPt = format.SpacingAfterPt,
                LineSpacing = format.LineSpacing,
                KeepWithNext = format.KeepWithNext,
                LeftIndentPt = format.LeftIndentPt,
                RightIndentPt = format.RightIndentPt,
                FirstLineIndentPt = format.FirstLineIndentPt,
                Runs = _runs.ToList(),
            };
            _runs.Clear();
            return paragraph;
        }
    }
}
