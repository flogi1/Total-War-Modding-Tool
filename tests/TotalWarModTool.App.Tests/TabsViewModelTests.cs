using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class TabsViewModelTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource A = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
    private static readonly PackSource B = A with { Path = @"D:\data\b.pack", Name = "b.pack" };
    private static OpenedEntry Text(string path) => new(path, EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, new TextContent("x", "UTF-8", false, 1));

    // Review Focus 5
    [Fact] public void Opening_an_open_entry_activates_its_tab()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(A, Text("b.txt"), null);
        Assert.Equal("b.txt", tabs.SelectedTab!.Title);
        Assert.True(tabs.TryActivate(@"d:\DATA\a.pack", "A.TXT"));
        Assert.Equal(("a.txt", 2), (tabs.SelectedTab!.Title, tabs.Tabs.Count));
        Assert.False(tabs.TryActivate(B.Path, "a.txt"));
    }

    [Fact] public void Add_without_select_keeps_the_selection()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(A, Text("b.txt"), null, select: false);
        Assert.Equal("a.txt", tabs.SelectedTab!.Title);
    }

    [Fact] public void Close_selects_the_right_neighbour_then_the_left_one()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("1"), null); tabs.Add(A, Text("2"), null); tabs.Add(A, Text("3"), null);
        tabs.SelectedTab = tabs.Tabs[1];
        tabs.SelectedTab.CloseCommand.Execute(null);
        Assert.Equal("3", tabs.SelectedTab!.Title);
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Equal("1", tabs.SelectedTab!.Title);
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Null(tabs.SelectedTab); Assert.Empty(tabs.Tabs);
        tabs.CloseSelectedCommand.Execute(null);                       // nothing selected: nothing happens
    }

    [Fact] public void MarkStale_marks_only_the_tabs_of_that_pack()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(B, Text("b.txt"), null);
        tabs.MarkStale(@"D:\DATA\A.PACK");
        Assert.Equal([true, false], tabs.Tabs.Select(t => t.IsStale));
    }

    [Fact] public void Changed_is_raised_for_add_close_and_selection()
    {
        var tabs = new TabsViewModel(Texts); var changes = 0; tabs.Changed += (_, _) => changes++;
        tabs.Add(A, Text("a.txt"), null); var afterAdd = changes;
        tabs.Add(A, Text("b.txt"), null); tabs.SelectedTab = tabs.Tabs[0]; var afterSelect = changes;
        tabs.Close(tabs.Tabs[0]);
        Assert.True(afterAdd >= 1); Assert.True(afterSelect > afterAdd); Assert.True(changes > afterSelect);
    }
}
