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

        /// <summary>Every distinct file (source and entry path) of the replaced layers that held the key, in order.</summary>
        public List<OverriddenLayer> HistoryLayers { get; } = [];

        /// <summary>The sources the current rows replaced.</summary>
        public IReadOnlyList<TableSource> Overridden { get; set; } = [];

        /// <summary>The files of the replaced layers, see <see cref="HistoryLayers"/>.</summary>
        public IReadOnlyList<OverriddenLayer> OverriddenLayers { get; set; } = [];
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

        var listings = List(tableName, order, cancellationToken);
        var identity = IdentityOf(tableName, order.Sources);
        var signature = Signature(listings);
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

    /// <summary>
    /// True when a Vanilla or Mod source of the order holds an entry with this pack path ('/' or '\', ignoring case). Project sources are not
    /// asked. A pack that cannot be read counts as not holding it. Thread-safe like <see cref="Get"/>.
    /// </summary>
    public bool HasPackEntry(LoadOrder order, string packPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(packPath);
        foreach (var source in order.Sources)
        {
            if (source.Kind != SourceKind.Project && _reader.HasEntry(source, packPath, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The state of the files that matter for the table over the order: the string <see cref="Get"/> uses as its cache key, one token per
    /// source (a pack without entries of the table gives "-", a missing pack "missing"). Equal signatures mean <see cref="Get"/> would see the
    /// same content. Decodes no entry and does not touch the table cache; only a pack whose file changed has its index read again.
    /// Throws only <see cref="OperationCanceledException"/>.
    /// </summary>
    public string SignatureOf(string tableName, LoadOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();
        return Signature(List(tableName, order, cancellationToken));
    }

    private List<SourceListing> List(string tableName, LoadOrder order, CancellationToken cancellationToken)
    {
        var listings = new List<SourceListing>(order.Sources.Count);
        foreach (var source in order.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listings.Add(_reader.ListTable(source, tableName, cancellationToken));
        }

        return listings;
    }

    private static string Signature(List<SourceListing> listings) => string.Join('\n', listings.Select(static l => l.Token));

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
        // A file of a later source with the same pack path replaces an earlier file as a whole: the earlier one is not read at all.
        var surviving = SourceReader.SurvivingEntries(listings);

        for (var sourceIndex = 0; sourceIndex < listings.Count; sourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listing = listings[sourceIndex];
            foreach (var problem in listing.Problems)
            {
                problems.Add(problem.Problem);
                transient |= problem.Transient;
            }

            foreach (var entry in surviving[sourceIndex])
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
                    mapping = RowLayout.MapFields(definition, read.Definition);
                    mappings[read.Definition] = mapping;
                }

                if (mapping is null)
                {
                    problems.Add(new SourceProblem(listing.Source, entry.EntryPath, SourceProblemKind.DecodeFailed,
                        "The latest definition of the table has a field of an unsupported type, so older layouts cannot be merged"));
                    continue;
                }

                var olderLayout = !RowLayout.IsIdentity(mapping, definition, read.Definition);
                foreach (var row in read.Rows)
                {
                    var cells = olderLayout ? RowLayout.Remap(definition, read.Definition, mapping, row) : row;
                    AddRow(rows, keyStates, definition, keyFields, listings, sourceIndex, cells, olderLayout, entry.EntryPath);
                }
            }
        }

        var merged = rows.Where(static r => r is not null).Select(static r => r!).ToList();
        return (new MergedTable(tableName, definition, keyFields, merged, problems), transient);
    }

    private static void AddRow(List<MergedRow?> rows, Dictionary<string, KeyState> keyStates, TableDefinition definition,
        IReadOnlyList<int> keyFields, List<SourceListing> listings, int sourceIndex, object?[] cells, bool olderLayout, string entryPath)
    {
        var source = listings[sourceIndex].Source;
        if (keyFields.Count == 0)
        {
            rows.Add(new MergedRow(cells, new RowProvenance(source, [], olderLayout, false, entryPath)));
            return;
        }

        var key = MergedTable.KeyOf(definition, keyFields, cells);
        if (!keyStates.TryGetValue(key, out var state))
        {
            keyStates[key] = new KeyState(sourceIndex, rows.Count);
            rows.Add(new MergedRow(cells, new RowProvenance(source, [], olderLayout, false, entryPath)));
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
            rows.Add(new MergedRow(cells, new RowProvenance(source, state.Overridden, olderLayout, true, entryPath) { OverriddenLayers = state.OverriddenLayers }));
            return;
        }

        // A later source replaces every row of the key, at the position of the first one. Every file of the replaced layer that held the
        // key is remembered, so a delete can strip the key from all of them.
        foreach (var position in state.Positions)
        {
            var layer = new OverriddenLayer(listings[state.SourceIndex].Source, rows[position]!.Provenance.EntryPath);
            if (!state.HistoryLayers.Contains(layer))
            {
                state.HistoryLayers.Add(layer);
            }
        }

        foreach (var position in state.Positions.Skip(1))
        {
            rows[position] = null;
        }

        var first = state.Positions[0];
        state.History.Add(listings[state.SourceIndex].Source);
        var overridden = state.History.ToArray();
        var overriddenLayers = state.HistoryLayers.ToArray();
        rows[first] = new MergedRow(cells, new RowProvenance(source, overridden, olderLayout, false, entryPath) { OverriddenLayers = overriddenLayers });
        state.Positions = [first];
        state.SourceIndex = sourceIndex;
        state.Overridden = overridden;
        state.OverriddenLayers = overriddenLayers;
    }
}
