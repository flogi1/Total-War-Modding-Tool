using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects;

/// <summary>A field of <see cref="SourceTable"/> that references a column of another table. FieldIndex is the index in the latest definition of SourceTable.</summary>
public sealed record IncomingReference(string SourceTable, string FieldName, int FieldIndex, string TargetColumn);

/// <summary>Reverse index over the foreign-key references of a <see cref="GameSchema"/>. Built once, read-only afterwards.</summary>
public sealed class ReferenceGraph
{
    private const string TableSuffix = "_tables";

    private static readonly IReadOnlyList<IncomingReference> NoReferences = [];
    private static readonly IReadOnlyList<int> NoFields = [];

    private readonly Dictionary<string, TableDefinition> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<IncomingReference>> _incoming = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _canonicalNames = new(StringComparer.OrdinalIgnoreCase);

    public static ReferenceGraph Build(GameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var graph = new ReferenceGraph();

        foreach (var (name, definitions) in schema.Tables)
        {
            graph._canonicalNames[name] = name;
            var latest = PickLatest(definitions);
            if (latest is not null)
            {
                graph._latest[name] = latest;
            }
        }

        var collected = new Dictionary<string, List<IncomingReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, definition) in graph._latest)
        {
            for (var index = 0; index < definition.Fields.Count; index++)
            {
                var field = definition.Fields[index];
                if (field.Reference is null)
                {
                    continue;
                }

                var target = graph.ResolveTableName(field.Reference.Table);
                if (!collected.TryGetValue(target, out var list))
                {
                    list = [];
                    collected[target] = list;
                }

                list.Add(new IncomingReference(name, field.Name, index, field.Reference.Column));
            }
        }

        foreach (var (target, list) in collected)
        {
            list.Sort(static (x, y) =>
            {
                var byTable = string.CompareOrdinal(x.SourceTable, y.SourceTable);
                return byTable != 0 ? byTable : string.CompareOrdinal(x.FieldName, y.FieldName);
            });
            graph._incoming[target] = list;
        }

        return graph;
    }

    /// <summary>The definition with the highest version, null when the schema does not know the table.</summary>
    public TableDefinition? Latest(string table) => _latest.GetValueOrDefault(table);

    /// <summary>Every field of every table that references <paramref name="table"/>; empty when none. Sorted by SourceTable, then FieldName (ordinal).</summary>
    public IReadOnlyList<IncomingReference> Incoming(string table) =>
        _incoming.GetValueOrDefault(ResolveTableName(table)) ?? NoReferences;

    /// <summary>Exactly two reference fields and at most four fields in the latest definition.</summary>
    public bool IsJunction(string table)
    {
        var definition = Latest(table);
        if (definition is null || definition.Fields.Count > 4)
        {
            return false;
        }

        return definition.Fields.Count(static f => f.Reference is not null) == 2;
    }

    /// <summary>Indexes of the fields with is_key in the latest definition; empty when unknown or without key.</summary>
    public IReadOnlyList<int> KeyFields(string table)
    {
        var definition = Latest(table);
        if (definition is null)
        {
            return NoFields;
        }

        var keys = new List<int>();
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            if (definition.Fields[index].IsKey)
            {
                keys.Add(index);
            }
        }

        return keys.Count == 0 ? NoFields : keys;
    }

    public bool HasObjects(string table) => KeyFields(table).Count > 0;

    /// <summary>"building_levels" gives "building_levels_tables" when the schema has it, otherwise the name unchanged.</summary>
    public string ResolveTableName(string referenceTable)
    {
        ArgumentNullException.ThrowIfNull(referenceTable);
        return _canonicalNames.TryGetValue(referenceTable + TableSuffix, out var canonical) ? canonical : referenceTable;
    }

    /// <summary>The highest version wins; with several of the same version the first one in the list, like <see cref="GameSchema.Find"/>.</summary>
    private static TableDefinition? PickLatest(IReadOnlyList<TableDefinition> definitions)
    {
        TableDefinition? best = null;
        foreach (var definition in definitions)
        {
            if (best is null || definition.Version > best.Version)
            {
                best = definition;
            }
        }

        return best;
    }
}
