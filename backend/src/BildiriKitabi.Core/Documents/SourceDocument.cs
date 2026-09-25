namespace BildiriKitabi.Core.Documents;

/// <summary>
/// Format-independent content of a single paper, as read from its source file.
/// </summary>
public sealed record SourceDocument(IReadOnlyList<Block> Blocks, IReadOnlyList<Footnote> Footnotes)
{
    public SourceDocument(IReadOnlyList<Block> blocks)
        : this(blocks, [])
    {
    }

    public IEnumerable<Paragraph> Paragraphs => EnumerateParagraphs(Blocks);

    private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Table table:
                    foreach (var paragraph in table.Rows.SelectMany(r => r.Cells).SelectMany(c => EnumerateParagraphs(c.Blocks)))
                    {
                        yield return paragraph;
                    }

                    break;
            }
        }
    }
}

public sealed record Footnote(int Number, IReadOnlyList<Paragraph> Paragraphs);
