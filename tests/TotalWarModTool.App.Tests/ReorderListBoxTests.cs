using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TotalWarModTool.App.Tests;

public class ReorderListBoxTests
{
    private static readonly ReorderTexts Texts = new("Up", "Down", "Remove");

    // The owner of the list: commit stores the new order and rebuilds the control, as the settings pages do through settings.Changed.
    private sealed class Owner
    {
        public List<string> Order { get; set; }
        public List<IReadOnlyList<string>> Commits { get; } = new();
        public List<string> Removed { get; } = new();
        public ReorderListBox Box { get; }

        public Owner(params string[] order)
        {
            Order = order.ToList();
            Box = new ReorderListBox(() => Order, commit =>
            {
                Commits.Add(commit);
                Order = commit.ToList();
                Box!.Rebuild();
            }, id => id.StartsWith("sep", StringComparison.Ordinal)
                ? new ReorderRow(id, "Separator", IsSeparator: true, Remove: () => Removed.Add(id))
                : new ReorderRow(id, "Label " + id), Texts);
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact] public void Rows_follow_the_order_and_arrows_move_them_through_commit() => Sta.Run(() =>
    {
        var owner = new Owner("a", "b", "c");
        Assert.Equal(["a", "b", "c"], owner.Box.CurrentIds);
        Assert.Equal(3, owner.Box.Items.Count);
        Assert.Same(owner.Box.Row("a"), owner.Box.Items[0]);
        Assert.Contains(Tree.Descendants<TextBlock>(owner.Box.Row("b")), text => text.Text == "Label b");

        Click(owner.Box.DownButton("a"));
        Assert.Equal(["b", "a", "c"], owner.Order);
        Assert.Equal(["b", "a", "c"], owner.Box.CurrentIds);

        Click(owner.Box.UpButton("c"));
        Assert.Equal(["b", "c", "a"], owner.Order);
        Assert.Equal(2, owner.Commits.Count);
        Assert.Equal(("Up", "Down"), ((string)owner.Box.UpButton("c").ToolTip, (string)owner.Box.DownButton("c").ToolTip));
    });

    [Fact] public void The_first_row_cannot_move_up_and_the_last_cannot_move_down() => Sta.Run(() =>
    {
        var owner = new Owner("a", "b", "c");
        Assert.False(owner.Box.UpButton("a").IsEnabled);
        Assert.True(owner.Box.DownButton("a").IsEnabled);
        Assert.True(owner.Box.UpButton("b").IsEnabled && owner.Box.DownButton("b").IsEnabled);
        Assert.True(owner.Box.UpButton("c").IsEnabled);
        Assert.False(owner.Box.DownButton("c").IsEnabled);

        // The methods refuse as well and commit nothing.
        Assert.False(owner.Box.MoveUp("a"));
        Assert.False(owner.Box.MoveDown("c"));
        Assert.False(owner.Box.MoveUp("unknown"));
        Assert.Empty(owner.Commits);
    });

    [Fact] public void MoveNextTo_uses_the_reorder_rule() => Sta.Run(() =>
    {
        var owner = new Owner("a", "b", "c", "d");

        // From the front: lands behind the target.
        Assert.True(owner.Box.MoveNextTo("a", "c"));
        Assert.Equal(["b", "c", "a", "d"], owner.Order);

        // From the back: lands before the target.
        Assert.True(owner.Box.MoveNextTo("d", "b"));
        Assert.Equal(["d", "b", "c", "a"], owner.Order);

        Assert.False(owner.Box.MoveNextTo("b", "b"));
        Assert.Equal(2, owner.Commits.Count);
        Assert.False(owner.Box.CanAcceptDrop("x", "b"));
        Assert.True(owner.Box.CanAcceptDrop("a", "b"));
    });

    [Fact] public void A_separator_row_shows_its_remove_button() => Sta.Run(() =>
    {
        var owner = new Owner("a", "sep:1", "b");

        var separatorButtons = Tree.Descendants<Button>(owner.Box.Row("sep:1")).ToList();
        var remove = Assert.Single(separatorButtons, button => Equals(button.ToolTip, "Remove"));
        Assert.Equal(2, Tree.Descendants<Button>(owner.Box.Row("a")).Count());

        Click(remove);
        Assert.Equal(["sep:1"], owner.Removed);
        Assert.Equal(FontStyles.Italic, Tree.Descendants<TextBlock>(owner.Box.Row("sep:1")).Single(text => text.Text == "Separator").FontStyle);
    });
}
