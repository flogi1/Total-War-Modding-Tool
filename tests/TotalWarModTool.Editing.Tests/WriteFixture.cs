using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing.Tests;

/// <summary>A small schema, packs and project folders for the tests of the object write.</summary>
internal static class WriteFixture
{
    public const string X = "x_tables";
    public const string Y = "y_tables";
    public const string Z = "z_tables";
    public const string Colour = "colour_tables";
    public const string Levels = "building_levels_tables";

    private static readonly TableReference LevelKey = new(Levels, "key");

    private static FieldDefinition F(string name, FieldType type = FieldType.StringU8, bool key = false, TableReference? reference = null) =>
        new(name, type, type.ToString(), key, reference);

    public static readonly GameSchema MiniSchema = new(new Dictionary<string, IReadOnlyList<TableDefinition>>(StringComparer.OrdinalIgnoreCase)
    {
        [X] =
        [
            new TableDefinition(1, [F("key", key: true), F("building", reference: LevelKey), F("note")]),
            new TableDefinition(2, [F("key", key: true), F("building", reference: LevelKey), F("note"), F("extra", FieldType.I32)]),
        ],
        [Y] = [new TableDefinition(1, [F("building", reference: LevelKey), F("a"), F("b")])],
        [Z] = [new TableDefinition(1, [F("building", key: true, reference: LevelKey), F("value", FieldType.I32)])],
        [Colour] = [new TableDefinition(1, [F("key", key: true), F("c_r", FieldType.I32), F("c_g", FieldType.I32), F("c_b", FieldType.I32)])],
        [Levels] = [new TableDefinition(1, [F("key", key: true), F("create_cost", FieldType.I32)])],
    });

    public static readonly ReferenceGraph Graph = ReferenceGraph.Build(MiniSchema, GameTableVersions.Empty);

    /// <summary>The game uses version 2 of x_tables and has no y_tables.</summary>
    public static readonly ReferenceGraph GraphWithGame = ReferenceGraph.Build(MiniSchema, new GameTableVersions(new Dictionary<string, int> { [X] = 2 }));

    public static TableDefinition Definition(string table, int version) =>
        MiniSchema.Find(table, version) ?? throw new ArgumentException($"{table} v{version} is not in the test schema");

    /// <summary>A row of x_tables v2.</summary>
    public static object?[] XRow(string key, string building = "b1", string note = "n", int extra = 1) => [key, building, note, extra];

    /// <summary>Writes pack\db\&lt;table&gt;\&lt;name&gt;.tsv below the project folder and returns the path.</summary>
    public static string WriteProjectTable(string projectFolder, string table, int version, string name, params object?[][] rows)
    {
        var result = TableTsv.Write(table, version, $"db/{table}/{name}", Definition(table, version), rows);
        var path = Path.Combine(projectFolder, "pack", "db", table, name + ".tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, result.Text ?? throw new InvalidOperationException(result.Detail));
        return path;
    }

    /// <summary>Writes pack\text\db\&lt;name&gt;.loc.tsv below the project folder and returns the path.</summary>
    public static string WriteProjectLoc(string projectFolder, string name, params LocRow[] rows)
    {
        var result = TableTsv.WriteLoc($"text/db/{name}.loc", rows);
        var path = Path.Combine(projectFolder, "pack", "text", "db", name + ".loc.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, result.Text ?? throw new InvalidOperationException(result.Detail));
        return path;
    }

    /// <summary>Writes a pack with one entry db/&lt;table&gt;/&lt;name&gt; per item.</summary>
    public static string WritePack(string path, params (string Name, string Table, int Version, object?[][] Rows)[] entries)
    {
        var builder = new PackBuilder();
        foreach (var (name, table, version, rows) in entries)
        {
            builder.AddEntry($"db/{table}/{name}", DbTableEncoder.Encode(Definition(table, version), version, guid: null, rows));
        }

        return builder.WriteTo(path);
    }

    public static TableSource Vanilla(string path) => new(SourceKind.Vanilla, Path.GetFileName(path), path);

    public static TableSource Mod(string path) => new(SourceKind.Mod, Path.GetFileName(path), path);

    public static TableSource Project(string folder) => new(SourceKind.Project, Path.GetFileName(folder), folder);

    public static RowProvenance From(TableSource source, string entryPath, params OverriddenLayer[] overridden) =>
        new(source, overridden.Select(static l => l.Source).ToList(), false, false, entryPath) { OverriddenLayers = overridden };

    public static string[] Cells(object?[] row, string table, int version)
    {
        var definition = Definition(table, version);
        return row.Select((value, i) => CellText.Format(definition.Fields[i].Type, value)).ToArray();
    }

    public static RowChange Change(RowChangeKind kind, string table, string[] cells, string[]? previous, RowProvenance? provenance, int line,
        params string[] changedFields) =>
        new(kind, table, null, null, cells, changedFields, provenance, line, previous);

    public static ObjectChanges Changes(IReadOnlyList<RowChange> rows, IReadOnlyList<TextChange>? texts = null) =>
        new(rows, texts ?? [], []);
}
