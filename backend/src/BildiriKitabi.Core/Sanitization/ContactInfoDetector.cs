using System.Text.RegularExpressions;

namespace BildiriKitabi.Core.Sanitization;

public enum ContactKind
{
    Email,
    Phone,
}

public readonly record struct ContactMatch(ContactKind Kind, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds e-mail addresses and phone numbers in plain text.
/// Shared by the sanitizer and by the post-render leak scan so both apply the same rules.
/// </summary>
public static partial class ContactInfoDetector
{
    private static readonly string[] NonContactNumberLabels = ["ORCID", "ISBN", "ISSN", "DOI"];

    public static IReadOnlyList<ContactMatch> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var emails = FindEmails(text);
        var matches = new List<ContactMatch>(emails);
        foreach (var phone in FindPhones(text))
        {
            if (!emails.Any(e => e.Start < phone.End && phone.Start < e.End))
            {
                matches.Add(phone);
            }
        }

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        return matches;
    }

    private static List<ContactMatch> FindEmails(string text)
    {
        var candidates = new List<ContactMatch>();
        foreach (Match m in EmailRegex().Matches(text))
        {
            candidates.Add(new ContactMatch(ContactKind.Email, m.Index, m.Length));
        }

        foreach (Match m in ObfuscatedEmailRegex().Matches(text))
        {
            candidates.Add(new ContactMatch(ContactKind.Email, m.Index, m.Length));
        }

        candidates.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        var result = new List<ContactMatch>();
        foreach (var candidate in candidates)
        {
            if (result.Count > 0 && candidate.Start < result[^1].End)
            {
                var last = result[^1];
                var end = Math.Max(last.End, candidate.End);
                result[^1] = last with { Length = end - last.Start };
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private static IEnumerable<ContactMatch> FindPhones(string text)
    {
        foreach (Match m in PhoneCandidateRegex().Matches(text))
        {
            var start = m.Index;
            var value = m.Value;

            // A leading "(" without a matching ")" belongs to the surrounding prose, not the number.
            if (value.Count(c => c == '(') > value.Count(c => c == ')'))
            {
                var firstParen = value.IndexOf('(', StringComparison.Ordinal);
                var digitStart = firstParen + 1;
                while (digitStart < value.Length && !char.IsAsciiDigit(value[digitStart]))
                {
                    digitStart++;
                }

                if (firstParen == 0 || value[..firstParen].Trim() == "+")
                {
                    start += digitStart;
                    value = value[digitStart..];
                }
            }

            var end = start + value.Length;
            if (!HasValidBoundaries(text, start, end) || IsPrecededByNonContactLabel(text, start) || !IsPhoneNumber(value))
            {
                continue;
            }

            yield return new ContactMatch(ContactKind.Phone, start, value.Length);
        }
    }

    private static bool HasValidBoundaries(string text, int start, int end)
    {
        if (start > 0 && !IsAllowedLeftNeighbour(text[start - 1]))
        {
            return false;
        }

        if (end < text.Length)
        {
            var next = text[end];
            if (next == '.')
            {
                // Sentence-ending period is fine; anything glued after it (e.g. "2020.123456abc") is not.
                if (end + 1 < text.Length && !char.IsWhiteSpace(text[end + 1]))
                {
                    return false;
                }
            }
            else if (!IsAllowedRightNeighbour(next))
            {
                return false;
            }
        }

        // Another digit group within two separator characters means this is a fragment of a longer number (e.g. an IBAN).
        return !HasNearbyDigit(text, start - 1, -1) && !HasNearbyDigit(text, end, +1);
    }

    private static bool HasNearbyDigit(string text, int index, int step)
    {
        for (var i = 0; i < 3 && index >= 0 && index < text.Length; i++, index += step)
        {
            var c = text[index];
            if (char.IsDigit(c))
            {
                return true;
            }

            if (!IsNumberSeparator(c))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsNumberSeparator(char c) => c is ' ' or ' ' or '.' or '-' or '(' or ')';

    private static bool IsAllowedLeftNeighbour(char c) =>
        char.IsWhiteSpace(c) || c is '(' or '[' or '{' or ':' or ';' or ',' or '|' or '/' or '"' or '\'' or '“' or '‘' or '«' or '–' or '—' or '=';

    private static bool IsAllowedRightNeighbour(char c) =>
        char.IsWhiteSpace(c) || c is ')' or ']' or '}' or ';' or ',' or '|' or '/' or '"' or '\'' or '”' or '’' or '»' or '!' or '?' or '–' or '—';

    private static bool IsPrecededByNonContactLabel(string text, int start)
    {
        var i = start - 1;
        while (i >= 0 && (char.IsWhiteSpace(text[i]) || text[i] is ':' or '#' or '-'))
        {
            i--;
        }

        var wordEnd = i + 1;
        while (i >= 0 && char.IsLetter(text[i]))
        {
            i--;
        }

        var word = text[(i + 1)..wordEnd];
        return NonContactNumberLabels.Any(label => string.Equals(word, label, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPhoneNumber(string candidate)
    {
        var trimmed = candidate.Trim();
        if (DateRegex().IsMatch(trimmed) || YearRangeRegex().IsMatch(trimmed)
            || DecimalRegex().IsMatch(trimmed) || ThousandsRegex().IsMatch(trimmed))
        {
            return false;
        }

        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        var hasPlus = trimmed.StartsWith('+');

        if (IsTurkishNumber(digits))
        {
            return true;
        }

        if (hasPlus)
        {
            return digits.Length is >= 8 and <= 15;
        }

        return digits.StartsWith("00", StringComparison.Ordinal) && digits.Length - 2 is >= 8 and <= 15;
    }

    private static bool IsTurkishNumber(string digits)
    {
        var national = digits switch
        {
            { Length: 12 } when digits.StartsWith("90", StringComparison.Ordinal) => digits[2..],
            { Length: 14 } when digits.StartsWith("0090", StringComparison.Ordinal) => digits[4..],
            { Length: 11 } when digits[0] == '0' => digits[1..],
            { Length: 10 } => digits,
            _ => null,
        };

        return national is { Length: 10 } && national[0] is '2' or '3' or '4' or '5' or '8';
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}._%+\-])(?:mailto:)?[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}\-]+(?:\.[\p{L}\p{N}\-]+)*\.\p{L}{2,}(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}._%+\-])[\p{L}\p{N}._%+\-]+\s*[\[({]\s*(?:at|@)\s*[\])}]\s*[\p{L}\p{N}\-]+(?:(?:\s*[\[({]\s*(?:dot|nokta)\s*[\])}]\s*|\.)[\p{L}\p{N}\-]+)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObfuscatedEmailRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\+[  ]?)?(?:\([  ]?)?\d(?:[  .\-()]{0,2}\d)*")]
    private static partial Regex PhoneCandidateRegex();

    [GeneratedRegex(@"^\d{1,2}([./\-])\d{1,2}\1\d{2,4}$")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^\d{4}\s?[-–]\s?\d{4}$")]
    private static partial Regex YearRangeRegex();

    [GeneratedRegex(@"^\d+[.,]\d+$")]
    private static partial Regex DecimalRegex();

    [GeneratedRegex(@"^\d{1,3}(?:[.,]\d{3})+$")]
    private static partial Regex ThousandsRegex();
}
