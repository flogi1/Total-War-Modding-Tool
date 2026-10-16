using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ParadoxModTool.Settings;
using Xunit;

namespace ParadoxModTool.Shell.Tests;

public sealed class TileBarTests
{
    private static TileSource Source(Func<string, bool>? offered = null, Func<string, bool>? enabled = null, Func<string, ContextMenu?>? menu = null) =>
        new(id => "L:" + id, id => "T:" + id, offered ?? (_ => true), enabled ?? (_ => true), _ => "disabled", menu ?? (_ => null));

    private static TileBar Bar(AppSettings settings, TileSource? source = null)
    {
        var bar = new TileBar();
        bar.Bind(settings, source ?? Source());
        return bar;
    }

    private static TextBlock Label(Button tile) => Tree.Descendants<TextBlock>(tile).Single(t => t.Name == "TileLabel");

    private static Border Badge(Button tile) => Tree.Descendants<Border>(tile).Single(b => b.Name == "TileBadge");

    [Fact]
    public void DefaultSessionShowsThirteenTilesInCatalogOrder() => Sta.Run(() =>
    {
        var bar = Bar(new AppSettings());
        Assert.Equal(TileCatalog.All.Where(t => t.DefaultVisible).Select(t => t.Id), bar.VisibleIds);
        Assert.Equal("L:" + TileIds.Project, Label(bar.TileButton(TileIds.Project)).Text);
        Assert.Equal("T:" + TileIds.Project, bar.TileButton(TileIds.Project).ToolTip);
    });

    [Fact]
    public void HiddenAndNotOfferedTilesAreLeftOutButKeepTheirPlace() => Sta.Run(() =>
    {
        var settings = new AppSettings();
        settings.SetTileVisible(TileIds.Compatch, false);
        var bar = Bar(settings, Source(offered: id => TileCatalog.Find(id)?.RequiresGuiTools != true));

        Assert.DoesNotContain(TileIds.Compatch, bar.VisibleIds);
        Assert.DoesNotContain(TileIds.Gui, bar.VisibleIds);
        settings.SetTileVisible(TileIds.Compatch, true);
        Assert.Equal(2, bar.VisibleIds.ToList().IndexOf(TileIds.Compatch));
    });

    [Fact]
    public void SizesFollowDefaultSizeAndIconOnly() => Sta.Run(() =>
    {
        var settings = new AppSettings();
        settings.SetTileIconOnly(TileIds.CuTool, true);
        var bar = Bar(settings);
        Assert.Equal(TileBar.LargeWidth, bar.TileButton(TileIds.Project).Width);
        Assert.Equal(TileBar.CompactSize, bar.TileButton(TileIds.CuTool).Width);
        Assert.Equal(Visibility.Collapsed, Label(bar.TileButton(TileIds.CuTool)).Visibility);

        settings.TileSize = "Medium";
        Assert.Equal(TileBar.MediumHeight, bar.TileButton(TileIds.Project).Height);
        settings.TileSize = "Compact";
        Assert.Equal(TileBar.CompactSize, bar.TileButton(TileIds.Project).Height);
    });

    [Fact]
    public void ReloadedSettingsRebuildTheTiles() => Sta.Run(() =>
    {
        var settings = new AppSettings();
        var bar = Bar(settings);
        Assert.Equal(TileBar.LargeWidth, bar.TileButton(TileIds.Project).Width);

        settings.Load(new ParadoxModTool.Session.AppSessionState { TileSize = "Compact", HiddenTiles = new List<string> { TileIds.Compatch } }, Array.Empty<string>());

        Assert.Equal(TileBar.CompactSize, bar.TileButton(TileIds.Project).Width);
        Assert.DoesNotContain(TileIds.Compatch, bar.VisibleIds);
    });

    [Fact]
    public void LabelsCanBeHiddenWithoutChangingTheSize() => Sta.Run(() =>
    {
        var settings = new AppSettings { TileLabelsVisible = false };
        var bar = Bar(settings);
        Assert.Equal(Visibility.Collapsed, Label(bar.TileButton(TileIds.Project)).Visibility);
        Assert.Equal(TileBar.LargeHeight, bar.TileButton(TileIds.Project).Height);
    });

    [Fact]
    public void BadgeShowsOnlyPositiveCountsAndSurvivesRebuild() => Sta.Run(() =>
    {
        var settings = new AppSettings();
        var bar = Bar(settings);
        Assert.Equal(Visibility.Collapsed, Badge(bar.TileButton(TileIds.ErrorLog)).Visibility);

        bar.SetBadge(TileIds.ErrorLog, 3);
        Assert.Equal(Visibility.Visible, Badge(bar.TileButton(TileIds.ErrorLog)).Visibility);
        Assert.Equal("3", Tree.Descendants<TextBlock>(Badge(bar.TileButton(TileIds.ErrorLog))).Single().Text);

        settings.TileSize = "Medium";
        Assert.Equal(3, bar.BadgeOf(TileIds.ErrorLog));
        Assert.Equal(Visibility.Visible, Badge(bar.TileButton(TileIds.ErrorLog)).Visibility);
        bar.SetBadge(TileIds.ErrorLog, 0);
        Assert.Equal(Visibility.Collapsed, Badge(bar.TileButton(TileIds.ErrorLog)).Visibility);
    });

    [Fact]
    public void WarningShowsTheSignExtendsTheTooltipAndSurvivesRebuild() => Sta.Run(() =>
    {
        var settings = new AppSettings();
        var bar = Bar(settings);
        TextBlock Warning() => Tree.Descendants<TextBlock>(bar.TileButton(TileIds.BugTracker)).Single(t => t.Name == "TileWarning");
        Assert.Equal(Visibility.Collapsed, Warning().Visibility);

        bar.SetWarning(TileIds.BugTracker, "Abruf fehlgeschlagen");
        Assert.Equal(Visibility.Visible, Warning().Visibility);
        Assert.Equal("⚠", Warning().Text);
        Assert.Equal("T:" + TileIds.BugTracker + "\nAbruf fehlgeschlagen", bar.TileButton(TileIds.BugTracker).ToolTip);
        Assert.Equal("Abruf fehlgeschlagen", bar.WarningOf(TileIds.BugTracker));

        // Auch im Kompaktmodus ohne Beschriftung sichtbar, und nach dem Neuaufbau noch da.
        settings.TileSize = "Compact";
        Assert.Equal(Visibility.Visible, Warning().Visibility);
        Assert.Equal("T:" + TileIds.BugTracker + "\nAbruf fehlgeschlagen", bar.TileButton(TileIds.BugTracker).ToolTip);

        bar.SetWarning(TileIds.BugTracker, null);
        Assert.Equal(Visibility.Collapsed, Warning().Visibility);
        Assert.Equal("T:" + TileIds.BugTracker, bar.TileButton(TileIds.BugTracker).ToolTip);
        Assert.Null(bar.WarningOf(TileIds.BugTracker));
    });

    [Fact]
    public void WarningExtendsTheDisabledTooltip() => Sta.Run(() =>
    {
        var bar = Bar(new AppSettings(), Source(enabled: id => id != TileIds.SaveExplorer));
        bar.SetWarning(TileIds.SaveExplorer, "w");
        Assert.Equal("disabled\nw", bar.TileButton(TileIds.SaveExplorer).ToolTip);
    });

    [Fact]
    public void ClickRaisesTileClickedOrOpensTheMenu() => Sta.Run(() =>
    {
        var menu = new ContextMenu();
        var bar = Bar(new AppSettings(), Source(menu: id => id == TileIds.Project ? menu : null));
        var clicked = new List<string>();
        bar.TileClicked += (_, id) => clicked.Add(id);

        bar.TileButton(TileIds.CuTool).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(new[] { TileIds.CuTool }, clicked);
        Assert.Single(Tree.Descendants<TextBlock>(bar.TileButton(TileIds.Project)), t => t.Name == "TileMenuArrow");
        Assert.DoesNotContain(Tree.Descendants<TextBlock>(bar.TileButton(TileIds.CuTool)), t => t.Name == "TileMenuArrow");

        bar.TileButton(TileIds.Project).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(new[] { TileIds.CuTool }, clicked);
        Assert.Same(bar.TileButton(TileIds.Project), menu.PlacementTarget);
        menu.IsOpen = false;
    });

    [Fact]
    public void DisabledTileKeepsATooltip() => Sta.Run(() =>
    {
        var bar = Bar(new AppSettings(), Source(enabled: id => id != TileIds.SaveExplorer));
        var tile = bar.TileButton(TileIds.SaveExplorer);
        Assert.False(tile.IsEnabled);
        Assert.Equal("disabled", tile.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(tile));
    });

    [Fact]
    public void TileIconsUseTheAccentAndTurnGreyWhenTheTileIsDisabled() => Sta.Run(() =>
    {
        var bar = new TileBar();
        bar.Resources["Theme.Accent"] = Brushes.Blue;
        bar.Resources["Theme.TextDisabled"] = Brushes.Gray;
        bar.Bind(new AppSettings(), Source(enabled: id => id != TileIds.SaveExplorer));
        bar.Measure(new Size(4000, 200));
        bar.Arrange(new Rect(0, 0, 4000, 200));

        Assert.Same(Brushes.Blue, Tree.Descendants<System.Windows.Shapes.Path>(bar.TileButton(TileIds.Project)).Single().Stroke);
        Assert.Same(Brushes.Gray, Tree.Descendants<System.Windows.Shapes.Path>(bar.TileButton(TileIds.SaveExplorer)).Single().Stroke);

        bar.TileButton(TileIds.Project).IsEnabled = false;
        Assert.Same(Brushes.Gray, Tree.Descendants<System.Windows.Shapes.Path>(bar.TileButton(TileIds.Project)).Single().Stroke);
    });

    [Fact]
    public void MoveTileWritesTheOrderAndRejectsUnknownIds() => Sta.Run(() =>
    {
        var settings = new AppSettings();
        var bar = Bar(settings);

        Assert.True(bar.MoveTile(TileIds.Compatch, TileIds.Project));
        Assert.Equal(new[] { TileIds.Compatch, TileIds.Project, TileIds.CuTool }, settings.TileOrder.Take(3));
        Assert.Equal(TileIds.Compatch, bar.VisibleIds[0]);
        Assert.False(bar.MoveTile("tileX", TileIds.Project));
        Assert.False(bar.MoveTile(ToolbarItemIds.Undo, TileIds.Project));
    });
}
