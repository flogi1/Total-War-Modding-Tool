using System.Text;

namespace TotalWarModTool.Workspace;

/// <summary>Decoded text of an entry. <see cref="TotalBytes"/> is the full length; with <see cref="Truncated"/> the text covers only the start.</summary>
public sealed record TextContent(string Text, string EncodingName, bool Truncated, long TotalBytes);

public static class EntryText
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private const string Utf8Name = "UTF-8";
    private const string Utf16LeName = "UTF-16 LE";
    private const string Utf16BeName = "UTF-16 BE";

    /// <summary>
    /// Decodes by byte order mark (UTF-8 BOM, UTF-16 LE, UTF-16 BE; UTF-8 without one). The BOM is not part of the text. At most
    /// <paramref name="maxBytes"/> bytes (BOM included) are decoded; the cut never lies inside a character. Invalid bytes become the
    /// replacement character, never an exception.
    /// </summary>
    public static TextContent Decode(byte[] bytes, int maxBytes = MaxBytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var (encoding, name, bomLength) = DetectEncoding(bytes);
        var truncated = bytes.Length > maxBytes;
        var end = truncated ? maxBytes : bytes.Length;
        if (end <= bomLength)
        {
            return new TextContent(string.Empty, name, truncated, bytes.Length);
        }

        if (truncated)
        {
            end = encoding == Encoding.UTF8
                ? CutUtf8(bytes, bomLength, end)
                : CutUtf16(bytes, bomLength, end, bigEndian: encoding == Encoding.BigEndianUnicode);
        }

        var text = encoding.GetString(bytes, bomLength, Math.Max(0, end - bomLength));
        return new TextContent(text, name, truncated, bytes.Length);
    }

    private static (Encoding Encoding, string Name, int BomLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8, Utf8Name, 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode, Utf16LeName, 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode, Utf16BeName, 2);
        }

        return (Encoding.UTF8, Utf8Name, 0);
    }

    // A UTF-8 character has at most 4 bytes, so its start lies at most 3 bytes before the cut. When the character that
    // starts there reaches past the cut, the cut moves in front of it.
    private static int CutUtf8(byte[] bytes, int start, int end)
    {
        for (var p = end - 1; p >= start && p >= end - 3; p--)
        {
            var b = bytes[p];
            if ((b & 0xC0) == 0x80)
            {
                continue;
            }

            var length = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
            return p + length <= end ? end : p;
        }

        return end;
    }

    private static int CutUtf16(byte[] bytes, int start, int end, bool bigEndian)
    {
        end = start + ((end - start) & ~1);
        if (end - start >= 2)
        {
            var high = bigEndian ? bytes[end - 2] : bytes[end - 1];
            if (high is >= 0xD8 and <= 0xDB)
            {
                end -= 2;
            }
        }

        return end;
    }
}
