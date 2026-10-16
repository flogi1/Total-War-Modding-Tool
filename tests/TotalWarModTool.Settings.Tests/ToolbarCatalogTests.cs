namespace TotalWarModTool.Settings.Tests;

public class ToolbarCatalogTests
{
    [Fact] public void The_default_order_has_twelve_entries_and_four_separators()
    {
        Assert.Equal("newProject openProject importPack newTable newLoc sep:1 save saveAll sep:2 undo redo sep:3 refreshPacks sep:4 build buildAndInstall",
            string.Join(' ', ToolbarCatalog.DefaultOrder));
        Assert.Equal((12, 4), (ToolbarCatalog.All.Count, ToolbarCatalog.DefaultOrder.Count(ToolbarCatalog.IsSeparator)));
    }

    [Theory, InlineData("sep:1", true), InlineData("sep:12", true), InlineData("sep:", false), InlineData("sep:0", false), InlineData("sep:x", false), InlineData(null, false)]
    public void IsSeparator_accepts_only_sep_followed_by_a_positive_number(string? id, bool expected) => Assert.Equal(expected, ToolbarCatalog.IsSeparator(id));

    [Fact] public void Every_id_of_the_default_order_is_known_or_a_separator() =>
        Assert.All(ToolbarCatalog.DefaultOrder, id => Assert.True(ToolbarCatalog.IsKnown(id) || ToolbarCatalog.IsSeparator(id)));

    [Fact] public void Separator_builds_the_prefixed_id_and_separators_are_not_catalog_ids()
    {
        Assert.Equal("sep:3", ToolbarCatalog.Separator(3));
        Assert.DoesNotContain(ToolbarCatalog.All, ToolbarCatalog.IsSeparator);
        Assert.False(ToolbarCatalog.IsKnown("sep:1"));
    }
}
