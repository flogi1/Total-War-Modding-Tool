namespace TotalWarModTool.App;

/// <summary>
/// The cap of every list in a question or a message: at most <see cref="MaxLines"/> lines, then one line that says how many are missing.
/// Pure; used by <see cref="EditDialogs"/> and by the messages of <see cref="DocumentsController"/>.
/// </summary>
internal static class CappedLines
{
    /// <summary>The most lines a question or a message lists before it only counts the rest.</summary>
    public const int MaxLines = 10;

    /// <param name="lines">The lines in the order to show them.</param>
    /// <param name="moreLine">Builds the closing line from the number of lines that are not listed; called only when there are any.</param>
    public static List<string> Of(IReadOnlyList<string> lines, Func<int, string> moreLine)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(moreLine);

        var shown = lines.Take(MaxLines).ToList();
        if (lines.Count > MaxLines)
        {
            shown.Add(moreLine(lines.Count - MaxLines));
        }

        return shown;
    }
}
