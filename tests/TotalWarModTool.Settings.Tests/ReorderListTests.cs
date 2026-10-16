namespace TotalWarModTool.Settings.Tests;

public class ReorderListTests
{
    [Fact] public void MoveNextTo_puts_a_later_item_before_the_target_and_an_earlier_item_behind_it()
    {
        var a = new List<string> { "a", "b", "c" }; Assert.True(ReorderList.MoveNextTo(a, "c", "a")); Assert.Equal(["c", "a", "b"], a);
        var b = new List<string> { "a", "b", "c" }; Assert.True(ReorderList.MoveNextTo(b, "a", "c")); Assert.Equal(["b", "c", "a"], b);
    }

    [Fact] public void MoveNextTo_rejects_the_same_target_and_unknown_ids()
    {
        var list = new List<string> { "a", "b" };
        Assert.False(ReorderList.MoveNextTo(list, "a", "a")); Assert.False(ReorderList.MoveNextTo(list, "x", "a")); Assert.Equal(["a", "b"], list);
    }

    [Fact] public void Swap_exchanges_two_entries_and_rejects_equal_or_invalid_indexes()
    {
        var list = new List<string> { "a", "b", "c" };
        Assert.True(ReorderList.Swap(list, 0, 2)); Assert.Equal(["c", "b", "a"], list);
        Assert.False(ReorderList.Swap(list, 1, 1)); Assert.False(ReorderList.Swap(list, -1, 0)); Assert.False(ReorderList.Swap(list, 0, 3));
        Assert.Equal(["c", "b", "a"], list);
    }

    [Fact] public void Complete_keeps_known_ids_in_order_drops_unknown_and_duplicates_and_appends_missing() =>
        Assert.Equal(["b", "sep:1", "a", "c"], ReorderList.Complete(["b", "gone", "sep:1", "b", "a"], ["a", "b", "c"], ToolbarCatalog.IsSeparator));

    [Fact] public void Complete_of_null_is_the_catalog() => Assert.Equal(["a", "b"], ReorderList.Complete(null, ["a", "b"]));
}
