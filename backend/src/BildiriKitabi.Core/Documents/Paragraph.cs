namespace BildiriKitabi.Core.Documents;

public sealed record Paragraph : Block
{
    /// <summary>Resolved style name (for example "Title"), not the localized style id.</summary>
    public string? Style { get; init; }

    public ParagraphAlignment Alignment { get; init; } = ParagraphAlignment.Left;

    public float SpacingBeforePt { get; init; }

    public float SpacingAfterPt { get; init; }

    public LineSpacing LineSpacing { get; init; } = LineSpacing.Default;

    public bool KeepWithNext { get; init; }

    public float LeftIndentPt { get; init; }

    public float RightIndentPt { get; init; }

    /// <summary>Positive for a first-line indent, negative for a hanging indent.</summary>
    public float FirstLineIndentPt { get; init; }

    public IReadOnlyList<Run> Runs { get; init; } = [];

    public string Text => string.Concat(Runs.Select(r => r.Text));

    public bool IsBlank => string.IsNullOrWhiteSpace(Text);
}

public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

public enum LineSpacingRule
{
    /// <summary><see cref="LineSpacing.Value"/> is a multiple of single spacing.</summary>
    Multiple,

    /// <summary><see cref="LineSpacing.Value"/> is an exact line height in points.</summary>
    Exact,

    /// <summary><see cref="LineSpacing.Value"/> is a minimum line height in points.</summary>
    AtLeast,
}

public sealed record LineSpacing(LineSpacingRule Rule, float Value)
{
    public static LineSpacing Default { get; } = new(LineSpacingRule.Multiple, 1f);
}
