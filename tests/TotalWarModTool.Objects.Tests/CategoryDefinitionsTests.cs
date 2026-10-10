namespace TotalWarModTool.Objects.Tests;

public class CategoryDefinitionsTests
{
    private const string ValidFile = """
        {
          "schemaVersion": 1,
          "rootTable": "building_levels_tables",
          "titleKey": "objects.category.building",
          "sections": [
            { "table": "building_effects_junction_tables", "titleKey": "objects.section.effects", "mode": "list" },
            { "table": "building_level_required_technology_junctions_tables", "titleKey": "objects.section.requirements",
              "mode": "sentence", "pattern": "requires technology {technology_key}" },
            { "table": "cai_construction_system_synergies_tables", "hidden": true }
          ]
        }
        """;

    private static CategoryLoadResult Load(TempDir tmp, params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
        {
            File.WriteAllText(tmp.File(name), content);
        }

        return CategoryDefinitions.LoadAll(tmp.Path);
    }

    [Fact]
    public void A_valid_file_is_loaded_with_defaults()
    {
        using var tmp = new TempDir();

        var result = Load(tmp, ("building.json", ValidFile));

        Assert.Empty(result.Problems);
        var category = result.ByRootTable["building_levels_tables"];
        Assert.Equal("objects.category.building", category.TitleKey);
        Assert.Equal(3, category.Sections.Count);
        Assert.Equal(SectionMode.List, category.Sections[0].Mode);
        Assert.Equal("objects.section.effects", category.Sections[0].TitleKey);
        Assert.Equal(SectionMode.Sentence, category.Sections[1].Mode);
        Assert.Equal("requires technology {technology_key}", category.Sections[1].Pattern);
        Assert.True(category.Sections[2].Hidden);
        Assert.Equal(SectionMode.List, category.Sections[2].Mode);
        Assert.Null(category.Sections[2].TitleKey);
        Assert.All(category.Sections, rule => Assert.Null(rule.FieldName));
        Assert.False(category.Sections[0].Hidden);
    }

    [Theory]
    [InlineData("no_root.json", """{ "schemaVersion": 1, "titleKey": "t" }""")]
    [InlineData("version2.json", """{ "schemaVersion": 2, "rootTable": "a_tables", "titleKey": "t" }""")]
    [InlineData("sections_object.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": {} }""")]
    [InlineData("bad_mode.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "mode": "foo" } ] }""")]
    [InlineData("no_pattern.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "mode": "sentence" } ] }""")]
    [InlineData("open_brace.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "mode": "sentence", "pattern": "needs {tech" } ] }""")]
    [InlineData("adjacent.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "mode": "sentence", "pattern": "{a}{b}" } ] }""")]
    [InlineData("no_table.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "mode": "list" } ] }""")]
    [InlineData("hidden_text.json", """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "hidden": "yes" } ] }""")]
    [InlineData("array_root.json", "[]")]
    [InlineData("broken.json", "{")]
    public void Missing_fields_wrong_types_and_schema_versions_are_problems(string name, string content)
    {
        using var tmp = new TempDir();

        var result = Load(tmp, (name, content));

        Assert.Empty(result.ByRootTable);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(name, problem.FileName);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public void Adjacent_placeholders_are_rejected_with_a_detail()
    {
        using var tmp = new TempDir();

        var result = Load(tmp, ("adjacent.json",
            """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "mode": "sentence", "pattern": "needs {a}{b}" } ] }"""));

        var problem = Assert.Single(result.Problems);
        Assert.Contains("two placeholders without text between them", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholders_with_text_between_them_are_accepted()
    {
        using var tmp = new TempDir();

        var result = Load(tmp, ("ok.json",
            """{ "schemaVersion": 1, "rootTable": "a_tables", "titleKey": "t", "sections": [ { "table": "x", "mode": "sentence", "pattern": "{a} to {b}" } ] }"""));

        Assert.Empty(result.Problems);
        Assert.Single(result.ByRootTable);
    }

    [Fact]
    public void A_second_file_for_the_same_root_table_is_a_problem()
    {
        using var tmp = new TempDir();

        var result = Load(tmp, ("b.json", ValidFile.Replace("objects.category.building", "second")), ("a.json", ValidFile));

        Assert.Single(result.ByRootTable);
        Assert.Equal("objects.category.building", result.ByRootTable["building_levels_tables"].TitleKey);
        var problem = Assert.Single(result.Problems);
        Assert.Equal("b.json", problem.FileName);
    }

    [Fact]
    public void A_broken_file_does_not_hide_the_good_ones()
    {
        using var tmp = new TempDir();

        var result = Load(tmp, ("a.json", "{"), ("b.json", ValidFile));

        Assert.Single(result.ByRootTable);
        Assert.Equal("a.json", Assert.Single(result.Problems).FileName);
    }

    [Fact]
    public void A_missing_folder_gives_an_empty_result()
    {
        using var tmp = new TempDir();

        var result = CategoryDefinitions.LoadAll(tmp.File("nope"));

        Assert.Empty(result.ByRootTable);
        Assert.Empty(result.Problems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_folder_gives_an_empty_result(string folder)
    {
        var result = CategoryDefinitions.LoadAll(folder);

        Assert.Empty(result.ByRootTable);
        Assert.Empty(result.Problems);
    }
}
