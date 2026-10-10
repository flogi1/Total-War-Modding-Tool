using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>Spans of one text line that has no baseline line (the user changed it), from its text alone. Pure; the colours follow SpanBrushes.</summary>
public static class LineSyntax
{
    private const string Assignment = " = ";

    /// <summary>
    /// The spans of the line text (without its line break), covering it completely: leading spaces as Normal; the head line (isHead) as
    /// TableName, Normal, Key; "name = value" as FieldName, Normal (" = "), then LocText when the value starts with a quote, else Value; a line
    /// without " = " at indent 0 as TableName (a section head), at indent 1 or 2 as Value (a sentence or junction row); an empty line as no spans.
    /// </summary>
    public static IReadOnlyList<TextSpan> Classify(string lineText, bool isHead)
    {
        ArgumentNullException.ThrowIfNull(lineText);
        var spans = new List<TextSpan>();
        var start = 0;
        while (start < lineText.Length && (lineText[start] == ' ' || lineText[start] == '\t'))
        {
            start++;
        }

        if (start > 0)
        {
            spans.Add(new TextSpan(lineText[..start], SpanKind.Normal));
        }

        var body = lineText[start..];
        if (body.Length == 0)
        {
            return spans;
        }

        if (isHead)
        {
            AddHead(spans, body);
            return spans;
        }

        var assignment = body.IndexOf(Assignment, StringComparison.Ordinal);
        var bare = assignment < 0 && body.EndsWith(" =", StringComparison.Ordinal);
        if (assignment >= 0 || bare)
        {
            var at = bare ? body.Length - 2 : assignment;
            spans.Add(new TextSpan(body[..at], SpanKind.FieldName));
            if (bare)
            {
                spans.Add(new TextSpan(" =", SpanKind.Normal));
                return spans;
            }

            spans.Add(new TextSpan(Assignment, SpanKind.Normal));
            var value = body[(at + Assignment.Length)..];
            if (value.Length > 0)
            {
                spans.Add(new TextSpan(value, value[0] == '"' ? SpanKind.LocText : SpanKind.Value));
            }

            return spans;
        }

        spans.Add(new TextSpan(body, start == 0 ? SpanKind.TableName : SpanKind.Value));
        return spans;
    }

    private static void AddHead(List<TextSpan> spans, string body)
    {
        var space = body.IndexOf(' ');
        if (space < 0)
        {
            spans.Add(new TextSpan(body, SpanKind.TableName));
            return;
        }

        var keyStart = space;
        while (keyStart < body.Length && body[keyStart] == ' ')
        {
            keyStart++;
        }

        spans.Add(new TextSpan(body[..space], SpanKind.TableName));
        spans.Add(new TextSpan(body[space..keyStart], SpanKind.Normal));
        if (keyStart < body.Length)
        {
            spans.Add(new TextSpan(body[keyStart..], SpanKind.Key));
        }
    }
}
