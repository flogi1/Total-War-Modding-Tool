using static TotalWarModTool.Objects.Tests.TestSchema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects.Tests;

public class ReferenceGraphTests
{
    [Fact]
    public void Incoming_lists_every_field_that_references_the_table()
    {
        var schema = Build(
            ("a_tables", 1, [Field("key", key: true)]),
            ("b_tables", 1, [Field("owner", reference: ("a_tables", "key")), Field("x")]),
            ("c_tables", 1, [Field("left", reference: ("a_tables", "key")), Field("right", reference: ("a_tables", "key"))]));
        var graph = ReferenceGraph.Build(schema, GameTableVersions.Empty);

        Assert.Equal(
            [new IncomingReference("b_tables", "owner", 0, "key"), new IncomingReference("c_tables", "left", 0, "key"), new IncomingReference("c_tables", "right", 1, "key")],
            graph.Incoming("a_tables"));
        Assert.Empty(graph.Incoming("b_tables"));
        Assert.Empty(graph.Incoming("unknown_tables"));
    }

    private static GameTableVersions Game(params (string Table, int Version)[] versions) =>
        new(versions.ToDictionary(v => v.Table, v => v.Version));

    [Fact]
    public void Current_is_the_game_version()
    {
        var schema = Build(
            ("a_tables", 3, [Field("one"), Field("two")]),
            ("a_tables", 1, [Field("one")]));
        var graph = ReferenceGraph.Build(schema, Game(("a_tables", 1)));

        Assert.Single(graph.Current("a_tables")!.Fields);
        Assert.Null(graph.Current("nope"));
        Assert.Equal(1, graph.GameVersions.VersionOf("a_tables"));
    }

    [Fact]
    public void Current_without_game_version_is_the_highest()
    {
        var schema = Build(
            ("a_tables", 3, [Field("one"), Field("two")]),
            ("a_tables", 1, [Field("one")]));
        var graph = ReferenceGraph.Build(schema, GameTableVersions.Empty);

        Assert.Equal(2, graph.Current("a_tables")!.Fields.Count);
    }

    [Fact]
    public void A_table_whose_game_version_the_schema_lacks_is_left_out()
    {
        var schema = Build(
            ("a_tables", 1, [Field("key", key: true)]),
            ("b_tables", 1, [Field("owner", reference: ("a_tables", "key"))]));
        var graph = ReferenceGraph.Build(schema, Game(("b_tables", 2)));

        Assert.Null(graph.Current("b_tables"));
        Assert.Empty(graph.Incoming("a_tables"));
    }

    [Fact]
    public void Incoming_uses_the_fields_of_the_game_version()
    {
        var schema = Build(
            ("a_tables", 1, [Field("key", key: true)]),
            ("b_tables", 3, [Field("x")]),
            ("b_tables", 1, [Field("owner", reference: ("a_tables", "key"))]));

        var onGameVersion = ReferenceGraph.Build(schema, Game(("b_tables", 1)));
        Assert.Equal([new IncomingReference("b_tables", "owner", 0, "key")], onGameVersion.Incoming("a_tables"));

        var withoutVersions = ReferenceGraph.Build(schema, GameTableVersions.Empty);
        Assert.Empty(withoutVersions.Incoming("a_tables"));
    }

    [Fact]
    public void IsJunction_needs_exactly_two_references_and_at_most_four_fields()
    {
        var r = ("t_tables", "key");
        var schema = Build(
            ("two_refs_tables", 1, [Field("a", reference: r), Field("b", reference: r)]),
            ("four_tables", 1, [Field("a", reference: r), Field("b", reference: r), Field("c"), Field("d")]),
            ("five_tables", 1, [Field("a", reference: r), Field("b", reference: r), Field("c"), Field("d"), Field("e")]),
            ("three_refs_tables", 1, [Field("a", reference: r), Field("b", reference: r), Field("c", reference: r)]),
            ("one_ref_tables", 1, [Field("a", reference: r), Field("b")]));
        var graph = ReferenceGraph.Build(schema, GameTableVersions.Empty);

        Assert.True(graph.IsJunction("two_refs_tables"));
        Assert.True(graph.IsJunction("four_tables"));
        Assert.False(graph.IsJunction("five_tables"));
        Assert.False(graph.IsJunction("three_refs_tables"));
        Assert.False(graph.IsJunction("one_ref_tables"));
        Assert.False(graph.IsJunction("unknown_tables"));
    }

    [Fact]
    public void KeyFields_follow_the_schema_order()
    {
        var schema = Build(
            ("a_tables", 1, [Field("a", key: true), Field("b"), Field("c", key: true)]),
            ("b_tables", 1, [Field("a"), Field("b")]));
        var graph = ReferenceGraph.Build(schema, GameTableVersions.Empty);

        Assert.Equal([0, 2], graph.KeyFields("a_tables"));
        Assert.True(graph.HasObjects("a_tables"));
        Assert.Empty(graph.KeyFields("b_tables"));
        Assert.False(graph.HasObjects("b_tables"));
        Assert.Empty(graph.KeyFields("unknown_tables"));
    }

    [Fact]
    public void ResolveTableName_appends_the_suffix_only_when_the_schema_knows_it()
    {
        var graph = ReferenceGraph.Build(Build(("a_tables", 1, [Field("key", key: true)])), GameTableVersions.Empty);

        Assert.Equal("a_tables", graph.ResolveTableName("a"));
        Assert.Equal("zzz", graph.ResolveTableName("zzz"));
        Assert.Equal("a_tables", graph.ResolveTableName("A"));
    }

    [Fact]
    public void Incoming_resolves_references_without_the_suffix()
    {
        var schema = Build(
            ("a_tables", 1, [Field("key", key: true)]),
            ("b_tables", 1, [Field("owner", reference: ("a", "key"))]));
        var graph = ReferenceGraph.Build(schema, GameTableVersions.Empty);

        var expected = new[] { new IncomingReference("b_tables", "owner", 0, "key") };
        Assert.Equal(expected, graph.Incoming("a_tables"));
        Assert.Equal(expected, graph.Incoming("a"));
    }
}
