namespace TotalWarModTool.Schema;

/// <summary>The field types the table reader can decode; every other RPFM type is <see cref="Unsupported"/> and keeps its name in <see cref="FieldDefinition.RawType"/>.</summary>
public enum FieldType { Boolean, F32, F64, I16, I32, I64, ColourRgb, StringU8, StringU16, OptionalStringU8, Unsupported }

public sealed record TableReference(string Table, string Column);

/// <summary><paramref name="RawType"/> is the type name exactly as the schema file spells it.</summary>
public sealed record FieldDefinition(string Name, FieldType Type, string RawType, bool IsKey, TableReference? Reference);

public sealed record TableDefinition(int Version, IReadOnlyList<FieldDefinition> Fields);

public sealed class GameSchema
{
    public GameSchema(IReadOnlyDictionary<string, IReadOnlyList<TableDefinition>> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var copy = new Dictionary<string, IReadOnlyList<TableDefinition>>(tables.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, definitions) in tables)
        {
            copy[name] = definitions;
        }

        Tables = copy;
    }

    /// <summary>Table name to all known versions. The key lookup ignores case.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TableDefinition>> Tables { get; }

    /// <summary>The definition with exactly this version; with several of the same version the first one.</summary>
    public TableDefinition? Find(string tableName, int version)
    {
        if (!Tables.TryGetValue(tableName, out var definitions))
        {
            return null;
        }

        foreach (var definition in definitions)
        {
            if (definition.Version == version)
            {
                return definition;
            }
        }

        return null;
    }
}
