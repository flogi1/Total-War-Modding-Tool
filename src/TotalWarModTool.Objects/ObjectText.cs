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
    Duplicate,
    OlderLayout,
}

/// <summary>
/// Target for Link; Section (index into ObjectView.Sections) for Title.
/// The texts of Duplicate ("duplicate") and OlderLayout ("older layout") are technical fallbacks: the App renders its own text from Kind.
/// </summary>
public sealed record TextSpan(string Text, SpanKind Kind, ObjectId? Target = null, int? Section = null, int? Count = null);

/// <summary>
/// Shared rules of the block text: section order, patterns and spans. See RenderBlock.
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

}
