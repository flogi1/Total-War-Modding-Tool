using System.Globalization;
using System.Text;

namespace TotalWarModTool.App;

/// <summary>Makes text from foreign files (paths, reasons) safe to show: no control characters, no broken surrogates, a limited length.</summary>
public static class DisplayText
{
    private const char Backslash = (char)0x5C;
    private const string DefaultMarker = " (shortened)";

    /// <summary>
    /// Characters that are escaped as \uXXXX: control characters (category Cc), unpaired surrogates, the line and paragraph separators
    /// and the bidirectional controls, which can break a line of UI text or disguise what it says. Other format characters (for example
    /// the zero width joiners) stay.
    /// </summary>
    private static bool NeedsEscape(char c) =>
        char.IsControl(c) || char.IsSurrogate(c)
        || c is (char)0x2028 or (char)0x2029 or (char)0x061C or (char)0x200E or (char)0x200F
            or (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069);

    /// <summary>
    /// Characters that need escaping as \uXXXX (see <see cref="NeedsEscape"/>), at most <paramref name="maxChars"/> characters of
    /// <paramref name="text"/>. The limit counts the characters of the text before the escaping; a cut is marked with
    /// <paramref name="marker"/> after them and never splits a surrogate pair. Valid surrogate pairs stay as they are.
    /// </summary>
    /// <param name="marker">Appended after a cut text. Pass the localised marker of the UI; the default is the English one.</param>
    public static string Safe(string text, int maxChars = 300, string marker = DefaultMarker)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);

        var length = Math.Min(text.Length, maxChars);
        var cut = length < text.Length;
        if (cut && length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
        {
            length--;
        }

        var result = new StringBuilder(length + marker.Length + 8);
        for (var i = 0; i < length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
            {
                result.Append(c).Append(text[++i]);
            }
            else if (NeedsEscape(c))
            {
                result.Append(Backslash).Append('u').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                result.Append(c);
            }
        }

        return cut ? result.Append(marker).ToString() : result.ToString();
    }
}
