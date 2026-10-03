using System.Windows;
using TotalWarModTool.Localization;
using TotalWarModTool.Session;
using TotalWarModTool.Settings;
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
}
