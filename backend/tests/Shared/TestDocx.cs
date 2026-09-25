using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace BildiriKitabi.Tests.Shared;

/// <summary>Builds small .docx packages in memory from WordprocessingML fragments.</summary>
internal static class TestDocx
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static MemoryStream Create(
        string bodyXml,
        string? stylesXml = null,
        string? footnotesXml = null,
        Action<MainDocumentPart>? configure = null,
        Action<WordprocessingDocument>? configurePackage = null)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            configure?.Invoke(mainPart);
            Feed(mainPart, Wrap("document", $"<w:body>{bodyXml}</w:body>"));

            if (stylesXml is not null)
            {
                Feed(mainPart.AddNewPart<StyleDefinitionsPart>(), Wrap("styles", stylesXml));
            }

            if (footnotesXml is not null)
            {
                Feed(mainPart.AddNewPart<FootnotesPart>(), Wrap("footnotes", footnotesXml));
            }

            configurePackage?.Invoke(document);
        }

        stream.Position = 0;
        return stream;
    }

    public static string Paragraph(string text, string? runProperties = null, string? paragraphProperties = null) =>
        $"<w:p>{(paragraphProperties is null ? string.Empty : $"<w:pPr>{paragraphProperties}</w:pPr>")}" +
        $"<w:r>{(runProperties is null ? string.Empty : $"<w:rPr>{runProperties}</w:rPr>")}<w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Wrap(string root, string content) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:{root} xmlns:w=\"{WordNamespace}\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "mc:Ignorable=\"wps\">" +
        $"{content}</w:{root}>";

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        part.FeedData(data);
    }
}
