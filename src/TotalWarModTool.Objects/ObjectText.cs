namespace TotalWarModTool.Objects;

public enum SpanKind
{
    Normal,
    TableName,
    Key,
    FieldName,
    Value,
    Link,
    LocText,
    Title,
    ProvenanceVanilla,
    ProvenanceMod,
    ProvenanceProject,
    Overridden,
    Warning,
    Collapsed,
}

/// <summary>Target for Link; Section (index into ObjectView.Sections) for Collapsed and Title.</summary>
public sealed record TextSpan(string Text, SpanKind Kind, ObjectId? Target = null, int? Section = null);

public sealed record TextLine(IReadOnlyList<TextSpan> Spans, int Indent);

public sealed record RenderOptions(IReadOnlySet<int> ExpandedSections, int CollapseThreshold = ObjectText.DefaultCollapseThreshold,
    int CollapsedPreview = ObjectText.DefaultCollapsedPreview);

/// <summary>
/// Turns an <see cref="ObjectView"/> into lines of typed spans, a pure and deterministic function. The App colours and links the spans.
/// Rows of a list section show every field except the field that points at the root (the section's FieldName); a category rule cannot hide more yet.
/// </summary>
public static class ObjectText
{
    public const int DefaultCollapseThreshold = 20;
    public const int DefaultCollapsedPreview = 5;

    private const string TablesSuffix = "_tables";
    private const string Gap = "  ";

    public static IReadOnlyList<TextLine> Render(ObjectView view, CategoryDefinition? category, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(options);

        var lines = new List<TextLine>();
        AddHead(lines, view);

        foreach (var warning in view.Warnings)
        {
            var text = warning.Name is null
                ? warning.Kind + ": " + warning.Detail
                : warning.Kind + ": " + warning.Name + ": " + warning.Detail;
            lines.Add(Line(0, new TextSpan(text, SpanKind.Warning)));
        }

        if (view.Root is not null)
        {
            foreach (var field in view.Root.Fields)
            {
                var spans = new List<TextSpan> { new(field.Name, SpanKind.FieldName), new(" = ", SpanKind.Normal) };
                spans.AddRange(ValueSpans(field));
                lines.Add(new TextLine(spans, 1));
            }
        }

        foreach (var entry in view.Texts)
        {
            lines.Add(Line(1,
                new TextSpan(entry.Column, SpanKind.FieldName),
                new TextSpan(Gap, SpanKind.Normal),
                new TextSpan(Quote(entry.Text), SpanKind.LocText),
                new TextSpan(Gap, SpanKind.Normal),
                Provenance(entry.Source)));
        }

        foreach (var (index, rule) in OrderSections(view, category))
        {
            AddSection(lines, view.Sections[index], index, rule, options);
        }

        return lines;
    }

    /// <summary>"building_levels_tables" gives "building_levels".</summary>
    public static string ShortTableName(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Length > TablesSuffix.Length && table.EndsWith(TablesSuffix, StringComparison.OrdinalIgnoreCase)
            ? table[..^TablesSuffix.Length]
            : table;
    }

    internal sealed record PatternPart(string Text, bool IsPlaceholder);

    /// <summary>Splits a pattern into literals and {placeholder} parts. An unclosed or empty placeholder makes the pattern invalid.</summary>
    internal static bool TryParsePattern(string pattern, out IReadOnlyList<PatternPart> parts)
    {
        var result = new List<PatternPart>();
        parts = result;
        var position = 0;
        while (position < pattern.Length)
        {
            var open = pattern.IndexOf('{', position);
            if (open < 0)
            {
                result.Add(new PatternPart(pattern[position..], false));
                break;
            }

            var close = pattern.IndexOf('}', open + 1);
            if (close < 0 || close == open + 1)
            {
                return false;
            }

            if (open > position)
            {
                result.Add(new PatternPart(pattern[position..open], false));
            }

            result.Add(new PatternPart(pattern[(open + 1)..close], true));
            position = close + 1;
        }

        return true;
    }

    private static void AddHead(List<TextLine> lines, ObjectView view)
    {
        var spans = new List<TextSpan>
        {
            new(ShortTableName(view.Id.Table), SpanKind.TableName),
            new(Gap, SpanKind.Normal),
            new(view.Id.JoinedKey, SpanKind.Key),
        };

        if (view.Root is not null)
        {
            spans.Add(new TextSpan(Gap, SpanKind.Normal));
            spans.Add(Provenance(view.Root.Provenance.Source));
            foreach (var overridden in view.Root.Provenance.Overridden)
            {
                spans.Add(new TextSpan(" ", SpanKind.Normal));
                spans.Add(new TextSpan(Bracketed(overridden), SpanKind.Overridden));
            }
        }

        lines.Add(new TextLine(spans, 0));
    }

    private static void AddSection(List<TextLine> lines, ObjectSection section, int index, SectionRule? rule, RenderOptions options)
    {
        lines.Add(new TextLine([], 0));

        var title = rule?.TitleKey is { } titleKey
            ? new TextSpan(titleKey, SpanKind.Title, Section: index)
            : new TextSpan(ShortTableName(section.SourceTable), SpanKind.TableName, Section: index);
        var head = new List<TextSpan>
        {
            title,
            new(Gap, SpanKind.Normal),
            new("(" + section.Rows.Count + ")", SpanKind.Normal),
        };
        foreach (var source in section.Rows.Select(row => row.Provenance.Source).Distinct())
        {
            head.Add(new TextSpan(" ", SpanKind.Normal));
            head.Add(Provenance(source));
        }

        lines.Add(new TextLine(head, 0));

        var collapse = section.Rows.Count > options.CollapseThreshold && !options.ExpandedSections.Contains(index);
        var shown = collapse ? Math.Min(options.CollapsedPreview, section.Rows.Count) : section.Rows.Count;
        for (var i = 0; i < shown; i++)
        {
            lines.Add(new TextLine(RowSpans(section, section.Rows[i], rule), 1));
        }

        if (collapse)
        {
            lines.Add(Line(1, new TextSpan("+" + (section.Rows.Count - shown), SpanKind.Collapsed, Section: index)));
        }
    }

    private static List<TextSpan> RowSpans(ObjectSection section, ObjectRow row, SectionRule? rule)
    {
        var spans = new List<TextSpan>();
        if (rule is { Mode: SectionMode.Sentence, Pattern: { } pattern } && TryParsePattern(pattern, out var parts))
        {
            foreach (var part in parts)
            {
                if (!part.IsPlaceholder)
                {
                    spans.Add(new TextSpan(part.Text, SpanKind.Normal));
                    continue;
                }

                var field = FindField(row, part.Text);
                if (field is null)
                {
                    spans.Add(new TextSpan("{" + part.Text + "}", SpanKind.Warning));
                }
                else
                {
                    spans.AddRange(ValueSpans(field));
                }
            }
        }
        else if (section.IsJunction && row.OtherEnd is not null)
        {
            spans.AddRange(ValueSpans(row.OtherEnd));
        }
        else
        {
            foreach (var field in row.Fields)
            {
                if (string.Equals(field.Name, section.FieldName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (spans.Count > 0)
                {
                    spans.Add(new TextSpan(Gap, SpanKind.Normal));
                }

                spans.Add(new TextSpan(field.Name, SpanKind.FieldName));
                spans.Add(new TextSpan("=", SpanKind.Normal));
                spans.AddRange(ValueSpans(field));
            }
        }

        if (spans.Count > 0)
        {
            spans.Add(new TextSpan(Gap, SpanKind.Normal));
        }

        spans.Add(Provenance(row.Provenance.Source));
        foreach (var overridden in row.Provenance.Overridden)
        {
            spans.Add(new TextSpan(" ", SpanKind.Normal));
            spans.Add(new TextSpan(Bracketed(overridden), SpanKind.Overridden));
        }

        if (row.Provenance.Duplicate)
        {
            spans.Add(new TextSpan(" ", SpanKind.Normal));
            spans.Add(new TextSpan("duplicate", SpanKind.Warning));
        }

        if (row.Provenance.OlderLayout)
        {
            spans.Add(new TextSpan(" ", SpanKind.Normal));
            spans.Add(new TextSpan("older layout", SpanKind.Warning));
        }

        return spans;
    }

    private static FieldValue? FindField(ObjectRow row, string name)
    {
        // The other end of a junction row wins: it is the resolved field and carries the link and loc text.
        if (row.OtherEnd is { } other && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return other;
        }

        foreach (var field in row.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    private static List<TextSpan> ValueSpans(FieldValue field)
    {
        var spans = new List<TextSpan>();
        switch (field.Resolution)
        {
            case ReferenceResolution.Found or ReferenceResolution.Unchecked when field.Target is not null:
                spans.Add(new TextSpan(field.Text, SpanKind.Link, field.Target));
                break;
            case ReferenceResolution.NotFound:
                spans.Add(new TextSpan(field.Text, SpanKind.Warning));
                return spans;
            default:
                spans.Add(new TextSpan(field.Text, SpanKind.Value));
                break;
        }

        if (field.LocText is not null && field.Resolution is ReferenceResolution.Found or ReferenceResolution.LocOnly)
        {
            spans.Add(new TextSpan(Gap, SpanKind.Normal));
            spans.Add(new TextSpan(Quote(field.LocText), SpanKind.LocText));
        }

        return spans;
    }

    /// <summary>Sections in rule order (a rule without field matches every section of its table), then the rest alphabetically. Hidden sections are dropped.</summary>
    private static List<(int Index, SectionRule? Rule)> OrderSections(ObjectView view, CategoryDefinition? category)
    {
        var placed = new HashSet<int>();
        var result = new List<(int, SectionRule?)>();

        if (category is not null)
        {
            foreach (var rule in category.Sections)
            {
                for (var i = 0; i < view.Sections.Count; i++)
                {
                    if (placed.Contains(i) || !Matches(rule, view.Sections[i]))
                    {
                        continue;
                    }

                    placed.Add(i);
                    if (!rule.Hidden)
                    {
                        result.Add((i, rule));
                    }
                }
            }
        }

        var rest = Enumerable.Range(0, view.Sections.Count).Where(i => !placed.Contains(i)).ToList();
        rest.Sort((a, b) =>
        {
            var byTable = string.CompareOrdinal(view.Sections[a].SourceTable, view.Sections[b].SourceTable);
            if (byTable != 0)
            {
                return byTable;
            }

            var byField = string.CompareOrdinal(view.Sections[a].FieldName, view.Sections[b].FieldName);
            return byField != 0 ? byField : a.CompareTo(b);
        });
        result.AddRange(rest.Select(i => (i, (SectionRule?)null)));
        return result;
    }

    private static bool Matches(SectionRule rule, ObjectSection section) =>
        string.Equals(ShortTableName(rule.Table), ShortTableName(section.SourceTable), StringComparison.OrdinalIgnoreCase)
        && (rule.FieldName is null || string.Equals(rule.FieldName, section.FieldName, StringComparison.OrdinalIgnoreCase));

    private static TextSpan Provenance(TableSource source) => new(Bracketed(source), source.Kind switch
    {
        SourceKind.Vanilla => SpanKind.ProvenanceVanilla,
        SourceKind.Mod => SpanKind.ProvenanceMod,
        _ => SpanKind.ProvenanceProject,
    });

    private static string Bracketed(TableSource source) => "[" + source.Name + "]";

    private static string Quote(string text) => "\"" + text + "\"";

    private static TextLine Line(int indent, params TextSpan[] spans) => new(spans, indent);
}
