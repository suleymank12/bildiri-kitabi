using System.Globalization;
using System.Text.RegularExpressions;
using BildiriKitabi.Core.Documents;

namespace BildiriKitabi.Core.Sanitization;

public sealed record ParagraphSanitizationResult(Paragraph? Paragraph, int RemovedEmailCount, int RemovedPhoneCount)
{
    public bool IsRemoved => Paragraph is null;
}

public sealed record DocumentSanitizationResult(SourceDocument Document, int RemovedEmailCount, int RemovedPhoneCount);

/// <summary>
/// Removes e-mail addresses and phone numbers, together with the labels and separators that only existed to
/// introduce them. Everything else in the paragraph, including run formatting, is left untouched.
/// </summary>
public static partial class ContactInfoSanitizer
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static DocumentSanitizationResult Sanitize(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var counter = new Counter();
        var blocks = SanitizeBlocks(document.Blocks, counter);
        var footnotes = new List<Footnote>();
        foreach (var footnote in document.Footnotes)
        {
            var paragraphs = SanitizeBlocks(footnote.Paragraphs, counter).OfType<Paragraph>().ToList();
            if (paragraphs.Any(p => !p.IsBlank))
            {
                footnotes.Add(footnote with { Paragraphs = paragraphs });
            }
        }

        return new DocumentSanitizationResult(new SourceDocument(blocks, footnotes), counter.Emails, counter.Phones);
    }

    public static ParagraphSanitizationResult Sanitize(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var runs = paragraph.Runs.Select(DropContactHyperlink).ToList();
        var text = string.Concat(runs.Select(r => r.Text));
        var matches = ContactInfoDetector.Find(text);
        if (matches.Count == 0)
        {
            return new ParagraphSanitizationResult(paragraph with { Runs = runs }, 0, 0);
        }

        var removed = new bool[text.Length];
        foreach (var match in matches)
        {
            var start = FindLabelStart(text, match.Start);
            Array.Fill(removed, true, start, match.End - start);
        }

        RemoveEmptiedBrackets(text, removed);
        CleanUpGaps(text, removed);

        var sanitizedRuns = new List<Run>(runs.Count);
        var offset = 0;
        foreach (var run in runs)
        {
            var kept = new char[run.Text.Length];
            var count = 0;
            for (var i = 0; i < run.Text.Length; i++)
            {
                if (!removed[offset + i])
                {
                    kept[count++] = run.Text[i];
                }
            }

            offset += run.Text.Length;
            if (count > 0)
            {
                sanitizedRuns.Add(count == run.Text.Length ? run : run with { Text = new string(kept, 0, count) });
            }
        }

        var emails = matches.Count(m => m.Kind == ContactKind.Email);
        var phones = matches.Count - emails;
        var result = paragraph with { Runs = sanitizedRuns };
        return new ParagraphSanitizationResult(result.IsBlank ? null : result, emails, phones);
    }

    private static List<Block> SanitizeBlocks(IEnumerable<Block> blocks, Counter counter)
    {
        var result = new List<Block>();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    var sanitized = Sanitize(paragraph);
                    counter.Emails += sanitized.RemovedEmailCount;
                    counter.Phones += sanitized.RemovedPhoneCount;
                    if (sanitized.Paragraph is not null)
                    {
                        result.Add(sanitized.Paragraph);
                    }

                    break;
                case Table table:
                    var rows = table.Rows
                        .Select(row => row with
                        {
                            Cells = row.Cells.Select(cell => cell with { Blocks = SanitizeBlocks(cell.Blocks, counter) }).ToList(),
                        })
                        .ToList();
                    result.Add(table with { Rows = rows });
                    break;
                default:
                    result.Add(block);
                    break;
            }
        }

        return result;
    }

    private static Run DropContactHyperlink(Run run)
    {
        if (run.Hyperlink is not { } target)
        {
            return run;
        }

        var isContactLink = target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("callto:", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("sms:", StringComparison.OrdinalIgnoreCase)
            || ContactInfoDetector.Find(Uri.UnescapeDataString(target)).Count > 0;
        return isContactLink ? run with { Hyperlink = null } : run;
    }

    /// <summary>Extends a match to the left over a label such as "Tel:" or "E-posta" that introduces it.</summary>
    private static int FindLabelStart(string text, int matchStart)
    {
        const int window = 24;
        var windowStart = Math.Max(0, matchStart - window);
        var lowered = text[windowStart..matchStart].ToLower(Turkish);
        var label = LabelRegex().Match(lowered);
        return label.Success ? windowStart + label.Index : matchStart;
    }

    private static void RemoveEmptiedBrackets(string text, bool[] removed)
    {
        for (var start = 0; start < text.Length; start++)
        {
            if (!removed[start] || (start > 0 && removed[start - 1]))
            {
                continue;
            }

            var end = start;
            while (end < text.Length && removed[end])
            {
                end++;
            }

            var left = start - 1;
            while (left >= 0 && (removed[left] || IsGapChar(text[left])))
            {
                left--;
            }

            var right = end;
            while (right < text.Length && (removed[right] || IsGapChar(text[right])))
            {
                right++;
            }

            if (left >= 0 && right < text.Length && IsBracketPair(text[left], text[right]))
            {
                removed[left] = true;
                removed[right] = true;
            }
        }
    }

    /// <summary>
    /// Handles the whitespace/separator runs that touch a removed range: at the paragraph edges they are dropped;
    /// between two pieces of remaining content, a separator is kept only when the removed item was delimited on
    /// both sides ("A | x@y.org | B" becomes "A | B"), otherwise a single space is kept.
    /// </summary>
    private static void CleanUpGaps(string text, bool[] removed)
    {
        var kept = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (!removed[i])
            {
                kept.Add(i);
            }
        }

        var n = kept.Count;
        var boundary = new bool[n + 1];
        for (var k = 0; k <= n; k++)
        {
            var from = k == 0 ? 0 : kept[k - 1] + 1;
            var to = k == n ? text.Length : kept[k];
            boundary[k] = to > from;
        }

        var a = 0;
        while (a <= n)
        {
            if (a < n && !IsGapChar(text[kept[a]]))
            {
                a++;
                continue;
            }

            var b = a;
            while (b < n && IsGapChar(text[kept[b]]))
            {
                b++;
            }

            var firstBoundary = -1;
            var lastBoundary = -1;
            for (var k = a; k <= b; k++)
            {
                if (boundary[k])
                {
                    firstBoundary = firstBoundary < 0 ? k : firstBoundary;
                    lastBoundary = k;
                }
            }

            if (firstBoundary >= 0 && b > a)
            {
                ResolveGap(text, removed, kept, a, b, firstBoundary, lastBoundary, hasLeftContent: a > 0, hasRightContent: b < n);
            }

            a = b + 1;
        }
    }

    private static void ResolveGap(
        string text, bool[] removed, List<int> kept, int a, int b, int firstBoundary, int lastBoundary, bool hasLeftContent, bool hasRightContent)
    {
        if (!hasLeftContent || !hasRightContent)
        {
            RemoveKept(removed, kept, a, b);
            return;
        }

        var leftHasSeparator = Enumerable.Range(a, firstBoundary - a).Any(k => IsSeparator(text[kept[k]]));
        var rightHasSeparator = Enumerable.Range(lastBoundary, b - lastBoundary).Any(k => IsSeparator(text[kept[k]]));
        if (leftHasSeparator && rightHasSeparator)
        {
            RemoveKept(removed, kept, firstBoundary, b);
            return;
        }

        var space = Enumerable.Range(a, b - a).FirstOrDefault(k => char.IsWhiteSpace(text[kept[k]]), -1);
        RemoveKept(removed, kept, a, b);
        if (space >= 0)
        {
            removed[kept[space]] = false;
        }
    }

    private static void RemoveKept(bool[] removed, List<int> kept, int from, int to)
    {
        for (var k = from; k < to; k++)
        {
            removed[kept[k]] = true;
        }
    }

    private static bool IsGapChar(char c) => char.IsWhiteSpace(c) || IsSeparator(c);

    private static bool IsSeparator(char c) => c is '|' or '/' or ';' or ',' or '-' or '–' or '—' or '·' or '•';

    private static bool IsBracketPair(char open, char close) => (open, close) is ('(', ')') or ('[', ']') or ('{', '}');

    // Matched against tr-TR lower-cased text, so an upper-case English "I" arrives as a dotless "ı".
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:cep\s+telefonu|tel\.?\s*no|telefon|tel|e-?posta|e-?ma[iı]l|ma[iı]l|gsm|cep|mob[iı]le|phone|faks|fax|[iı]rt[iı]bat|[iı]let[iı]ş[iı]m|contact)\s*[:.]?\s*\z")]
    private static partial Regex LabelRegex();

    private sealed class Counter
    {
        public int Emails { get; set; }

        public int Phones { get; set; }
    }
}
