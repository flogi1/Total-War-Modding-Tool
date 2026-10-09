using TotalWarModTool.GameProfiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// The schema and the sources of the object controller tests: <c>x_tables</c> (key, label), <c>y_tables</c> (x refers to x_tables.key, value; no
/// key) and <c>z_tables</c> (key, no relation to x). Packs go into the data folder of a <see cref="TestGame"/>, project tables below a project folder.
/// </summary>
internal static class ObjectsTestSchema
{
    public const string LocPackName = "local_en.pack";

    /// <summary><see cref="TestGame.Profile"/> plus the default loc pack, so a loc pack in the data folder feeds the display names.</summary>
    public static readonly GameProfile Profile = TestGame.Profile with { DefaultLocPack = LocPackName };

    public static readonly TableDefinition X = new(0,
    [
        new FieldDefinition("key", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
        new FieldDefinition("label", FieldType.StringU8, nameof(FieldType.StringU8), false, null),
    ]);

    public static readonly TableDefinition Y = new(0,
    [
        new FieldDefinition("x", FieldType.StringU8, nameof(FieldType.StringU8), false, new TableReference("x", "key")),
        new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null),
    ]);

    public static readonly TableDefinition Z = new(0,
    [
        new FieldDefinition("key", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
    ]);

    /// <summary>x_tables with k1 and k2.</summary>
    public static readonly object?[][] XRows = [["k1", "First"], ["k2", "Second"]];

    /// <summary>Two rows of y_tables that point at k1.</summary>
    public static readonly object?[][] YRows = [["k1", 1], ["k1", 2]];

    private const string SchemaText = """
        (
            version: 5,
            definitions: {
                "x_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, is_reference: None),
                            (name: "label", field_type: StringU8, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "y_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "x", field_type: StringU8, is_key: false, is_reference: Some(("x", "key"))),
                            (name: "value", field_type: I32, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "z_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
            },
            patches: {},
        )
        """;

    /// <summary>Writes the schema file of the test profile into <paramref name="folder"/> (default: the shipped schema folder of the game).</summary>
    public static void WriteSchema(TestGame game, string? folder = null)
    {
        folder ??= game.Paths.SchemasDirectory;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Profile.SchemaFile), SchemaText);
    }

    /// <summary>A vanilla pack (type release) with x_tables and y_tables in the data folder; returns its path.</summary>
    public static string WriteVanillaPack(TestGame game, string name, object?[][] xRows, object?[][] yRows) =>
        new PackBuilder(packType: 1)
            .AddEntry("db/x_tables/data__", DbTableEncoder.Encode(X, 0, null, xRows))
            .AddEntry("db/y_tables/data__", DbTableEncoder.Encode(Y, 0, null, yRows))
            .WriteTo(Path.Combine(game.DataPath, name));

    /// <summary>A vanilla pack with z_tables only; returns its path.</summary>
    public static string WriteZPack(TestGame game, string name, object?[][] zRows) =>
        new PackBuilder(packType: 1).AddEntry("db/z_tables/data__", DbTableEncoder.Encode(Z, 0, null, zRows)).WriteTo(Path.Combine(game.DataPath, name));

    /// <summary>A mod pack (type mod) with x_tables in the data folder; it is not in used_mods.txt. Returns its path.</summary>
    public static string WriteModPack(TestGame game, string name, object?[][] xRows) =>
        new PackBuilder().AddEntry("db/x_tables/data__", DbTableEncoder.Encode(X, 0, null, xRows)).WriteTo(Path.Combine(game.DataPath, name));

    /// <summary>The loc pack of the profile with text/db/x.loc; returns its path.</summary>
    public static string WriteLocPack(TestGame game, params LocRow[] rows) =>
        new PackBuilder(packType: 1).AddEntry("text/db/x.loc", LocEncoder.Encode(rows)).WriteTo(Path.Combine(game.DataPath, LocPackName));

    /// <summary>Writes pack\db\x_tables\a.tsv below the project folder and returns its path.</summary>
    public static string WriteProjectTable(string projectFolder, object?[][] xRows)
    {
        var path = Path.Combine(projectFolder, "pack", "db", "x_tables", "a.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, TableTsv.Write("x_tables", 0, "db/x_tables/a.tsv", X, xRows).Text);
        return path;
    }
}
