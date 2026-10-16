using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ShippedCategoryTests
{
    private const string RootTable = "building_levels_tables";

    private static string Repo => RepoRoot.Find();

    private static CategoryDefinition LoadBuilding()
    {
        var result = CategoryDefinitions.LoadAll(Path.Combine(Repo, "profiles", "warhammer_3", "objects"));
        Assert.Empty(result.Problems);
        return result.ByRootTable[RootTable];
    }

    [Fact] public void The_building_category_loads_without_problems()
    {
        var result = CategoryDefinitions.LoadAll(Path.Combine(Repo, "profiles", "warhammer_3", "objects"));
        Assert.Empty(result.Problems);
        Assert.True(result.ByRootTable.TryGetValue(RootTable, out var category));
        Assert.Equal(9, category.Sections.Count);
    }

    [Fact] public void Every_section_table_and_field_of_the_category_exists_in_the_shipped_schema()
    {
        var category = LoadBuilding();
        var loaded = SchemaLoader.Load(Path.Combine(Repo, "schemas", "schema_wh3.ron"));
        Assert.NotNull(loaded.Schema);
        var graph = ReferenceGraph.Build(loaded.Schema, GameTableVersions.Empty);
        var incoming = graph.Incoming(RootTable);
        var placeholder = new Regex(@"\{([^{}]+)\}");

        foreach (var rule in category.Sections)
        {
            Assert.True(loaded.Schema.Tables.ContainsKey(rule.Table), "table " + rule.Table);
            if (rule.FieldName is not null)
            {
                Assert.True(incoming.Any(r => r.SourceTable == rule.Table && r.FieldName == rule.FieldName),
                    $"{rule.Table}.{rule.FieldName} does not reference {RootTable}");
            }

            if (rule.Pattern is null)
            {
                continue;
            }

            var names = graph.Current(rule.Table)!.Fields.Select(f => f.Name).ToHashSet();
            foreach (Match match in placeholder.Matches(rule.Pattern))
            {
                Assert.True(names.Contains(match.Groups[1].Value), $"{rule.Table} has no field {match.Groups[1].Value}");
            }
        }
    }

    [Fact] public void Every_title_key_of_the_category_is_a_text_of_the_app()
    {
        var category = LoadBuilding();
        var texts = new AppTexts(new UiText(Path.Combine(Repo, "locales")));
        foreach (var property in typeof(AppTexts).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            {
                property.GetValue(texts);
            }
        }

        var keys = category.Sections.Select(s => s.TitleKey).Append(category.TitleKey).OfType<string>().ToList();
        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(texts.RequestedKeys.ContainsKey(key), key));
    }

    [Fact] public void Theme_tokens_for_provenance_exist_in_both_themes()
    {
        foreach (var key in new[] { "ObjectVanilla", "ObjectMod", "ObjectProject", "ObjectLocText" })
        {
            var token = Assert.Single(ThemePalette.All, t => t.Key == key);
            Assert.Matches("^#[0-9A-F]{6}$", token.Light);
            Assert.Matches("^#[0-9A-F]{6}$", token.Dark);
        }
    }

    [RealDataFact] public void Shipped_categories_and_wizards_match_the_game_versions()
    {
        var loaded = SchemaLoader.Load(Path.Combine(Repo, "schemas", "schema_wh3.ron"));
        Assert.NotNull(loaded.Schema);
        var profile = GameProfileLoader.LoadAll(Path.Combine(Repo, "profiles")).Profiles.Single(p => p.Id == "warhammer_3");
        var catalog = PackCatalog.Scan(profile, RealData.Wh3Dir!, RealData.Wh3Workshop, null, CancellationToken.None);
        var versions = GameTableVersionsReader.Read(catalog.Packs, CancellationToken.None);
        Assert.NotNull(versions.Versions);
        Assert.Empty(versions.Problems);
        var graph = ReferenceGraph.Build(loaded.Schema, versions.Versions);

        var pairs = new List<(string Source, string Table, string Field)>();
        var categories = CategoryDefinitions.LoadAll(Path.Combine(Repo, "profiles", "warhammer_3", "objects"));
        Assert.Empty(categories.Problems);
        foreach (var category in categories.ByRootTable.Values)
        {
            foreach (var rule in category.Sections)
            {
                if (rule.FieldName is not null)
                {
                    pairs.Add(("category " + category.RootTable, rule.Table, rule.FieldName));
                }

                if (rule.Pattern is null)
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(rule.Pattern, @"\{([^{}]+)\}"))
                {
                    pairs.Add(("category " + category.RootTable, rule.Table, match.Groups[1].Value));
                }
            }
        }

        using var wizards = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "profiles", "warhammer_3", "wizards.json")));
        var building = wizards.RootElement.GetProperty("building");
        string Text(string name) => building.GetProperty(name).GetString()!;
        pairs.Add(("wizards building", Text("levelTable"), Text("chainField")));
        pairs.Add(("wizards building", Text("levelTable"), Text("levelField")));
        pairs.Add(("wizards building", Text("upgradeTable"), Text("upgradeFromField")));
        pairs.Add(("wizards building", Text("upgradeTable"), Text("upgradeToField")));
        pairs.Add(("wizards building", Text("variantTable"), Text("variantBuildingField")));
        pairs.Add(("wizards building", Text("variantTable"), Text("variantCultureField")));
        pairs.Add(("wizards building", Text("variantTable"), Text("variantSubcultureField")));
        pairs.Add(("wizards building", Text("subcultureTable"), Text("subcultureCultureField")));
        // The nameText columns are loc columns (loc keys), not fields of the table definition: only their tables are checked.
        var tables = new HashSet<string>(pairs.Select(p => p.Table), StringComparer.Ordinal)
        {
            Text("chainTable"),
            building.GetProperty("nameText").GetProperty("table").GetString()!,
            wizards.RootElement.GetProperty("trait").GetProperty("nameText").GetProperty("table").GetString()!,
            wizards.RootElement.GetProperty("trait").GetProperty("traitTable").GetString()!,
        };
        foreach (var table in tables)
        {
            Assert.NotNull(graph.Current(table));
        }

        var missing = pairs
            .Where(p => graph.Current(p.Table)!.Fields.All(f => !string.Equals(f.Name, p.Field, StringComparison.Ordinal)))
            .Select(p => $"{p.Source}: {p.Table}.{p.Field} (game version {versions.Versions.VersionOf(p.Table)})")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Fields missing in the game version: " + string.Join("; ", missing));
    }
}
