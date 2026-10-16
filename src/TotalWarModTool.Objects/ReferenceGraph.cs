using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>A field of <see cref="SourceTable"/> that references a column of another table. FieldIndex is the index in the current definition of SourceTable.</summary>
public sealed record IncomingReference(string SourceTable, string FieldName, int FieldIndex, string TargetColumn);

/// <summary>Reverse index over the foreign-key references of a <see cref="GameSchema"/>. Built once, read-only afterwards.</summary>
public sealed class ReferenceGraph
{
    private const string TableSuffix = "_tables";

    private static readonly IReadOnlyList<IncomingReference> NoReferences = [];
    private static readonly IReadOnlyList<int> NoFields = [];

    private readonly Dictionary<string, TableDefinition> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<IncomingReference>> _incoming = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _canonicalNames = new(StringComparer.OrdinalIgnoreCase);

    private ReferenceGraph(GameTableVersions gameVersions)
    {
        GameVersions = gameVersions;
    }

    /// <summary>The table versions the game uses; the definitions of this graph follow them.</summary>
    public GameTableVersions GameVersions { get; }

    /// <summary>Builds the graph with the definition the game uses for each table. A table whose game version the schema lacks, or that is not in the schema, is left out.</summary>
    public static ReferenceGraph Build(GameSchema schema, GameTableVersions gameVersions)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(gameVersions);
        var graph = new ReferenceGraph(gameVersions);

        foreach (var name in schema.Tables.Keys)
        {
            graph._canonicalNames[name] = name;
            var current = TableVersionRule.Current(schema, gameVersions, name).Definition;
            if (current is not null)
            {
                graph._current[name] = current;
            }
        }

        var collected = new Dictionary<string, List<IncomingReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, definition) in graph._current)
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

    /// <summary>The definition of the version the game uses (the highest schema version when the game lacks the table); null when the table is not in the schema or the schema lacks the game's version.</summary>
    public TableDefinition? Current(string table) => _current.GetValueOrDefault(table);

    /// <summary>Every field of every table that references <paramref name="table"/>; empty when none. Sorted by SourceTable, then FieldName (ordinal).</summary>
    public IReadOnlyList<IncomingReference> Incoming(string table) =>
        _incoming.GetValueOrDefault(ResolveTableName(table)) ?? NoReferences;

    /// <summary>Exactly two reference fields and at most four fields in the current definition.</summary>
    public bool IsJunction(string table)
    {
        var definition = Current(table);
        if (definition is null || definition.Fields.Count > 4)
        {
            return false;
        }

        return definition.Fields.Count(static f => f.Reference is not null) == 2;
    }

    /// <summary>Indexes of the fields with is_key in the current definition; empty when unknown or without key.</summary>
    public IReadOnlyList<int> KeyFields(string table)
    {
        var definition = Current(table);
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
}
