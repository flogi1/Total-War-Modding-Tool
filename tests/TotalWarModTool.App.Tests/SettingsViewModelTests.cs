using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsViewModelTests
{
    private static readonly UiLanguageOption[] Languages = [new("en", "English"), new("de", "Deutsch")];
    private static string Locales => Path.Combine(RepoRoot.Find(), "locales");

    // The general settings do not depend on a game: a view model without a profile (nothing is located or loaded).
    internal static SettingsViewModel NewViewModel(GlobalSettings settings, AppTexts texts, IReadOnlyList<UiLanguageOption>? languages = null)
    {
        var gameSettings = new GameSettings();
        var root = Path.Combine(Path.GetTempPath(), "twmt-none");
        var paths = new AppPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"), Path.Combine(root, "program"));
        var workspace = new WorkspaceController(null, gameSettings, settings, paths, new NoSteam(), new FakeOperationRunner(), new FakeAppLog(), texts);
        return new SettingsViewModel(settings, texts, languages ?? Languages, gameSettings, workspace, _ => null);
    }

    [Fact] public void Selection_reflects_and_writes_settings()
    {
        var settings = new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark };
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));
        Assert.Equal(("de", ThemeChoice.Dark), (vm.SelectedLanguage.Code, vm.SelectedTheme.Value));
        vm.SelectedLanguage = Languages[0]; vm.SelectedTheme = vm.ThemeOptions.Single(o => o.Value == ThemeChoice.Light);
        Assert.Equal(("en", ThemeChoice.Light), (settings.Language, settings.Theme));
    }

    [Fact] public void Theme_options_are_System_Light_Dark_with_translated_names()
    {
        var ui = new UiText(Locales);
        var vm = NewViewModel(new GlobalSettings(), new AppTexts(ui));
        Assert.Equal(["Same as Windows", "Light", "Dark"], vm.ThemeOptions.Select(o => o.Name));
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        ui.Load("de");
        Assert.Equal(["Wie Windows", "Hell", "Dunkel"], vm.ThemeOptions.Select(o => o.Name));
        Assert.Contains(nameof(SettingsViewModel.ThemeOptions), changed);
        Assert.Contains(nameof(SettingsViewModel.SelectedTheme), changed);
    }

    [Fact] public void Selection_follows_external_settings_change()
    {
        var settings = new GlobalSettings();
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));
        settings.Language = "de"; settings.Theme = ThemeChoice.Dark;
        Assert.Equal(("de", ThemeChoice.Dark), (vm.SelectedLanguage.Code, vm.SelectedTheme.Value));
    }

    // The getters read the settings anyway; this checks that the bound selection boxes are told to look again.
    [Fact] public void External_settings_change_raises_PropertyChanged_for_the_selection()
    {
        var settings = new GlobalSettings();
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Language = "de";
        Assert.Equal([nameof(SettingsViewModel.SelectedLanguage)], changed);

        changed.Clear();
        settings.Theme = ThemeChoice.Dark;
        Assert.Equal([nameof(SettingsViewModel.SelectedTheme)], changed);

        // Not a setting of this window: no notification.
        changed.Clear();
        settings.MainWindow = new WindowPlacement(1, 2, 300, 400, false);
        Assert.Empty(changed);
    }

    // The language of a hand-edited file may not be in the list (after the start-up repair it normally is). Showing English is only a
    // display fallback: the setting is left alone, so nothing is written behind the user's back.
    [Fact] public void Language_that_is_not_offered_shows_English_and_leaves_the_settings_alone()
    {
        var settings = new GlobalSettings { Language = "xx" };
        var changes = new List<string>(); settings.Changed += (_, property) => changes.Add(property);
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));

        Assert.Equal("en", vm.SelectedLanguage.Code);
        Assert.Equal("xx", settings.Language);
        Assert.Empty(changes);
    }

    // WPF writes null into the bound property when a selection box loses its selection, e.g. when the list of theme options is replaced
    // after a language switch. That must neither throw nor reset the setting.
    [Fact] public void Null_selection_is_ignored()
    {
        var settings = new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark };
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));

        vm.SelectedLanguage = null!;
        vm.SelectedTheme = null!;

        Assert.Equal(("de", ThemeChoice.Dark), (settings.Language, settings.Theme));
    }
}

public class SettingsViewModelGameTests
{
    private static async Task<(TestGame Game, WorkspaceController C, SettingsViewModel Vm, List<string?> Picked)> Create(string? pick = null)
    {
        var game = new TestGame(); game.AddPack("a.pack");
        var c = game.NewController(); await c.LoadAsync();
        var picked = new List<string?>();
        var vm = new SettingsViewModel(game.Settings, game.Texts, [new("en", "English")], game.GameSettings, c, start => { picked.Add(start); return pick; });
        return (game, c, vm, picked);
    }

    [Fact] public async Task Shows_the_game_and_the_manual_path()
    {
        var (game, _, vm, _) = await Create();
        using (game) Assert.Equal((true, "Test Game", game.InstallPath, (string?)null, ""), (vm.HasGame, vm.GameName, vm.GamePath, vm.GamePathError, vm.GamePathInfo));
    }

    // Review Focus 4
    [Fact] public async Task Invalid_game_path_is_rejected_and_keeps_the_old_value()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            var wrong = game.Root.File("wrong"); Directory.CreateDirectory(wrong);
            vm.GamePath = wrong;
            Assert.Equal("This folder has no \"packs\" folder. Choose the folder that contains it.", vm.GamePathError);
            Assert.Equal((game.InstallPath, wrong, GameStatus.Found), (game.GameSettings.InstallPath, vm.GamePath, c.Status));
            Assert.Empty(game.Runner.Titles);
            vm.GamePath = "  " + game.InstallPath + "  ";
            Assert.Equal(((string?)null, game.InstallPath), (vm.GamePathError, vm.GamePath));
        }
    }

    [Fact] public async Task Clearing_the_path_switches_to_automatic_detection()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.ClearGamePathCommand.Execute(null);
            Assert.Equal(((string?)null, "", GameStatus.NotFound), (game.GameSettings.InstallPath, vm.GamePath, c.Status));
            Assert.Equal("The game was not found automatically. Choose the game folder.", vm.GamePathInfo);
            Assert.Contains(nameof(SettingsViewModel.GamePathInfo), changed);
        }
    }

    [Fact] public async Task Browse_takes_the_chosen_folder_and_a_cancel_changes_nothing()
    {
        var (game, _, vm, picked) = await Create();
        using (game)
        {
            vm.BrowseGamePathCommand.Execute(null);                       // pickFolder returns null
            Assert.Equal((game.InstallPath, game.InstallPath), (game.GameSettings.InstallPath, Assert.Single(picked)));
        }
        var other = await Create(pick: null);
        using (other.Game)
        {
            // a second install folder with the data folder
            var second = other.Game.Root.File("second"); Directory.CreateDirectory(Path.Combine(second, "packs"));
            var vm2 = new SettingsViewModel(other.Game.Settings, other.Game.Texts, [new("en", "English")], other.Game.GameSettings, other.C, _ => second);
            vm2.BrowseGamePathCommand.Execute(null);
            Assert.Equal(second, other.Game.GameSettings.InstallPath);
        }
    }

    [Fact] public async Task Schema_folder_is_checked_and_its_status_is_shown()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            Assert.StartsWith("In use: shipped with the app, ", vm.SchemaStatus);
            vm.SchemaFolder = game.Root.File("missing");
            Assert.Equal(("This folder does not exist.", (string?)null), (vm.SchemaFolderError, game.Settings.SchemaFolder));
            var empty = game.Root.File("empty"); Directory.CreateDirectory(empty);
            vm.SchemaFolder = empty;
            Assert.Equal(((string?)null, empty), (vm.SchemaFolderError, game.Settings.SchemaFolder));
            Assert.Contains("The schema folder has no usable schema_test.ron (file not found).", vm.SchemaStatus);
            File.Copy(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"), Path.Combine(empty, "schema_test.ron"));
            await c.ReloadSchemaAsync();
            Assert.StartsWith("In use: from the schema folder, ", vm.SchemaStatus);
            vm.ClearSchemaFolderCommand.Execute(null);
            Assert.Equal(((string?)null, ""), (game.Settings.SchemaFolder, vm.SchemaFolder));
        }
    }

    [Fact] public void Without_a_profile_the_game_controls_are_off()
    {
        using var game = new TestGame();
        var c = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        var vm = new SettingsViewModel(game.Settings, game.Texts, [new("en", "English")], game.GameSettings, c, _ => null);
        Assert.Equal((false, ""), (vm.HasGame, vm.GameName));
        var before = game.GameSettings.InstallPath;
        vm.GamePath = @"D:\x";
        Assert.Equal(before, game.GameSettings.InstallPath);              // without a profile nothing is committed
    }
}
