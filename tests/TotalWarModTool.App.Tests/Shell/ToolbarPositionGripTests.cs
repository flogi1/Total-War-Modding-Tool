using System.Windows;
using System.Windows.Controls;
using TotalWarModTool.App.Shell;
using TotalWarModTool.Settings;
using Xunit;

namespace TotalWarModTool.App.Tests.Shell;

public sealed class ToolbarPositionGripTests
{
    [Fact]
    public void Menu_lists_four_positions_with_the_current_one_checked() => Sta.Run(() =>
    {
        var grip = new ToolbarPositionGrip(() => ToolbarPosition.Right, p => "L:" + p, "tip");
        var items = grip.BuildMenu().Items.OfType<MenuItem>().ToList();
        Assert.Equal(["L:Top", "L:Left", "L:Right", "L:Bottom"], items.Select(i => i.Header));
        Assert.Equal([false, false, true, false], items.Select(i => i.IsChecked));
    });

    [Fact]
    public void Choosing_a_position_raises_the_event() => Sta.Run(() =>
    {
        var grip = new ToolbarPositionGrip(() => ToolbarPosition.Top, p => p.ToString(), "tip");
        ToolbarPosition? chosen = null;
        grip.PositionChosen += (_, value) => chosen = value;
        var bottom = grip.BuildMenu().Items.OfType<MenuItem>().Single(i => (ToolbarPosition)i.Tag == ToolbarPosition.Bottom);
        bottom.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, bottom));
        Assert.Equal(ToolbarPosition.Bottom, chosen);
        Assert.False(grip.Focusable);
        Assert.Equal("tip", grip.ToolTip);
    });
}
