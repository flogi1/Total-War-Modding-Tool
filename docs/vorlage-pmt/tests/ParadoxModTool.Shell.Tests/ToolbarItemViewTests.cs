using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ParadoxModTool.Settings;
using Xunit;

namespace ParadoxModTool.Shell.Tests;

public sealed class ToolbarItemViewTests
{
    [Fact]
    public void ContentHasIconAndLabel() => Sta.Run(() =>
    {
        var content = ToolbarItemView.Content(ToolbarItemIds.Undo, "Rückgängig", showLabel: true);
        Assert.Single(Tree.Descendants<Path>(content));
        Assert.Equal("Rückgängig", ToolbarItemView.LabelOf(content)!.Text);
        Assert.Equal(Visibility.Visible, ToolbarItemView.LabelOf(content)!.Visibility);
    });

    [Fact]
    public void IconFollowsTheForegroundOfItsButton() => Sta.Run(() =>
    {
        // Deaktiviert und eingerastet setzen die Theme-Stile die Vordergrundfarbe des Knopfs (Controls.xaml); das Icon folgt ihr.
        var content = ToolbarItemView.Content(ToolbarItemIds.SplitView, "Split", showLabel: true);
        var toggle = new ToggleButton { Foreground = Brushes.Red, Content = content };
        var path = Tree.Descendants<Path>(content).Single();
        Assert.Same(Brushes.Red, path.Stroke);

        toggle.Foreground = Brushes.Gray;
        Assert.Same(Brushes.Gray, path.Stroke);
    });

    [Fact]
    public void LabelCanBeHidden() => Sta.Run(() =>
        Assert.Equal(Visibility.Collapsed, ToolbarItemView.LabelOf(ToolbarItemView.Content(ToolbarItemIds.Undo, "x", showLabel: false))!.Visibility));

    [Fact]
    public void SetLabelChangesTheTextInPlaceAndKeepsVisibility() => Sta.Run(() =>
    {
        var shown = ToolbarItemView.Content(ToolbarItemIds.BugTracker, "Bugs", showLabel: true);
        var hidden = ToolbarItemView.Content(ToolbarItemIds.BugTracker, "Bugs", showLabel: false);
        var icon = Tree.Descendants<Path>(shown).Single();

        Assert.True(ToolbarItemView.SetLabel(shown, "Bugs (2 neu)"));
        Assert.True(ToolbarItemView.SetLabel(hidden, "Bugs (2 neu)"));

        Assert.Equal("Bugs (2 neu)", ToolbarItemView.LabelOf(shown)!.Text);
        Assert.Same(icon, Tree.Descendants<Path>(shown).Single());
        Assert.Equal(Visibility.Visible, ToolbarItemView.LabelOf(shown)!.Visibility);
        Assert.Equal(Visibility.Collapsed, ToolbarItemView.LabelOf(hidden)!.Visibility);
    });

    [Fact]
    public void SetLabelRejectsForeignContent() => Sta.Run(() =>
        Assert.False(ToolbarItemView.SetLabel(new System.Windows.Controls.TextBlock { Text = "x" }, "y")));

    [Fact]
    public void SeparatorFollowsOrientation() => Sta.Run(() =>
    {
        Assert.Equal(1, ToolbarItemView.Separator(vertical: false).Width);
        Assert.Equal(1, ToolbarItemView.Separator(vertical: true).Height);
    });
}
