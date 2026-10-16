using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using TotalWarModTool.Settings;
using TotalWarModTool.App.Shell;
using Xunit;

namespace TotalWarModTool.App.Tests.Shell;

public sealed class ToolbarItemViewTests
{
    [Fact]
    public void ContentHasIconAndLabel() => Sta.Run(() =>
    {
        var content = ToolbarItemView.Content(ToolbarItemIds.Undo, "Undo", showLabel: true);
        Assert.Single(Tree.Descendants<Path>(content));
        Assert.Equal("Undo", ToolbarItemView.LabelOf(content)!.Text);
        Assert.Equal(Visibility.Visible, ToolbarItemView.LabelOf(content)!.Visibility);
    });

    [Fact]
    public void IconFollowsTheForegroundOfItsButton() => Sta.Run(() =>
    {
        // Disabled and checked states set the button foreground in the theme styles (Controls.xaml); the icon follows it.
        var content = ToolbarItemView.Content(ToolbarItemIds.Save, "Split", showLabel: true);
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
        var shown = ToolbarItemView.Content(ToolbarItemIds.Build, "Build", showLabel: true);
        var hidden = ToolbarItemView.Content(ToolbarItemIds.Build, "Build", showLabel: false);
        var icon = Tree.Descendants<Path>(shown).Single();

        Assert.True(ToolbarItemView.SetLabel(shown, "Build (2 new)"));
        Assert.True(ToolbarItemView.SetLabel(hidden, "Build (2 new)"));

        Assert.Equal("Build (2 new)", ToolbarItemView.LabelOf(shown)!.Text);
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
