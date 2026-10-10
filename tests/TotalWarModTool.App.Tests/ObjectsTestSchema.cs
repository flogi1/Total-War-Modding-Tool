using TotalWarModTool.GameProfiles;
using TotalWarModTool.Objects;
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

    // ---- The mini building schema of the wizard tests (as in BuildingWizardTests of the objects library) ----

    public static readonly TableDefinition Levels = new(0,
    [
        new FieldDefinition("level_name", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
        new FieldDefinition("chain", FieldType.StringU8, nameof(FieldType.StringU8), false, new TableReference("chains", "key")),
        new FieldDefinition("level", FieldType.I32, nameof(FieldType.I32), false, null),
    ]);

    public static readonly TableDefinition Chains = new(0,
    [
        new FieldDefinition("key", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
    ]);

    public static readonly TableDefinition Variants = new(0,
    [
        new FieldDefinition("building", FieldType.StringU8, nameof(FieldType.StringU8), true, new TableReference("levels", "level_name")),
        new FieldDefinition("culture", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
        new FieldDefinition("subculture", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
        new FieldDefinition("faction", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
    ]);

    public static readonly TableDefinition Subcultures = new(0,
    [
        new FieldDefinition("subculture", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
        new FieldDefinition("culture", FieldType.StringU8, nameof(FieldType.StringU8), false, null),
    ]);

    public static readonly TableDefinition Upgrades = new(0,
    [
        new FieldDefinition("from", FieldType.StringU8, nameof(FieldType.StringU8), true, new TableReference("levels", "level_name")),
        new FieldDefinition("to", FieldType.StringU8, nameof(FieldType.StringU8), true, new TableReference("levels", "level_name")),
    ]);

    // The mini trait tables of the trait wizard tests: a trait, its levels (key = trait key + "_" + number) and the effects of a level.
    public static readonly TableDefinition Traits = new(0,
    [
        new FieldDefinition("key", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
    ]);

    public static readonly TableDefinition TraitLevels = new(0,
    [
        new FieldDefinition("key", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
        new FieldDefinition("trait", FieldType.StringU8, nameof(FieldType.StringU8), false, new TableReference("traits", "key")),
        new FieldDefinition("rank", FieldType.I32, nameof(FieldType.I32), false, null),
    ]);

    public static readonly TableDefinition LevelEffects = new(0,
    [
        new FieldDefinition("level", FieldType.StringU8, nameof(FieldType.StringU8), true, new TableReference("trait_levels", "key")),
        new FieldDefinition("effect", FieldType.StringU8, nameof(FieldType.StringU8), true, null),
    ]);

    /// <summary>The trait wizard over the mini schema; the names of the levels are the loc column "onscreen_name" of trait_levels_tables.</summary>
    public static readonly TraitWizardDefinition TraitDefinition = new("traits_tables", new TextRef("trait_levels_tables", "onscreen_name"));

    /// <summary>The building wizard over the mini schema; the chain of a new level is "&lt;key&gt;_chain".</summary>
    public static readonly BuildingWizardDefinition BuildingDefinition = new(
        "levels_tables", "chain", "level", "chains_tables", "_chain", "upgrades_tables", "from", "to", "variants_tables", "building", "culture",
        "subculture", "subcultures_tables", "culture", new TextRef("variants_tables", "name"));

    /// <summary>The wizard file of the mini schema, suffix " (copy)".</summary>
    public const string WizardsJson = """
        {
          "schemaVersion": 1,
          "nameSuffix": " (copy)",
          "building": {
            "levelTable": "levels_tables",
            "chainField": "chain",
            "levelField": "level",
            "chainTable": "chains_tables",
            "chainKeySuffix": "_chain",
            "upgradeTable": "upgrades_tables",
            "upgradeFromField": "from",
            "upgradeToField": "to",
            "variantTable": "variants_tables",
            "variantBuildingField": "building",
            "variantCultureField": "culture",
            "variantSubcultureField": "subculture",
            "subcultureTable": "subcultures_tables",
            "subcultureCultureField": "culture",
            "nameText": { "table": "variants_tables", "column": "name" }
          },
          "trait": {
            "traitTable": "traits_tables",
            "nameText": { "table": "trait_levels_tables", "column": "onscreen_name" }
          }
        }
        """;

    /// <summary>
    /// Subcultures sc1 "Empire" and sc2 "Kislev" (culture c1), sc3 "Orcs" (c2). Levels b_1 (stage 0) and b_2 (stage 1) of b_chain, solo without
    /// chain, o_1 of o_chain, taken_1 of taken_chain. b_1 and solo carry a variant of sc1, b_2 one of culture c1, o_1 one of c2, taken_1 one of a faction only.
    /// b_1 is upgraded to b_2.
    /// </summary>
    public static readonly object?[][] LevelRows = [["b_1", "b_chain", 0], ["b_2", "b_chain", 1], ["solo", "", 0], ["o_1", "o_chain", 0], ["taken_1", "taken_chain", 0]];

    public static readonly object?[][] ChainRows = [["b_chain"], ["o_chain"], ["taken_chain"]];

    public static readonly object?[][] VariantRows =
        [["b_1", "", "sc1", ""], ["b_2", "c1", "", ""], ["solo", "", "sc1", ""], ["o_1", "c2", "", ""], ["taken_1", "", "", "f1"]];

    public static readonly object?[][] SubcultureRows = [["sc1", "c1"], ["sc2", "c1"], ["sc3", "c2"]];

    public static readonly object?[][] UpgradeRows = [["b_1", "b_2"]];

    /// <summary>Traits t1 (levels t1_1 and t1_2, three effect rows) and t2 (no level).</summary>
    public static readonly object?[][] TraitRows = [["t1"], ["t2"]];

    public static readonly object?[][] TraitLevelRows = [["t1_1", "t1", 1], ["t1_2", "t1", 2]];

    public static readonly object?[][] LevelEffectRows = [["t1_1", "e_a"], ["t1_1", "e_b"], ["t1_2", "e_a"]];

    public static readonly LocRow[] BuildingLocRows =
    [
        new("subcultures_name_sc1", "Empire", false),
        new("subcultures_name_sc2", "Kislev", false),
        new("subcultures_name_sc3", "Orcs", false),
        new("variants_name_b_1sc1", "Barracks", false),
        new("variants_name_b_2c1", "Big barracks", false),
        new("variants_name_solosc1", "Shrine", false),
        new("trait_levels_onscreen_name_t1_1", "Brave", false),
        new("trait_levels_onscreen_name_t1_2", "Braver", false),
    ];

    private const string BuildingSchemaText = """
        (
            version: 5,
            definitions: {
                "levels_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "level_name", field_type: StringU8, is_key: true, is_reference: None),
                            (name: "chain", field_type: StringU8, is_key: false, is_reference: Some(("chains", "key"))),
                            (name: "level", field_type: I32, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "chains_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "variants_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "building", field_type: StringU8, is_key: true, is_reference: Some(("levels", "level_name"))),
                            (name: "culture", field_type: StringU8, is_key: true, is_reference: None),
                            (name: "subculture", field_type: StringU8, is_key: true, is_reference: None),
                            (name: "faction", field_type: StringU8, is_key: true, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "subcultures_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "subculture", field_type: StringU8, is_key: true, is_reference: None),
                            (name: "culture", field_type: StringU8, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "upgrades_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "from", field_type: StringU8, is_key: true, is_reference: Some(("levels", "level_name"))),
                            (name: "to", field_type: StringU8, is_key: true, is_reference: Some(("levels", "level_name"))),
                        ],
                        localised_fields: [],
                    ),
                ],
                "traits_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "trait_levels_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, is_reference: None),
                            (name: "trait", field_type: StringU8, is_key: false, is_reference: Some(("traits", "key"))),
                            (name: "rank", field_type: I32, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
                "level_effects_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "level", field_type: StringU8, is_key: true, is_reference: Some(("trait_levels", "key"))),
                            (name: "effect", field_type: StringU8, is_key: true, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
            },
            patches: {},
        )
        """;

    /// <summary>Writes the mini building schema as the schema file of the test profile (shipped schema folder of the game).</summary>
    public static void WriteBuildingSchema(TestGame game)
    {
        Directory.CreateDirectory(game.Paths.SchemasDirectory);
        File.WriteAllText(Path.Combine(game.Paths.SchemasDirectory, Profile.SchemaFile), BuildingSchemaText);
    }

    /// <summary>A vanilla pack with the building tables of the mini schema at <paramref name="path"/>; returns the path.</summary>
    public static string WriteBuildingPack(string path) =>
        new PackBuilder(packType: 1)
            .AddEntry("db/levels_tables/data__", DbTableEncoder.Encode(Levels, 0, null, LevelRows))
            .AddEntry("db/chains_tables/data__", DbTableEncoder.Encode(Chains, 0, null, ChainRows))
            .AddEntry("db/variants_tables/data__", DbTableEncoder.Encode(Variants, 0, null, VariantRows))
            .AddEntry("db/subcultures_tables/data__", DbTableEncoder.Encode(Subcultures, 0, null, SubcultureRows))
            .AddEntry("db/upgrades_tables/data__", DbTableEncoder.Encode(Upgrades, 0, null, UpgradeRows))
            .AddEntry("db/traits_tables/data__", DbTableEncoder.Encode(Traits, 0, null, TraitRows))
            .AddEntry("db/trait_levels_tables/data__", DbTableEncoder.Encode(TraitLevels, 0, null, TraitLevelRows))
            .AddEntry("db/level_effects_tables/data__", DbTableEncoder.Encode(LevelEffects, 0, null, LevelEffectRows))
            .WriteTo(path);

    /// <summary>The building pack in the data folder of the game (data.pack) and the loc pack with the building texts; returns the path of the building pack.</summary>
    public static string WriteBuildingVanilla(TestGame game)
    {
        var path = WriteBuildingPack(Path.Combine(game.DataPath, "data.pack"));
        WriteLocPack(game, BuildingLocRows);
        return path;
    }

    /// <summary>Writes the wizard file of the test profile: ProfilesDirectory\&lt;profile&gt;\wizards.json.</summary>
    public static void WriteWizards(TestGame game, string json)
    {
        var folder = Path.Combine(game.Paths.ProfilesDirectory, Profile.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, WizardDefinitionLoader.FileName), json);
    }

    /// <summary>The catalog of the building wizard over the mini data, read from packs in a temp folder that is deleted afterwards.</summary>
    public static BuildingCatalog NewBuildingCatalog()
    {
        using var dir = new TempDir();
        var pack = WriteBuildingPack(dir.File("data.pack"));
        var locPack = new PackBuilder(packType: 1).AddEntry("text/db/x.loc", LocEncoder.Encode(BuildingLocRows)).WriteTo(dir.File(LocPackName));
        var schema = new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>>
        {
            ["levels_tables"] = [Levels],
            ["chains_tables"] = [Chains],
            ["variants_tables"] = [Variants],
            ["subcultures_tables"] = [Subcultures],
            ["upgrades_tables"] = [Upgrades],
        });
        var store = new TableStore(schema, ReferenceGraph.Build(schema));
        var order = new LoadOrder([new TableSource(SourceKind.Vanilla, "data.pack", pack)], new TableSource(SourceKind.Vanilla, LocPackName, locPack), []);
        var catalog = BuildingWizard.Catalog(BuildingDefinition, table => store.Get(table, order, CancellationToken.None),
            new LocStore().Get(order, CancellationToken.None));
        store.Clear();
        return catalog;
    }

    /// <summary>Writes pack\db\x_tables\a.tsv below the project folder and returns its path.</summary>
    public static string WriteProjectTable(string projectFolder, object?[][] xRows)
    {
        var path = Path.Combine(projectFolder, "pack", "db", "x_tables", "a.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, TableTsv.Write("x_tables", 0, "db/x_tables/a.tsv", X, xRows).Text);
        return path;
    }
}
