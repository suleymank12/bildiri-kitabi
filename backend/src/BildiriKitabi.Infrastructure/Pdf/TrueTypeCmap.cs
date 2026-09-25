using System.Buffers.Binary;

namespace BildiriKitabi.Infrastructure.Pdf;

/// <summary>
/// Reads the character-to-glyph map ('cmap' table) of a TrueType font: the set of Unicode code points the font
/// has a real glyph for (glyph 0, ".notdef", does not count). Supports the Unicode subtables used by all common
/// fonts: format 4 (BMP) and format 12 (full Unicode).
/// </summary>
internal static class TrueTypeCmap
{
    public static HashSet<int> ReadCodePoints(Stream font)
    {
        ArgumentNullException.ThrowIfNull(font);
        using var buffer = new MemoryStream();
        font.CopyTo(buffer);
        return ReadCodePoints(buffer.ToArray());
    }

    public static HashSet<int> ReadCodePoints(ReadOnlySpan<byte> font)
    {
        var cmap = FindTable(font, "cmap");
        var subtableCount = U16(cmap, 2);
        int? best = null;
        var bestRank = int.MaxValue;
        for (var i = 0; i < subtableCount; i++)
        {
            var record = 4 + (i * 8);
            var platform = U16(cmap, record);
            var encoding = U16(cmap, record + 2);
            var offset = (int)U32(cmap, record + 4);
            var format = U16(cmap, offset);

            // Prefer the full-Unicode table, then the BMP table (Windows or Unicode platform).
            var rank = (platform, encoding, format) switch
            {
                (3, 10, 12) => 0,
                (0, _, 12) => 1,
                (3, 1, 4) => 2,
                (0, _, 4) => 3,
                _ => int.MaxValue,
            };
            if (rank < bestRank)
            {
                bestRank = rank;
                best = offset;
            }
        }

        if (best is not { } subtable)
        {
            throw new InvalidDataException("The font has no Unicode cmap subtable.");
        }

        return U16(cmap, subtable) == 12 ? ReadFormat12(cmap[subtable..]) : ReadFormat4(cmap[subtable..]);
    }

    private static HashSet<int> ReadFormat4(ReadOnlySpan<byte> table)
    {
        var segments = U16(table, 6) / 2;
        var endCodes = 14;
        var startCodes = endCodes + (segments * 2) + 2;
        var idDeltas = startCodes + (segments * 2);
        var idRangeOffsets = idDeltas + (segments * 2);
        var codePoints = new HashSet<int>();
        for (var s = 0; s < segments; s++)
        {
            var end = U16(table, endCodes + (s * 2));
            var start = U16(table, startCodes + (s * 2));
            var delta = (short)U16(table, idDeltas + (s * 2));
            var rangeOffsetPosition = idRangeOffsets + (s * 2);
            var rangeOffset = U16(table, rangeOffsetPosition);
            for (int c = start; c <= end; c++)
            {
                if (c == 0xFFFF)
                {
                    break;
                }

                int glyph;
                if (rangeOffset == 0)
                {
                    glyph = (c + delta) & 0xFFFF;
                }
                else
                {
                    var glyphPosition = rangeOffsetPosition + rangeOffset + ((c - start) * 2);
                    glyph = U16(table, glyphPosition);
                    if (glyph != 0)
                    {
                        glyph = (glyph + delta) & 0xFFFF;
                    }
                }

                if (glyph != 0)
                {
                    codePoints.Add(c);
                }
            }
        }

        return codePoints;
    }

    private static HashSet<int> ReadFormat12(ReadOnlySpan<byte> table)
    {
        var groups = (int)U32(table, 12);
        var codePoints = new HashSet<int>();
        for (var g = 0; g < groups; g++)
        {
            var group = 16 + (g * 12);
            var start = (int)U32(table, group);
            var end = (int)U32(table, group + 4);
            var startGlyph = U32(table, group + 8);
            for (var c = start; c <= end; c++)
            {
                if (startGlyph + (uint)(c - start) != 0)
                {
                    codePoints.Add(c);
                }
            }
        }

        return codePoints;
    }

    private static ReadOnlySpan<byte> FindTable(ReadOnlySpan<byte> font, string tag)
    {
        var tableCount = U16(font, 4);
        for (var i = 0; i < tableCount; i++)
        {
            var record = 12 + (i * 16);
            if (font[record] == tag[0] && font[record + 1] == tag[1] && font[record + 2] == tag[2] && font[record + 3] == tag[3])
            {
                var offset = (int)U32(font, record + 8);
                var length = (int)U32(font, record + 12);
                return font.Slice(offset, length);
            }
        }

        throw new InvalidDataException($"The font has no '{tag}' table.");
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
}
