namespace BildiriKitabi.Core.Sanitization;

/// <summary>
/// Contact values that may appear in a finished book because the user typed them into the book name.
/// Values are compared in normalized form: phone numbers as digits only, e-mail addresses in lower case.
/// </summary>
public sealed class PermittedContactValues
{
    private readonly List<(ContactKind Kind, string Value)> _values;

    private PermittedContactValues(List<(ContactKind Kind, string Value)> values)
    {
        _values = values;
    }

    public static PermittedContactValues None { get; } = new([]);

    public static PermittedContactValues FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        var values = ContactInfoDetector.Find(text)
            .Select(match => (match.Kind, Normalize(match.Kind, text.Substring(match.Start, match.Length))))
            .Where(value => value.Item2.Length > 0)
            .ToList();
        return new PermittedContactValues(values);
    }

    /// <summary>
    /// True when the matched text equals a permitted value or is a piece of one; the running head may cut the book
    /// name short with "…", which can leave a shorter number that still looks like a phone number.
    /// </summary>
    public bool Permits(ContactKind kind, string matchedText)
    {
        ArgumentNullException.ThrowIfNull(matchedText);

        var normalized = Normalize(kind, matchedText);
        return normalized.Length > 0
            && _values.Any(value => value.Kind == kind && value.Value.Contains(normalized, StringComparison.Ordinal));
    }

    // Only a comparison key, never printed: e-mail addresses are ASCII in practice, so the invariant culture is used.
    private static string Normalize(ContactKind kind, string text) => kind == ContactKind.Phone
        ? string.Concat(text.Where(char.IsAsciiDigit))
        : text.Trim().ToLowerInvariant();
}
