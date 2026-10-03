namespace TotalWarModTool.Schema.Tests;

public class SchemaSourceTests
{
    private const string Good = """( definitions: { "a_tables": [ ( version: 1, fields: [ (name: "key", field_type: StringU8, is_key: true) ] ) ] } )""";

    [Fact] public void Override_wins_when_it_loads()
    {
        using var shipped = new TempDir(); using var over = new TempDir();
        File.WriteAllText(shipped.File("s.ron"), Good);
        File.WriteAllText(over.File("s.ron"), Good.Replace("a_tables", "b_tables"));
        var r = SchemaSource.Resolve(shipped.Path, over.Path, "s.ron");
        Assert.Equal((SchemaOrigin.Override, over.File("s.ron")), (r.Origin, r.Path));
        Assert.True(r.Schema!.Tables.ContainsKey("b_tables"));
        Assert.Equal(File.GetLastWriteTimeUtc(over.File("s.ron")), r.FileDateUtc);
        Assert.Null(r.ShippedAttempt);
    }

    [Theory]
    [InlineData(null, SchemaProblem.NotFound)]          // override folder has no such file
    [InlineData("( broken", SchemaProblem.InvalidSyntax)]
    public void Falls_back_to_shipped_and_keeps_the_override_problem(string? overrideContent, SchemaProblem expected)
    {
        using var shipped = new TempDir(); using var over = new TempDir();
        File.WriteAllText(shipped.File("s.ron"), Good);
        if (overrideContent != null) File.WriteAllText(over.File("s.ron"), overrideContent);
        var r = SchemaSource.Resolve(shipped.Path, over.Path, "s.ron");
        Assert.Equal((SchemaOrigin.Shipped, shipped.File("s.ron"), expected), (r.Origin, r.Path, r.OverrideAttempt!.Problem));
        Assert.NotNull(r.Schema);
    }

    [Fact] public void No_override_folder_uses_shipped_without_an_attempt()
    {
        using var shipped = new TempDir();
        File.WriteAllText(shipped.File("s.ron"), Good);
        var r = SchemaSource.Resolve(shipped.Path, null, "s.ron");
        Assert.Equal(SchemaOrigin.Shipped, r.Origin); Assert.Null(r.OverrideAttempt);
    }

    [Fact] public void Nothing_usable_gives_origin_None()
    {
        using var shipped = new TempDir();
        var r = SchemaSource.Resolve(shipped.Path, null, "s.ron");
        Assert.Equal((SchemaOrigin.None, (GameSchema?)null, SchemaProblem.NotFound), (r.Origin, r.Schema, r.ShippedAttempt!.Problem));
    }
}
