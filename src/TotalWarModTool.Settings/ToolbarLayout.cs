namespace TotalWarModTool.Settings;

/// <summary>Visible sequence of the small toolbar from the user's order and the visibility of each entry.</summary>
public static class ToolbarLayout
{
    /// <summary>
    /// Hidden entries drop out; then separators at the start, at the end and directly after another separator drop out, so at most
    /// one separator stands between two visible entries.
    /// </summary>
    public static IReadOnlyList<string> VisibleSequence(IReadOnlyList<string> order, Func<string, bool> isVisible)
    {
        var result = new List<string>();
        foreach (var id in order)
        {
            if (ToolbarCatalog.IsSeparator(id))
            {
                if (result.Count > 0 && !ToolbarCatalog.IsSeparator(result[^1]))
                {
                    result.Add(id);
                }
            }
            else if (isVisible(id))
            {
                result.Add(id);
            }
        }

        while (result.Count > 0 && ToolbarCatalog.IsSeparator(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
