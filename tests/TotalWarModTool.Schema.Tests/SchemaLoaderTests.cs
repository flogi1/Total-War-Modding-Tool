namespace TotalWarModTool.Schema.Tests;

public class SchemaLoaderTests
{
    private const string Sample = """
        (
            version: 5,
            definitions: {
                "units_tables": [
                    (
                        version: 2,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, default_value: None, is_reference: None, ca_order: 0, enum_values: {}),
                            (name: "faction", field_type: OptionalStringU8, is_key: false, is_reference: Some(("factions", "key"))),
                            (name: "tint", field_type: ColourRGB, is_key: false, is_reference: None),
                            (name: "extra", field_type: SequenceU32((version: 0, fields: [])), is_reference: None),
                            (name: "maybe", field_type: OptionalI32),
                        ],
                        localised_fields: [],
                    ),
                    ( version: 1, fields: [ (name: "key", field_type: StringU8, is_key: true) ] ),
                ],
            },
            patches: {},
        )
        """;

    [Fact] public void File_above_the_size_cap_is_Unreadable_and_a_file_at_the_cap_loads()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.ron"), Sample);
        var length = new FileInfo(dir.File("s.ron")).Length;
        Assert.Equal(SchemaProblem.None, SchemaLoader.Load(dir.File("s.ron"), length).Problem);
        var r = SchemaLoader.Load(dir.File("s.ron"), length - 1);
        Assert.Equal((null, SchemaProblem.Unreadable), (r.Schema, r.Problem));
        Assert.Contains("larger than", r.Detail);
    }

    [Fact] public void Loads_tables_versions_fields_and_references()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.ron"), Sample);
        var r = SchemaLoader.Load(dir.File("s.ron"));
        Assert.Equal(SchemaProblem.None, r.Problem);
        var v2 = r.Schema!.Find("UNITS_TABLES", 2)!;
        Assert.Equal(
            [new FieldDefinition("key", FieldType.StringU8, "StringU8", true, null),
             new FieldDefinition("faction", FieldType.OptionalStringU8, "OptionalStringU8", false, new TableReference("factions", "key")),
             new FieldDefinition("tint", FieldType.ColourRgb, "ColourRGB", false, null),
             new FieldDefinition("extra", FieldType.Unsupported, "SequenceU32", false, null),
             new FieldDefinition("maybe", FieldType.Unsupported, "OptionalI32", false, null)],
            v2.Fields);
        Assert.Single(r.Schema.Find("units_tables", 1)!.Fields);
        Assert.Null(r.Schema.Find("units_tables", 3));
        Assert.Null(r.Schema.Find("other_tables", 1));
    }

    [Theory]
    [InlineData("( definitions: {", SchemaProblem.InvalidSyntax)]
    [InlineData("( version: 5 )", SchemaProblem.InvalidStructure)]
    [InlineData("""( definitions: { "t_tables": [ ( version: "x", fields: [] ) ] } )""", SchemaProblem.InvalidStructure)]
    [InlineData("""( definitions: { "t_tables": [ ( version: 1, fields: [ (field_type: StringU8) ] ) ] } )""", SchemaProblem.InvalidStructure)]
    public void Broken_schema_gives_a_problem(string text, SchemaProblem expected)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.ron"), text);
        var r = SchemaLoader.Load(dir.File("s.ron"));
        Assert.Equal(expected, r.Problem);
        Assert.Null(r.Schema);
        Assert.False(string.IsNullOrWhiteSpace(r.Detail));
    }

    [Fact] public void Missing_file_is_NotFound() => Assert.Equal(SchemaProblem.NotFound, SchemaLoader.Load(@"X:\none.ron").Problem);
}
