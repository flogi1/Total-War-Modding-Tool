using System.Text;

namespace TotalWarModTool.Objects;

public enum BlockLineKind
{
    Head,
    RootField,
    RootText,
    SectionHead,
    RowHead,
    RowField,
    RowText,
    Blank,
    Annotation,
}

/// <summary>
/// Spans is the editable text of the line (empty for Blank and Annotation), Trailing the annotations drawn behind it. Indent counts levels of
/// two spaces. Section is the index into ObjectView.Sections for SectionHead, RowHead, RowField and RowText; Row the index into the
/// section's Rows for the row lines.
/// </summary>
public sealed record BlockLine(BlockLineKind Kind, IReadOnlyList<TextSpan> Spans, IReadOnlyList<TextSpan> Trailing, int Indent, int? Section = null,
    int? Row = null);

/// <summary>FirstLine and LastLine are inclusive line indexes of one row block (its head line and its field and text lines).</summary>
public sealed record BlockRowRange(int Section, int Row, int FirstLine, int LastLine);

/// <summary>
/// HeadLine is the section head; FirstLine..LastLine the lines of its rows. Rows lists the row blocks in order, so the App can fold everything
/// after ObjectText.DefaultCollapsedPreview rows.
/// </summary>
public sealed record BlockSectionRange(int Section, int HeadLine, int FirstLine, int LastLine, IReadOnlyList<BlockRowRange> Rows);

public sealed record BlockText(IReadOnlyList<BlockLine> Lines, IReadOnlyList<BlockSectionRange> Sections)
{
    /// <summary>
    /// The editable text: the Spans of every line joined, Indent * 2 spaces in front, lines joined with "\n". Blank and Annotation lines are
    /// empty lines.
    /// </summary>
    public string Text { get; } = Join(Lines);

    private static string Join(IReadOnlyList<BlockLine> lines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            var line = lines[i];
            if (line.Spans.Count == 0)
            {
                continue;
            }

            sb.Append(' ', line.Indent * 2);
            foreach (var span in line.Spans)
            {
                sb.Append(span.Text);
            }
        }

        return sb.ToString();
    }
}
