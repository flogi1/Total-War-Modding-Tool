using System.Globalization;

namespace TotalWarModTool.Tables;

/// <summary>
/// The one rule for a colour stored as text: hex digits only (ASCII, either case), no sign, no prefix, no spaces. The encoder and
/// the TSV code share it, so that a colour one of them accepts is a colour the other accepts too.
/// </summary>
internal static class ColourText
{
    /// <summary>A colour field is a u32, so at most eight hex digits.</summary>
    internal const int MaxDigits = 8;

    /// <summary>One to <see cref="MaxDigits"/> hex digits.</summary>
    internal static bool TryParse(string text, out uint colour) => TryParse(text, 1, MaxDigits, out colour);

    /// <summary>Between <paramref name="minDigits"/> and <paramref name="maxDigits"/> hex digits.</summary>
    internal static bool TryParse(string text, int minDigits, int maxDigits, out uint colour)
    {
        colour = 0;
        if (text.Length < minDigits || text.Length > maxDigits)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out colour);
    }
}
