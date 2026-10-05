using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class TabsViewModelTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource A = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
    private static readonly PackSource B = A with { Path = @"D:\data\b.pack", Name = "b.pack" };
    private static OpenedEntry Text(string path) => new(path, EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, new TextContent("x", "UTF-8", false, 1));

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

    private const string SchemaStaleText = "The schema changed. Close this tab and open the entry again to see it with the new schema.";
    private static readonly TableDefinition Def = new(0, [new("value", FieldType.I32, "I32", false, null)]);
    private static OpenedEntry TableEntry(string path) => new(path, EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false,
        new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, Def, [[1]], null), null, null);
    private static OpenedEntry TableHint(string path) => new(path, EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, true, null, null, null);

    [Fact] public void MarkSchemaStale_marks_table_tabs_and_table_hints_but_not_other_tabs()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, TableEntry("db/x_tables/data__"), null); tabs.Add(A, TableHint("db/y_tables/data__"), null); tabs.Add(A, Text("a.txt"), null);
        tabs.MarkSchemaStale();
        Assert.Equal([true, true, false], tabs.Tabs.Select(t => t.IsStale));
        Assert.Equal([SchemaStaleText, SchemaStaleText, ""], tabs.Tabs.Select(t => t.StaleText));
    }

    [Fact] public void A_pack_change_wins_over_a_schema_change_in_either_order()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, TableEntry("db/x_tables/data__"), null); tabs.Add(B, TableEntry("db/x_tables/data__"), null);
        tabs.MarkSchemaStale(); tabs.MarkStale(A.Path);                           // schema first, then the pack
        tabs.MarkStale(B.Path); tabs.MarkSchemaStale();                           // pack first: the schema does not replace it
        Assert.Equal([Texts.TabStale, Texts.TabStale], tabs.Tabs.Select(t => t.StaleText));
    }

    [Fact] public void The_stale_text_follows_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var tabs = new TabsViewModel(texts);
        tabs.Add(A, TableEntry("db/x_tables/data__"), null);
        var raised = new List<string?>(); tabs.Tabs[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tabs.MarkSchemaStale();
        ui.Load("de");
        Assert.Equal("Das Schema hat sich geändert. Schließe diesen Tab und öffne den Eintrag erneut, um ihn mit dem neuen Schema zu sehen.", tabs.Tabs[0].StaleText);
        Assert.Contains(nameof(TabViewModel.StaleText), raised);
        tabs.MarkStale(A.Path);
        Assert.Equal("Das Pack wurde auf der Platte geändert. Dieser Tab zeigt den alten Inhalt.", tabs.Tabs[0].StaleText);
    }

    [Fact] public void Changed_is_raised_for_add_close_and_selection()
    {
        var tabs = new TabsViewModel(Texts); var changes = 0; tabs.Changed += (_, _) => changes++;
        tabs.Add(A, Text("a.txt"), null); var afterAdd = changes;
        tabs.Add(A, Text("b.txt"), null); tabs.SelectedTab = tabs.Tabs[0]; var afterSelect = changes;
        tabs.Close(tabs.Tabs[0]);
        Assert.True(afterAdd >= 1); Assert.True(afterSelect > afterAdd); Assert.True(changes > afterSelect);
    }

    [Fact] public void A_project_source_is_a_tab_source_of_its_own()
    {
        var id = Guid.NewGuid();
        var source = TabSource.ForProject(id, "My Mod");
        Assert.Equal("project:" + id.ToString("D"), source.Key);
        Assert.True(TabSource.TryGetProjectId(source.Key, out var back));
        Assert.Equal(id, back);
        Assert.False(TabSource.TryGetProjectId(@"D:\a.pack", out _));
        Assert.False(TabSource.TryGetProjectId("project:not-a-guid", out _));

        var tabs = new TabsViewModel(Texts);
        tabs.Add(source, Text("scripts/a.txt"), null);
        Assert.True(tabs.TryActivate("project:" + id.ToString("D"), "scripts/a.txt"));
        Assert.Equal("My Mod: scripts/a.txt", tabs.SelectedTab!.ToolTip);
        Assert.False(tabs.TryActivate(A.Path, "scripts/a.txt"));
    }

    [Fact] public void A_pack_source_converts_to_a_tab_source_with_its_path_and_name()
    {
        TabSource source = A;
        Assert.Equal((A.Path, A.Name), (source.Key, source.Name));
    }

    private static readonly TableDefinition UnitsDef = new(2, [new("key", FieldType.StringU8, "StringU8", true, null), new("cost", FieldType.I32, "I32", false, null)]);

    private static TableTabViewModel UnitsTable(AppTexts texts, IReadOnlyList<object?[]>? rows = null)
    {
        var tabs = new TabsViewModel(texts);
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 2, false, UnitsDef, rows ?? [["a", 1]], null);
        tabs.Add(A, new OpenedEntry("db/units_tables/x", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null), null);
        return Assert.IsType<TableTabViewModel>(tabs.SelectedTab);
    }

    [Fact] public void The_text_view_of_a_table_is_built_on_first_use()
    {
        var tab = UnitsTable(Texts);
        Assert.Null(tab.Text);
        Assert.True(tab.ShowGrid);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.ShowText = true;

        Assert.Equal("key\tcost\n#units_tables;2;db/units_tables/x\t\na\t1\n", tab.Text);
        Assert.False(tab.ShowGrid);
        Assert.Null(tab.TextNotice);
        Assert.Contains(nameof(TableTabViewModel.ShowText), raised);
        Assert.Contains(nameof(TableTabViewModel.ShowGrid), raised);
        Assert.Contains(nameof(TableTabViewModel.Text), raised);
        Assert.Contains(nameof(TableTabViewModel.TextNotice), raised);

        var text = tab.Text; tab.ShowText = false; tab.ShowText = true;
        Assert.Same(text, tab.Text);                                       // built once
        Assert.True(tab.ShowText == !tab.ShowGrid);
    }

    [Fact] public void A_table_the_dialect_cannot_carry_has_a_notice_instead_of_text()
    {
        var def = new TableDefinition(0, [new("r", FieldType.I32, "I32", false, null), new("g", FieldType.I32, "I32", false, null), new("b", FieldType.I32, "I32", false, null)]);
        var tabs = new TabsViewModel(Texts);
        var table = new DecodedTable(TableDecodeStatus.Ok, "colours_tables", 0, false, def, [[300, 1, 2]], null);
        tabs.Add(A, new OpenedEntry("db/colours_tables/x", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null), null);
        var tab = Assert.IsType<TableTabViewModel>(tabs.SelectedTab);
        var detail = TableTsv.Write("colours_tables", 0, "db/colours_tables/x", def, table.Rows).Detail;
        Assert.NotNull(detail);

        tab.ShowText = true;

        Assert.Null(tab.Text);
        Assert.Equal(Texts.TabTextViewUnavailable(detail), tab.TextNotice);
        Assert.Contains(detail, tab.TextNotice);
    }

    [Fact] public void A_table_with_a_name_the_dialect_rejects_has_a_notice_and_does_not_throw()
    {
        var tabs = new TabsViewModel(Texts);
        var table = new DecodedTable(TableDecodeStatus.Ok, "bad;name", 2, false, UnitsDef, [["a", 1]], null);
        tabs.Add(A, new OpenedEntry("db/x/y", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null), null);
        var tab = Assert.IsType<TableTabViewModel>(tabs.SelectedTab);

        tab.ShowText = true;

        Assert.Null(tab.Text);
        Assert.StartsWith("This table cannot be shown as text: The table name must not contain", tab.TextNotice);
        Assert.DoesNotContain("'tableName'", tab.TextNotice);          // the suffix " (Parameter 'tableName')" of .NET is not shown
        Assert.DoesNotContain("Parameter", tab.TextNotice);
    }

    [Fact] public void A_table_without_a_definition_has_a_notice_and_no_text()
    {
        var tabs = new TabsViewModel(Texts);
        // A decoded table that carries rows but no definition cannot come from the factory; the tab still has to survive it.
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 2, false, null, [], null);
        var tab = new TableTabViewModel(A, "db/units_tables/x", Texts, _ => { }, table);

        tab.ShowText = true;

        Assert.Null(tab.Text);
        Assert.Equal("This table cannot be shown as text: The table has no definition.", tab.TextNotice);
        Assert.Equal(Texts.TabTextViewUnavailable(Texts.TabTextViewNoDefinition), tab.TextNotice);
        tabs.Close(tab);
    }

    [Fact] public void The_no_definition_notice_follows_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 2, false, null, [], null);
        var tab = new TableTabViewModel(A, "db/units_tables/x", texts, _ => { }, table);
        tab.ShowText = true;
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ui.Load("de");

        Assert.Equal("Diese Tabelle lässt sich nicht als Text zeigen: Die Tabelle hat keine Definition.", tab.TextNotice);
        Assert.Contains(nameof(TableTabViewModel.TextNotice), raised);
        tab.Dispose();
    }

    [Fact] public void A_long_text_is_cut_at_a_line_end()
    {
        var tab = UnitsTable(Texts, [["a", 1], ["b", 2], ["c", 3], ["d", 4]]);
        tab.MaxChars = 40;

        tab.ShowText = true;

        Assert.EndsWith("\n", tab.Text);
        Assert.True(tab.Text!.Length <= 40);
        Assert.StartsWith("key\tcost\n", tab.Text);
        Assert.Equal(Texts.TabTextViewTruncated, tab.TextNotice);
    }

    [Fact] public void A_text_that_fits_exactly_is_not_cut()
    {
        var tab = UnitsTable(Texts);
        tab.MaxChars = "key\tcost\n#units_tables;2;db/units_tables/x\t\na\t1\n".Length;
        tab.ShowText = true;
        Assert.Null(tab.TextNotice);
        Assert.EndsWith("a\t1\n", tab.Text);
    }

    [Fact] public void The_notice_follows_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var tab = UnitsTable(texts, [["a", 1], ["b", 2]]);
        tab.MaxChars = 40;
        tab.ShowText = true;
        Assert.Equal("Only the first part of the text is shown.", tab.TextNotice);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ui.Load("de");

        Assert.Equal("Nur der Anfang des Textes wird gezeigt.", tab.TextNotice);
        Assert.Contains(nameof(TableTabViewModel.TextNotice), raised);
    }

    [Fact] public void The_text_view_of_a_loc_tab_uses_the_loc_form()
    {
        var tabs = new TabsViewModel(Texts);
        var loc = new DecodedLoc(LocDecodeStatus.Ok, [new("k1", "t1", true)], null);
        tabs.Add(A, new OpenedEntry("text/names.loc", EntryKind.Loc, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, loc, null), null);
        var tab = Assert.IsType<LocTabViewModel>(tabs.SelectedTab);
        Assert.Null(tab.Text);

        tab.ShowText = true;

        Assert.StartsWith("key\ttext\ttooltip\n#Loc;1;", tab.Text);
        Assert.Contains("k1\tt1\ttrue\n", tab.Text);
        Assert.False(tab.ShowGrid);
    }

    [Fact] public void SpecialTab_replaces_the_default_tab_and_null_keeps_it()
    {
        var tabs = new TabsViewModel(Texts);
        var asked = new List<(TabSource, string)>();
        TabViewModel? special = null;
        Action<TabViewModel>? handedClose = null;
        tabs.SpecialTab = (source, entry, close) =>
        {
            asked.Add((source, entry.EntryPath));
            handedClose = close;
            return entry.EntryPath == "special.txt" ? special = new TextTabViewModel(source, entry.EntryPath, Texts, close, new TextContent("s", "UTF-8", false, 1)) : null;
        };

        tabs.Add(A, Text("special.txt"), null);
        tabs.Add(A, Text("plain.txt"), null);

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Same(special, tabs.Tabs[0]);                                  // the tab of the hook is the one that is added
        Assert.IsType<TextTabViewModel>(tabs.Tabs[1]);                      // null: the default tab
        Assert.NotSame(special, tabs.Tabs[1]);
        Assert.Equal([(A.Path, "special.txt"), (A.Path, "plain.txt")], asked.Select(a => (a.Item1.Key, a.Item2)));

        // The callback it is given closes the tab through the list, and it asks (see CanClose).
        var asks = 0; tabs.CanClose = _ => { asks++; return true; };
        handedClose!(special!);
        Assert.Equal(1, asks);
        Assert.DoesNotContain(special, tabs.Tabs);
    }

    [Fact] public void CanClose_false_keeps_the_tab_for_the_command_and_for_close_selected()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("1"), null); tabs.Add(A, Text("2"), null);
        var asked = new List<string>();
        var allow = false;
        tabs.CanClose = tab => { asked.Add(tab.Title); return allow; };

        tabs.Tabs[0].CloseCommand.Execute(null);                            // the tab's own command asks
        tabs.CloseSelectedCommand.Execute(null);                            // so does close selected (and the middle button, which runs CloseCommand)
        tabs.Close(tabs.Tabs[0]);

        Assert.Equal(["1", "2", "1"], asked);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal("2", tabs.SelectedTab!.Title);

        allow = true;
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Equal(["1"], tabs.Tabs.Select(t => t.Title));
        Assert.Equal("1", tabs.SelectedTab!.Title);
    }

    [Fact] public void CloseWithoutAsking_does_not_ask()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("1"), null); tabs.Add(A, Text("2"), null);
        var asked = 0; tabs.CanClose = _ => { asked++; return false; };
        var second = tabs.Tabs[1];

        tabs.CloseWithoutAsking(second);

        Assert.Equal(0, asked);
        Assert.Equal(["1"], tabs.Tabs.Select(t => t.Title));
        Assert.Equal("1", tabs.SelectedTab!.Title);
        tabs.CloseWithoutAsking(second);                                    // already closed: nothing happens
        Assert.Single(tabs.Tabs);
    }

    [Fact] public void Close_of_a_tab_that_is_not_open_does_not_ask()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("1"), null);
        var stranger = new TextTabViewModel(A, "x", Texts, _ => { }, new TextContent("x", "UTF-8", false, 1));
        var asked = 0; tabs.CanClose = _ => { asked++; return true; };

        tabs.Close(stranger);

        Assert.Equal(0, asked);
        Assert.Single(tabs.Tabs);
    }

    // ---- copying the rows of a read-only tab ----------------------------------------------------------------------------------------

    private static OpenedEntry TableOfThree(string path) => new(path, EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false,
        new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, Def, [[1], [2], [3]], null), null, null);

    private static OpenedEntry LocOfTwo(string path) => new(path, EntryKind.Loc, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null,
        new DecodedLoc(LocDecodeStatus.Ok, [new LocRow("k1", "one\ttwo", true), new LocRow("k2", "text 2", false)], null), null);

    [Fact] public void A_read_only_table_and_a_read_only_loc_tab_copy_their_selected_rows()
    {
        var clipboard = new FakeClipboard();
        var tabs = new TabsViewModel(Texts) { Clipboard = clipboard };
        tabs.Add(A, TableOfThree("db/x_tables/data__"), null);
        tabs.Add(A, LocOfTwo("text/db/a.loc"), null);
        var table = Assert.IsType<TableTabViewModel>(tabs.Tabs[0]);
        var loc = Assert.IsType<LocTabViewModel>(tabs.Tabs[1]);

        Assert.False(table.CopyRowsCommand.CanExecute(null));           // nothing selected
        table.SelectedRows = [2, 0];
        Assert.True(table.CopyRowsCommand.CanExecute(null));
        table.CopyRowsCommand.Execute(null);
        Assert.Equal("value\n1\n3\n", clipboard.Text);                  // the header and the rows in the order of the table
        Assert.Null(table.Notice);

        loc.SelectedRows = [0, 1];
        loc.CopyRowsCommand.Execute(null);
        Assert.Equal("key\ttext\ttooltip\nk1\tone\\\\ttwo\ttrue\nk2\ttext 2\tfalse\n", clipboard.Text);     // the three columns of a loc document, a tab in a cell escaped

        // The same text that a paste into the editable tab of the file reads back.
        var back = TotalWarModTool.Editing.ClipboardRows.Read(clipboard.Text!, TotalWarModTool.Editing.RowsDocument.ForLoc("text/db/a.loc", []));
        Assert.Equal(TotalWarModTool.Editing.PasteProblem.None, back.Problem);
        Assert.Equal(new object?[] { "k1", "one\ttwo", true }, back.Rows[0]);
    }

    [Fact] public void A_read_only_tab_without_a_clipboard_or_a_definition_cannot_copy()
    {
        var withoutClipboard = new TabsViewModel(Texts);
        withoutClipboard.Add(A, TableOfThree("db/x_tables/data__"), null);
        var table = Assert.IsType<TableTabViewModel>(withoutClipboard.Tabs[0]);
        table.SelectedRows = [0];
        Assert.False(table.CopyRowsCommand.CanExecute(null));

        var clipboard = new FakeClipboard();
        var noDefinition = new TableTabViewModel(A, "db/y_tables/data__", Texts, _ => { },
            new DecodedTable(TableDecodeStatus.Ok, "y_tables", 0, false, null, [[1]], null), clipboard)
        {
            SelectedRows = [0],
        };
        noDefinition.CopyRowsCommand.Execute(null);                     // no columns to write the cells with: nothing happens
        Assert.Null(clipboard.Text);
    }

    [Fact] public void A_read_only_table_that_cannot_be_written_says_so_and_changes_nothing()
    {
        var clipboard = new FakeClipboard { Text = "before" };
        var odd = new TableDefinition(0, [new("value", FieldType.I32, "I32", false, null)]);
        var table = new TableTabViewModel(A, "db/x_tables/data__", Texts, _ => { },
            new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, odd, [["not a number"]], null), clipboard)
        {
            SelectedRows = [0],
        };

        table.CopyRowsCommand.Execute(null);

        Assert.StartsWith("The selected rows cannot be copied as text.", table.Notice);
        Assert.Equal("before", clipboard.Text);
    }

    [Fact] public void A_clipboard_that_cannot_be_opened_is_reported_by_a_read_only_tab()
    {
        var clipboard = new FakeClipboard { Available = false };
        var tabs = new TabsViewModel(Texts) { Clipboard = clipboard };
        tabs.Add(A, TableOfThree("db/x_tables/data__"), null);
        var table = Assert.IsType<TableTabViewModel>(tabs.Tabs[0]);
        table.SelectedRows = [1];

        table.CopyRowsCommand.Execute(null);

        Assert.Equal(Texts.GridCopyClipboardFailed, table.Notice);
    }

    [Fact] public void The_clipboard_of_the_tab_list_reaches_the_tabs_that_are_made_afterwards()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, TableOfThree("db/a_tables/data__"), null);
        var clipboard = new FakeClipboard();
        tabs.Clipboard = clipboard;
        tabs.Add(A, TableOfThree("db/b_tables/data__"), null);
        var before = Assert.IsType<TableTabViewModel>(tabs.Tabs[0]);
        var after = Assert.IsType<TableTabViewModel>(tabs.Tabs[1]);
        before.SelectedRows = [0];
        after.SelectedRows = [0];

        Assert.False(before.CopyRowsCommand.CanExecute(null));
        Assert.True(after.CopyRowsCommand.CanExecute(null));
    }
}
