namespace BildiriKitabi.Core.Documents;

/// <summary>
/// A span of uniformly formatted text. Line breaks are represented as '\n' and tabs as '\t'.
/// </summary>
public sealed record Run
{
    public required string Text { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public float FontSizePt { get; init; } = 11f;

    public FontFamilyKind FontFamilyKind { get; init; } = FontFamilyKind.Serif;

    public VerticalPosition VerticalPosition { get; init; } = VerticalPosition.Baseline;

    /// <summary>External link target, if the run is part of a hyperlink.</summary>
    public string? Hyperlink { get; init; }
}

public enum FontFamilyKind
{
    Serif,
    Sans,
}

public enum VerticalPosition
{
    Baseline,
    Superscript,
    Subscript,
}
