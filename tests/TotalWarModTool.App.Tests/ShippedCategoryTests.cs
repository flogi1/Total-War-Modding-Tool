using System.Reflection;
using System.Text.RegularExpressions;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

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
}
