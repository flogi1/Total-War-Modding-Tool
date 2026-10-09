using System.Diagnostics.CodeAnalysis;

namespace TotalWarModTool.Objects;

public sealed record LocText(string Text, TableSource Source);

/// <summary>All loc texts of a load order merged: the loc pack of the game language, then the mods, then the project. Read-only.</summary>
public sealed class LocTable
{
    private readonly Dictionary<string, LocText> _texts;
    private readonly Lazy<string[]> _sortedKeys;

    internal LocTable(Dictionary<string, LocText> texts, IReadOnlyList<SourceProblem> problems)
    {
        _texts = texts;
        Problems = problems;
        _sortedKeys = new Lazy<string[]>(() =>
        {
            var keys = texts.Keys.ToArray();
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        });
    }

    /// <summary>The keys that start with <paramref name="prefix"/> (ordinal), ascending, as a new list; empty when none. The keys are sorted once, on the first call.</summary>
    public IReadOnlyList<string> KeysWithPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        var keys = _sortedKeys.Value;
        int low = 0, high = keys.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (string.CompareOrdinal(keys[middle], prefix) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var result = new List<string>();
        for (var i = low; i < keys.Length && keys[i].StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            result.Add(keys[i]);
        }

        return result;
    }

    public int Count => _texts.Count;

    public IReadOnlyList<SourceProblem> Problems { get; }

    public bool TryGet(string key, [NotNullWhen(true)] out LocText? text)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _texts.TryGetValue(key, out text);
    }
}

/// <summary>
/// Merges the loc files of a load order. Loc entries are read from the loc pack of the order, then from every mod, then from the
/// project; a later source replaces the text of the same key. Other vanilla packs are not read. Only the table of the last
/// load order is kept, as long as the files that matter are unchanged. Reads only. Thread-safe.
/// </summary>
public sealed class LocStore
{
    private sealed record Cached(string Signature, LocTable Table);

    private readonly SourceReader _reader = new();
    private readonly object _gate = new();
    private Cached? _cached;

    /// <summary>Drops the cached table and every cached pack index.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cached = null;
        }

        _reader.Clear();
    }

    /// <summary>
    /// The merged loc texts. A source that cannot be read or decoded is reported in <see cref="LocTable.Problems"/> and the others are
    /// merged anyway. Throws only <see cref="OperationCanceledException"/>.
    /// </summary>
    public LocTable Get(LoadOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();

        var sources = new List<TableSource>();
        if (order.LocPack is not null)
        {
            sources.Add(order.LocPack);
        }

        sources.AddRange(order.Sources.Where(static s => s.Kind == SourceKind.Mod));
        sources.AddRange(order.Sources.Where(static s => s.Kind == SourceKind.Project));

        var listings = new List<SourceListing>(sources.Count);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listings.Add(_reader.ListLoc(source, cancellationToken));
        }

        var signature = string.Join('\n', listings.Select(static l => (int)l.Source.Kind + "|" + l.Source.Path + "|" + l.Token));
        lock (_gate)
        {
            if (_cached is { } cached && cached.Signature == signature)
            {
                return cached.Table;
            }
        }

        var texts = new Dictionary<string, LocText>(StringComparer.Ordinal);
        var problems = new List<SourceProblem>();
        var transient = false;
        foreach (var listing in listings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var problem in listing.Problems)
            {
                problems.Add(problem.Problem);
                transient |= problem.Transient;
            }

            foreach (var entry in listing.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (rows, problem) = _reader.ReadLoc(listing.Source, entry, cancellationToken);
                if (rows is null)
                {
                    problems.Add(problem!.Problem);
                    transient |= problem.Transient;
                    continue;
                }

                foreach (var row in rows)
                {
                    texts[row.Key] = new LocText(row.Text, listing.Source);
                }
            }
        }

        var table = new LocTable(texts, problems);
        lock (_gate)
        {
            // A table with a problem that may go away (locked file) is not remembered.
            _cached = transient ? null : new Cached(signature, table);
        }

        return table;
    }
}
