using TotalWarModTool.Operations;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>
/// Builds the <see cref="ObjectView"/> of one row: the row itself with its references resolved, its loc texts, and every row of
/// other tables whose reference field points at it. Reads only; holds no state, so one builder can serve several threads.
/// Loc texts: for (a) the root with its joined key, (b) the objects the root references found, with their joined key, (c) every section row
/// with a RowId, with the joined key of the whole row (composite keys concatenate all key values), the entries table_column_key are looked up
/// for every loc column of the table. Loc columns are discovered from the loc keys themselves (prefix "table_", the joined keys of the table as
/// separator), not from the table definition, because most loc columns are missing in the schema. A reference to a table the schema does not know
/// is found by the exact key "table_column_value" for a display column, else by the shortest loc key with prefix "table_" and suffix "_value"
/// (LocOnly). Reference fields of section rows
/// never ask the table store: unknown targets are looked up in loc only, all others stay Unchecked.
/// </summary>
public sealed class ObjectBuilder
{
    private const string TableSuffix = "_tables";

    private static readonly string[] DisplayColumns =
        ["onscreen_name", "name", "screen_name", "localised_title", "title", "localised_name", "local_name"];

    private readonly ReferenceGraph _graph;
    private readonly TableStore _tables;
    private readonly LocStore _loc;

    public ObjectBuilder(ReferenceGraph graph, TableStore tables, LocStore loc)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(loc);
        _graph = graph;
        _tables = tables;
        _loc = loc;
    }

    /// <summary>Columns tried, in this order, for the display name of an object: onscreen_name, name, screen_name, localised_title, title, localised_name, local_name.</summary>
    public static IReadOnlyList<string> DisplayNameColumns => DisplayColumns;

    /// <summary>
    /// Throws OperationCanceledException; every other problem is a warning. Progress counts steps: the loc texts, the root table and
    /// every candidate section table (Done, Total, Item = table name). Tables that only serve to resolve a reference belong to the step that needs them.
    /// </summary>
    public ObjectView Build(ObjectId id, LoadOrder order, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();

        var rootDefinition = _graph.Latest(id.Table);
        var candidates = FindCandidates(id, rootDefinition);
        long total = candidates.Count + 2;
        long done = 0;

        void Step(string item) => progress?.Report(new OperationProgress(++done, total, item));

        var run = new Run(this, order, cancellationToken);
        run.LoadLoc();
        Step("loc");

        var warnings = new List<ObjectWarning>();
        var texts = new TextCollector(run.Loc!);

        // Root.
        var rootTable = run.Fetch(id.Table);
        ObjectRow? root = null;
        if (rootTable.Definition is null)
        {
            warnings.Add(new ObjectWarning(ObjectWarningKind.TableNotInSchema, id.Table, "The table is not in the schema"));
        }
        else
        {
            var rows = rootTable.FindByKey(id.KeyValues);
            if (rows.Count == 0)
            {
                warnings.Add(new ObjectWarning(ObjectWarningKind.RootNotFound, id.ToString(), "No row with this key in the load order"));
            }
            else
            {
                root = BuildRoot(run, rootTable, rows[0]);
            }
        }

        Step(id.Table);

        // Loc texts of the root and of the objects its references found, in field order.
        if (rootTable.Definition is not null)
        {
            texts.Add(id, run.LocColumns(StripSuffix(id.Table), rootTable));
        }

        if (root is not null)
        {
            foreach (var field in root.Fields)
            {
                if (field is { Resolution: ReferenceResolution.Found, Target: { } target })
                {
                    texts.Add(target, run.LocColumns(StripSuffix(target.Table), run.Fetch(target.Table)));
                }
            }
        }

        // Sections.
        var sections = new List<ObjectSection>();
        foreach (var (reference, value) in candidates)
        {
            var merged = run.Fetch(reference.SourceTable);
            if (merged.Definition is null)
            {
                warnings.Add(new ObjectWarning(ObjectWarningKind.TableNotInSchema, reference.SourceTable, "The table is not in the schema"));
            }
            else if (merged.FieldIndex(reference.FieldName) >= 0)
            {
                var rows = merged.FindByField(reference.FieldName, value);
                if (rows.Count > 0)
                {
                    var section = BuildSection(run, merged, reference, rows);
                    sections.Add(section);

                    // The loc key of a row is built from all its key values, not from the root key alone.
                    var columns = run.LocColumns(StripSuffix(reference.SourceTable), merged);
                    foreach (var sectionRow in section.Rows)
                    {
                        if (sectionRow.RowId is not null)
                        {
                            texts.Add(sectionRow.RowId, columns);
                        }
                    }
                }
            }

            Step(reference.SourceTable);
        }

        return new ObjectView(id, root, texts.Entries, sections, CollectWarnings(order, warnings, run), order);
    }

    /// <summary>Display name of an object from loc (first DisplayNameColumns hit), null when none. Uses only the LocStore.</summary>
    public string? TryDisplayName(ObjectId id, LoadOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(order);
        return DisplayName(_loc.Get(order, cancellationToken), id);
    }

    private static string? DisplayName(LocTable loc, ObjectId id)
    {
        var table = StripSuffix(id.Table);
        foreach (var column in DisplayColumns)
        {
            if (loc.TryGet(LocKey(table, column, id.JoinedKey), out var text))
            {
                return text.Text;
            }
        }

        return null;
    }

    private static string StripSuffix(string table) =>
        table.EndsWith(TableSuffix, StringComparison.OrdinalIgnoreCase) ? table[..^TableSuffix.Length] : table;

    private static string LocKey(string tableWithoutSuffix, string column, string key) => tableWithoutSuffix + "_" + column + "_" + key;

    /// <summary>The incoming references whose target column is a key field of the root, with the root's key value of that column.</summary>
    private List<(IncomingReference Reference, string Value)> FindCandidates(ObjectId id, TableDefinition? rootDefinition)
    {
        var result = new List<(IncomingReference, string)>();
        if (rootDefinition is null)
        {
            return result;
        }

        var keyFields = _graph.KeyFields(id.Table);
        foreach (var reference in _graph.Incoming(id.Table))
        {
            for (var position = 0; position < keyFields.Count && position < id.KeyValues.Count; position++)
            {
                if (string.Equals(rootDefinition.Fields[keyFields[position]].Name, reference.TargetColumn, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((reference, id.KeyValues[position]));
                    break;
                }
            }
        }

        return result;
    }

    private ObjectRow BuildRoot(Run run, MergedTable table, MergedRow row)
    {
        var definition = table.Definition!;
        var fields = new List<FieldValue>(definition.Fields.Count);
        for (var i = 0; i < definition.Fields.Count; i++)
        {
            var field = definition.Fields[i];
            fields.Add(ResolveField(run, field, CellText.Format(field.Type, row.Cells[i])));
        }

        return new ObjectRow(fields, row.Provenance, RowIdOf(table, row), null);
    }

    private ObjectSection BuildSection(Run run, MergedTable table, IncomingReference reference, IReadOnlyList<MergedRow> rows)
    {
        var definition = table.Definition!;
        var isJunction = _graph.IsJunction(reference.SourceTable);
        var otherIndex = -1;
        if (isJunction)
        {
            for (var i = 0; i < definition.Fields.Count; i++)
            {
                if (definition.Fields[i].Reference is not null
                    && !string.Equals(definition.Fields[i].Name, reference.FieldName, StringComparison.OrdinalIgnoreCase))
                {
                    otherIndex = i;
                    break;
                }
            }
        }

        var result = new List<ObjectRow>(rows.Count);
        foreach (var row in rows)
        {
            var fields = new List<FieldValue>(definition.Fields.Count);
            for (var i = 0; i < definition.Fields.Count; i++)
            {
                var field = definition.Fields[i];
                var text = CellText.Format(field.Type, row.Cells[i]);
                fields.Add(field.Reference is null
                    ? new FieldValue(field.Name, field.Type, text, null, ReferenceResolution.None, null, null)
                    : SectionReference(run, field, field.Reference, text));
            }

            var otherEnd = otherIndex >= 0 ? ResolveField(run, definition.Fields[otherIndex], fields[otherIndex].Text) : null;
            result.Add(new ObjectRow(fields, row.Provenance, RowIdOf(table, row), otherEnd));
        }

        return new ObjectSection(reference.SourceTable, reference.FieldName, isJunction, result);
    }

    /// <summary>
    /// A reference field of a section row. The store is not asked: a target table the schema does not know is looked up in the loc texts
    /// only (LocOnly on a hit), everything else stays Unchecked.
    /// </summary>
    private FieldValue SectionReference(Run run, FieldDefinition field, TableReference reference, string text)
    {
        if (text.Length > 0 && _graph.Latest(_graph.ResolveTableName(reference.Table)) is null
            && FindLocOnly(run.Loc!, reference.Table, text) is { } locText)
        {
            return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.LocOnly, null, locText);
        }

        return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.Unchecked, UncheckedTarget(reference, text), null);
    }

    /// <summary>The target of an unchecked reference: only when the table has exactly one key field and the reference points at that field.</summary>
    private ObjectId? UncheckedTarget(TableReference reference, string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var target = _graph.ResolveTableName(reference.Table);
        var keyFields = _graph.KeyFields(target);
        if (keyFields.Count != 1)
        {
            return null;
        }

        var keyName = _graph.Latest(target)!.Fields[keyFields[0]].Name;
        return string.Equals(keyName, reference.Column, StringComparison.OrdinalIgnoreCase) ? new ObjectId(target, [text]) : null;
    }

    private FieldValue ResolveField(Run run, FieldDefinition field, string text)
    {
        var reference = field.Reference;
        if (reference is null || text.Length == 0)
        {
            return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.None, null, null);
        }

        var targetTable = _graph.ResolveTableName(reference.Table);
        var merged = run.Fetch(targetTable);
        var hits = merged.FindByField(reference.Column, text);
        if (hits.Count > 0)
        {
            var target = RowIdOf(merged, hits[0]);
            var name = target is null ? null : DisplayName(run.Loc!, target);
            return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.Found, target, name);
        }

        if (_graph.Latest(targetTable) is null && FindLocOnly(run.Loc!, reference.Table, text) is { } locText)
        {
            return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.LocOnly, null, locText);
        }

        return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.NotFound, null, null);
    }

    /// <summary>
    /// The text of a table the schema does not know: first the exact keys "table_column_value" for the DisplayNameColumns in their order,
    /// then the shortest loc key (ties: the first in ordinal order) that starts with "table_" and ends with "_value". In the second case
    /// the loc column is the part between and is not the column the reference names. Null when there is none.
    /// </summary>
    private static string? FindLocOnly(LocTable loc, string referenceTable, string value)
    {
        var table = StripSuffix(referenceTable);
        foreach (var column in DisplayColumns)
        {
            if (loc.TryGet(LocKey(table, column, value), out var exact))
            {
                return exact.Text;
            }
        }

        var prefix = table + "_";
        var suffix = "_" + value;
        string? best = null;
        foreach (var key in loc.KeysWithPrefix(prefix))
        {
            // The keys come in ordinal order, so a strict comparison keeps the first of equal length.
            if (key.Length > prefix.Length + suffix.Length && (best is null || key.Length < best.Length)
                && key.EndsWith(suffix, StringComparison.Ordinal))
            {
                best = key;
            }
        }

        return best is not null && loc.TryGet(best, out var text) ? text.Text : null;
    }

    /// <summary>The id of a row (its key values, whose JoinedKey is the form loc keys use); null for a table without key.</summary>
    private static ObjectId? RowIdOf(MergedTable table, MergedRow row)
    {
        if (table.KeyFields.Count == 0 || table.Definition is null)
        {
            return null;
        }

        var values = new string[table.KeyFields.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var index = table.KeyFields[i];
            values[i] = CellText.Format(table.Definition.Fields[index].Type, row.Cells[index]);
        }

        return new ObjectId(table.TableName, values);
    }

    private static List<ObjectWarning> CollectWarnings(LoadOrder order, List<ObjectWarning> buildWarnings, Run run)
    {
        var result = new List<ObjectWarning>();
        var seen = new HashSet<ObjectWarning>();

        void Add(ObjectWarning warning)
        {
            if (seen.Add(warning))
            {
                result.Add(warning);
            }
        }

        foreach (var warning in order.Warnings)
        {
            var detail = warning.Name is null ? warning.Problem.ToString() : warning.Problem + ": " + warning.Name;
            Add(new ObjectWarning(ObjectWarningKind.LoadOrder, warning.Name, detail));
        }

        foreach (var warning in buildWarnings)
        {
            Add(warning);
        }

        foreach (var problem in run.FetchedTables.SelectMany(static t => t.Problems).Concat(run.Loc!.Problems))
        {
            var detail = problem.EntryPath.Length == 0 ? problem.Detail : problem.EntryPath + ": " + problem.Detail;
            Add(new ObjectWarning(ObjectWarningKind.SourceProblem, problem.Source.Name, detail));
        }

        return result;
    }

    /// <summary>The state of one Build: the loc table and the tables fetched so far, so a table is asked of the store once.</summary>
    private sealed class Run
    {
        private readonly ObjectBuilder _owner;
        private readonly LoadOrder _order;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, MergedTable> _fetched = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _locColumns = new(StringComparer.Ordinal);

        public Run(ObjectBuilder owner, LoadOrder order, CancellationToken cancellationToken)
        {
            _owner = owner;
            _order = order;
            _cancellationToken = cancellationToken;
        }

        public LocTable? Loc { get; private set; }

        public List<MergedTable> FetchedTables { get; } = [];

        public void LoadLoc()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Loc = _owner._loc.Get(_order, _cancellationToken);
        }

        /// <summary>
        /// The loc columns that exist for a table, found in the loc keys themselves (the schema lacks most of them): for every loc key
        /// that starts with "table_", the remainder is split at each "_" from the left, and the first split whose tail is the joined key
        /// of a row of the table gives the column (the head). Distinct columns in order of first appearance, cached per table.
        /// </summary>
        public IReadOnlyList<string> LocColumns(string tableWithoutSuffix, MergedTable merged)
        {
            if (_locColumns.TryGetValue(tableWithoutSuffix, out var cached))
            {
                return cached;
            }

            var columns = new List<string>();
            if (merged.KeyFields.Count > 0 && merged.Rows.Count > 0)
            {
                var prefix = tableWithoutSuffix + "_";
                var candidates = Loc!.KeysWithPrefix(prefix);
                if (candidates.Count > 0)
                {
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in merged.Rows)
                    {
                        keys.Add(RowIdOf(merged, row)!.JoinedKey);
                    }

                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var locKey in candidates)
                    {
                        var rest = locKey[prefix.Length..];
                        for (var i = rest.IndexOf('_'); i >= 0; i = rest.IndexOf('_', i + 1))
                        {
                            if (i > 0 && keys.Contains(rest[(i + 1)..]))
                            {
                                var column = rest[..i];
                                if (seen.Add(column))
                                {
                                    columns.Add(column);
                                }

                                break;
                            }
                        }
                    }
                }
            }

            _locColumns[tableWithoutSuffix] = columns;
            return columns;
        }

        public MergedTable Fetch(string table)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_fetched.TryGetValue(table, out var merged))
            {
                merged = _owner._tables.Get(table, _order, _cancellationToken);
                _fetched[table] = merged;
                FetchedTables.Add(merged);
            }

            return merged;
        }
    }

    /// <summary>Collects the loc entries of the columns of a table for one key, without doubles, in the order they are added.</summary>
    private sealed class TextCollector
    {
        private readonly LocTable _loc;
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly List<LocEntry> _entries = [];

        public TextCollector(LocTable loc) => _loc = loc;

        public IReadOnlyList<LocEntry> Entries => _entries;

        /// <summary>The texts of one object, looked up with its table and its joined key.</summary>
        public void Add(ObjectId owner, IReadOnlyList<string> columns)
        {
            var name = StripSuffix(owner.Table);
            foreach (var column in columns)
            {
                var locKey = LocKey(name, column, owner.JoinedKey);
                if (_loc.TryGet(locKey, out var text) && _keys.Add(locKey))
                {
                    _entries.Add(new LocEntry(name, column, locKey, text.Text, text.Source, owner));
                }
            }
        }
    }
}
