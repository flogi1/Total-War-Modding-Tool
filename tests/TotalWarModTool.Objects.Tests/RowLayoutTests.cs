using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.TestSchema;

namespace TotalWarModTool.Objects.Tests;

public class RowLayoutTests
{
    private static TableDefinition Def(int version, params FieldDefinition[] fields) => new(version, fields);

    [Fact]
    public void Fields_are_mapped_by_name_and_missing_ones_get_defaults()
    {
        var target = Def(2, Field("key"), Field("extra", FieldType.I32), Field("note"));
        var source = Def(1, Field("note"), Field("key"));

        var mapping = RowLayout.MapFields(target, source);

        Assert.Equal([1, -1, 0], mapping!);
        Assert.Equal(["k", 0, "n"], RowLayout.Remap(target, source, mapping!, ["n", "k"]));
    }

    [Fact]
    public void A_changed_type_goes_through_its_text()
    {
        var target = Def(2, Field("n", FieldType.I32));
        var source = Def(1, Field("n"));
        var mapping = RowLayout.MapFields(target, source)!;

        Assert.Equal([12], RowLayout.Remap(target, source, mapping, ["12"]));
        Assert.Equal([0], RowLayout.Remap(target, source, mapping, ["abc"]));
    }

    [Fact]
    public void Identity_is_recognised_only_for_equal_layouts()
    {
        var a = Def(1, Field("key"), Field("n", FieldType.I32));
        var same = Def(1, Field("key"), Field("n", FieldType.I32));
        var swapped = Def(1, Field("n", FieldType.I32), Field("key"));
        var retyped = Def(1, Field("key"), Field("n"));

        Assert.True(RowLayout.IsIdentity(RowLayout.MapFields(a, same)!, a, same));
        Assert.False(RowLayout.IsIdentity(RowLayout.MapFields(a, swapped)!, a, swapped));
        Assert.False(RowLayout.IsIdentity(RowLayout.MapFields(a, retyped)!, a, retyped));
    }
}
