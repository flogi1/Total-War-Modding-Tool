namespace TotalWarModTool.Settings.Tests;

public class ToolbarLayoutTests
{
    [Fact] public void Hidden_items_drop_and_separators_collapse_at_start_end_and_in_a_row() =>
        Assert.Equal(["save", "sep:2", "undo"], ToolbarLayout.VisibleSequence(["sep:1", "newProject", "save", "sep:2", "sep:3", "undo", "sep:4"], id => id != "newProject"));

    [Fact] public void An_order_of_only_separators_is_empty() =>
        Assert.Empty(ToolbarLayout.VisibleSequence(["sep:1", "sep:2"], _ => true));
}
