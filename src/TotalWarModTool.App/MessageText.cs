using System.Globalization;
using System.Text;

namespace TotalWarModTool.App;

/// <summary>Helpers that put a UI text and the technical data of a library together into one message.</summary>
internal static class MessageText
{
    private const char Backslash = (char)0x5C;

    /// <summary>The UI text and, when there is a technical detail, the detail after a blank line.</summary>
    internal static string WithDetail(string text, string? detail) =>
        string.IsNullOrEmpty(detail) ? text : text + "\n\n" + detail;

    /// <summary>
    /// <paramref name="text"/> with every control character written as <c>\uXXXX</c>, for data from foreign files that is shown inside
    /// a message.
    /// </summary>
    internal static string EscapeControlCharacters(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        var escaped = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                escaped.Append(Backslash).Append('u').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }
}
