using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TotalWarModTool.App.Shell;
using TotalWarModTool.Settings;
using Xunit;

namespace TotalWarModTool.App.Tests.Shell;

public sealed class TileBarTests
{
    private static TileSource Source(Func<string, bool>? offered = null, Func<string, bool>? enabled = null, Func<string, ContextMenu?>? menu = null) =>
        new(id => "L:" + id, id => "T:" + id, offered ?? (_ => true), enabled ?? (_ => true), _ => "disabled", menu ?? (_ => null));

    private static TileBar Bar(GlobalSettings settings, TileSource? source = null)
    {
        var bar = new TileBar();
        bar.Bind(settings, source ?? Source());
        return bar;
    }

    private static TextBlock Label(Button tile) => Tree.Descendants<TextBlock>(tile).Single(t => t.Name == "TileLabel");

    private static Border Badge(Button tile) => Tree.Descendants<Border>(tile).Single(b => b.Name == "TileBadge");

    [Fact]
    public void Default_session_shows_the_default_tiles_in_catalog_order() => Sta.Run(() =>
    {
        var bar = Bar(new GlobalSettings());
        Assert.Equal(TileCatalog.DefaultOrder, bar.VisibleIds);
        Assert.Equal("L:" + TileIds.NewBuilding, Label(bar.TileButton(TileIds.NewBuilding)).Text);
        Assert.Equal("T:" + TileIds.NewBuilding, bar.TileButton(TileIds.NewBuilding).ToolTip);
    });

    [Fact]
    public void Hidden_and_not_offered_tiles_are_left_out_but_keep_their_place() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        settings.SetTileVisible(TileIds.NewBuilding, false);
        var bar = Bar(settings, Source(offered: id => id != TileIds.NewTrait));

        Assert.DoesNotContain(TileIds.NewBuilding, bar.VisibleIds);
        Assert.DoesNotContain(TileIds.NewTrait, bar.VisibleIds);
        Assert.False(bar.IsVisibleTile(TileIds.NewBuilding));
        settings.SetTileVisible(TileIds.NewBuilding, true);
        Assert.Equal(0, bar.VisibleIds.ToList().IndexOf(TileIds.NewBuilding));
    });

    [Fact]
    public void Sizes_follow_default_size_and_icon_only() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        settings.SetTileIconOnly(TileIds.NewTrait, true);
        var bar = Bar(settings);
        Assert.Equal(TileBar.LargeWidth, bar.TileButton(TileIds.NewBuilding).Width);
        Assert.Equal(TileBar.CompactSize, bar.TileButton(TileIds.NewTrait).Width);
        Assert.Equal(Visibility.Collapsed, Label(bar.TileButton(TileIds.NewTrait)).Visibility);

        settings.TileSize = TileSize.Medium;
        Assert.Equal(TileBar.MediumHeight, bar.TileButton(TileIds.NewBuilding).Height);
        settings.TileSize = TileSize.Compact;
        Assert.Equal(TileBar.CompactSize, bar.TileButton(TileIds.NewBuilding).Height);
    });

    [Fact]
    public void A_changed_order_rebuilds_the_tiles() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        var bar = Bar(settings);
        var before = bar.TileButton(TileIds.NewBuilding);

        settings.TileOrder = [TileIds.NewTrait, TileIds.NewBuilding];

        Assert.Equal([TileIds.NewTrait, TileIds.NewBuilding], bar.VisibleIds);
        Assert.NotSame(before, bar.TileButton(TileIds.NewBuilding));
    });

    [Fact]
    public void Labels_can_be_hidden_without_changing_the_size() => Sta.Run(() =>
    {
        var settings = new GlobalSettings { TileLabelsVisible = false };
        var bar = Bar(settings);
        Assert.Equal(Visibility.Collapsed, Label(bar.TileButton(TileIds.NewBuilding)).Visibility);
        Assert.Equal(TileBar.LargeHeight, bar.TileButton(TileIds.NewBuilding).Height);
    });

    [Fact]
    public void Badge_shows_only_positive_counts_and_survives_rebuild() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        var bar = Bar(settings);
        Assert.Equal(Visibility.Collapsed, Badge(bar.TileButton(TileIds.NewTrait)).Visibility);

        bar.SetBadge(TileIds.NewTrait, 3);
        Assert.Equal(Visibility.Visible, Badge(bar.TileButton(TileIds.NewTrait)).Visibility);
        Assert.Equal("3", Tree.Descendants<TextBlock>(Badge(bar.TileButton(TileIds.NewTrait))).Single().Text);

        settings.TileSize = TileSize.Medium;
        Assert.Equal(3, bar.BadgeOf(TileIds.NewTrait));
        Assert.Equal(Visibility.Visible, Badge(bar.TileButton(TileIds.NewTrait)).Visibility);
        bar.SetBadge(TileIds.NewTrait, 0);
        Assert.Equal(Visibility.Collapsed, Badge(bar.TileButton(TileIds.NewTrait)).Visibility);
    });

    [Fact]
    public void Warning_shows_the_sign_extends_the_tooltip_and_survives_rebuild() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        var bar = Bar(settings);
        TextBlock Warning() => Tree.Descendants<TextBlock>(bar.TileButton(TileIds.NewTrait)).Single(t => t.Name == "TileWarning");
        Assert.Equal(Visibility.Collapsed, Warning().Visibility);

        bar.SetWarning(TileIds.NewTrait, "Fetch failed");
        Assert.Equal(Visibility.Visible, Warning().Visibility);
        Assert.Equal("⚠", Warning().Text);
        Assert.Equal("T:" + TileIds.NewTrait + "\nFetch failed", bar.TileButton(TileIds.NewTrait).ToolTip);
        Assert.Equal("Fetch failed", bar.WarningOf(TileIds.NewTrait));

        // Still visible in compact mode without a label, and after the rebuild.
        settings.TileSize = TileSize.Compact;
        Assert.Equal(Visibility.Visible, Warning().Visibility);
        Assert.Equal("T:" + TileIds.NewTrait + "\nFetch failed", bar.TileButton(TileIds.NewTrait).ToolTip);

        bar.SetWarning(TileIds.NewTrait, null);
        Assert.Equal(Visibility.Collapsed, Warning().Visibility);
        Assert.Equal("T:" + TileIds.NewTrait, bar.TileButton(TileIds.NewTrait).ToolTip);
        Assert.Null(bar.WarningOf(TileIds.NewTrait));
    });

    [Fact]
    public void Warning_extends_the_disabled_tooltip() => Sta.Run(() =>
    {
        var bar = Bar(new GlobalSettings(), Source(enabled: id => id != TileIds.NewTrait));
        bar.SetWarning(TileIds.NewTrait, "w");
        Assert.Equal("disabled\nw", bar.TileButton(TileIds.NewTrait).ToolTip);
    });

    [Fact]
    public void Click_raises_TileClicked_and_tiles_without_a_menu_show_no_arrow() => Sta.Run(() =>
    {
        // No catalog tile has a submenu yet, so the menu source must be ignored for them.
        var bar = Bar(new GlobalSettings(), Source(menu: _ => new ContextMenu()));
        var clicked = new List<string>();
        bar.TileClicked += (_, id) => clicked.Add(id);

        bar.TileButton(TileIds.NewTrait).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal([TileIds.NewTrait], clicked);
        Assert.DoesNotContain(Tree.Descendants<TextBlock>(bar.TileButton(TileIds.NewTrait)), t => t.Name == "TileMenuArrow");
    });

    [Fact]
    public void Disabled_tile_keeps_a_tooltip() => Sta.Run(() =>
    {
        var bar = Bar(new GlobalSettings(), Source(enabled: id => id != TileIds.NewTrait));
        var tile = bar.TileButton(TileIds.NewTrait);
        Assert.False(tile.IsEnabled);
        Assert.Equal("disabled", tile.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(tile));
    });

    [Fact]
    public void Tile_icons_use_the_accent_and_turn_grey_when_the_tile_is_disabled() => Sta.Run(() =>
    {
        var bar = new TileBar();
        bar.Resources["Theme.Accent"] = Brushes.Blue;
        bar.Resources["Theme.TextDisabled"] = Brushes.Gray;
        bar.Bind(new GlobalSettings(), Source(enabled: id => id != TileIds.NewTrait));
        bar.Measure(new Size(4000, 200));
        bar.Arrange(new Rect(0, 0, 4000, 200));

        Assert.Same(Brushes.Blue, Tree.Descendants<System.Windows.Shapes.Path>(bar.TileButton(TileIds.NewBuilding)).Single().Stroke);
        Assert.Same(Brushes.Gray, Tree.Descendants<System.Windows.Shapes.Path>(bar.TileButton(TileIds.NewTrait)).Single().Stroke);

        bar.TileButton(TileIds.NewBuilding).IsEnabled = false;
        Assert.Same(Brushes.Gray, Tree.Descendants<System.Windows.Shapes.Path>(bar.TileButton(TileIds.NewBuilding)).Single().Stroke);
    });

    [Fact]
    public void MoveTile_writes_the_order_and_rejects_unknown_ids() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        var bar = Bar(settings);

        Assert.True(bar.MoveTile(TileIds.NewTrait, TileIds.NewBuilding));
        Assert.Equal([TileIds.NewTrait, TileIds.NewBuilding], settings.TileOrder.Take(2));
        Assert.Equal(TileIds.NewTrait, bar.VisibleIds[0]);
        Assert.False(bar.MoveTile("unknownId", TileIds.NewBuilding));
        Assert.False(bar.MoveTile(ToolbarItemIds.Undo, TileIds.NewBuilding));
    });

    [Fact]
    public void RefreshState_updates_enabled_and_tooltip_without_rebuilding() => Sta.Run(() =>
    {
        var enabled = false;
        var bar = Bar(new GlobalSettings(), Source(enabled: _ => enabled));
        var tile = bar.TileButton(TileIds.NewBuilding);
        Assert.False(tile.IsEnabled);
        Assert.Equal("disabled", tile.ToolTip);
        enabled = true;
        bar.RefreshState();
        Assert.Same(tile, bar.TileButton(TileIds.NewBuilding));
        Assert.True(tile.IsEnabled);
        Assert.Equal("T:newBuilding", tile.ToolTip);
    });
}
