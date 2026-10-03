namespace TotalWarModTool.Schema.Tests;

public class RonParserTests
{
    private static RonValue Parse(string text) { Assert.True(RonParser.TryParse(text, out var v, out var e), e?.Message); return v!; }

    [Fact] public void Parses_structs_tuples_maps_and_lists()
    {
        var root = (RonStruct)Parse("""( a: 1, b: "x", c: [true, false,], d: { "k": -2.5, }, e: ("p", "q"), f: (), )""");
        Assert.Equal(new RonNumber("1"), root.Fields["a"]);
        Assert.Equal(new RonString("x"), root.Fields["b"]);
        Assert.Equal([new RonBool(true), new RonBool(false)], ((RonList)root.Fields["c"]).Items);
        var entry = Assert.Single(((RonMap)root.Fields["d"]).Entries);
        Assert.Equal(new RonString("k"), entry.Key);
        Assert.Equal(new RonNumber("-2.5"), entry.Value);
        Assert.Equal([new RonString("p"), new RonString("q")], ((RonTuple)root.Fields["e"]).Items);
        Assert.Empty(((RonStruct)root.Fields["f"]).Fields);
    }

    [Fact] public void Parses_identifiers_with_and_without_payload()
    {
        var list = (RonList)Parse("""[None, StringU8, Some("x"), Some(("t", "c")), SequenceU32((version: 0))]""");
        Assert.Equal(new RonIdent("None", null), list.Items[0]);
        Assert.Equal(new RonIdent("StringU8", null), list.Items[1]);
        Assert.Equal(new RonString("x"), Assert.Single(((RonTuple)((RonIdent)list.Items[2]).Payload!).Items));
        var reference = (RonTuple)Assert.Single(((RonTuple)((RonIdent)list.Items[3]).Payload!).Items);
        Assert.Equal([new RonString("t"), new RonString("c")], reference.Items);
        Assert.Equal("SequenceU32", ((RonIdent)list.Items[4]).Name);
    }

    [Fact] public void Parses_escapes_and_comments()
    {
        var s = (RonString)Parse("// header\n\"a\\\"b\\\\c\\n\\u{e9}\" // tail");
        Assert.Equal("a\"b\\c\né", s.Value);
    }

    [Theory]
    [InlineData("(a: )", 1, 5)]
    [InlineData("(a: 1", 1, 6)]
    [InlineData("[1, 2] x", 1, 8)]
    [InlineData("\"open", 1, 6)]
    [InlineData("(\n  a: 1,\n  b: ?\n)", 3, 6)]
    public void Reports_errors_with_position(string text, int line, int column)
    {
        Assert.False(RonParser.TryParse(text, out var value, out var error));
        Assert.Null(value);
        Assert.Equal((line, column), (error!.Line, error.Column));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   // only a comment")]
    [InlineData("\"bad \\q escape\"")]
    [InlineData("\"\\u{}\"")]
    [InlineData("\"\\u{d800}\"")]
    [InlineData("\"\\u{1234567}\"")]
    [InlineData("\"\\u00e9\"")]
    [InlineData("-")]
    [InlineData("{ \"k\" }")]
    [InlineData("(a: 1 b: 2)")]
    [InlineData("Some(")]
    public void Malformed_input_is_an_error_not_an_exception(string text)
    {
        Assert.False(RonParser.TryParse(text, out var value, out var error));
        Assert.Null(value);
        Assert.NotNull(error);
        Assert.True(error.Line >= 1 && error.Column >= 1);
    }

    [Fact] public void Nesting_up_to_the_limit_parses_and_one_more_fails()
    {
        string Nested(int n) => new string('[', n) + new string(']', n);
        Assert.True(RonParser.TryParse(Nested(RonParser.MaxDepth), out _, out _));
        Assert.False(RonParser.TryParse(Nested(RonParser.MaxDepth + 1), out _, out _));
        Assert.False(RonParser.TryParse(string.Concat(Enumerable.Repeat("A(", 100_000)), out _, out _));
        Assert.False(RonParser.TryParse(string.Concat(Enumerable.Repeat("{1:", 100_000)), out _, out _));
    }

    [Fact] public void Deep_nesting_is_an_error_not_a_stack_overflow()
    {
        Assert.False(RonParser.TryParse(new string('[', 100_000), out _, out var error));
        Assert.NotNull(error);
    }
}
