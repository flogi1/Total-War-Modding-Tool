using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>A folding of one section: the offsets in the document and the section index. Pure over the block and the document line offsets.</summary>
public sealed record SectionFolding(int Section, int StartOffset, int EndOffset, int HiddenRows);

public static class BlockFolding
{
    /// <summary>
    /// One folding per section with more than threshold rows: from the end of the last line of row preview-1 to the end of the section's last
    /// line (both from lineStartOffsets, which has the start offset of every 0-based line and the text length as its last entry). Sections with
    /// at most threshold rows have none. HiddenRows is Rows.Count - preview.
    /// </summary>
    public static IReadOnlyList<SectionFolding> Compute(BlockText block, IReadOnlyList<int> lineStartOffsets,
        int threshold = ObjectText.DefaultCollapseThreshold, int preview = ObjectText.DefaultCollapsedPreview)
    {
        ArgumentNullException.ThrowIfNull(lineStartOffsets);
        return Compute(block, line => EndOfLine(lineStartOffsets, line), threshold, preview);
    }

    /// <summary>
    /// Like the overload with line starts, with the end offset of a 0-based line of the block as a function; null when the line is not in the
    /// document any more. A section whose first or last needed line is gone has no folding.
    /// </summary>
    public static IReadOnlyList<SectionFolding> Compute(BlockText block, Func<int, int?> endOfLine,
        int threshold = ObjectText.DefaultCollapseThreshold, int preview = ObjectText.DefaultCollapsedPreview)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(endOfLine);
        var result = new List<SectionFolding>();
        var shown = Math.Max(1, preview);
        foreach (var section in block.Sections)
        {
            var hidden = section.Rows.Count - shown;
            if (section.Rows.Count <= Math.Max(0, threshold) || hidden <= 0)
            {
                continue;
            }

            if (endOfLine(section.Rows[shown - 1].LastLine) is { } start && endOfLine(section.LastLine) is { } end && end > start)
            {
                result.Add(new SectionFolding(section.Section, start, end, hidden));
            }
        }

        return result;
    }

    // The end of a 0-based line without its line break: the next line starts behind the break, the last line ends with the text.
    private static int EndOfLine(IReadOnlyList<int> starts, int line) => line + 1 < starts.Count - 1 ? starts[line + 1] - 1 : starts[^1];
}
