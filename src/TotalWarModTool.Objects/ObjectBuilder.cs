using TotalWarModTool.Operations;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>
/// Builds the <see cref="ObjectView"/> of one row: the row itself with its references resolved, its loc texts, and every row of
/// other tables whose reference field points at it. Reads only; holds no state, so one builder can serve several threads.
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
        if (rootDefinition is not null)
        {
            texts.Add(id.Table, rootDefinition, id.JoinedKey);
        }

        if (root is not null)
        {
            foreach (var field in root.Fields)
            {
                if (field is { Resolution: ReferenceResolution.Found, Target: { } target } && _graph.Latest(target.Table) is { } targetDefinition)
                {
                    texts.Add(target.Table, targetDefinition, target.JoinedKey);
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
                    sections.Add(BuildSection(run, merged, reference, rows));
                    texts.Add(reference.SourceTable, merged.Definition, value);
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
                    : new FieldValue(field.Name, field.Type, text, field.Reference, ReferenceResolution.Unchecked, UncheckedTarget(field.Reference, text), null));
            }

            var otherEnd = otherIndex >= 0 ? ResolveField(run, definition.Fields[otherIndex], fields[otherIndex].Text) : null;
            result.Add(new ObjectRow(fields, row.Provenance, RowIdOf(table, row), otherEnd));
        }

        return new ObjectSection(reference.SourceTable, reference.FieldName, isJunction, result);
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

        if (run.Loc!.TryGet(LocKey(StripSuffix(reference.Table), reference.Column, text), out var locText))
        {
            return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.LocOnly, null, locText.Text);
        }

        return new FieldValue(field.Name, field.Type, text, reference, ReferenceResolution.NotFound, null, null);
    }

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

        public void Add(string table, TableDefinition definition, string key)
        {
            var name = StripSuffix(table);
            foreach (var field in definition.Fields)
            {
                var locKey = LocKey(name, field.Name, key);
                if (_loc.TryGet(locKey, out var text) && _keys.Add(locKey))
                {
                    _entries.Add(new LocEntry(name, field.Name, locKey, text.Text, text.Source));
                }
            }
        }
    }
}
