using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects.Tests;

internal static class TestSchema
{
    public static GameSchema Build(params (string Table, int Version, FieldDefinition[] Fields)[] defs)
    {
        var tables = new Dictionary<string, IReadOnlyList<TableDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, version, fields) in defs)
        {
            if (!tables.TryGetValue(table, out var list))
            {
                list = new List<TableDefinition>();
                tables[table] = list;
            }

            ((List<TableDefinition>)list).Add(new TableDefinition(version, fields));
        }

        return new GameSchema(tables);
    }

    public static FieldDefinition Field(string name, FieldType type = FieldType.StringU8, bool key = false, (string Table, string Column)? reference = null) =>
        new(name, type, type.ToString(), key, reference is { } r ? new TableReference(r.Table, r.Column) : null);
}
