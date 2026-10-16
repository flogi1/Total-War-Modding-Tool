using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class TableVersionRuleTests
{
    private static TableDefinition Def(int version) =>
        new(version, [new FieldDefinition("key", FieldType.StringU8, "StringU8", true, null)]);

    private static GameSchema Schema(string table, params TableDefinition[] definitions) =>
        new(new Dictionary<string, IReadOnlyList<TableDefinition>> { [table] = definitions });

    private static GameTableVersions Versions(string table, int version) =>
        new(new Dictionary<string, int> { [table] = version });

    [Fact]
    public void Game_version_wins_over_a_higher_schema_version()
    {
        var result = TableVersionRule.Current(Schema("a_tables", Def(16), Def(3)), Versions("a_tables", 3), "a_tables");

        Assert.Equal(CurrentDefinitionKind.GameVersion, result.Kind);
        Assert.Equal(3, result.Definition!.Version);
        Assert.Equal(3, result.GameVersion);
    }

    [Fact]
    public void Unknown_game_version_gives_no_definition()
    {
        var result = TableVersionRule.Current(Schema("a_tables", Def(3), Def(16)), Versions("a_tables", 9), "a_tables");

        Assert.Null(result.Definition);
        Assert.Equal(CurrentDefinitionKind.SchemaLacksGameVersion, result.Kind);
        Assert.Equal(9, result.GameVersion);
    }

    [Fact]
    public void A_table_the_game_lacks_takes_the_highest_schema_version()
    {
        var result = TableVersionRule.Current(Schema("a_tables", Def(1), Def(3), Def(2)), GameTableVersions.Empty, "a_tables");

        Assert.Equal(3, result.Definition!.Version);
        Assert.Equal(CurrentDefinitionKind.SchemaNewest, result.Kind);
        Assert.Null(result.GameVersion);
    }

    [Fact]
    public void Equal_highest_versions_take_the_first()
    {
        var first = Def(2);
        var second = Def(2);

        var result = TableVersionRule.Current(Schema("a_tables", first, Def(1), second), GameTableVersions.Empty, "a_tables");

        Assert.Same(first, result.Definition);
    }

    [Fact]
    public void A_table_not_in_the_schema_has_no_definition()
    {
        var result = TableVersionRule.Current(Schema("a_tables", Def(1)), Versions("b_tables", 1), "b_tables");

        Assert.Null(result.Definition);
        Assert.Equal(CurrentDefinitionKind.NotInSchema, result.Kind);
    }

    [Fact]
    public void Versions_ignore_the_case_of_the_table_name()
    {
        Assert.Equal(3, Versions("a_tables", 3).VersionOf("A_TABLES"));
        Assert.Null(Versions("a_tables", 3).VersionOf("b_tables"));
    }

    [Fact]
    public void Mismatch_detail_names_both_versions()
    {
        Assert.Equal(
            "the table file has version 16, the game uses version 3; the game crashes on this file. Delete the file or create the table again.",
            TableVersionRule.MismatchDetail(Versions("a_tables", 3), "a_tables", 16));
    }

    [Fact]
    public void No_mismatch_for_the_game_version_or_a_table_the_game_lacks()
    {
        Assert.Null(TableVersionRule.MismatchDetail(Versions("a_tables", 3), "a_tables", 3));
        Assert.Null(TableVersionRule.MismatchDetail(GameTableVersions.Empty, "a_tables", 16));
    }

    [Fact]
    public void SameAs_compares_content()
    {
        Assert.True(Versions("a_tables", 3).SameAs(Versions("A_TABLES", 3)));
        Assert.False(Versions("a_tables", 3).SameAs(Versions("a_tables", 4)));
        Assert.False(Versions("a_tables", 3).SameAs(GameTableVersions.Empty));
    }
}
