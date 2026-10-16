using System.Windows.Media;
using System.Windows.Shapes;
using ParadoxModTool.Settings;
using Xunit;

namespace ParadoxModTool.Shell.Tests;

public sealed class ToolIconsTests
{
    public static IEnumerable<object[]> AllIds() =>
        TileCatalog.DefaultOrder.Concat(ToolbarCatalog.All.Select(i => i.Id)).Distinct().Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AllIds))]
    public void EveryCatalogIdHasADrawableIcon(string id) => Sta.Run(() =>
    {
        Assert.True(ToolIcons.Has(id));
        var geometry = Geometry.Parse(ToolIcons.PathData(id)!);
        var bounds = geometry.Bounds;
        Assert.True(bounds.Width > 4 && bounds.Height > 4, $"{id}: {bounds}");
        Assert.True(bounds.Left >= 2 && bounds.Top >= 2 && bounds.Right <= 22 && bounds.Bottom <= 22, $"{id}: {bounds}");
    });

    [Fact]
    public void UnknownIdHasNoPathButCreatesAFallback() => Sta.Run(() =>
    {
        Assert.False(ToolIcons.Has("nope"));
        Assert.Null(ToolIcons.PathData("nope"));
        var element = ToolIcons.Create("nope", 16, "Theme.Text");
        Assert.Equal(16, element.Width);
        Assert.Equal("nope", element.Tag);
        Assert.Single(Tree.Descendants<Path>(element));
    });

    [Fact]
    public void CreateUsesTheRequestedSizeAndBrushKey() => Sta.Run(() =>
    {
        var element = ToolIcons.Create(ToolbarItemIds.Save, 20, "Theme.Accent");
        var path = Tree.Descendants<Path>(element).Single();
        Assert.Equal(20, element.Height);
        Assert.Null(path.Fill);
        Assert.Equal(1.6, path.StrokeThickness);
    });
}
