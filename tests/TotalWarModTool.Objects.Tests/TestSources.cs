using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects.Tests;

/// <summary>Writes packs and project folders for the store tests.</summary>
internal static class TestSources
{
    /// <summary>One entry db/&lt;table&gt;/&lt;pack file name without extension&gt; per table, encoded with the definition of that version. The name differs per pack, so packs override each other row by row; use WritePackEntries for same-named files.</summary>
    public static string WritePack(string path, GameSchema schema, params (string Table, int Version, object?[][] Rows)[] tables) =>
        WritePack(path, schema, tables, []);

    /// <summary>Like the other overload, plus one entry text/&lt;Path&gt;.loc per loc file.</summary>
    public static string WritePack(string path, GameSchema schema, (string Table, int Version, object?[][] Rows)[] tables,
        (string Path, LocRow[] Rows)[] locs)
    {
        var builder = new PackBuilder();
        foreach (var (table, version, rows) in tables)
        {
            var definition = schema.Find(table, version) ?? throw new ArgumentException($"{table} v{version} is not in the test schema");
            builder.AddEntry($"db/{table}/{Path.GetFileNameWithoutExtension(path)}", DbTableEncoder.Encode(definition, version, guid: null, rows));
        }

        foreach (var (locPath, rows) in locs)
        {
            builder.AddEntry($"text/{locPath}.loc", LocEncoder.Encode(rows));
        }

        return builder.WriteTo(path);
    }

    /// <summary>One entry db/&lt;table&gt;/&lt;name&gt; per item, so a pack can hold several files of one table.</summary>
    public static string WritePackEntries(string path, GameSchema schema, params (string Name, string Table, int Version, object?[][] Rows)[] entries)
    {
        var builder = new PackBuilder();
        foreach (var (name, table, version, rows) in entries)
        {
            var definition = schema.Find(table, version) ?? throw new ArgumentException($"{table} v{version} is not in the test schema");
            builder.AddEntry($"db/{table}/{name}", DbTableEncoder.Encode(definition, version, guid: null, rows));
        }

        return builder.WriteTo(path);
    }

    /// <summary>Writes pack\db\&lt;table&gt;\&lt;fileName&gt;.tsv below the project folder and returns the file path.</summary>
    public static string WriteProjectTable(string projectFolder, GameSchema schema, string table, int version, string fileName, object?[][] rows)
    {
        var definition = schema.Find(table, version) ?? throw new ArgumentException($"{table} v{version} is not in the test schema");
        var result = TableTsv.Write(table, version, $"db/{table}/{fileName}", definition, rows);
        var path = Path.Combine(projectFolder, "pack", "db", table, fileName + ".tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, result.Text ?? throw new InvalidOperationException(result.Detail));
        return path;
    }

    /// <summary>Writes pack\&lt;relativePath&gt; (for example text\p.loc.tsv) below the project folder and returns the file path.</summary>
    public static string WriteProjectLoc(string projectFolder, string relativePath, LocRow[] rows)
    {
        var result = TableTsv.WriteLoc(relativePath.Replace('\\', '/'), rows);
        var path = Path.Combine(projectFolder, "pack", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, result.Text ?? throw new InvalidOperationException(result.Detail));
        return path;
    }

    public static TableSource Vanilla(string path) => new(SourceKind.Vanilla, Path.GetFileName(path), path);

    public static TableSource Mod(string path) => new(SourceKind.Mod, Path.GetFileName(path), path);

    public static TableSource Project(string folder) => new(SourceKind.Project, Path.GetFileName(folder), folder);

    public static LoadOrder Order(params TableSource[] sources) => new(sources, null, []);

    public static LoadOrder OrderWithLoc(TableSource locPack, params TableSource[] sources) => new(sources, locPack, []);
}
