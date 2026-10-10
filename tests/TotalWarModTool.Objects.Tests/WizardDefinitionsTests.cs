using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.TestSchema;

namespace TotalWarModTool.Objects.Tests;

public class WizardDefinitionsTests : IDisposable
{
    private const string BuildingJson = """
        "building": {
          "levelTable": "building_levels_tables",
          "chainField": "chain",
          "levelField": "level",
          "chainTable": "building_chains_tables",
          "chainKeySuffix": "_chain",
          "upgradeTable": "building_upgrades_junction_tables",
          "upgradeFromField": "from",
          "upgradeToField": "to",
          "variantTable": "building_culture_variants_tables",
          "variantBuildingField": "building",
          "variantCultureField": "culture",
          "variantSubcultureField": "subculture",
          "subcultureTable": "cultures_subcultures_tables",
          "subcultureCultureField": "culture",
          "nameText": { "table": "building_culture_variants_tables", "column": "name" }
        }
        """;

    private const string TraitJson = """
        "trait": {
          "traitTable": "character_traits_tables",
          "nameText": { "table": "character_trait_levels_tables", "column": "onscreen_name" }
        }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "twmt-wizards-" + Guid.NewGuid().ToString("N"));

    public WizardDefinitionsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string content)
    {
        var path = Path.Combine(_folder, WizardDefinitionLoader.FileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_missing_file_gives_neither_definitions_nor_a_problem()
    {
        var result = WizardDefinitionLoader.Load(Path.Combine(_folder, "nothing.json"));

        Assert.Null(result.Definitions);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void A_complete_file_loads_both_wizards()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, \"nameSuffix\": \" (copy)\", " + BuildingJson + ", " + TraitJson + " }"));

        Assert.Null(result.Problem);
        var definitions = result.Definitions!;
        Assert.Equal(" (copy)", definitions.NameSuffix);
        Assert.Equal(
            new BuildingWizardDefinition("building_levels_tables", "chain", "level", "building_chains_tables", "_chain",
                "building_upgrades_junction_tables", "from", "to", "building_culture_variants_tables", "building", "culture", "subculture",
                "cultures_subcultures_tables", "culture", new TextRef("building_culture_variants_tables", "name")),
            definitions.Building);
        Assert.Equal(new TraitWizardDefinition("character_traits_tables", new TextRef("character_trait_levels_tables", "onscreen_name")), definitions.Trait);
    }

    [Fact]
    public void A_file_with_only_the_trait_wizard_has_no_building_wizard()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, " + TraitJson + " }"));

        Assert.Null(result.Problem);
        Assert.Null(result.Definitions!.Building);
        Assert.NotNull(result.Definitions.Trait);
        Assert.Equal(string.Empty, result.Definitions.NameSuffix);
    }

    [Fact]
    public void A_missing_field_makes_the_file_unusable()
    {
        var withoutChainField = BuildingJson.Replace("\"chainField\": \"chain\",", string.Empty);
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, " + withoutChainField + ", " + TraitJson + " }"));

        Assert.Null(result.Definitions);
        Assert.Equal("building.chainField is missing.", result.Problem);
    }

    [Fact]
    public void An_empty_field_makes_the_file_unusable()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, " + TraitJson.Replace("\"character_traits_tables\"", "\" \"") + " }"));

        Assert.Null(result.Definitions);
        Assert.Equal("trait.traitTable is missing.", result.Problem);
    }

    [Fact]
    public void A_missing_text_column_makes_the_file_unusable()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, " + TraitJson.Replace(", \"column\": \"onscreen_name\"", string.Empty) + " }"));

        Assert.Null(result.Definitions);
        Assert.Equal("trait.nameText.column is missing.", result.Problem);
    }

    [Fact]
    public void A_wrong_schema_version_makes_the_file_unusable()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 2, " + TraitJson + " }"));

        Assert.Null(result.Definitions);
        Assert.Equal("schemaVersion must be 1.", result.Problem);
    }

    [Fact]
    public void Broken_json_makes_the_file_unusable()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, "));

        Assert.Null(result.Definitions);
        Assert.StartsWith("Invalid JSON: ", result.Problem);
    }

    [Fact]
    public void MissingIn_names_unknown_tables_and_fields()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, " + BuildingJson + ", " + TraitJson + " }"));
        var graph = ReferenceGraph.Build(Build(
            ("building_levels_tables", 1, [Field("key", key: true), Field("chain")]),
            ("building_chains_tables", 1, [Field("key", key: true)]),
            ("building_upgrades_junction_tables", 1, [Field("FROM"), Field("to")]),
            ("building_culture_variants_tables", 1, [Field("building"), Field("culture"), Field("subculture")]),
            ("cultures_subcultures_tables", 1, [Field("culture")]),
            ("character_traits_tables", 1, [Field("key", key: true)])));

        Assert.Equal(["building_levels_tables.level"], result.Definitions!.Building!.MissingIn(graph));
        Assert.Equal(["character_trait_levels_tables"], result.Definitions.Trait!.MissingIn(graph));
    }

    [Fact]
    public void MissingIn_skips_the_fields_of_an_unknown_table()
    {
        var result = WizardDefinitionLoader.Load(Write("{ \"schemaVersion\": 1, " + BuildingJson + " }"));
        var graph = ReferenceGraph.Build(Build(("other_tables", 1, [Field("key", key: true)])));

        Assert.Equal(
            ["building_levels_tables", "building_chains_tables", "building_upgrades_junction_tables", "building_culture_variants_tables", "cultures_subcultures_tables"],
            result.Definitions!.Building!.MissingIn(graph));
    }

    [Fact]
    public void The_shipped_file_names_only_tables_and_fields_of_the_shipped_schema()
    {
        var root = RepoRoot.Find();
        var schema = SchemaLoader.Load(Path.Combine(root, "schemas", "schema_wh3.ron")).Schema!;
        var graph = ReferenceGraph.Build(schema);

        var result = WizardDefinitionLoader.Load(Path.Combine(root, "profiles", "warhammer_3", WizardDefinitionLoader.FileName));

        Assert.Null(result.Problem);
        Assert.NotNull(result.Definitions!.Building);
        Assert.NotNull(result.Definitions.Trait);
        Assert.Empty(result.Definitions.Building.MissingIn(graph));
        Assert.Empty(result.Definitions.Trait.MissingIn(graph));
    }
}
