using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace ParadoxModTool.Shell.Tests;

public sealed class ToolbarBuilderTests
{
    private static Dictionary<string, FrameworkElement> Elements(params string[] ids) =>
        ids.ToDictionary(id => id, id => (FrameworkElement)new Button { Content = id });

    [Fact]
    public void PlacesVisibleItemsAndCollapsesSeparators() => Sta.Run(() =>
    {
        var elements = Elements("new", "open", "undo");
        var order = new[] { "new", "open", "sep:1", "sep:2", "undo", "sep:3" };
        var panel = ToolbarBuilder.Build(new ToolbarBuild(order, _ => true, id => elements.GetValueOrDefault(id), Vertical: false));

        Assert.IsType<WrapPanel>(panel);
        Assert.Equal(new[] { "new", "open", "sep:1", "undo" }, ToolbarBuilder.PlacedIds(panel));
        Assert.Same(elements["undo"], panel.Children[3]);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparatorsKeepTheirOwnMarginAndItemsGetTwo(bool vertical) => Sta.Run(() =>
    {
        var elements = Elements("new", "undo");
        var order = new[] { "new", "sep:1", "undo" };
        var panel = ToolbarBuilder.Build(new ToolbarBuild(order, _ => true, id => elements.GetValueOrDefault(id), vertical, MoveNextTo: (_, _) => { }));

        var separator = ((ReorderDragHost)panel.Children[1]).Content;
        Assert.Equal(ToolbarItemView.Separator(vertical).Margin, separator.Margin);
        Assert.Equal(new Thickness(2), elements["undo"].Margin);
    });

    [Fact]
    public void HiddenItemsAreLeftOutAndUnknownIdsSkipped() => Sta.Run(() =>
    {
        var elements = Elements("new", "undo");
        var order = new[] { "new", "sep:1", "undo", "sep:2", "ghost" };
        var panel = ToolbarBuilder.Build(new ToolbarBuild(order, id => id != "undo", id => elements.GetValueOrDefault(id), Vertical: false));

        Assert.Equal(new[] { "new" }, ToolbarBuilder.PlacedIds(panel));
    });

    [Fact]
    public void VerticalBuildUsesANarrowStackPanel() => Sta.Run(() =>
    {
        var elements = Elements("new");
        var panel = ToolbarBuilder.Build(new ToolbarBuild(new[] { "new" }, _ => true, id => elements[id], Vertical: true));
        Assert.IsType<StackPanel>(panel);
        Assert.Equal(Orientation.Vertical, ((StackPanel)panel).Orientation);
        Assert.Equal(ToolbarBuilder.VerticalWidth, panel.Width);
    });

    [Fact]
    public void RebuildMovesElementsOutOfTheOldPanel() => Sta.Run(() =>
    {
        var elements = Elements("new", "open");
        var first = ToolbarBuilder.Build(new ToolbarBuild(new[] { "new", "open" }, _ => true, id => elements[id], Vertical: false, MoveNextTo: (_, _) => { }));
        var second = ToolbarBuilder.Build(new ToolbarBuild(new[] { "open", "new" }, _ => true, id => elements[id], Vertical: true));

        Assert.Empty(first.Children);
        Assert.Equal(new[] { "open", "new" }, ToolbarBuilder.PlacedIds(second));
        Assert.Same(elements["open"], second.Children[0]);
    });

    [Fact]
    public void MoveNextToWrapsEveryEntryInADragHostAndGripComesFirst() => Sta.Run(() =>
    {
        var elements = Elements("new", "undo");
        var grip = new Button { Content = "⋮" };
        var moves = new List<(string, string)>();
        var panel = ToolbarBuilder.Build(new ToolbarBuild(new[] { "new", "sep:1", "undo" }, _ => true, id => elements[id], Vertical: false, grip, (a, b) => moves.Add((a, b))));

        Assert.Same(grip, panel.Children[0]);
        Assert.Equal("grip", grip.Tag);
        var hosts = panel.Children.OfType<ReorderDragHost>().ToList();
        Assert.Equal(new[] { "new", "sep:1", "undo" }, hosts.Select(h => h.Id));
        Assert.True(hosts[2].AcceptsDrop("sep:1"));
        Assert.False(hosts[2].AcceptsDrop("tileX"));
    });

    [Fact]
    public void SecondBuildWithChangedVisibilityShowsTheNewItem() => Sta.Run(() =>
    {
        var elements = Elements("search", "guiDataHelper", "format");
        var order = new[] { "search", "guiDataHelper", "sep:1", "format" };
        var offered = false;
        Func<string, bool> visible = id => id != "guiDataHelper" || offered;
        ToolbarBuild Build() => new(order, visible, id => elements.GetValueOrDefault(id), Vertical: true);

        Assert.Equal(new[] { "search", "sep:1", "format" }, ToolbarBuilder.PlacedIds(ToolbarBuilder.Build(Build())));
        offered = true;
        Assert.Equal(new[] { "search", "guiDataHelper", "sep:1", "format" }, ToolbarBuilder.PlacedIds(ToolbarBuilder.Build(Build())));
    });
}
