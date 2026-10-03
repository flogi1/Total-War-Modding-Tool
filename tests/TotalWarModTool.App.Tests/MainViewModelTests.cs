using System.Windows;
using TotalWarModTool.Localization;
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
