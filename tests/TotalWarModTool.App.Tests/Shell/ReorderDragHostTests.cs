using System.Windows;
using System.Windows.Controls;
using TotalWarModTool.App.Shell;
using Xunit;

namespace TotalWarModTool.App.Tests.Shell;

public sealed class ReorderDragHostTests
{
    private static ReorderDragHost Host(string id, List<string> order, List<(string, string)> drops, string list = "list") =>
        new(list, id, new Button { Content = id }, order.Contains, (dragged, target) => drops.Add((dragged, target)));

    [Fact]
    public void AcceptsOnlyForeignIdsOfTheSameList() => Sta.Run(() =>
    {
        var host = Host("b", new List<string> { "a", "b" }, new());
        Assert.True(host.AcceptsDrop("a"));
        Assert.False(host.AcceptsDrop("b"));
        Assert.False(host.AcceptsDrop("unknownId"));
        Assert.False(host.AcceptsDrop((string?)null));
    });

    [Fact]
    public void DraggedIdReadsOnlyTheOwnFormat() => Sta.Run(() =>
    {
        Assert.Equal("a", ReorderDragHost.DraggedId(new DataObject(ReorderDragHost.DataFormat, "list|a"), "list"));
        Assert.Null(ReorderDragHost.DraggedId(new DataObject(ReorderDragHost.DataFormat, "other|a"), "list"));
        Assert.Null(ReorderDragHost.DraggedId(new DataObject(ReorderDragHost.DataFormat, "a"), "list"));
        Assert.Null(ReorderDragHost.DraggedId(new DataObject(DataFormats.Text, "list|a"), "list"));
    });

    [Fact]
    public void HostWrapsContentAndAllowsDrop() => Sta.Run(() =>
    {
        var button = new Button();
        var host = new ReorderDragHost("list", "a", button, _ => true, (_, _) => { });
        Assert.True(host.AllowDrop);
        Assert.Equal("a", host.Tag);
        Assert.Same(button, host.Content);
        Assert.Contains(button, host.Children.OfType<FrameworkElement>());
        Assert.False(host.IsDropTarget);
    });

    [Fact]
    public void ButtonsStartADragButComboBoxesAndTextBoxesDoNot() => Sta.Run(() =>
    {
        var label = new TextBlock { Text = "x" };
        var button = new Button { Content = label };
        Assert.True(ReorderDragHost.StartsDrag(button));
        Assert.True(ReorderDragHost.StartsDrag(label));
        Assert.True(ReorderDragHost.StartsDrag(null));

        var itemText = new TextBlock { Text = "Entry" };
        var combo = new ComboBox();
        combo.Items.Add(new ComboBoxItem { Content = itemText });
        Assert.False(ReorderDragHost.StartsDrag(combo));
        Assert.False(ReorderDragHost.StartsDrag(itemText));
        Assert.False(ReorderDragHost.StartsDrag(new TextBox()));

        var run = new System.Windows.Documents.Run("r");
        var text = new TextBlock(run);
        var host = new ComboBox();
        host.Items.Add(text);
        Assert.False(ReorderDragHost.StartsDrag(run));
    });

    [Fact]
    public void ContentOfTheComboBoxDropDownDoesNotStartADrag() => Sta.Run(() =>
    {
        // The drop-down list lives in its own popup tree; its parts hang off the combo box through TemplatedParent.
        var combo = new ComboBox
        {
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate TargetType=\"ComboBox\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
                + "<Grid><Popup Name=\"PART_Popup\"><Border><ItemsPresenter /></Border></Popup></Grid></ControlTemplate>"),
        };
        combo.Items.Add("a");
        combo.ApplyTemplate();
        var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
        var dropDown = (Border)popup.Child;
        Assert.False(ReorderDragHost.StartsDrag(dropDown));
        Assert.False(ReorderDragHost.StartsDrag(dropDown.Child));
    });

    [Fact]
    public void SameIdFromTheOtherBarIsRejected() => Sta.Run(() =>
    {
        // Tile and toolbar ids may overlap in the future; only the own list may accept a drop.
        var tile = Host("newBuilding", new List<string> { "newBuilding", "newTrait" }, new(), list: TileBar.ListKey);
        Assert.True(tile.AcceptsDrop(new DataObject(ReorderDragHost.DataFormat, TileBar.ListKey + "|newTrait")));
        Assert.False(tile.AcceptsDrop(new DataObject(ReorderDragHost.DataFormat, ToolbarBuilder.ListKey + "|newTrait")));
        Assert.False(tile.AcceptsDrop(new DataObject(ReorderDragHost.DataFormat, "newTrait")));
        Assert.Equal(TileBar.ListKey, tile.ListKey);
    });
}
