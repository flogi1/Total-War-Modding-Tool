using System.Windows;
using TotalWarModTool.Localization;
using TotalWarModTool.Projects;
using TotalWarModTool.Session;
using TotalWarModTool.Settings;
using TotalWarModTool.Storage;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class MainViewModelTests
{
    private static readonly AppPaths Paths = new(@"X:\roaming", @"X:\local", @"X:\program");

    private static AppTexts NewTexts() => new(new UiText(@"X:\none"));

    // A controller of the synthetic game that finds nothing: the commands under test never reach the workspace.
    private static WorkspaceController NewWorkspace(AppTexts texts, IOperationRunner runner) =>
        new(TestGame.Profile, new GameSettings(), new GlobalSettings(), Paths, new NoSteam(), runner, new FakeAppLog(), texts);

    // The runner only needs the windows to lock; a plain Window is enough. Created on the STA thread of the test, like the runner's windows.
    private static (MainViewModel Vm, Window Main, List<(string Title, Exception Error)> Reported, List<ProgressWindow> Shown) CreateWithRunner(TimeSpan showDelay)
    {
        var main = new Window();
        var reported = new List<(string, Exception)>();
        var shown = new List<ProgressWindow>();
        var texts = NewTexts();
        var runner = new ProgressRunner(() => [main], texts, new FakeAppLog(), TimeProvider.System, (title, ex) => reported.Add((title, ex)), showDelay);
        runner.WindowShown += (_, w) => shown.Add(w);
        return (new MainViewModel(texts, Paths, _ => { }, runner, () => { }, NewWorkspace(texts, runner)), main, reported, shown);
    }

    // Waits for the effect, no fixed sleep. The Sta.RunAsync timeout is the outer guard.
    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected state was not reached in time.");
    }

    [Fact] public void OpenLogFolder_opens_the_log_directory()
    {
        string? opened = null;
        var vm = NewVm(p => opened = p);
        vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal(@"X:\local\logs", opened);
    }

    [Fact] public void OpenSettings_calls_the_delegate()
    {
        var opened = 0;
        var vm = NewVm(_ => { }, () => opened++);
        vm.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, opened);
    }

    [Fact] public void DebugThrow_throws_the_test_exception()
    {
        var vm = NewVm(_ => { });
        var ex = Assert.Throws<InvalidOperationException>(() => vm.DebugThrowCommand.Execute(null));
        Assert.Equal("Debug test exception", ex.Message);
    }

    // The run goes through the progress runner: the window is locked at once and the failure is reported with the operation title and the reason.
    [Fact] public void DebugFail_runs_through_the_runner_and_reports_the_failure() => Sta.RunAsync(async () =>
    {
        var (vm, main, reported, shown) = CreateWithRunner(TimeSpan.FromSeconds(30));
        vm.DebugFailCommand.Execute(null);
        Assert.False(main.IsHitTestVisible);

        await WaitUntil(() => reported.Count > 0);
        var (title, error) = Assert.Single(reported);
        Assert.Equal(("Run failing operation", "Debug failure"), (title, error.Message));
        Assert.IsType<IOException>(error);
        await WaitUntil(() => main.IsHitTestVisible);
        Assert.Empty(shown);
    });

    // Progress arrives as "n / 100", and Cancel ends the run quickly and without an error report. "Quickly" has to be clearly below the five
    // seconds the work takes on its own: if the work ignored the token, the lock would only be released after about five seconds.
    [Fact] public void DebugSlow_reports_progress_and_ends_quickly_on_cancel() => Sta.RunAsync(async () =>
    {
        var (vm, main, reported, shown) = CreateWithRunner(TimeSpan.FromMilliseconds(10));
        vm.DebugSlowCommand.Execute(null);
        Assert.False(main.IsHitTestVisible);

        await WaitUntil(() => shown.Count > 0 && shown[0].CountText.EndsWith(" / 100", StringComparison.Ordinal));
        var window = Assert.Single(shown);
        Assert.Equal("Run slow operation", window.Title);

        var cancelledAt = Environment.TickCount64;
        window.RequestCancel();
        await WaitUntil(() => main.IsHitTestVisible, timeoutMs: 2000);
        var endedAfterMs = Environment.TickCount64 - cancelledAt;

        Assert.True(endedAfterMs < 2000, $"The run took {endedAfterMs} ms to end after Cancel; the work does not honour the token.");
        var lastCount = int.Parse(window.CountText.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(lastCount, 1, 99);
        Assert.Empty(reported);
        Assert.False(window.IsVisible);
    });

    // For the commands that never start a run.
    private static MainViewModel NewVm(Action<string> openFolder, Action? openSettings = null)
    {
        var texts = NewTexts();
        var runner = new ProgressRunner(() => [], texts, new FakeAppLog(), TimeProvider.System, (_, _) => { });
        return new MainViewModel(texts, Paths, openFolder, runner, openSettings ?? (() => { }), NewWorkspace(texts, runner));
    }
}

public class MainViewModelWorkspaceTests
{
    private static (TestGame Game, WorkspaceController C, MainViewModel Vm) Create(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var c = game.NewController();
        return (game, c, new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => { }, c));
    }
    private static PackSource Pack(WorkspaceController c, string name) => c.Catalog!.Packs.Single(p => p.Name == name);

    [Fact] public async Task Opening_an_entry_adds_a_tab_and_a_second_request_only_activates_it()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray()), ("b.txt", "two"u8.ToArray())));
        using (game)
        {
            await c.LoadAsync();
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "a.txt"); await vm.OpenEntryAsync(Pack(c, "a.pack"), "b.txt");
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "A.TXT");
            Assert.Equal((2, "a.txt"), (vm.Tabs.Tabs.Count, vm.Tabs.SelectedTab!.Title));
            Assert.Equal(2, game.Runner.Titles.Count);                    // the third request read nothing
        }
    }

    [Fact] public async Task A_cancelled_open_adds_no_tab()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            await c.LoadAsync();
            game.Runner.CancelNext = true;
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "a.txt");
            Assert.Empty(vm.Tabs.Tabs);
        }
    }

    // Review Focus 5
    [Fact] public async Task Refresh_is_disabled_while_loading()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack"));
        using (game)
        {
            var load = c.LoadAsync();
            Assert.False(vm.RefreshCommand.CanExecute(null));
            await load;
            Assert.True(vm.RefreshCommand.CanExecute(null));
            vm.RefreshCommand.Execute(null);
            Assert.Equal(["Refresh pack list"], game.Runner.Titles);
        }
    }

    [Fact] public async Task Status_line_names_game_and_schema()
    {
        var (game, c, vm) = Create();
        using (game)
        {
            Assert.Equal("Game: Test Game", vm.StatusGame.Split(" – ")[0]);
            var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            await c.LoadAsync();
            Assert.Equal(("Game: Test Game", game.InstallPath, (string?)null), (vm.StatusGame, vm.StatusGameToolTip, vm.StatusHint));
            Assert.StartsWith("Schema: shipped with the app, ", vm.StatusSchema);
            Assert.Contains(nameof(MainViewModel.StatusGame), changed); Assert.Contains(nameof(MainViewModel.StatusSchema), changed);
            game.GameSettings.InstallPath = game.Root.File("nowhere");
            Assert.Equal(("Game: Test Game (not found)", (string?)null), (vm.StatusGame, vm.StatusGameToolTip));
        }
    }

    [Fact] public async Task Status_hint_names_a_broken_schema_override_and_a_missing_schema()
    {
        var (game, c, vm) = Create();
        using (game)
        {
            await c.LoadAsync();
            game.Settings.SchemaFolder = game.Root.File("no-schemas");
            Assert.Equal("The schema folder has no usable schema_test.ron (file not found). The shipped schema is used.", vm.StatusHint);
            File.Delete(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"));
            await c.ReloadSchemaAsync();
            Assert.StartsWith("No schema could be loaded (file not found).", vm.StatusHint);
            Assert.Equal("Schema: none", vm.StatusSchema);
        }
    }

    [Fact] public async Task A_pack_change_marks_the_open_tabs_of_that_pack_as_stale()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray()), ("b.txt", "two"u8.ToArray())));
        using (game)
        {
            await c.LoadAsync();
            var pack = Pack(c, "a.pack");
            await vm.OpenEntryAsync(pack, "a.txt");
            new PackBuilder().AddEntry("a.txt", "changed"u8.ToArray()).AddEntry("b.txt", "two!"u8.ToArray()).WriteTo(pack.Path);
            await vm.OpenEntryAsync(pack, "b.txt");
            Assert.Equal([true, false], vm.Tabs.Tabs.Select(t => t.IsStale));
            Assert.Equal("two!", Assert.IsType<TextTabViewModel>(vm.Tabs.Tabs[1]).Text);
        }
    }

    // Item A: the silent restore ignores clicks, so the user must see that the app is busy.
    [Fact] public async Task The_status_line_says_loading_during_the_silent_restore_only()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            await c.LoadAsync();
            Assert.Equal("Game: Test Game", vm.StatusGame);
            var during = new List<string>();
            c.Changed += (_, _) => { if (c.IsBusy) during.Add(vm.StatusGame); };
            await c.RestoreAsync(SessionState.Empty);
            Assert.Equal("Game: Test Game – Loading", Assert.Single(during));
            Assert.Equal("Game: Test Game", vm.StatusGame);
        }
    }

    // Item B
    private const string SchemaStaleText = "The schema changed. Close this tab and open the entry again to see it with the new schema.";
    private const string PackStaleText = "The pack was changed on disk. This tab shows the old content.";

    private static string UseOtherSchemaInstance(TestGame game)
    {
        var folder = game.Root.File("override"); Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"), Path.Combine(folder, "schema_test.ron"));
        return folder;
    }

    [Fact] public async Task A_schema_reload_marks_open_table_tabs_but_not_text_tabs_and_new_tabs_are_fresh()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes), ("db/y_tables/data__", TestGame.TableBytes),
            ("db/x_tables/more__", TestGame.TableBytes), ("a.txt", "one"u8.ToArray())));
        using (game)
        {
            await c.LoadAsync();
            var pack = Pack(c, "a.pack");
            foreach (var entry in new[] { "db/x_tables/data__", "db/y_tables/data__", "a.txt" })
            {
                await vm.OpenEntryAsync(pack, entry);
            }

            Assert.IsType<TableTabViewModel>(vm.Tabs.Tabs[0]); Assert.IsType<InfoTabViewModel>(vm.Tabs.Tabs[1]); Assert.IsType<TextTabViewModel>(vm.Tabs.Tabs[2]);
            Assert.Equal([false, false, false], vm.Tabs.Tabs.Select(t => t.IsStale));

            game.Settings.SchemaFolder = UseOtherSchemaInstance(game);                  // the fake runner reloads the schema at once
            Assert.Equal([true, true, false], vm.Tabs.Tabs.Select(t => t.IsStale));
            Assert.Equal([SchemaStaleText, SchemaStaleText, ""], vm.Tabs.Tabs.Select(t => t.StaleText));

            await vm.OpenEntryAsync(pack, "db/x_tables/more__");
            Assert.False(vm.Tabs.Tabs[3].IsStale);
        }
    }

    // A reload that resolves the same file with the same date is not a change of the schema.
    [Fact] public async Task A_refresh_with_an_unchanged_schema_marks_no_table_tab()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes)));
        using (game)
        {
            await c.LoadAsync();
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "db/x_tables/data__");
            var schemaBefore = c.Schema;

            vm.RefreshCommand.Execute(null);                                            // new schema instance, same file and date
            await c.ReloadSchemaAsync();

            Assert.NotSame(schemaBefore, c.Schema);
            Assert.False(vm.Tabs.Tabs[0].IsStale);
        }
    }

    [Fact] public async Task A_schema_file_with_another_date_marks_the_open_table_tabs()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes)));
        using (game)
        {
            await c.LoadAsync();
            var folder = UseOtherSchemaInstance(game);
            game.Settings.SchemaFolder = folder;                                        // shipped to override
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "db/x_tables/data__");
            Assert.False(vm.Tabs.Tabs[0].IsStale);

            var file = Path.Combine(folder, "schema_test.ron");
            File.AppendAllText(file, "\n");
            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddHours(1));
            await c.ReloadSchemaAsync();

            Assert.Equal((true, SchemaStaleText), (vm.Tabs.Tabs[0].IsStale, vm.Tabs.Tabs[0].StaleText));
        }
    }

    [Fact] public async Task A_pack_change_after_a_schema_change_switches_the_hint_to_the_pack_text()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes), ("b.txt", "two"u8.ToArray())));
        using (game)
        {
            await c.LoadAsync();
            var pack = Pack(c, "a.pack");
            await vm.OpenEntryAsync(pack, "db/x_tables/data__");
            game.Settings.SchemaFolder = UseOtherSchemaInstance(game);
            Assert.Equal(SchemaStaleText, vm.Tabs.Tabs[0].StaleText);

            new PackBuilder().AddEntry("db/x_tables/data__", TestGame.TableBytes).AddEntry("b.txt", "two!!!"u8.ToArray()).WriteTo(pack.Path);
            await vm.OpenEntryAsync(pack, "b.txt");
            Assert.Equal((true, PackStaleText), (vm.Tabs.Tabs[0].IsStale, vm.Tabs.Tabs[0].StaleText));
        }
    }

    // Item H.2: the button of the explorer leads to the page "Game"; the menu entry keeps its own action.
    [Fact] public void The_explorer_has_its_own_settings_action_when_one_is_given_else_it_shares_the_menu_action()
    {
        using var game = new TestGame(); var c = game.NewController();
        var calls = new List<string>();
        var vm = new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => calls.Add("menu"), c, () => calls.Add("game"));
        vm.Explorer.OpenSettingsCommand.Execute(null); vm.OpenSettingsCommand.Execute(null);
        Assert.Equal(["game", "menu"], calls);

        var shared = new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => calls.Add("shared"), c);
        shared.Explorer.OpenSettingsCommand.Execute(null);
        Assert.Equal("shared", calls[^1]);
    }

    // ---- projects --------------------------------------------------------------------------------------------------------------

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected state was not reached in time.");
    }

    [Fact] public async Task A_project_file_opens_in_a_tab_once()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        var state = game.NewProject(m.Projects, "Alpha", ("readme.txt", "hello"), (@"sub\more.txt", "more"));
        var main = m.Main; var titlesBefore = game.Runner.Titles.Count;

        await main.OpenProjectFileAsync(state.Id, "readme.txt");
        await main.OpenProjectFileAsync(state.Id, "readme.txt");
        await main.OpenProjectFileAsync(state.Id, @"sub\more.txt");
        await main.OpenProjectFileAsync(state.Id, @"SUB\More.txt");                       // the explorer gives '\', the tab has '/', case is ignored

        Assert.Equal(2, main.Tabs.Tabs.Count);
        var tab = main.Tabs.Tabs[0];
        Assert.StartsWith("project:", tab.PackPath);
        Assert.Equal((TabSource.ForProject(state.Id, "Alpha").Key, "readme.txt", "Alpha: readme.txt"), (tab.PackPath, tab.EntryPath, tab.ToolTip));
        Assert.Equal("sub/more.txt", main.Tabs.Tabs[1].EntryPath);
        Assert.Equal(["Open readme.txt", "Open more.txt"], game.Runner.Titles.Skip(titlesBefore));      // the repeated requests read nothing
        Assert.Same(main.Tabs.Tabs[1], main.Tabs.SelectedTab);
    }

    [Fact] public async Task A_project_file_of_an_unknown_project_or_a_cancelled_open_adds_no_tab()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        var state = game.NewProject(m.Projects, "Alpha", ("readme.txt", "hello"));
        await m.Main.OpenProjectFileAsync(Guid.NewGuid(), "readme.txt");
        game.Runner.CancelNext = true;
        await m.Main.OpenProjectFileAsync(state.Id, "readme.txt");
        Assert.Empty(m.Main.Tabs.Tabs);
    }

    // A tab of the entry that appears while the entry is being read (a session restore) wins: what was read is dropped, there is no second tab.
    private static OpenedEntry HandBuiltEntry(string entryPath) =>
        new(entryPath, EntryKind.Other, 1, 1, TotalWarModTool.PackFiles.CompressionFormat.None, TotalWarModTool.PackFiles.EntryProblem.None, null, false, null, null, null);

    [Fact] public async Task A_project_file_that_got_a_tab_while_it_was_read_gets_no_second_tab()
    {
        using var game = new TestGame();
        var c = game.NewController(); var runner = new ManualOperationRunner(); var m = game.NewMain(c, runner);
        await c.LoadAsync();
        var state = game.NewProject(m.Projects, "Alpha", ("readme.txt", "hello"));
        var source = TabSource.ForProject(state.Id, "Alpha");

        var opening = m.Main.OpenProjectFileAsync(state.Id, @"readme.txt");
        await runner.WaitForRun();
        m.Main.Tabs.Add(source, HandBuiltEntry("readme.txt"), null);               // the restore was faster
        var existing = m.Main.Tabs.Tabs[0];
        m.Main.Tabs.Add(source, HandBuiltEntry("other.txt"), null);                // another tab is selected meanwhile
        runner.Complete();
        await opening.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(["readme.txt", "other.txt"], m.Main.Tabs.Tabs.Select(t => t.EntryPath));
        Assert.Same(existing, m.Main.Tabs.SelectedTab);                             // the existing tab is shown
    }

    [Fact] public async Task A_pack_entry_that_got_a_tab_while_it_was_read_gets_no_second_tab()
    {
        using var game = new TestGame(); game.AddPack("a.pack", ("a.txt", "one"u8.ToArray()));
        var runner = new ManualOperationRunner(); var c = game.NewController(runner: runner);
        var vm = new MainViewModel(game.Texts, game.Paths, _ => { }, runner, () => { }, c);
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");

        var opening = vm.OpenEntryAsync(pack, "a.txt");
        while (!opening.IsCompleted)
        {
            await Task.WhenAny(opening, runner.WaitForRun());
            if (opening.IsCompleted)
            {
                break;
            }

            if (vm.Tabs.Tabs.Count == 0)
            {
                vm.Tabs.Add(pack, HandBuiltEntry("a.txt"), null);                    // the restore was faster
            }

            runner.Complete();
        }

        await opening;
        Assert.Equal(["a.txt"], vm.Tabs.Tabs.Select(t => t.EntryPath));
        Assert.IsType<InfoTabViewModel>(vm.Tabs.Tabs[0]);                           // the one that was there first
    }

    private static IEnumerable<ProjectNode> ProjectNodesOf(ExplorerViewModel explorer) =>
        explorer.Groups.OfType<ProjectsGroupNode>().SelectMany(group => group.Children).OfType<ProjectNode>();

    [Fact] public async Task The_remove_entry_of_the_explorer_follows_the_unsaved_changes_of_the_tabs()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c, withDocuments: true);
        await c.LoadAsync();
        var table = TotalWarModTool.Tables.TableTsv.Write("x_tables", 0, "db/x_tables/a",
            new TotalWarModTool.Schema.TableDefinition(0, [new TotalWarModTool.Schema.FieldDefinition("value", TotalWarModTool.Schema.FieldType.I32, "I32", false, null)]),
            [[42]]).Text!;
        var state = game.NewProject(m.Projects, "Alpha", (@"db\x_tables\a.tsv", table));
        await m.Main.OpenProjectFileAsync(state.Id, @"db\x_tables\a.tsv");
        var tab = Assert.IsType<EditableRowsTabViewModel>(Assert.Single(m.Main.Tabs.Tabs));
        NodeAction Remove() => Assert.Single(ProjectNodesOf(m.Main.Explorer).Single().Actions,
            action => action.Header == game.Texts.ProjectActionRemove);
        Assert.True(Remove().Command.CanExecute(null));

        tab.Rows[0][0] = "50";                                                     // the menu of the node is built again: no stale reason

        Assert.False(Remove().Command.CanExecute(null));
        Assert.Equal(game.Texts.ProjectRemoveUnsaved, Remove().DisabledReason);

        tab.Document.MarkSaved();

        Assert.True(Remove().Command.CanExecute(null));
        Assert.Null(Remove().DisabledReason);
    }

    [Fact] public async Task The_remove_entry_of_the_explorer_follows_a_cell_that_is_being_edited()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c, withDocuments: true);
        await c.LoadAsync();
        var table = TotalWarModTool.Tables.TableTsv.Write("x_tables", 0, "db/x_tables/a",
            new TotalWarModTool.Schema.TableDefinition(0, [new TotalWarModTool.Schema.FieldDefinition("value", TotalWarModTool.Schema.FieldType.I32, "I32", false, null)]),
            [[42]]).Text!;
        var state = game.NewProject(m.Projects, "Alpha", (@"db\x_tables\a.tsv", table));
        await m.Main.OpenProjectFileAsync(state.Id, @"db\x_tables\a.tsv");
        var tab = Assert.IsType<EditableRowsTabViewModel>(Assert.Single(m.Main.Tabs.Tabs));
        NodeAction Remove() => Assert.Single(ProjectNodesOf(m.Main.Explorer).Single().Actions,
            action => action.Header == game.Texts.ProjectActionRemove);
        Assert.True(Remove().Command.CanExecute(null));

        tab.SetPendingEdit(true);                                                  // a value is typed into a cell, the tab is not modified yet

        Assert.False(tab.IsModified);
        Assert.False(Remove().Command.CanExecute(null));
        Assert.Equal(game.Texts.ProjectRemoveUnsaved, Remove().DisabledReason);

        tab.SetPendingEdit(false);                                                 // the cell is left without a change

        Assert.True(Remove().Command.CanExecute(null));
        Assert.Null(Remove().DisabledReason);
    }

    [Fact] public async Task Refresh_reloads_the_projects()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        var state = game.NewProject(m.Projects, "Alpha");
        Assert.True(m.Projects.Projects[0].IsUsable);

        File.Delete(Path.Combine(state.Folder, "twmt-project.json"));
        m.Main.RefreshCommand.Execute(null);

        await WaitUntil(() => !m.Projects.Projects[0].IsUsable);
        Assert.True(m.Main.Explorer.Groups[0].Children.OfType<ProjectNode>().Single().HasProblem);
    }

    [Fact] public async Task Refresh_also_checks_the_open_project_files_for_outside_changes()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c, withDocuments: true);
        await c.LoadAsync();
        var definition = new TotalWarModTool.Schema.TableDefinition(0, [new TotalWarModTool.Schema.FieldDefinition("value", TotalWarModTool.Schema.FieldType.I32, "I32", false, null)]);
        string Table(int value) => TotalWarModTool.Tables.TableTsv.Write("x_tables", 0, "db/x_tables/a", definition, [[value]]).Text!;
        var state = game.NewProject(m.Projects, "Alpha", (@"db\x_tables\a.tsv", Table(42)));
        await m.Main.OpenProjectFileAsync(state.Id, @"db\x_tables\a.tsv");
        var tab = Assert.Single(m.Main.Tabs.Tabs);
        Assert.False(tab.IsStale);

        // The project itself does not change, so no event of the project list would tell: only the check after F5 finds it.
        var path = Path.Combine(state.Folder, "pack", "db", "x_tables", "a.tsv");
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, Table(43));
        File.SetLastWriteTimeUtc(path, before.AddMinutes(5));
        m.Main.RefreshCommand.Execute(null);

        await WaitUntil(() => tab.IsStale);
        Assert.Equal(game.Texts.TabStaleChanged, tab.StaleText);
    }

    [Fact] public async Task The_project_status_shows_the_first_line_and_the_whole_text_as_tool_tip()
    {
        using var game = new TestGame();
        var store = new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "schemaVersion": 99, "projects": [] }""");          // a newer list: it is not saved, so a note follows the status
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        var main = m.Main;
        Assert.Equal((false, (string?)null, (string?)null), (main.HasProjectStatus, main.ProjectStatusLine, main.ProjectStatusToolTip));
        var changed = new List<string?>(); main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        m.Dialogs.ProjectAnswer = new ProjectInput("Alpha", game.NewFolder("alpha"), "alpha.pack");
        main.Projects!.NewProjectCommand.Execute(null);

        var texts = game.Texts;
        Assert.Equal(texts.ProjectCreated("Alpha") + "\n" + texts.ProjectListNotSaved, main.Projects.StatusMessage);
        Assert.Equal((true, texts.ProjectCreated("Alpha"), main.Projects.StatusMessage), (main.HasProjectStatus, main.ProjectStatusLine, main.ProjectStatusToolTip));
        Assert.Contains(nameof(MainViewModel.ProjectStatusLine), changed);
        Assert.Contains(nameof(MainViewModel.ProjectStatusToolTip), changed);
        Assert.Contains(nameof(MainViewModel.HasProjectStatus), changed);
    }

    [Fact] public async Task Without_projects_there_is_no_project_menu_state()
    {
        var (game, c, vm) = Create();
        using (game)
        {
            await c.LoadAsync();
            Assert.Null(vm.Projects);
            Assert.False(vm.HasProjects);
            Assert.Equal((false, (string?)null), (vm.HasProjectStatus, vm.ProjectStatusLine));
            await vm.OpenProjectFileAsync(Guid.NewGuid(), "a.txt");                      // nothing to open it with: no tab, no exception
            Assert.Empty(vm.Tabs.Tabs);
        }
    }

    [Fact] public async Task The_menu_tool_tips_of_build_name_the_reason()
    {
        using var game = new TestGame(inSteamLibrary: true);                               // the game is not found: nothing can be installed
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        var texts = game.Texts;
        Assert.Equal((texts.ProjectNoneActive, texts.ProjectNoneActive), (m.Main.ProjectBuildToolTip, m.Main.ProjectBuildAndInstallToolTip));
        game.NewProject(m.Projects, "Alpha");
        Assert.Equal(((string?)null, texts.InstallNoGameFolder), (m.Main.ProjectBuildToolTip, m.Main.ProjectBuildAndInstallToolTip));
    }

    // The reason order of the build entries: the project first, then "busy"; nothing is disabled without a text.
    [Fact] public async Task The_menu_tool_tips_say_busy_while_the_workspace_loads()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c);
        game.NewProject(m.Projects, "Alpha");
        var texts = game.Texts;
        var changed = new List<string?>(); m.Main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var load = c.LoadAsync();                                                    // returns at its first await: the workspace is busy
        Assert.True(c.IsBusy);
        Assert.Equal(texts.ProjectBusy, m.Main.ProjectBuildToolTip);
        Assert.Equal(texts.ProjectBusy, m.Main.ProjectBuildAndInstallToolTip);
        Assert.Equal(texts.ProjectBusy, m.Main.ProjectImportToolTip);
        Assert.Contains(nameof(MainViewModel.ProjectBuildToolTip), changed);
        Assert.Contains(nameof(MainViewModel.ProjectImportToolTip), changed);

        await load;
        Assert.Equal(((string?)null, (string?)null), (m.Main.ProjectBuildToolTip, m.Main.ProjectBuildAndInstallToolTip));
        Assert.Null(m.Main.ProjectImportToolTip);
    }

    [Fact] public void Without_a_game_profile_the_menu_tool_tips_name_the_profile()
    {
        using var game = new TestGame();
        var noProfile = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        var projects = new ProjectsController(null, new ProjectListLoadResult(ProjectList.Empty, LoadStatus.Missing, null), noProfile, game.Runner, game.Log,
            game.Texts, TimeProvider.System);
        var main = new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => { }, noProfile, null, projects,
            new ProjectsViewModel(projects, new FakeProjectDialogs(), game.Texts));
        Assert.Equal((game.Texts.ProjectNoGameProfile, game.Texts.ProjectNoGameProfile), (main.ProjectBuildToolTip, main.ProjectBuildAndInstallToolTip));
        Assert.Equal(game.Texts.ProjectNoGameProfile, main.ProjectImportToolTip);
    }

    [Fact] public async Task Removing_a_project_closes_its_tabs_and_leaves_the_others()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("x.txt", [1]));
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        var alpha = game.NewProject(m.Projects, "Alpha", ("a.txt", "a"), ("b.txt", "b"));
        var beta = game.NewProject(m.Projects, "Beta", ("c.txt", "c"));
        var main = m.Main;
        await main.OpenProjectFileAsync(alpha.Id, "a.txt");
        await main.OpenProjectFileAsync(beta.Id, "c.txt");
        await main.OpenEntryAsync(c.Catalog!.Packs.Single(pack => pack.Name == "a.pack"), "x.txt");
        await main.OpenProjectFileAsync(alpha.Id, "b.txt");
        Assert.Equal(4, main.Tabs.Tabs.Count);

        main.Projects!.Remove(alpha.Id);

        Assert.Equal([TabSource.KeyOf(beta.Id), c.Catalog.Packs.Single(pack => pack.Name == "a.pack").Path], main.Tabs.Tabs.Select(tab => tab.PackPath));
    }

    // The status line and the explorer both follow a language switch (the explorer has its own test).
    [Fact] public async Task The_status_line_follows_a_language_switch()
    {
        var locales = Path.Combine(RepoRoot.Find(), "locales");
        using var game = new TestGame();
        var ui = new UiText(locales); var texts = new AppTexts(ui);
        var c = new WorkspaceController(TestGame.Profile, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, texts);
        var vm = new MainViewModel(texts, game.Paths, _ => { }, game.Runner, () => { }, c);
        await c.LoadAsync();
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        ui.Load("de");
        Assert.Equal("Spiel: Test Game", vm.StatusGame);
        Assert.StartsWith("Schema: mit der App geliefert", vm.StatusSchema);
        Assert.Contains(nameof(MainViewModel.StatusGame), changed); Assert.Contains(nameof(MainViewModel.StatusHint), changed);
    }

    // ---- file operations -------------------------------------------------------------------------------------------------------

    [Fact] public async Task The_project_menu_has_new_table_and_new_loc_file()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c, withFiles: true);
        await c.LoadAsync();
        var main = m.Main; var texts = game.Texts;
        Assert.True(main.HasProjectFiles);

        // No active project: both entries say so and cannot run.
        Assert.Equal((texts.ProjectNoneActive, false, false), (main.ProjectNewFileToolTip, main.NewTableCommand.CanExecute(null), main.NewLocCommand.CanExecute(null)));
        var changed = new List<string?>(); main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var project = game.NewProject(m.Projects, "Alpha");
        Assert.Equal(((string?)null, true, true), (main.ProjectNewFileToolTip, main.NewTableCommand.CanExecute(null), main.NewLocCommand.CanExecute(null)));
        Assert.Contains(nameof(MainViewModel.ProjectNewFileToolTip), changed);

        // Another operation runs: the busy text.
        game.Runner.IsRunning = true;
        Assert.Equal((texts.ProjectBusy, false, false), (main.ProjectNewFileToolTip, main.NewTableCommand.CanExecute(null), main.NewLocCommand.CanExecute(null)));
        game.Runner.IsRunning = false;

        // The commands act on the active project.
        m.FileDialogs!.NameAnswers.Enqueue("mine");
        main.NewLocCommand.Execute(null);
        var ask = Assert.Single(m.FileDialogs.NameAsks);
        Assert.Equal(texts.FileNewLocTitle, ask.Title);
        Assert.True(File.Exists(Path.Combine(project.Folder, "pack", "text", "db", "mine.loc.tsv")));
        await WaitUntil(() => main.Tabs.Tabs.Count == 1);

        m.FileDialogs.TableAnswers.Enqueue(new NewTableInput("x_tables", "mine"));
        main.NewTableCommand.Execute(null);
        Assert.Equal(["x_tables"], Assert.Single(m.FileDialogs.TableAsks).Tables);
        Assert.True(File.Exists(Path.Combine(project.Folder, "pack", "db", "x_tables", "mine.tsv")));
    }

    [Fact] public async Task Without_the_file_operations_the_new_file_entries_are_off()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c);
        await c.LoadAsync();
        game.NewProject(m.Projects, "Alpha");
        Assert.Equal((false, false, false), (m.Main.HasProjectFiles, m.Main.NewTableCommand.CanExecute(null), m.Main.NewLocCommand.CanExecute(null)));
    }

    [Fact] public async Task The_status_line_shows_the_result_of_the_last_file_action()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c, withFiles: true);
        await c.LoadAsync();
        var project = game.NewProject(m.Projects, "Alpha");
        var main = m.Main; var texts = game.Texts;
        var changed = new List<string?>(); main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal((false, (string?)null, (string?)null), (main.HasProjectStatus, main.ProjectStatusLine, main.ProjectStatusToolTip));

        // A file action: its result is in the status line, at the place of the project commands.
        m.FileDialogs!.NameAnswers.Enqueue("mine");
        main.NewLocCommand.Execute(null);
        await WaitUntil(() => main.Tabs.Tabs.Count == 1);                            // the new file was opened; the next steps start from a quiet state
        var created = texts.FileCreatedStatus(@"text\db\mine.loc.tsv");
        Assert.Equal((true, created, created), (main.HasProjectStatus, main.ProjectStatusLine, main.ProjectStatusToolTip));
        Assert.Contains(nameof(MainViewModel.ProjectStatusLine), changed);
        Assert.Contains(nameof(MainViewModel.ProjectStatusToolTip), changed);
        Assert.Contains(nameof(MainViewModel.HasProjectStatus), changed);

        // A project command after it: the newer message wins.
        m.Dialogs.ProjectAnswer = new ProjectInput("Beta", game.NewFolder("beta"), "beta.pack");
        main.Projects!.NewProjectCommand.Execute(null);
        Assert.Equal(texts.ProjectCreated("Beta"), main.ProjectStatusLine);

        // And a file action after that wins again, with its whole text as the tool tip.
        await m.Files!.DeleteAsync(project.Id, @"text\db\mine.loc.tsv");
        var deleted = texts.FileDeletedStatus(@"text\db\mine.loc.tsv");
        Assert.Equal((deleted, deleted), (main.ProjectStatusLine, main.ProjectStatusToolTip));

        // A file command that has nothing to say clears the line: the older message of the project commands does not come back.
        await m.Files.DeleteAsync(Guid.NewGuid(), "x.lua");
        Assert.Equal((false, (string?)null, (string?)null), (main.HasProjectStatus, main.ProjectStatusLine, main.ProjectStatusToolTip));
    }

    [Fact] public async Task A_new_file_opens_in_a_tab_and_its_project_is_expanded()
    {
        using var game = new TestGame();
        var c = game.NewController(); var m = game.NewMain(c, withFiles: true);
        await c.LoadAsync();
        var project = game.NewProject(m.Projects, "Alpha", ("a.lua", "x"));
        var main = m.Main;
        var node = main.Explorer.Groups[0].Children.OfType<ProjectNode>().Single();
        Assert.False(node.IsExpanded);

        m.FileDialogs!.NameAnswers.Enqueue("mine");
        main.NewLocCommand.Execute(null);
        await WaitUntil(() => main.Tabs.Tabs.Count == 1);

        var tab = main.Tabs.SelectedTab!;
        Assert.Equal((TabSource.KeyOf(project.Id), "text/db/mine.loc.tsv"), (tab.PackPath, tab.EntryPath));
        Assert.True(node.IsExpanded);
        Assert.Equal(["text", "a.lua"], node.Children.Select(n => n.Name));
    }
}
