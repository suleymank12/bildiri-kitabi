using BildiriKitabi.Core.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace BildiriKitabi.Infrastructure.Docx;

internal sealed record ParagraphFormat(
    ParagraphAlignment Alignment,
    float SpacingBeforePt,
    float SpacingAfterPt,
    LineSpacing LineSpacing,
    bool KeepWithNext,
    bool PageBreakBefore,
    float LeftIndentPt,
    float RightIndentPt,
    float FirstLineIndentPt);

internal sealed record RunFormat(
    bool Bold,
    bool Italic,
    bool Underline,
    bool Hidden,
    float FontSizePt,
    string? FontName,
    VerticalPosition VerticalPosition);

/// <summary>
/// Computes effective formatting the way Word layers it: document defaults, then the paragraph style
/// (following its basedOn chain), then the character style, then direct formatting.
/// Styles are looked up by id but identified by their name, because ids are localized ("KonuBal" is "Title").
/// </summary>
internal sealed class StyleResolver
{
    private const float DefaultFontSizePt = 10f;

    private readonly Dictionary<string, Style> _styles;
    private readonly string? _defaultParagraphStyleId;
    private readonly OpenXmlElement? _defaultParagraphProperties;
    private readonly OpenXmlElement? _defaultRunProperties;
    private readonly string? _majorFont;
    private readonly string? _minorFont;

    public StyleResolver(StyleDefinitionsPart? stylesPart, ThemePart? themePart)
    {
        var styles = stylesPart?.Styles;
        _styles = new Dictionary<string, Style>(StringComparer.Ordinal);
        foreach (var style in styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is { } id)
            {
                _styles.TryAdd(id, style);
            }
        }

        _defaultParagraphStyleId = styles?.Elements<Style>()
            .FirstOrDefault(s => s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true)
            ?.StyleId?.Value;

        var docDefaults = styles?.DocDefaults;
        _defaultParagraphProperties = docDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
        _defaultRunProperties = docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;

        var fontScheme = themePart?.Theme?.ThemeElements?.FontScheme;
        _majorFont = fontScheme?.MajorFont?.LatinFont?.Typeface?.Value;
        _minorFont = fontScheme?.MinorFont?.LatinFont?.Typeface?.Value;
    }

    /// <summary>Returns the style name for a paragraph style id, falling back to the default paragraph style.</summary>
    public string? GetParagraphStyleName(string? styleId)
    {
        var id = EffectiveParagraphStyleId(styleId);
        return id is not null && _styles.TryGetValue(id, out var style) ? style.StyleName?.Val?.Value ?? id : null;
    }

    public ParagraphFormat ResolveParagraph(string? styleId, W.ParagraphProperties? direct)
    {
        var builder = new ParagraphFormatBuilder();
        builder.Apply(_defaultParagraphProperties);
        foreach (var style in StyleChain(EffectiveParagraphStyleId(styleId)))
        {
            builder.Apply(style.StyleParagraphProperties);
        }

        builder.Apply(direct);
        return builder.Build();
    }

    public RunFormat ResolveRun(string? paragraphStyleId, W.RunProperties? direct)
    {
        var builder = new RunFormatBuilder(_majorFont, _minorFont);
        builder.Apply(_defaultRunProperties);
        foreach (var style in StyleChain(EffectiveParagraphStyleId(paragraphStyleId)))
        {
            builder.Apply(style.StyleRunProperties);
        }

        foreach (var style in StyleChain(direct?.RunStyle?.Val?.Value))
        {
            builder.Apply(style.StyleRunProperties);
        }

        builder.Apply(direct);
        return builder.Build();
    }

    private string? EffectiveParagraphStyleId(string? styleId) =>
        styleId is not null && _styles.ContainsKey(styleId) ? styleId : _defaultParagraphStyleId;

    /// <summary>The style and its basedOn ancestors, root first.</summary>
    private List<Style> StyleChain(string? styleId)
    {
        var chain = new List<Style>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (styleId is not null && visited.Add(styleId) && _styles.TryGetValue(styleId, out var style))
        {
            chain.Add(style);
            styleId = style.BasedOn?.Val?.Value;
        }

        chain.Reverse();
        return chain;
    }

    private sealed class ParagraphFormatBuilder
    {
        private ParagraphAlignment _alignment = ParagraphAlignment.Left;
        private float _before;
        private float _after;
        private LineSpacing _lineSpacing = LineSpacing.Default;
        private bool _keepNext;
        private bool _pageBreakBefore;
        private float _left;
        private float _right;
        private float _firstLine;

        public void Apply(OpenXmlElement? properties)
        {
            if (properties is null)
            {
                return;
            }

            if (properties.GetFirstChild<Justification>()?.Val is { } jc)
            {
                _alignment = MapAlignment(jc.Value);
            }

            if (properties.GetFirstChild<SpacingBetweenLines>() is { } spacing)
            {
                _before = Twips.ToPoints(spacing.Before?.Value) ?? _before;
                _after = Twips.ToPoints(spacing.After?.Value) ?? _after;
                if (Twips.Parse(spacing.Line?.Value) is { } line)
                {
                    var rule = spacing.LineRule?.Value;
                    _lineSpacing = rule == LineSpacingRuleValues.Exact
                        ? new LineSpacing(LineSpacingRule.Exact, line / 20f)
                        : rule == LineSpacingRuleValues.AtLeast
                            ? new LineSpacing(LineSpacingRule.AtLeast, line / 20f)
                            : new LineSpacing(LineSpacingRule.Multiple, line / 240f);
                }
            }

            if (properties.GetFirstChild<KeepNext>() is { } keepNext)
            {
                _keepNext = OnOff.IsOn(keepNext.Val);
            }

            if (properties.GetFirstChild<PageBreakBefore>() is { } pageBreakBefore)
            {
                _pageBreakBefore = OnOff.IsOn(pageBreakBefore.Val);
            }

            if (properties.GetFirstChild<Indentation>() is { } indentation)
            {
                _left = Twips.ToPoints(indentation.Left?.Value ?? indentation.Start?.Value) ?? _left;
                _right = Twips.ToPoints(indentation.Right?.Value ?? indentation.End?.Value) ?? _right;
                if (Twips.ToPoints(indentation.Hanging?.Value) is { } hanging)
                {
                    _firstLine = -hanging;
                }
                else if (Twips.ToPoints(indentation.FirstLine?.Value) is { } firstLine)
                {
                    _firstLine = firstLine;
                }
            }
        }

        public ParagraphFormat Build() =>
            new(_alignment, _before, _after, _lineSpacing, _keepNext, _pageBreakBefore, _left, _right, _firstLine);

        private static ParagraphAlignment MapAlignment(JustificationValues value)
        {
            if (value == JustificationValues.Center)
            {
                return ParagraphAlignment.Center;
            }

            if (value == JustificationValues.Right || value == JustificationValues.End)
            {
                return ParagraphAlignment.Right;
            }

            if (value == JustificationValues.Both || value == JustificationValues.Distribute)
            {
                return ParagraphAlignment.Justify;
            }

            return ParagraphAlignment.Left;
        }
    }

    private sealed class RunFormatBuilder(string? majorFont, string? minorFont)
    {
        private bool _bold;
        private bool _italic;
        private bool _underline;
        private bool _hidden;
        private float _sizePt = DefaultFontSizePt;
        private string? _fontName;
        private VerticalPosition _verticalPosition = VerticalPosition.Baseline;

        public void Apply(OpenXmlElement? properties)
        {
            if (properties is null)
            {
                return;
            }

            if (properties.GetFirstChild<Bold>() is { } bold)
            {
                _bold = OnOff.IsOn(bold.Val);
            }

            if (properties.GetFirstChild<Italic>() is { } italic)
            {
                _italic = OnOff.IsOn(italic.Val);
            }

            if (properties.GetFirstChild<W.Underline>() is { } underline)
            {
                _underline = underline.Val is null || underline.Val.Value != UnderlineValues.None;
            }

            if (properties.GetFirstChild<Vanish>() is { } vanish)
            {
                _hidden = OnOff.IsOn(vanish.Val);
            }

            if (properties.GetFirstChild<FontSize>()?.Val?.Value is { } halfPoints
                && float.TryParse(halfPoints, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size)
                && size > 0)
            {
                _sizePt = size / 2f;
            }

            if (properties.GetFirstChild<RunFonts>() is { } fonts)
            {
                _fontName = ResolveFont(fonts) ?? _fontName;
            }

            if (properties.GetFirstChild<VerticalTextAlignment>()?.Val is { } vertical)
            {
                _verticalPosition = vertical.Value == VerticalPositionValues.Superscript
                    ? VerticalPosition.Superscript
                    : vertical.Value == VerticalPositionValues.Subscript
                        ? VerticalPosition.Subscript
                        : VerticalPosition.Baseline;
            }
        }

        public RunFormat Build() => new(_bold, _italic, _underline, _hidden, _sizePt, _fontName, _verticalPosition);

        private string? ResolveFont(RunFonts fonts)
        {
            if (fonts.AsciiTheme?.Value is { } theme)
            {
                var isMajor = theme == ThemeFontValues.MajorAscii || theme == ThemeFontValues.MajorHighAnsi
                    || theme == ThemeFontValues.MajorBidi || theme == ThemeFontValues.MajorEastAsia;
                return isMajor ? majorFont : minorFont;
            }

            return fonts.Ascii?.Value ?? fonts.HighAnsi?.Value;
        }
    }
}

internal static class OnOff
{
    /// <summary>Toggle properties are on when present without a value; "0", "false" and "off" turn them off.</summary>
    public static bool IsOn(OnOffValue? value) => value is null || !value.HasValue || value.Value;
}
