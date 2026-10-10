using System.Text;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects;

/// <summary>
/// Reads the block text back into a <see cref="ParsedObject"/> (spec 5.2 and 5.3). A pure function: it never throws on text and collects
/// every problem as a diagnostic instead of stopping at the first.
/// </summary>
public static class ObjectTextParser
{
    private const string TablesSuffix = "_tables";
    private const string TabEscape = "\\t";
    private const string LineFeedEscape = "\\n";

    /// <summary>Never throws on text; collects every problem. baseline.Id names the root the head must match. Lines are split at \n (a \r before it is dropped).</summary>
    public static ParsedObject Parse(string text, ObjectView baseline, CategoryDefinition? category, ReferenceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(graph);
        return new Run(baseline, category, graph).Execute(text);
    }

    private sealed class Block
    {
        public List<ParsedField> Fields { get; } = [];

        public List<ParsedText> Texts { get; } = [];

        public HashSet<string> FieldNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> TextNames { get; } = new(StringComparer.Ordinal);

        public int Line { get; init; }
    }

    private sealed class SectionState
    {
        public SectionState(string sourceTable, string fieldName, TableDefinition definition, int line)
        {
            SourceTable = sourceTable;
            FieldName = fieldName;
            Definition = definition;
            Line = line;
        }

        public string SourceTable { get; }

        public string FieldName { get; }

        public TableDefinition Definition { get; }

        public int Line { get; }

        public IReadOnlyList<ObjectText.PatternPart>? Pattern { get; init; }

        /// <summary>The reference field that is not FieldName, for a junction section without a sentence rule.</summary>
        public string? OtherEnd { get; init; }

        public bool IsJunction { get; init; }

        public List<Block> Rows { get; } = [];
    }

    private sealed class Run
    {
        private readonly ObjectView _baseline;
        private readonly CategoryDefinition? _category;
        private readonly ReferenceGraph _graph;
        private readonly TableDefinition? _rootDefinition;
        private readonly List<BlockDiagnostic> _diagnostics = [];
        private readonly Block _root = new();
        private readonly List<SectionState> _sections = [];
        private readonly HashSet<string> _seenSections = new(StringComparer.OrdinalIgnoreCase);

        public Run(ObjectView baseline, CategoryDefinition? category, ReferenceGraph graph)
        {
            _baseline = baseline;
            _category = category;
            _graph = graph;
            _rootDefinition = graph.Latest(baseline.Id.Table);
        }

        public ParsedObject Execute(string text)
        {
            var headLine = 0;
            var inSections = false;
            var skipping = false;
            SectionState? section = null;
            Block? row = null;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i].TrimEnd('\r');
                var lineNumber = i + 1;
                var width = 0;
                var position = 0;
                while (position < raw.Length && raw[position] is ' ' or '\t')
                {
                    width += raw[position] == ' ' ? 1 : 2;
                    position++;
                }

                var content = raw[position..];
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (headLine == 0)
                {
                    headLine = lineNumber;
                    CheckHead(content, lineNumber);
                    continue;
                }

                var level = width / 2;
                if (level == 0)
                {
                    inSections = true;
                    row = null;
                    (section, skipping) = StartSection(content.Trim(), lineNumber);
                    continue;
                }

                if (!inSections)
                {
                    if (level == 1)
                    {
                        ParseAssignmentLine(content.TrimEnd(), lineNumber, _rootDefinition, _root, null);
                    }
                    else if (level == 2)
                    {
                        Add(lineNumber, DiagnosticKind.FieldWithoutRow, "a field line needs a row above it");
                    }
                    else
                    {
                        Add(lineNumber, DiagnosticKind.UnknownLineForm, "nested deeper than two levels");
                    }

                    continue;
                }

                if (skipping)
                {
                    continue;
                }

                if (level == 1)
                {
                    row = new Block { Line = lineNumber };
                    section!.Rows.Add(row);
                    ParseRowHead(section, row, content, lineNumber);
                }
                else if (level == 2)
                {
                    if (row is null)
                    {
                        Add(lineNumber, DiagnosticKind.FieldWithoutRow, "a field line needs a row above it");
                    }
                    else
                    {
                        ParseAssignmentLine(content.TrimEnd(), lineNumber, section!.Definition, row, section.FieldName);
                    }
                }
                else
                {
                    Add(lineNumber, DiagnosticKind.UnknownLineForm, "nested deeper than two levels");
                }
            }

            if (headLine == 0)
            {
                headLine = 1;
                Add(1, DiagnosticKind.HeadChanged, "the head line is missing");
            }

            ReportMissingRootFields(headLine);
            var sections = _sections
                .Select(s => new ParsedSection(s.SourceTable, s.FieldName, s.Rows.Select(ToRow).ToList(), s.Line))
                .ToList();
            var ordered = _diagnostics.OrderBy(d => d.Line).ToList();
            return new ParsedObject(_baseline.Id, _root.Fields, _root.Texts, sections, ordered);
        }

        private static ParsedRow ToRow(Block block) => new(block.Fields, block.Texts, block.Line);

        private void Add(int line, DiagnosticKind kind, string detail, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
            _diagnostics.Add(new BlockDiagnostic(line, severity, kind, detail));

        private void CheckHead(string content, int line)
        {
            var shortName = ObjectText.ShortTableName(_baseline.Id.Table);
            var trimmed = content.TrimEnd();
            var matches = trimmed.Length > shortName.Length
                && trimmed.StartsWith(shortName, StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(trimmed[shortName.Length])
                && string.Equals(trimmed[shortName.Length..].Trim(), _baseline.Id.DisplayKey, StringComparison.Ordinal);
            if (!matches)
            {
                Add(line, DiagnosticKind.HeadChanged, "the head line must read '" + shortName + " " + _baseline.Id.DisplayKey + "'");
            }
        }

        private void ReportMissingRootFields(int headLine)
        {
            if (_rootDefinition is null)
            {
                return;
            }

            foreach (var field in _rootDefinition.Fields)
            {
                if (!_root.FieldNames.Contains(field.Name))
                {
                    Add(headLine, DiagnosticKind.RootFieldMissing, "field missing, its value stays: " + field.Name, DiagnosticSeverity.Warning);
                }
            }
        }

        // ---- sections ----

        private (SectionState? Section, bool Skipping) StartSection(string token, int line)
        {
            if (token.Any(char.IsWhiteSpace))
            {
                Add(line, DiagnosticKind.UnknownLineForm, "not a section head: expected 'table' or 'table.field'");
                return (null, true);
            }

            var dot = token.IndexOf('.', StringComparison.Ordinal);
            var name = dot < 0 ? token : token[..dot];
            var fieldName = dot < 0 ? null : token[(dot + 1)..];
            var table = ResolveTable(name);

            var rootKeys = KeyNames();
            var references = _graph.Incoming(_baseline.Id.Table)
                .Where(r => string.Equals(r.SourceTable, table, StringComparison.OrdinalIgnoreCase)
                    && rootKeys.Contains(r.TargetColumn, StringComparer.OrdinalIgnoreCase)
                    && (fieldName is null || string.Equals(r.FieldName, fieldName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (references.Count == 0)
            {
                Add(line, DiagnosticKind.UnknownSection, "no table '" + token + "' points at this object");
                return (null, true);
            }

            if (references.Count > 1)
            {
                Add(line, DiagnosticKind.UnknownSection, "field required: the table points at this object with more than one field");
                return (null, true);
            }

            var reference = references[0];
            var rule = _category?.Sections.FirstOrDefault(r =>
                string.Equals(ObjectText.ShortTableName(r.Table), ObjectText.ShortTableName(reference.SourceTable), StringComparison.OrdinalIgnoreCase)
                && (r.FieldName is null || string.Equals(r.FieldName, reference.FieldName, StringComparison.OrdinalIgnoreCase)));
            if (rule is { Hidden: true })
            {
                Add(line, DiagnosticKind.HiddenSection, "the section is hidden by the category");
                return (null, true);
            }

            if (!_seenSections.Add(reference.SourceTable + "." + reference.FieldName))
            {
                Add(line, DiagnosticKind.UnknownSection, "section repeated");
            }

            var definition = _graph.Latest(reference.SourceTable)!;
            IReadOnlyList<ObjectText.PatternPart>? pattern = rule is { Mode: SectionMode.Sentence, Pattern: { } text } && ObjectText.TryParsePattern(text, out var parts)
                ? parts
                : null;
            var isJunction = pattern is null && _graph.IsJunction(reference.SourceTable);
            var other = isJunction
                ? definition.Fields.FirstOrDefault(f => f.Reference is not null && !string.Equals(f.Name, reference.FieldName, StringComparison.OrdinalIgnoreCase))?.Name
                : null;
            var section = new SectionState(reference.SourceTable, reference.FieldName, definition, line) { Pattern = pattern, IsJunction = isJunction, OtherEnd = other };
            _sections.Add(section);
            return (section, false);
        }

        /// <summary>"building_effects_junction" stays; "technologies" becomes "technologies_tables" when the schema knows that.</summary>
        private string ResolveTable(string name)
        {
            if (name.EndsWith(TablesSuffix, StringComparison.OrdinalIgnoreCase) || _graph.Latest(name + TablesSuffix) is null)
            {
                return name;
            }

            return name + TablesSuffix;
        }

        private List<string> KeyNames()
        {
            var names = new List<string>();
            if (_rootDefinition is null)
            {
                return names;
            }

            foreach (var index in _graph.KeyFields(_baseline.Id.Table))
            {
                names.Add(_rootDefinition.Fields[index].Name);
            }

            return names;
        }

        // ---- rows ----

        private void ParseRowHead(SectionState section, Block row, string content, int line)
        {
            if (section.Pattern is not null)
            {
                ParseSentence(section, row, content, line);
            }
            else if (section.IsJunction)
            {
                if (section.OtherEnd is null)
                {
                    Add(line, DiagnosticKind.UnknownLineForm, "the junction table has no second reference field");
                    return;
                }

                AddField(section.Definition, row, section.OtherEnd, content.Trim(), line, section.FieldName);
            }
            else
            {
                ParseListHead(section, row, content.TrimEnd(), line);
            }
        }

        private void ParseSentence(SectionState section, Block row, string content, int line)
        {
            if (!TryMatchPattern(section.Pattern!, content, out var values))
            {
                Add(line, DiagnosticKind.SentenceMismatch, "the line does not match the pattern of the section");
                return;
            }

            foreach (var (name, value) in values)
            {
                AddField(section.Definition, row, name, value, line, section.FieldName);
            }
        }

        /// <summary>
        /// The pattern read backwards: the first literal must start the text, the last one end it, the others are searched in order, and a
        /// placeholder takes the (trimmed) text up to the next literal. A text that ends where the pattern's last literal ends is complete.
        /// </summary>
        private static bool TryMatchPattern(IReadOnlyList<ObjectText.PatternPart> parts, string text, out List<(string Name, string Value)> values)
        {
            values = [];
            var position = 0;
            string? pending = null;
            var pendingStart = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.IsPlaceholder)
                {
                    if (pending is not null)
                    {
                        return false;
                    }

                    pending = part.Text;
                    pendingStart = position;
                    continue;
                }

                int index;
                if (i == 0)
                {
                    if (!text.StartsWith(part.Text, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    index = 0;
                }
                else if (i == parts.Count - 1)
                {
                    index = text.EndsWith(part.Text, StringComparison.Ordinal) ? text.Length - part.Text.Length : -1;
                }
                else
                {
                    index = text.IndexOf(part.Text, position, StringComparison.Ordinal);
                }

                if (index < position)
                {
                    return false;
                }

                if (pending is not null)
                {
                    values.Add((pending, text[pendingStart..index].Trim()));
                    pending = null;
                }

                position = index + part.Text.Length;
            }

            if (pending is not null)
            {
                values.Add((pending, text[pendingStart..].Trim()));
                return true;
            }

            return position == text.Length;
        }

        /// <summary>
        /// "a = 1  b = 2": the text is split at a run of two or more spaces that is followed by the name of a field of the table and an equals
        /// sign; everything else stays in the value.
        /// </summary>
        private void ParseListHead(SectionState section, Block row, string content, int line)
        {
            var rest = content;
            while (true)
            {
                if (!TryAssignment(rest, out var name, out var value))
                {
                    Add(line, DiagnosticKind.UnknownLineForm, "expected 'field = value'");
                    return;
                }

                var split = FindNextField(value, section.Definition);
                AddFieldOrReport(section.Definition, row, name, split is null ? value : value[..split.Value.End], line, section.FieldName);
                if (split is null)
                {
                    return;
                }

                rest = value[split.Value.Next..];
            }
        }

        private static (int End, int Next)? FindNextField(string value, TableDefinition definition)
        {
            for (var i = 0; i + 1 < value.Length; i++)
            {
                if (value[i] != ' ' || value[i + 1] != ' ')
                {
                    continue;
                }

                var next = i;
                while (next < value.Length && value[next] == ' ')
                {
                    next++;
                }

                if (next < value.Length && StartsWithAssignment(value, next, definition))
                {
                    return (i, next);
                }

                i = next - 1;
            }

            return null;
        }

        private static bool StartsWithAssignment(string text, int start, TableDefinition definition)
        {
            foreach (var field in definition.Fields)
            {
                if (text.Length - start < field.Name.Length || string.Compare(text, start, field.Name, 0, field.Name.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    continue;
                }

                var position = start + field.Name.Length;
                while (position < text.Length && text[position] == ' ')
                {
                    position++;
                }

                if (position < text.Length && text[position] == '=')
                {
                    return true;
                }
            }

            return false;
        }

        // ---- fields and texts ----

        /// <summary>A field line or loc line: "name = value".</summary>
        private void ParseAssignmentLine(string content, int line, TableDefinition? definition, Block block, string? pointingField)
        {
            if (!TryAssignment(content, out var name, out var value))
            {
                Add(line, DiagnosticKind.UnknownLineForm, "expected 'field = value' or 'column = \"text\"'");
                return;
            }

            if (definition is not null && FindField(definition, name) is { } field)
            {
                AddField(definition, block, field.Name, value, line, pointingField);
                return;
            }

            var quoted = value.TrimStart();
            if (!quoted.StartsWith('"'))
            {
                Add(line, DiagnosticKind.UnknownField, "unknown field '" + name + "'");
                return;
            }

            if (!TryUnquote(quoted, out var text, out var problem))
            {
                Add(line, DiagnosticKind.UnterminatedText, problem);
                return;
            }

            if (!block.TextNames.Add(name))
            {
                Add(line, DiagnosticKind.FieldRepeated, "text '" + name + "' appears twice in the block");
                return;
            }

            if (text.Length == 0)
            {
                Add(line, DiagnosticKind.EmptyText, "the text of '" + name + "' is empty", DiagnosticSeverity.Warning);
            }

            block.Texts.Add(new ParsedText(name, text, line));
        }

        private void AddFieldOrReport(TableDefinition definition, Block block, string name, string value, int line, string? pointingField)
        {
            if (FindField(definition, name) is { } field)
            {
                AddField(definition, block, field.Name, value, line, pointingField);
            }
            else
            {
                Add(line, DiagnosticKind.UnknownField, "unknown field '" + name + "'");
            }
        }

        private void AddField(TableDefinition definition, Block block, string name, string value, int line, string? pointingField)
        {
            var field = FindField(definition, name);
            if (field is null)
            {
                Add(line, DiagnosticKind.UnknownField, "unknown field '" + name + "'");
                return;
            }

            if (pointingField is not null && string.Equals(field.Name, pointingField, StringComparison.OrdinalIgnoreCase))
            {
                Add(line, DiagnosticKind.UnknownField, "'" + field.Name + "' points at the root and is set when the row is written");
                return;
            }

            if (!block.FieldNames.Add(field.Name))
            {
                Add(line, DiagnosticKind.FieldRepeated, "field '" + field.Name + "' appears twice in the block");
                return;
            }

            if (value.Contains(TabEscape, StringComparison.Ordinal) || value.Contains(LineFeedEscape, StringComparison.Ordinal))
            {
                Add(line, DiagnosticKind.ControlCharacter, "a value cannot hold a tab or a line break, written as \\t or \\n");
            }

            block.Fields.Add(new ParsedField(field.Name, value, line));
        }

        private static FieldDefinition? FindField(TableDefinition definition, string name) =>
            definition.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>"name = value": the name is everything before the first '=' (no spaces), the value starts after one optional space.</summary>
        private static bool TryAssignment(string text, out string name, out string value)
        {
            name = string.Empty;
            value = string.Empty;
            var equals = text.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                return false;
            }

            name = text[..equals].Trim();
            if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            {
                return false;
            }

            value = text[(equals + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            return true;
        }

        /// <summary>A quoted loc text: \" is a quote, \\ a backslash, \n a line feed; nothing may follow the closing quote.</summary>
        private static bool TryUnquote(string quoted, out string text, out string problem)
        {
            var sb = new StringBuilder();
            text = string.Empty;
            var i = 1;
            while (i < quoted.Length)
            {
                var c = quoted[i];
                if (c == '\\' && i + 1 < quoted.Length)
                {
                    var next = quoted[i + 1];
                    switch (next)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        default:
                            sb.Append(c).Append(next);
                            break;
                    }

                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    if (i != quoted.Length - 1)
                    {
                        problem = "text after the closing quote (a quote inside the text is written \\\")";
                        return false;
                    }

                    text = sb.ToString();
                    problem = string.Empty;
                    return true;
                }

                sb.Append(c);
                i++;
            }

            problem = "the closing quote is missing";
            return false;
        }
    }
}
