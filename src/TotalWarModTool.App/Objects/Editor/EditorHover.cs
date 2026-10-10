using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>What the tool tip of a document position says. Pure over the tab and the map.</summary>
public static class EditorHover
{
    /// <summary>
    /// The lines of the tool tip at a 1-based document line and a 0-based column, or null: the diagnostics of the line (DiagnosticText(kind), plus
    /// " " and the escaped Detail for InvalidValue and ReferenceNotFound), then, for an intact line, the display name of a Link span under the column
    /// (tab.DisplayNameOf(target) or the short table name of the target) or ObjectsTargetUnknown for a Warning value span under the column.
    /// </summary>
    public static string? ToolTipAt(ObjectTabViewModel tab, BlockLineMap? map, int line, int column, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(texts);
        var lines = new List<string>();
        foreach (var diagnostic in tab.Diagnostics.Where(d => d.Line == line))
        {
            var text = texts.DiagnosticText(diagnostic.Kind);
            if (diagnostic.Kind is DiagnosticKind.InvalidValue or DiagnosticKind.ReferenceNotFound && diagnostic.Detail.Length > 0)
            {
                text += " " + MessageText.EscapeControlCharacters(diagnostic.Detail);
            }

            lines.Add(text);
        }

        if (SpanAt(map, line, column) is { } span)
        {
            if (span.Kind == SpanKind.Link && span.Target is { } target)
            {
                lines.Add(tab.DisplayNameOf(target) ?? MessageText.EscapeControlCharacters(ObjectText.ShortTableName(target.Table)));
            }
            else if (span.Kind == SpanKind.Warning && span.Target is null)
            {
                lines.Add(texts.ObjectsTargetUnknown);
            }
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>The Link span under the column of an intact line, or null. Columns count from the start of the line, indent included.</summary>
    public static TextSpan? LinkAt(BlockLineMap? map, int line, int column) =>
        SpanAt(map, line, column) is { Kind: SpanKind.Link, Target: not null } span ? span : null;

    // The value spans of an intact line (the colouring spans); a line the user changed has none, so it has no link and no warning tool tip.
    private static TextSpan? SpanAt(BlockLineMap? map, int line, int column)
    {
        if (map is null || column < 0 || map.IntactLineAt(line) is null)
        {
            return null;
        }

        var start = 0;
        foreach (var span in map.Resolve(line).Spans)
        {
            var end = start + span.Text.Length;
            if (column >= start && column < end)
            {
                return span;
            }

            start = end;
        }

        return null;
    }
}
