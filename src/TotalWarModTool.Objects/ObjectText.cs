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
    Duplicate,
    OlderLayout,
}

/// <summary>
/// Target for Link; Section (index into ObjectView.Sections) for Collapsed and Title; Count (the number of hidden rows) for Collapsed.
/// The texts of Collapsed ("+N"), Duplicate ("duplicate") and OlderLayout ("older layout") are technical fallbacks: the App renders its
/// own text from Kind and Count.
/// </summary>
public sealed record TextSpan(string Text, SpanKind Kind, ObjectId? Target = null, int? Section = null, int? Count = null);

public sealed record TextLine(IReadOnlyList<TextSpan> Spans, int Indent);

public sealed record RenderOptions(IReadOnlySet<int> ExpandedSections, int CollapseThreshold = ObjectText.DefaultCollapseThreshold,
    int CollapsedPreview = ObjectText.DefaultCollapsedPreview);

/// <summary>
/// Turns an <see cref="ObjectView"/> into lines of typed spans, a pure and deterministic function. The App colours and links the spans.
/// A row of a list section is a block: a head line with its key fields (without a key: its first non-empty field) and the provenance, then
/// one line per other non-empty field, an empty line between two blocks. The field that points at the root (the section's FieldName) is
/// never shown; a category rule cannot hide more yet. Sentence rows and junction rows stay one line.
/// </summary>
public static partial class ObjectText
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
                lines.Add(new TextLine(FieldSpans(field), 1));
            }
        }

        foreach (var entry in view.Texts)
        {
            // "table.column", the text, a link to the object the text belongs to unless it is the root, the source.
            var spans = new List<TextSpan>
            {
                new(entry.Table + "." + entry.Column, SpanKind.FieldName),
                new(Gap, SpanKind.Normal),
                new(Quote(entry.Text), SpanKind.LocText),
            };
            if (!entry.Owner.Equals(view.Id))
            {
                spans.Add(new TextSpan(Gap, SpanKind.Normal));
                spans.Add(new TextSpan(entry.Owner.DisplayKey, SpanKind.Link, entry.Owner));
            }

            spans.Add(new TextSpan(Gap, SpanKind.Normal));
            spans.Add(Provenance(entry.Source));
            lines.Add(new TextLine(spans, 1));
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
            new(view.Id.DisplayKey, SpanKind.Key),
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

        var threshold = Math.Max(0, options.CollapseThreshold);
        var preview = Math.Clamp(options.CollapsedPreview, 0, threshold);
        var collapse = section.Rows.Count > threshold && !options.ExpandedSections.Contains(index);
        var shown = collapse ? preview : section.Rows.Count;
        IReadOnlyList<PatternPart>? pattern = rule is { Mode: SectionMode.Sentence, Pattern: { } text } && TryParsePattern(text, out var parts)
            ? parts
            : null;
        var previousWasBlock = false;
        for (var i = 0; i < shown; i++)
        {
            var row = section.Rows[i];
            if (pattern is null && !(section.IsJunction && row.OtherEnd is not null))
            {
                if (previousWasBlock)
                {
                    lines.Add(new TextLine([], 1));
                }

                AddBlock(lines, section, row);
                previousWasBlock = true;
                continue;
            }

            var spans = pattern is not null ? SentenceSpans(row, pattern) : ValueSpans(row.OtherEnd!);
            AddRowProvenance(spans, row);
            lines.Add(new TextLine(spans, 1));
            previousWasBlock = false;
        }

        var hidden = section.Rows.Count - shown;
        if (collapse && hidden > 0)
        {
            lines.Add(Line(1, new TextSpan("+" + hidden, SpanKind.Collapsed, Section: index, Count: hidden)));
        }
    }

    /// <summary>
    /// A list row as a block: the head line holds the key fields (without one besides the field that points at the root: the first non-empty
    /// field) and the provenance, then one line per other non-empty field.
    /// </summary>
    private static void AddBlock(List<TextLine> lines, ObjectSection section, ObjectRow row)
    {
        var fields = row.Fields.Where(field => !string.Equals(field.Name, section.FieldName, StringComparison.OrdinalIgnoreCase)).ToList();
        var hasKey = fields.Any(field => field.IsKey);
        var fallback = hasKey ? null : fields.FirstOrDefault(field => field.Text.Length > 0);

        var head = new List<TextSpan>();
        foreach (var field in fields)
        {
            if (!InHead(field))
            {
                continue;
            }

            if (head.Count > 0)
            {
                head.Add(new TextSpan(Gap, SpanKind.Normal));
            }

            head.AddRange(FieldSpans(field));
        }

        AddRowProvenance(head, row);
        lines.Add(new TextLine(head, 1));

        foreach (var field in fields)
        {
            if (!InHead(field) && field.Text.Length > 0)
            {
                lines.Add(new TextLine(FieldSpans(field), 2));
            }
        }

        bool InHead(FieldValue field) => hasKey ? field.IsKey : ReferenceEquals(field, fallback);
    }

    private static List<TextSpan> SentenceSpans(ObjectRow row, IReadOnlyList<PatternPart> parts)
    {
        var spans = new List<TextSpan>();
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

        return spans;
    }

    /// <summary>"name = value" with the value spans.</summary>
    private static List<TextSpan> FieldSpans(FieldValue field)
    {
        var spans = new List<TextSpan> { new(field.Name, SpanKind.FieldName), new(" = ", SpanKind.Normal) };
        spans.AddRange(ValueSpans(field));
        return spans;
    }

    /// <summary>The source of the row, the sources it overrides and its duplicate and older-layout marks.</summary>
    private static void AddRowProvenance(List<TextSpan> spans, ObjectRow row)
    {
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
            spans.Add(new TextSpan("duplicate", SpanKind.Duplicate));
        }

        if (row.Provenance.OlderLayout)
        {
            spans.Add(new TextSpan(" ", SpanKind.Normal));
            spans.Add(new TextSpan("older layout", SpanKind.OlderLayout));
        }
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
        var spans = new List<TextSpan> { ValueSpan(field) };
        if (LocSpan(field) is { } loc)
        {
            spans.Add(new TextSpan(Gap, SpanKind.Normal));
            spans.Add(loc);
        }

        return spans;
    }

    /// <summary>The value of a field as one span: a link, a warning for a reference that was not found, otherwise a plain value.</summary>
    private static TextSpan ValueSpan(FieldValue field) => field.Resolution switch
    {
        ReferenceResolution.Found or ReferenceResolution.Unchecked when field.Target is not null =>
            new TextSpan(field.Text, SpanKind.Link, field.Target),
        ReferenceResolution.NotFound => new TextSpan(field.Text, SpanKind.Warning),
        _ => new TextSpan(field.Text, SpanKind.Value),
    };

    /// <summary>The quoted loc text behind a reference value, null when the field has none to show.</summary>
    private static TextSpan? LocSpan(FieldValue field) =>
        field.LocText is not null && field.Resolution is ReferenceResolution.Found or ReferenceResolution.LocOnly
            ? new TextSpan(Quote(field.LocText), SpanKind.LocText)
            : null;

    /// <summary>Sections in rule order (a rule without field matches every section of its table), then the rest alphabetically. Hidden sections are dropped.</summary>
    private static List<(int Index, SectionRule? Rule)> OrderSections(ObjectView view, CategoryDefinition? category)
    {
        // Empty sections are omitted (spec 5.6 rule 5); marking them placed keeps them out of the alphabetical rest too.
        var placed = Enumerable.Range(0, view.Sections.Count).Where(i => view.Sections[i].Rows.Count == 0).ToHashSet();
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

    private static bool Matches(SectionRule rule, ObjectSection section) => Matches(rule, section.SourceTable, section.FieldName);

    private static bool Matches(SectionRule rule, string table, string fieldName) =>
        string.Equals(ShortTableName(rule.Table), ShortTableName(table), StringComparison.OrdinalIgnoreCase)
        && (rule.FieldName is null || string.Equals(rule.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The first rule of the category that matches the section (table and field), null without one: the rule that decides for it.</summary>
    internal static SectionRule? RuleFor(CategoryDefinition? category, string table, string fieldName) =>
        category?.Sections.FirstOrDefault(rule => Matches(rule, table, fieldName));

    private static TextSpan Provenance(TableSource source) => new(Bracketed(source), source.Kind switch
    {
        SourceKind.Vanilla => SpanKind.ProvenanceVanilla,
        SourceKind.Mod => SpanKind.ProvenanceMod,
        _ => SpanKind.ProvenanceProject,
    });

    private static string Bracketed(TableSource source) => "[" + source.Name + "]";

    /// <summary>A line break inside a loc text becomes one space, so a text never breaks the line it stands in.</summary>
    private static string Quote(string text) => "\"" + text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ') + "\"";

    private static TextLine Line(int indent, params TextSpan[] spans) => new(spans, indent);
}
