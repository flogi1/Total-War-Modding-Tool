namespace TotalWarModTool.Objects;

/// <summary>
/// The block form of an object: the editable text of every line apart from its annotations (provenance, game texts behind references, counts,
/// titles, warnings), which the editor draws behind the line. See spec 5.2.
/// </summary>
public static partial class ObjectText
{
    private const int RowIndent = 1;
    private const int RowFieldIndent = 2;

    /// <summary>Loc text in the block: a quote becomes \", a backslash \\, any line break \n; wrapped in quotes.</summary>
    public static string QuoteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escaped = text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    public static BlockText RenderBlock(ObjectView view, CategoryDefinition? category)
    {
        ArgumentNullException.ThrowIfNull(view);

        var lines = new List<BlockLine>();
        var ranges = new List<BlockSectionRange>();
        AddBlockHead(lines, view);

        foreach (var warning in view.Warnings)
        {
            var text = warning.Name is null
                ? warning.Kind + ": " + warning.Detail
                : warning.Kind + ": " + warning.Name + ": " + warning.Detail;
            lines.Add(new BlockLine(BlockLineKind.Annotation, [], [new TextSpan(text, SpanKind.Warning)], 0));
        }

        if (view.Root is null)
        {
            return new BlockText(lines, ranges);
        }

        foreach (var field in view.Root.Fields)
        {
            lines.Add(new BlockLine(BlockLineKind.RootField, AssignmentSpans(field), LocTrailing(field), 1));
        }

        foreach (var entry in view.Texts)
        {
            if (entry.Owner.Equals(view.Id))
            {
                lines.Add(new BlockLine(BlockLineKind.RootText, TextEntrySpans(entry), [Provenance(entry.Source)], 1));
            }
        }

        foreach (var (index, rule) in OrderSections(view, category))
        {
            ranges.Add(AddBlockSection(lines, view, index, rule));
        }

        return new BlockText(lines, ranges);
    }

    private static void AddBlockHead(List<BlockLine> lines, ObjectView view)
    {
        var spans = new List<TextSpan>
        {
            new(ShortTableName(view.Id.Table), SpanKind.TableName),
            new(" ", SpanKind.Normal),
            new(view.Id.DisplayKey, SpanKind.Key),
        };

        var trailing = new List<TextSpan>();
        if (view.Root is not null)
        {
            trailing.Add(Provenance(view.Root.Provenance.Source));
            foreach (var overridden in view.Root.Provenance.Overridden)
            {
                trailing.Add(new TextSpan(" ", SpanKind.Normal));
                trailing.Add(new TextSpan(Bracketed(overridden), SpanKind.Overridden));
            }
        }

        lines.Add(new BlockLine(BlockLineKind.Head, spans, trailing, 0));
    }

    private static BlockSectionRange AddBlockSection(List<BlockLine> lines, ObjectView view, int index, SectionRule? rule)
    {
        var section = view.Sections[index];
        lines.Add(new BlockLine(BlockLineKind.Blank, [], [], 0));

        var headLine = lines.Count;
        var shortName = ShortTableName(section.SourceTable);
        var repeated = view.Sections.Count(other => string.Equals(ShortTableName(other.SourceTable), shortName, StringComparison.OrdinalIgnoreCase)) > 1;
        var name = repeated ? shortName + "." + section.FieldName : shortName;

        var trailing = new List<TextSpan>();
        if (rule?.TitleKey is { } titleKey)
        {
            trailing.Add(new TextSpan(titleKey, SpanKind.Title, Section: index));
            trailing.Add(new TextSpan(Gap, SpanKind.Normal));
        }

        trailing.Add(new TextSpan("(" + section.Rows.Count + ")", SpanKind.Normal));
        foreach (var source in section.Rows.Select(row => row.Provenance.Source).Distinct())
        {
            trailing.Add(new TextSpan(" ", SpanKind.Normal));
            trailing.Add(Provenance(source));
        }

        lines.Add(new BlockLine(BlockLineKind.SectionHead, [new TextSpan(name, SpanKind.TableName)], trailing, 0, index));

        IReadOnlyList<PatternPart>? pattern = rule is { Mode: SectionMode.Sentence, Pattern: { } text } && TryParsePattern(text, out var parts)
            ? parts
            : null;
        var rows = new List<BlockRowRange>(section.Rows.Count);
        var previousWasBlock = false;
        for (var i = 0; i < section.Rows.Count; i++)
        {
            var (rowLines, isList) = BlockRow(view, section, index, i, pattern);
            var isBlock = isList || rowLines.Count > 1;
            if (previousWasBlock && isBlock)
            {
                lines.Add(new BlockLine(BlockLineKind.Blank, [], [], RowIndent));
            }

            rows.Add(new BlockRowRange(index, i, lines.Count, lines.Count + rowLines.Count - 1));
            lines.AddRange(rowLines);
            previousWasBlock = isBlock;
        }

        return new BlockSectionRange(index, headLine, headLine + 1, lines.Count - 1, rows);
    }

    /// <summary>The lines of one row. IsList is true for a list row, which stands as a block even when it has no field lines.</summary>
    private static (List<BlockLine> Lines, bool IsList) BlockRow(ObjectView view, ObjectSection section, int sectionIndex, int rowIndex,
        IReadOnlyList<PatternPart>? pattern)
    {
        var row = section.Rows[rowIndex];
        var lines = new List<BlockLine>();
        var isJunction = pattern is null && section.IsJunction && row.OtherEnd is not null;
        var isList = pattern is null && !isJunction;

        var excluded = new List<string> { section.FieldName };
        List<TextSpan> head;
        List<TextSpan> trailing;
        if (isList)
        {
            var fields = row.Fields.Where(field => !string.Equals(field.Name, section.FieldName, StringComparison.OrdinalIgnoreCase)).ToList();
            var headFields = fields.Any(field => field.IsKey) ? fields.Where(field => field.IsKey).ToList() : fields.Take(1).ToList();
            head = [];
            var headLocs = new List<TextSpan>();
            foreach (var field in headFields)
            {
                if (head.Count > 0)
                {
                    head.Add(new TextSpan(Gap, SpanKind.Normal));
                }

                head.AddRange(AssignmentSpans(field));
                headLocs.AddRange(LocTrailing(field));
                excluded.Add(field.Name);
            }

            trailing = headLocs;
        }
        else if (pattern is not null)
        {
            (head, trailing) = SentenceBlockSpans(row, pattern, excluded);
        }
        else
        {
            var other = row.OtherEnd!;
            head = [ValueSpan(other)];
            trailing = LocTrailing(other);
            excluded.Add(other.Name);
        }

        AddRowProvenance(trailing, row);
        lines.Add(new BlockLine(BlockLineKind.RowHead, head, trailing, RowIndent, sectionIndex, rowIndex));

        var rowTexts = row.RowId is { } rowId ? view.Texts.Where(entry => entry.Owner.Equals(rowId)).ToList() : [];
        foreach (var field in row.Fields)
        {
            // An empty field stays out unless a text of the row has its name (the field line tells the parser that the quoted line below it
            // is the game text, not the value) or it is a key field (the row is identified by it, so the parser needs it even when empty).
            var shared = rowTexts.Any(entry => string.Equals(entry.Column, field.Name, StringComparison.OrdinalIgnoreCase));
            if ((field.Text.Length > 0 || shared || field.IsKey) &&!excluded.Any(name => string.Equals(name, field.Name, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add(new BlockLine(BlockLineKind.RowField, AssignmentSpans(field), LocTrailing(field), RowFieldIndent, sectionIndex, rowIndex));
            }
        }

        foreach (var entry in rowTexts)
        {
            lines.Add(new BlockLine(BlockLineKind.RowText, TextEntrySpans(entry), [Provenance(entry.Source)], RowFieldIndent, sectionIndex, rowIndex));
        }

        return (lines, isList);
    }

    /// <summary>The pattern with its values filled in, the loc texts behind the values in pattern order, and the field names the pattern names.</summary>
    private static (List<TextSpan> Spans, List<TextSpan> Trailing) SentenceBlockSpans(ObjectRow row, IReadOnlyList<PatternPart> parts, List<string> named)
    {
        var spans = new List<TextSpan>();
        var trailing = new List<TextSpan>();
        foreach (var part in parts)
        {
            if (!part.IsPlaceholder)
            {
                spans.Add(new TextSpan(part.Text, SpanKind.Normal));
                continue;
            }

            named.Add(part.Text);
            var field = FindField(row, part.Text);
            if (field is null)
            {
                spans.Add(new TextSpan("{" + part.Text + "}", SpanKind.Warning));
                continue;
            }

            if (field.Text.Length > 0)
            {
                spans.Add(ValueSpan(field));
            }

            trailing.AddRange(LocTrailing(field, trailing.Count > 0));
        }

        // Only the fields the pattern names stand in the sentence; the other end of a junction that it does not name is a field line.
        return (spans, trailing);
    }

    /// <summary>"name = value"; an empty value leaves out its span.</summary>
    private static List<TextSpan> AssignmentSpans(FieldValue field)
    {
        var spans = new List<TextSpan> { new(field.Name, SpanKind.FieldName), new(" = ", SpanKind.Normal) };
        if (field.Text.Length > 0)
        {
            spans.Add(ValueSpan(field));
        }

        return spans;
    }

    /// <summary>"column = "text"" of a game text.</summary>
    private static List<TextSpan> TextEntrySpans(LocEntry entry) =>
        [new(entry.Column, SpanKind.FieldName), new(" = ", SpanKind.Normal), new(QuoteText(entry.Text), SpanKind.LocText)];

    private static List<TextSpan> LocTrailing(FieldValue field, bool separate = false)
    {
        var spans = new List<TextSpan>();
        if (LocSpan(field) is { } loc)
        {
            if (separate)
            {
                spans.Add(new TextSpan(" ", SpanKind.Normal));
            }

            spans.Add(loc);
        }

        return spans;
    }
}
