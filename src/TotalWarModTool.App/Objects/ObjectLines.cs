using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// A span the view shows. <paramref name="Target"/> for Link; <paramref name="Section"/> for Collapsed and Title; <paramref name="ToolTip"/> for
/// Warning values (target unknown). Text is final: titles translated, Collapsed/Duplicate/OlderLayout in the current language.
/// </summary>
public sealed record ObjectSpan(string Text, SpanKind Kind, ObjectId? Target = null, int? Section = null, string? ToolTip = null);

public sealed record ObjectLine(IReadOnlyList<ObjectSpan> Spans, int Indent);

/// <summary>Turns the lines of the library into the lines the view shows: the texts of the app come in, nothing else changes.</summary>
public static class ObjectLines
{
    /// <param name="translate">A loc key of a section title to its text (<c>UiText.Get(key, fallback)</c> with the key as fallback).</param>
    public static IReadOnlyList<ObjectLine> Build(IReadOnlyList<TextLine> lines, AppTexts texts, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(translate);
        return lines.Select(line => new ObjectLine(line.Spans.Select(span => Translate(line, span, texts, translate)).ToArray(), line.Indent)).ToArray();
    }

    private static ObjectSpan Translate(TextLine line, TextSpan span, AppTexts texts, Func<string, string> translate) => span.Kind switch
    {
        SpanKind.Title => new ObjectSpan(translate(span.Text), span.Kind, span.Target, span.Section),
        SpanKind.Collapsed => new ObjectSpan(texts.ObjectsMoreRows(span.Count ?? 0), span.Kind, span.Target, span.Section),
        SpanKind.Duplicate => new ObjectSpan(texts.ObjectsDuplicate, span.Kind, span.Target, span.Section),
        SpanKind.OlderLayout => new ObjectSpan(texts.ObjectsOlderLayout, span.Kind, span.Target, span.Section),
        // A warning line of the view is one Warning span at indent 0 (data); any other Warning is a value that has no target.
        SpanKind.Warning when !(line.Indent == 0 && line.Spans.Count == 1) => new ObjectSpan(span.Text, span.Kind, span.Target, span.Section, texts.ObjectsTargetUnknown),
        _ => new ObjectSpan(span.Text, span.Kind, span.Target, span.Section),
    };
}
