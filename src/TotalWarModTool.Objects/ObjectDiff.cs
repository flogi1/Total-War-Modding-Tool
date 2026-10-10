using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>
/// The difference between the built object and the parsed block text (spec 5.3): rows to set, insert and delete, and loc texts to set and
/// insert. A pure function; the writing is another step.
/// </summary>
public static class ObjectDiff
{
    private const char KeySeparator = '\u0001';

    /// <summary>
    /// The diagnostics of the parsed object are carried over first, then the diff's own. With errors in the parsed object the diff is still
    /// computed where possible (so the App can show both), but callers must not apply a result with HasErrors. category tells which sections
    /// are hidden: their rows are never touched. referenceExists null means "do not check references".
    /// </summary>
    public static ObjectChanges Compute(ObjectView baseline, ParsedObject parsed, CategoryDefinition? category, ReferenceGraph graph,
        ReferenceExists? referenceExists)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(graph);
        return new Run(baseline, parsed, category, graph, referenceExists).Execute();
    }

    private sealed class Run
    {
        private readonly ObjectView _baseline;
        private readonly ParsedObject _parsed;
        private readonly CategoryDefinition? _category;
        private readonly ReferenceGraph _graph;
        private readonly ReferenceExists? _exists;
        private readonly List<BlockDiagnostic> _diagnostics = [];
        private readonly List<RowChange> _rows = [];
        private readonly List<TextChange> _texts = [];
        private readonly Dictionary<string, LocEntry> _baselineTexts = new(StringComparer.Ordinal);

        public Run(ObjectView baseline, ParsedObject parsed, CategoryDefinition? category, ReferenceGraph graph, ReferenceExists? exists)
        {
            _baseline = baseline;
            _parsed = parsed;
            _category = category;
            _graph = graph;
            _exists = exists;
            foreach (var entry in baseline.Texts)
            {
                _baselineTexts.TryAdd(entry.Key, entry);
            }
        }

        public ObjectChanges Execute()
        {
            _diagnostics.AddRange(_parsed.Diagnostics);
            var definition = _graph.Latest(_baseline.Id.Table);
            if (definition is null)
            {
                return Result();
            }

            DiffRoot(definition);
            var rootShort = ObjectText.ShortTableName(_baseline.Id.Table);
            foreach (var text in _parsed.Texts)
            {
                AddText(rootShort + "_" + text.Column + "_" + _baseline.Id.JoinedKey, text);
            }

            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in _parsed.Sections)
            {
                if (done.Add(SectionKey(section.SourceTable, section.FieldName)))
                {
                    DiffSection(section, FindBaselineSection(section.SourceTable, section.FieldName));
                }
            }

            foreach (var section in _baseline.Sections)
            {
                if (section.Rows.Count > 0 && !done.Contains(SectionKey(section.SourceTable, section.FieldName)) && !IsHidden(section))
                {
                    DiffSection(null, section);
                }
            }

            return Result();
        }

        private ObjectChanges Result() => new(_rows, _texts, _diagnostics);

        private void Add(int line, DiagnosticKind kind, string detail, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
            _diagnostics.Add(new BlockDiagnostic(line, severity, kind, detail));

        private static string SectionKey(string table, string field) => table + "." + field;

        private ObjectSection? FindBaselineSection(string table, string field) =>
            _baseline.Sections.FirstOrDefault(s => string.Equals(s.SourceTable, table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.FieldName, field, StringComparison.OrdinalIgnoreCase));

        private bool IsHidden(ObjectSection section) => ObjectText.RuleFor(_category, section.SourceTable, section.FieldName) is { Hidden: true };

        // ---- root ----

        private void DiffRoot(TableDefinition definition)
        {
            var table = _baseline.Id.Table;
            var keyIndexes = _graph.KeyFields(table);
            var (texts, invalid) = ParseCells(definition, _parsed.Root);
            var lines = LinesOf(definition, _parsed.Root);

            if (_baseline.Root is null)
            {
                var cells = new string[definition.Fields.Count];
                for (var i = 0; i < cells.Length; i++)
                {
                    var keyPosition = keyIndexes.ToList().IndexOf(i);
                    cells[i] = texts[i] ?? (keyPosition >= 0 && keyPosition < _baseline.Id.KeyValues.Count
                        ? _baseline.Id.KeyValues[keyPosition]
                        : DefaultText(definition.Fields[i].Type));
                    if (keyPosition >= 0 && keyPosition < _baseline.Id.KeyValues.Count
                        && !string.Equals(cells[i], _baseline.Id.KeyValues[keyPosition], StringComparison.Ordinal))
                    {
                        Add(lines[i] ?? 1, DiagnosticKind.RootKeyChanged, "the key field '" + definition.Fields[i].Name + "' cannot be changed");
                    }

                    if (texts[i] is not null && !invalid[i])
                    {
                        CheckReference(definition.Fields[i], texts[i]!, lines[i] ?? 1);
                    }
                }

                _rows.Add(new RowChange(RowChangeKind.Insert, table, null, null, cells, [], null, 1));
                return;
            }

            var baselineTexts = CellsOf(definition, _baseline.Root);
            var changed = new List<string>();
            var result = (string[])baselineTexts.Clone();
            for (var i = 0; i < result.Length; i++)
            {
                if (texts[i] is not { } text)
                {
                    continue;
                }

                if (keyIndexes.Contains(i))
                {
                    if (!SameValue(baselineTexts[i], text))
                    {
                        Add(lines[i] ?? 1, DiagnosticKind.RootKeyChanged, "the key field '" + definition.Fields[i].Name + "' cannot be changed");
                    }

                    continue;
                }

                if (SameValue(baselineTexts[i], text))
                {
                    continue;
                }

                result[i] = KeepTrailingWhitespace(baselineTexts[i], text);
                changed.Add(definition.Fields[i].Name);
                if (!invalid[i])
                {
                    CheckReference(definition.Fields[i], text, lines[i] ?? 1);
                }
            }

            if (changed.Count > 0)
            {
                _rows.Add(new RowChange(RowChangeKind.Set, table, null, null, result, changed, _baseline.Root.Provenance, 1, baselineTexts));
            }
        }

        // ---- sections ----

        private void DiffSection(ParsedSection? parsedSection, ObjectSection? baselineSection)
        {
            var table = parsedSection?.SourceTable ?? baselineSection!.SourceTable;
            var fieldName = parsedSection?.FieldName ?? baselineSection!.FieldName;
            var definition = _graph.Latest(table);
            var pointing = definition is null ? -1 : IndexOf(definition, fieldName);
            if (definition is null || pointing < 0)
            {
                return;
            }

            var rootValue = RootValueOf(table, fieldName);
            if (rootValue is null)
            {
                Add(parsedSection?.Line ?? 0, DiagnosticKind.UnknownSection, "the root value for '" + fieldName + "' cannot be resolved");
                return;
            }

            var keyIndexes = _graph.KeyFields(table);
            var baselineRows = baselineSection?.Rows ?? [];
            var baselineCells = baselineRows.Select(r => CellsOf(definition, r)).ToList();
            var matched = new bool[baselineRows.Count];
            var line = parsedSection?.Line ?? 0;

            if (parsedSection is not null)
            {
                if (keyIndexes.Count > 0)
                {
                    MatchByKey(parsedSection, definition, pointing, rootValue, keyIndexes, baselineRows, baselineCells, matched);
                }
                else
                {
                    MatchByFields(parsedSection, definition, pointing, rootValue, baselineRows, baselineCells, matched);
                }
            }

            for (var i = 0; i < baselineRows.Count; i++)
            {
                if (!matched[i])
                {
                    _rows.Add(new RowChange(RowChangeKind.Delete, table, fieldName, rootValue, baselineCells[i], [], baselineRows[i].Provenance, line, baselineCells[i]));
                }
            }
        }

        private void MatchByKey(ParsedSection section, TableDefinition definition, int pointing, string rootValue, IReadOnlyList<int> keyIndexes,
            IReadOnlyList<ObjectRow> baselineRows, List<string[]> baselineCells, bool[] matched)
        {
            // The pointing field is the same in every row of the section, so it is not part of the identity.
            var identity = keyIndexes.Where(i => i != pointing).ToList();
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < baselineRows.Count; i++)
            {
                var key = string.Join(KeySeparator, identity.Select(k => baselineCells[i][k].TrimEnd()));
                if (!groups.TryGetValue(key, out var group))
                {
                    group = [];
                    groups[key] = group;
                }

                group.Add(i);
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in section.Rows)
            {
                var (texts, invalid) = ParseCells(definition, row.Fields);
                var lines = LinesOf(definition, row.Fields);
                var missing = identity.Where(k => texts[k] is null).ToList();
                if (missing.Count > 0)
                {
                    Add(row.Line, DiagnosticKind.UnknownLineForm, "key field " + definition.Fields[missing[0]].Name + " missing");
                    continue;
                }

                var key = string.Join(KeySeparator, identity.Select(k => texts[k]!.TrimEnd()));
                seen[key] = seen.GetValueOrDefault(key) + 1;
                var ordinal = seen[key] - 1;
                groups.TryGetValue(key, out var group);

                if (group is not null && ordinal < group.Count)
                {
                    var index = group[ordinal];
                    matched[index] = true;
                    SetRow(section, definition, pointing, rootValue, row, texts, invalid, lines, baselineRows[index], baselineCells[index]);
                    AddRowTexts(section, row, baselineRows[index].RowId?.JoinedKey);
                    continue;
                }

                if (group is null && ordinal == 0)
                {
                    var cells = InsertRow(section, definition, pointing, rootValue, row, texts, invalid, lines);
                    AddRowTexts(section, row, string.Concat(keyIndexes.Select(k => cells[k])));
                    continue;
                }

                if (group is { Count: > 1 })
                {
                    Add(row.Line, DiagnosticKind.DuplicateCountExceeded, "more rows with this key than the object has");
                }
                else
                {
                    Add(row.Line, DiagnosticKind.KeyRepeated, "the key appears twice");
                }
            }
        }

        private void MatchByFields(ParsedSection section, TableDefinition definition, int pointing, string rootValue,
            IReadOnlyList<ObjectRow> baselineRows, List<string[]> baselineCells, bool[] matched)
        {
            foreach (var row in section.Rows)
            {
                var (texts, invalid) = ParseCells(definition, row.Fields);
                var lines = LinesOf(definition, row.Fields);
                var cells = new string[definition.Fields.Count];
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = i == pointing ? rootValue : texts[i] ?? DefaultText(definition.Fields[i].Type);
                }

                var index = -1;
                for (var i = 0; i < baselineCells.Count && index < 0; i++)
                {
                    if (!matched[i] && baselineCells[i].Zip(cells, SameValue).All(same => same))
                    {
                        index = i;
                    }
                }

                if (index >= 0)
                {
                    matched[index] = true;
                }
                else
                {
                    InsertRow(section, definition, pointing, rootValue, row, texts, invalid, lines);
                }

                // A keyless row has no key a loc key could be built from.
                AddRowTexts(section, row, null);
            }
        }

        private void SetRow(ParsedSection section, TableDefinition definition, int pointing, string rootValue, ParsedRow row, string?[] texts,
            bool[] invalid, int?[] lines, ObjectRow baselineRow, string[] baselineCells)
        {
            var result = (string[])baselineCells.Clone();
            var changed = new List<string>();
            for (var i = 0; i < result.Length; i++)
            {
                if (i == pointing || texts[i] is not { } text || SameValue(baselineCells[i], text))
                {
                    continue;
                }

                result[i] = KeepTrailingWhitespace(baselineCells[i], text);
                changed.Add(definition.Fields[i].Name);
                if (!invalid[i])
                {
                    CheckReference(definition.Fields[i], text, lines[i] ?? row.Line);
                }
            }

            if (changed.Count > 0)
            {
                _rows.Add(new RowChange(RowChangeKind.Set, section.SourceTable, section.FieldName, rootValue, result, changed, baselineRow.Provenance, row.Line, baselineCells));
            }
        }

        private string[] InsertRow(ParsedSection section, TableDefinition definition, int pointing, string rootValue, ParsedRow row, string?[] texts,
            bool[] invalid, int?[] lines)
        {
            var cells = new string[definition.Fields.Count];
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] = i == pointing ? rootValue : texts[i] ?? DefaultText(definition.Fields[i].Type);
                if (i != pointing && texts[i] is not null && !invalid[i])
                {
                    CheckReference(definition.Fields[i], texts[i]!, lines[i] ?? row.Line);
                }
            }

            _rows.Add(new RowChange(RowChangeKind.Insert, section.SourceTable, section.FieldName, rootValue, cells, [], null, row.Line));
            return cells;
        }

        // ---- texts ----

        private void AddRowTexts(ParsedSection section, ParsedRow row, string? joinedKey)
        {
            foreach (var text in row.Texts)
            {
                if (joinedKey is null)
                {
                    Add(text.Line, DiagnosticKind.TextWithoutOwnerKey, "the row has no key a text can belong to");
                    continue;
                }

                AddText(ObjectText.ShortTableName(section.SourceTable) + "_" + text.Column + "_" + joinedKey, text);
            }
        }

        private void AddText(string key, ParsedText text)
        {
            if (!_baselineTexts.TryGetValue(key, out var existing))
            {
                _texts.Add(new TextChange(TextChangeKind.Insert, key, text.Text, null, text.Line));
            }
            else if (!string.Equals(existing.Text, text.Text, StringComparison.Ordinal))
            {
                _texts.Add(new TextChange(TextChangeKind.Set, key, text.Text, existing.Source, text.Line));
            }
        }

        // ---- cells ----

        /// <summary>
        /// The text of every field of the table the block gave (null where it did not), normalised through the field type. A text that does
        /// not fit the type is an error and stays as typed.
        /// </summary>
        private (string?[] Texts, bool[] Invalid) ParseCells(TableDefinition definition, IReadOnlyList<ParsedField> fields)
        {
            var texts = new string?[definition.Fields.Count];
            var invalid = new bool[definition.Fields.Count];
            foreach (var field in fields)
            {
                var index = IndexOf(definition, field.Name);
                if (index < 0)
                {
                    continue;
                }

                if (TryNormalize(definition.Fields[index].Type, field.Text, out var normalised, out var reason))
                {
                    texts[index] = normalised;
                }
                else
                {
                    texts[index] = field.Text;
                    invalid[index] = true;
                    Add(field.Line, DiagnosticKind.InvalidValue, "'" + definition.Fields[index].Name + "': " + reason);
                }
            }

            return (texts, invalid);
        }

        private static int?[] LinesOf(TableDefinition definition, IReadOnlyList<ParsedField> fields)
        {
            var lines = new int?[definition.Fields.Count];
            foreach (var field in fields)
            {
                var index = IndexOf(definition, field.Name);
                if (index >= 0)
                {
                    lines[index] = field.Line;
                }
            }

            return lines;
        }

        private static string[] CellsOf(TableDefinition definition, ObjectRow row)
        {
            var cells = new string[definition.Fields.Count];
            for (var i = 0; i < cells.Length; i++)
            {
                var value = row.Fields.FirstOrDefault(f => string.Equals(f.Name, definition.Fields[i].Name, StringComparison.OrdinalIgnoreCase));
                cells[i] = value?.Text ?? DefaultText(definition.Fields[i].Type);
            }

            return cells;
        }

        private static bool TryNormalize(FieldType type, string text, out string normalised, out string reason)
        {
            normalised = text;
            reason = string.Empty;
            if (type == FieldType.Unsupported)
            {
                return true;
            }

            if (!CellText.TryParse(type, text, out var value, out var why))
            {
                reason = why;
                return false;
            }

            try
            {
                normalised = CellText.Format(type, value);
            }
            catch (ArgumentException)
            {
                normalised = text;
            }

            return true;
        }

        /// <summary>
        /// The block trims the end of every value, so a stored value that differs from the parsed one only by whitespace at its end (536 real
        /// values end with a space) is unchanged. An edit of such a value is still seen.
        /// </summary>
        /// <summary>The block shows values without their trailing whitespace, so an edited value gets the stored ending back.</summary>
        private static string KeepTrailingWhitespace(string baseline, string parsed)
        {
            var trimmed = baseline.TrimEnd();
            return trimmed.Length < baseline.Length && (parsed.Length == 0 || !char.IsWhiteSpace(parsed[^1]))
                ? parsed + baseline[trimmed.Length..]
                : parsed;
        }

        private static bool SameValue(string baseline, string parsed) =>
            string.Equals(baseline, parsed, StringComparison.Ordinal) || string.Equals(baseline.TrimEnd(), parsed, StringComparison.Ordinal);

        private static string DefaultText(FieldType type) => type == FieldType.Unsupported ? string.Empty : CellText.Format(type, CellText.DefaultValue(type));

        private static int IndexOf(TableDefinition definition, string name)
        {
            for (var i = 0; i < definition.Fields.Count; i++)
            {
                if (string.Equals(definition.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The key value of the root that the field of the section points at (the target column of the reference).</summary>
        private string? RootValueOf(string table, string fieldName)
        {
            var reference = _graph.Incoming(_baseline.Id.Table).FirstOrDefault(r =>
                string.Equals(r.SourceTable, table, StringComparison.OrdinalIgnoreCase) && string.Equals(r.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            var rootDefinition = _graph.Latest(_baseline.Id.Table);
            if (reference is null || rootDefinition is null)
            {
                return null;
            }

            var keys = _graph.KeyFields(_baseline.Id.Table);
            for (var position = 0; position < keys.Count && position < _baseline.Id.KeyValues.Count; position++)
            {
                if (string.Equals(rootDefinition.Fields[keys[position]].Name, reference.TargetColumn, StringComparison.OrdinalIgnoreCase))
                {
                    return _baseline.Id.KeyValues[position];
                }
            }

            return null;
        }

        private void CheckReference(FieldDefinition field, string value, int line)
        {
            if (_exists is null || field.Reference is null || value.Length == 0)
            {
                return;
            }

            if (!_exists(_graph.ResolveTableName(field.Reference.Table), field.Reference.Column, value))
            {
                Add(line, DiagnosticKind.ReferenceNotFound, "'" + value + "' is not in " + field.Reference.Table + "." + field.Reference.Column,
                    DiagnosticSeverity.Warning);
            }
        }
    }
}
