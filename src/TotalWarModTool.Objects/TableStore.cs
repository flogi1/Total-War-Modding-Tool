using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>
/// One merged view per table over a load order: the entries of every source in order, later sources replacing rows with the same key.
/// Merged tables are cached (least recently used first out) as long as the files that matter for a table are unchanged.
/// Reads only. Thread-safe; two threads asking for the same table at once may both build it.
/// </summary>
public sealed class TableStore
{
    public const long DefaultMaxCacheBytes = 200L * 1024 * 1024;

    private sealed record CacheEntry(string Identity, string Signature, MergedTable Table);

    private sealed class KeyState
    {
        public KeyState(int sourceIndex, int position)
        {
            SourceIndex = sourceIndex;
            Positions = [position];
        }

        /// <summary>Index of the source whose rows currently hold the key.</summary>
        public int SourceIndex { get; set; }

        /// <summary>Positions of the live rows of the key in the row list, the first one is where the key first appeared.</summary>
        public List<int> Positions { get; set; }

        /// <summary>Every source that had rows of the key before the current one, in order.</summary>
        public List<TableSource> History { get; } = [];

        /// <summary>The sources the current rows replaced.</summary>
        public IReadOnlyList<TableSource> Overridden { get; set; } = [];
    }

    private readonly GameSchema _schema;
    private readonly ReferenceGraph _graph;
    private readonly long _maxCacheBytes;
    private readonly SourceReader _reader = new();
    private readonly object _gate = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _byIdentity = new(StringComparer.Ordinal);
    private long _cachedBytes;

    public TableStore(GameSchema schema, ReferenceGraph graph, long maxCacheBytes = DefaultMaxCacheBytes)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCacheBytes);
        _schema = schema;
        _graph = graph;
        _maxCacheBytes = maxCacheBytes;
    }

    public long CachedBytes
    {
        get
        {
            lock (_gate)
            {
                return _cachedBytes;
            }
        }
    }

    public int CachedTables
    {
        get
        {
            lock (_gate)
            {
                return _lru.Count;
            }
        }
    }

    /// <summary>Drops every cached table and every cached pack index.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lru.Clear();
            _byIdentity.Clear();
            _cachedBytes = 0;
        }

        _reader.Clear();
    }

    /// <summary>
    /// The table merged over the sources of the order. A source that cannot be read or decoded is reported in
    /// <see cref="MergedTable.Problems"/> and the others are merged anyway. Throws only <see cref="OperationCanceledException"/>.
    /// </summary>
    public MergedTable Get(string tableName, LoadOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();

        var listings = new List<SourceListing>(order.Sources.Count);
        foreach (var source in order.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listings.Add(_reader.ListTable(source, tableName, cancellationToken));
        }

        var identity = IdentityOf(tableName, order.Sources);
        var signature = string.Join('\n', listings.Select(static l => l.Token));
        lock (_gate)
        {
            if (_byIdentity.TryGetValue(identity, out var node) && node.Value.Signature == signature)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Table;
            }
        }

        var (table, transient) = Build(tableName, listings, cancellationToken);
        Store(identity, signature, table, transient);
        return table;
    }

    private static string IdentityOf(string tableName, IReadOnlyList<TableSource> sources)
    {
        var sb = new StringBuilder(tableName.ToLowerInvariant());
        foreach (var source in sources)
        {
            sb.Append('\n').Append((int)source.Kind).Append('|').Append(source.Path);
        }

        return sb.ToString();
    }

    private void Store(string identity, string signature, MergedTable table, bool transient)
    {
        lock (_gate)
        {
            // The old state of the same table over the same sources is outdated now, whatever happens to the new one.
            if (_byIdentity.Remove(identity, out var stale))
            {
                _lru.Remove(stale);
                _cachedBytes -= stale.Value.Table.EstimatedBytes;
            }

            if (transient || table.EstimatedBytes > _maxCacheBytes)
            {
                return;
            }

            while (_lru.Last is { } oldest && _cachedBytes + table.EstimatedBytes > _maxCacheBytes)
            {
                _lru.RemoveLast();
                _byIdentity.Remove(oldest.Value.Identity);
                _cachedBytes -= oldest.Value.Table.EstimatedBytes;
            }

            _byIdentity[identity] = _lru.AddFirst(new CacheEntry(identity, signature, table));
            _cachedBytes += table.EstimatedBytes;
        }
    }

    private (MergedTable Table, bool Transient) Build(string tableName, List<SourceListing> listings, CancellationToken cancellationToken)
    {
        var definition = _graph.Latest(tableName);
        var keyFields = _graph.KeyFields(tableName);
        var rows = new List<MergedRow?>();
        var keyStates = new Dictionary<string, KeyState>(StringComparer.Ordinal);
        var problems = new List<SourceProblem>();
        var transient = false;
        var mappings = new Dictionary<TableDefinition, int[]?>(ReferenceEqualityComparer.Instance);

        for (var sourceIndex = 0; sourceIndex < listings.Count; sourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listing = listings[sourceIndex];
            foreach (var problem in listing.Problems)
            {
                problems.Add(problem.Problem);
                transient |= problem.Transient;
            }

            foreach (var entry in listing.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (definition is null)
                {
                    problems.Add(new SourceProblem(listing.Source, entry.EntryPath, SourceProblemKind.NotInSchema,
                        "The table is not in the schema: " + tableName));
                    continue;
                }

                var (read, problem) = _reader.ReadTable(listing.Source, entry, tableName, _schema, cancellationToken);
                if (read is null)
                {
                    problems.Add(problem!.Problem);
                    transient |= problem.Transient;
                    continue;
                }

                if (!mappings.TryGetValue(read.Definition, out var mapping))
                {
                    mapping = MapFields(definition, read.Definition);
                    mappings[read.Definition] = mapping;
                }

                if (mapping is null)
                {
                    problems.Add(new SourceProblem(listing.Source, entry.EntryPath, SourceProblemKind.DecodeFailed,
                        "The latest definition of the table has a field of an unsupported type, so older layouts cannot be merged"));
                    continue;
                }

                var olderLayout = !IsIdentity(mapping, definition, read.Definition);
                foreach (var row in read.Rows)
                {
                    var cells = olderLayout ? Remap(definition, mapping, row) : row;
                    AddRow(rows, keyStates, definition, keyFields, listings, sourceIndex, cells, olderLayout);
                }
            }
        }

        var merged = rows.Where(static r => r is not null).Select(static r => r!).ToList();
        return (new MergedTable(tableName, definition, keyFields, merged, problems), transient);
    }

    private static void AddRow(List<MergedRow?> rows, Dictionary<string, KeyState> keyStates, TableDefinition definition,
        IReadOnlyList<int> keyFields, List<SourceListing> listings, int sourceIndex, object?[] cells, bool olderLayout)
    {
        var source = listings[sourceIndex].Source;
        if (keyFields.Count == 0)
        {
            rows.Add(new MergedRow(cells, new RowProvenance(source, [], olderLayout, false)));
            return;
        }

        var key = MergedTable.KeyOf(definition, keyFields, cells);
        if (!keyStates.TryGetValue(key, out var state))
        {
            keyStates[key] = new KeyState(sourceIndex, rows.Count);
            rows.Add(new MergedRow(cells, new RowProvenance(source, [], olderLayout, false)));
            return;
        }

        if (state.SourceIndex == sourceIndex)
        {
            // The same key twice in one source: both stay and both are marked.
            foreach (var position in state.Positions)
            {
                var existing = rows[position]!;
                rows[position] = existing with { Provenance = existing.Provenance with { Duplicate = true } };
            }

            state.Positions.Add(rows.Count);
            rows.Add(new MergedRow(cells, new RowProvenance(source, state.Overridden, olderLayout, true)));
            return;
        }

        // A later source replaces every row of the key, at the position of the first one.
        foreach (var position in state.Positions.Skip(1))
        {
            rows[position] = null;
        }

        var first = state.Positions[0];
        state.History.Add(listings[state.SourceIndex].Source);
        var overridden = state.History.ToArray();
        rows[first] = new MergedRow(cells, new RowProvenance(source, overridden, olderLayout, false));
        state.Positions = [first];
        state.SourceIndex = sourceIndex;
        state.Overridden = overridden;
    }

    /// <summary>For every target field the index of the field of the same name and type in the source definition, -1 when there is none. Null when a target field has an unsupported type.</summary>
    private static int[]? MapFields(TableDefinition target, TableDefinition source)
    {
        var mapping = new int[target.Fields.Count];
        for (var i = 0; i < mapping.Length; i++)
        {
            var field = target.Fields[i];
            if (field.Type == FieldType.Unsupported)
            {
                return null;
            }

            mapping[i] = -1;
            for (var j = 0; j < source.Fields.Count; j++)
            {
                if (source.Fields[j].Type == field.Type && string.Equals(source.Fields[j].Name, field.Name, StringComparison.OrdinalIgnoreCase))
                {
                    mapping[i] = j;
                    break;
                }
            }
        }

        return mapping;
    }

    private static bool IsIdentity(int[] mapping, TableDefinition target, TableDefinition source)
    {
        if (target.Fields.Count != source.Fields.Count)
        {
            return false;
        }

        for (var i = 0; i < mapping.Length; i++)
        {
            if (mapping[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    private static object?[] Remap(TableDefinition target, int[] mapping, object?[] row)
    {
        var cells = new object?[mapping.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = mapping[i] >= 0 ? row[mapping[i]] : CellText.DefaultValue(target.Fields[i].Type);
        }

        return cells;
    }
}
