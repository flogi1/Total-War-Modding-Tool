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

    [Fact] public void SelectedCategory_is_stored_in_the_settings_and_read_back()
    {
        var settings = new GlobalSettings { SettingsCategory = SettingsViewModel.TilesCategory };
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));
        Assert.Equal(SettingsViewModel.TilesCategory, vm.SelectedCategory);

        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.SelectedCategory = SettingsViewModel.ToolbarCategory;
        Assert.Equal(SettingsViewModel.ToolbarCategory, settings.SettingsCategory);
        Assert.Equal([nameof(SettingsViewModel.SelectedCategory)], changed);

        // A list box that loses its selection writes -1, and a page that does not exist is no choice: both change nothing.
        vm.SelectedCategory = -1;
        vm.SelectedCategory = SettingsViewModel.ToolbarCategory + 1;
        Assert.Equal(SettingsViewModel.ToolbarCategory, settings.SettingsCategory);

        // A value from a hand-edited file that names no page shows the first page.
        settings.SettingsCategory = 99;
        Assert.Equal(0, vm.SelectedCategory);
        Assert.Equal(0, NewViewModel(new GlobalSettings { SettingsCategory = 99 }, new AppTexts(new UiText(Locales))).SelectedCategory);
    }

    [Fact] public void Bar_properties_pass_the_settings_through_and_follow_them()
    {
        var settings = new GlobalSettings();
        var vm = NewViewModel(settings, new AppTexts(new UiText(Locales)));
        Assert.Same(settings, vm.Settings);

        vm.ToolbarPosition = ToolbarPosition.Right; vm.ToolbarLabelsVisible = false; vm.TileBarVisible = false;
        vm.TileSize = TileSize.Compact; vm.TileLabelsVisible = false;
        Assert.Equal((ToolbarPosition.Right, false, false, TileSize.Compact, false),
            (settings.ToolbarPosition, settings.ToolbarLabelsVisible, settings.TileBarVisible, settings.TileSize, settings.TileLabelsVisible));

        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        settings.ToolbarPosition = ToolbarPosition.Left; settings.TileSize = TileSize.Medium;
        Assert.Equal([nameof(SettingsViewModel.ToolbarPosition), nameof(SettingsViewModel.TileSize)], changed);
        Assert.Equal((ToolbarPosition.Left, TileSize.Medium), (vm.ToolbarPosition, vm.TileSize));
    }

    [Fact] public void The_next_separator_follows_the_highest_number_in_use()
    {
        Assert.Equal("sep:5", SettingsViewModel.NextSeparator(ToolbarCatalog.DefaultOrder));
        Assert.Equal("sep:1", SettingsViewModel.NextSeparator([ToolbarItemIds.Save]));
        Assert.Equal("sep:8", SettingsViewModel.NextSeparator([ToolbarCatalog.Separator(7), ToolbarItemIds.Save, ToolbarCatalog.Separator(2)]));
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

    // Item D.2: a committed folder is stored the way Windows writes it, so the same folder is never stored in two spellings of the separators.
    [Fact] public async Task A_committed_game_folder_is_stored_normalised()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            var second = game.Root.File("second"); Directory.CreateDirectory(Path.Combine(second, "packs"));
            vm.GamePath = second.Replace('\\', '/') + "/";
            Assert.Equal((second, second, GameStatus.Found), (game.GameSettings.InstallPath, vm.GamePath, c.Status));
            Assert.Null(vm.GamePathError);
        }
    }

    [Fact] public async Task A_game_folder_that_cannot_be_a_path_is_rejected_as_invalid()
    {
        var (game, _, vm, _) = await Create();
        using (game)
        {
            vm.GamePath = "folder\0name";
            Assert.NotNull(vm.GamePathError);
            Assert.Equal(game.InstallPath, game.GameSettings.InstallPath);
        }
    }

    [Fact] public async Task A_committed_schema_folder_is_stored_normalised_and_one_that_cannot_be_a_path_is_rejected()
    {
        var (game, _, vm, _) = await Create();
        using (game)
        {
            var folder = game.Root.File("schemas"); Directory.CreateDirectory(folder);
            vm.SchemaFolder = folder.Replace('\\', '/') + "/";
            Assert.Equal((folder, folder, (string?)null), (game.Settings.SchemaFolder, vm.SchemaFolder, vm.SchemaFolderError));
            vm.SchemaFolder = "folder\0name";
            Assert.Equal(("This folder does not exist.", folder), (vm.SchemaFolderError, game.Settings.SchemaFolder));
        }
    }

    // Item H.1: the folder in game-settings.json can be wrong from the start; the page says so without being asked to commit anything.
    [Fact] public async Task A_stored_game_folder_without_a_data_folder_shows_the_message_until_it_is_fixed_or_cleared()
    {
        using var game = new TestGame(); game.AddPack("a.pack");
        var stored = game.Root.File("elsewhere"); Directory.CreateDirectory(stored);
        game.GameSettings.InstallPath = stored;
        var c = game.NewController(); await c.LoadAsync();
        var vm = new SettingsViewModel(game.Settings, game.Texts, [new("en", "English")], game.GameSettings, c, _ => null);
        const string message = "This folder has no \"packs\" folder. Choose the folder that contains it.";
        Assert.Equal((stored, message), (vm.GamePath, vm.GamePathError));

        vm.GamePath = game.InstallPath;                                                // fixed
        Assert.Equal(((string?)null, game.InstallPath), (vm.GamePathError, vm.GamePath));

        game.GameSettings.InstallPath = stored;                                        // wrong again, from outside
        Assert.Equal((stored, message), (vm.GamePath, vm.GamePathError));
        vm.ClearGamePathCommand.Execute(null);                                         // cleared
        Assert.Equal(((string?)null, ""), (vm.GamePathError, vm.GamePath));
    }

    // Item H.3: a window that is opened again must not show what was typed into the one before.
    [Fact] public async Task ResetInputs_shows_the_stored_values_again()
    {
        var (game, _, vm, _) = await Create();
        using (game)
        {
            vm.GamePath = game.Root.File("wrong");
            vm.SchemaFolder = game.Root.File("missing");
            Assert.NotNull(vm.GamePathError); Assert.NotNull(vm.SchemaFolderError);
            var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.ResetInputs();

            Assert.Equal((game.InstallPath, (string?)null, "", (string?)null), (vm.GamePath, vm.GamePathError, vm.SchemaFolder, vm.SchemaFolderError));
            Assert.All(new[] { nameof(SettingsViewModel.GamePath), nameof(SettingsViewModel.GamePathError), nameof(SettingsViewModel.SchemaFolder),
                nameof(SettingsViewModel.SchemaFolderError) }, name => Assert.Contains(name, changed));
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
