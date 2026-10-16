using System.Windows;
using System.Windows.Controls;
using ParadoxModTool.Settings;
using Xunit;

namespace ParadoxModTool.Shell.Tests;

public sealed class ToolbarPositionGripTests
{
    [Fact]
    public void MenuListsFourPositionsWithTheCurrentOneChecked() => Sta.Run(() =>
    {
        var grip = new ToolbarPositionGrip(() => "Left");
        var items = grip.BuildMenu().Items.OfType<MenuItem>().ToList();
        Assert.Equal(new[] { "Top", "Left", "Right", "Bottom" }, items.Select(i => (string)i.Tag));
        Assert.Equal(
            new[] { SettingsTexts.ToolbarPositionTop, SettingsTexts.ToolbarPositionLeft, SettingsTexts.ToolbarPositionRight, SettingsTexts.ToolbarPositionBottom },
            items.Select(i => (string)i.Header));
        Assert.Equal(new[] { false, true, false, false }, items.Select(i => i.IsChecked));
    });

    [Fact]
    public void ChoosingAPositionRaisesTheEvent() => Sta.Run(() =>
    {
        var grip = new ToolbarPositionGrip(() => "Top");
        string? chosen = null;
        grip.PositionChosen += (_, value) => chosen = value;
        var bottom = grip.BuildMenu().Items.OfType<MenuItem>().Single(i => (string)i.Tag == "Bottom");
        bottom.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, bottom));
        Assert.Equal("Bottom", chosen);
        Assert.False(grip.Focusable);
    });
}
