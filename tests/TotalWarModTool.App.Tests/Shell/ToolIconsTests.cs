using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using TotalWarModTool.App.Shell;
using TotalWarModTool.Settings;
using Xunit;

namespace TotalWarModTool.App.Tests.Shell;

public sealed class ToolIconsTests
{
    public static IEnumerable<object[]> AllIds() =>
        TileCatalog.DefaultOrder.Concat(ToolbarCatalog.All).Distinct().Select(id => new object[] { id });

    [Fact]
    public void Every_catalog_id_has_a_drawable_icon() =>
        Assert.All(TileCatalog.DefaultOrder.Concat(ToolbarCatalog.All), id => Assert.True(ToolIcons.Has(id), id));

    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_catalog_id_icon_fits_the_24_grid(string id) => Sta.Run(() =>
    {
        Assert.True(ToolIcons.Has(id));
        var bounds = Geometry.Parse(ToolIcons.PathData(id)!).Bounds;
        Assert.True(bounds.Width > 4 && bounds.Height > 4, $"{id}: {bounds}");
        Assert.True(bounds.Left >= 2 && bounds.Top >= 2 && bounds.Right <= 22 && bounds.Bottom <= 22, $"{id}: {bounds}");
    });

    [Fact]
    public void Unknown_id_has_no_path_but_creates_a_fallback() => Sta.Run(() =>
    {
        Assert.False(ToolIcons.Has("nope"));
        Assert.Null(ToolIcons.PathData("nope"));
        var element = ToolIcons.Create("nope", 16, "Theme.Text");
        Assert.Equal(16, element.Width);
        Assert.Equal("nope", element.Tag);
        Assert.Single(Tree.Descendants<Path>(element));
    });

    [Fact]
    public void Create_uses_the_requested_size_and_brush_key() => Sta.Run(() =>
    {
        var element = ToolIcons.Create(ToolbarItemIds.Save, 20, "Theme.Accent");
        var path = Tree.Descendants<Path>(element).Single();
        Assert.Equal(20, element.Height);
        Assert.Null(path.Fill);
        Assert.Equal(1.6, path.StrokeThickness);
    });
}
