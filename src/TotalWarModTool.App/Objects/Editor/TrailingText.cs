using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>One drawn piece of a trailing: the final text (translated, escaped) and its kind for the brush.</summary>
public sealed record TrailingPiece(string Text, SpanKind Kind);

/// <summary>The text pieces of a line's trailing, as the editor draws them. Pure: titles translated, Duplicate/OlderLayout in the current language, data escaped.</summary>
public static class TrailingText
{
    private const string Gap = "  ";

    /// <summary>Empty for an empty trailing; else the pieces, with the gap ("  ") in front of the first one as a Normal piece.</summary>
    public static IReadOnlyList<TrailingPiece> Of(BlockLine line, AppTexts texts, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(translate);
        if (line.Trailing.Count == 0)
        {
            return [];
        }

        var pieces = new List<TrailingPiece>(line.Trailing.Count + 1) { new(Gap, SpanKind.Normal) };
        foreach (var span in line.Trailing)
        {
            var text = span.Kind switch
            {
                SpanKind.Title => translate(span.Text),
                SpanKind.Duplicate => texts.ObjectsDuplicate,
                SpanKind.OlderLayout => texts.ObjectsOlderLayout,
                _ => span.Text,
            };
            pieces.Add(new TrailingPiece(MessageText.EscapeControlCharacters(text), span.Kind));
        }

        return pieces;
    }

    /// <summary>Annotations are muted (Theme.ObjectTrailing); provenance, overridden sources, duplicate marks and warnings keep the colour of their span.</summary>
    public static string BrushKeyOf(SpanKind kind) => kind switch
    {
        SpanKind.ProvenanceVanilla or SpanKind.ProvenanceMod or SpanKind.ProvenanceProject or SpanKind.Overridden or SpanKind.Warning
            or SpanKind.Duplicate or SpanKind.OlderLayout => SpanBrushes.ResourceKeyOf(kind),
        _ => ThemePalette.ResourcePrefix + "ObjectTrailing",
    };
}
