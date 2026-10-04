using System.Globalization;
using System.Text;

namespace TotalWarModTool.App;

/// <summary>Makes text from foreign files (paths, reasons) safe to show: no control characters, no broken surrogates, a limited length.</summary>
public static class DisplayText
{
    private const char Backslash = (char)0x5C;
    private const string Shortened = " (shortened)";

    /// <summary>
    /// Control characters and unpaired surrogates as \uXXXX, at most <paramref name="maxChars"/> characters of <paramref name="text"/>.
    /// The limit counts the characters of the text before the escaping; a cut is marked with " (shortened)" after them and never splits
    /// a surrogate pair. The marker is an English literal on purpose: the text is data of a foreign file, not a UI text.
    /// </summary>
    public static string Safe(string text, int maxChars = 300)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);

        var length = Math.Min(text.Length, maxChars);
        var cut = length < text.Length;
        if (cut && length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
        {
            length--;
        }

        var result = new StringBuilder(length + Shortened.Length + 8);
        for (var i = 0; i < length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
            {
                result.Append(c).Append(text[++i]);
            }
            else if (char.IsControl(c) || char.IsSurrogate(c))
            {
                result.Append(Backslash).Append('u').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                result.Append(c);
            }
        }

        return cut ? result.Append(Shortened).ToString() : result.ToString();
    }
}
