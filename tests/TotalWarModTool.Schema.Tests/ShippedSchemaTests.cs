namespace TotalWarModTool.Schema.Tests;

public class ShippedSchemaTests
{
    [Fact] public void Shipped_wh3_schema_loads_and_has_the_expected_shape()
    {
        var r = SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron"));
        Assert.Equal(SchemaProblem.None, r.Problem);
        var fields = r.Schema!.Tables.Values.SelectMany(defs => defs).SelectMany(d => d.Fields).ToList();
        Assert.True(r.Schema.Tables.Count > 1500);
        Assert.True(fields.Count(f => f.Reference != null) > 5000);
        Assert.All(fields, f => Assert.False(string.IsNullOrEmpty(f.Name)));
        var unsupported = fields.Where(f => f.Type == FieldType.Unsupported).Select(f => f.RawType).Distinct().ToList();
        Assert.True(unsupported.Count <= 3, string.Join(", ", unsupported));
        Assert.True(File.Exists(Path.Combine(RepoRoot.Find(), "schemas", "LICENSE")));
    }
}
