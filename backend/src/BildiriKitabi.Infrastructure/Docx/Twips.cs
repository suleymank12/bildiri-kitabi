using System.Globalization;

namespace BildiriKitabi.Infrastructure.Docx;

internal static class Twips
{
    private const float TwipsPerPoint = 20f;

    /// <summary>
    /// Parses a twip measure. Transitional documents use plain integers; strict documents may carry a unit suffix.
    /// </summary>
    public static float? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips))
        {
            return twips;
        }

        if (value.Length < 3)
        {
            return null;
        }

        var unit = value[^2..];
        if (!float.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        float? points = unit switch
        {
            "pt" => number,
            "in" => number * 72f,
            "cm" => number * 72f / 2.54f,
            "mm" => number * 72f / 25.4f,
            "pc" => number * 12f,
            "pi" => number * 12f,
            _ => null,
        };
        return points * TwipsPerPoint;
    }

    public static float? ToPoints(string? value) => Parse(value) / TwipsPerPoint;
}
