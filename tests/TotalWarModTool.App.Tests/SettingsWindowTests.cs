using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsWindowTests
{
    private static readonly UiLanguageOption[] Languages = [new("en", "English"), new("de", "Deutsch")];

    private sealed class FakeSystemTheme : ISystemThemeSource { public bool IsDark() => false; }

    private sealed record Setup(GlobalSettings Settings, UiText Ui, SettingsViewModel ViewModel, SettingsWindow Window);

    // The same chain as in the app: window (shown, as in the app), view model, settings, applier, texts. Everything runs on the STA thread of the test.
    private static Setup Create(GlobalSettings settings)
    {
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var texts = new AppTexts(ui);
        var theme = new ThemeManager(new ResourceDictionary(), new FakeSystemTheme());
        theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme, new FakeAppLog());

        var viewModel = SettingsViewModelTests.NewViewModel(settings, texts, Languages);
        var window = new SettingsWindow(viewModel);
        window.Show();
        Pump(window);
        return new Setup(settings, ui, viewModel, window);
    }

    // Bindings to the DataContext are delivered by the dispatcher at DataBind priority; a window nobody pumps needs this nudge.
    private static void Pump(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // What the box really has selected, not only what its SelectedItem property says: a box can hold a SelectedItem that it has not selected
    // (SelectedIndex -1, nothing shown, the items of the drop-down are not marked), which is what the user would see as an empty box.
    private static void AssertSelected(ComboBox box, int index, object item)
    {
        Assert.Equal(index, box.SelectedIndex);
        Assert.Same(item, box.SelectedItem);
        Assert.Same(item, box.SelectionBoxItem);
    }

    private static T Find<T>(Window window, string name) where T : class => Assert.IsType<T>(window.FindName(name));

    // The window over the view model of a loaded TestGame, shown. The game controls are on the second page.
    private static async Task<(TestGame Game, SettingsViewModel ViewModel, SettingsWindow Window)> CreateWithGame()
    {
        var game = new TestGame(); game.AddPack("a.pack");
        var controller = game.NewController();
        await controller.LoadAsync();
        var viewModel = new SettingsViewModel(game.Settings, game.Texts, Languages, game.GameSettings, controller, _ => null);
        var window = new SettingsWindow(viewModel);
        window.Show();
        Pump(window);
        return (game, viewModel, window);
    }

    private static IEnumerable<object> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            yield return child;
            if (child is DependencyObject element)
            {
                foreach (var descendant in LogicalDescendants(element))
                {
                    yield return descendant;
                }
            }
        }
    }

    [Fact] public void Shows_the_two_selection_boxes_bound_to_the_view_model_and_no_buttons() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark });
        var window = setup.Window;
        var languageBox = Find<ComboBox>(window, "LanguageBox");
        var themeBox = Find<ComboBox>(window, "ThemeBox");

        Assert.Equal(("Settings", 760d, 520d), (window.Title, window.Width, window.Height));
        Assert.Same(setup.ViewModel.Languages, languageBox.ItemsSource);
        Assert.Same(setup.ViewModel.ThemeOptions, themeBox.ItemsSource);
        Assert.Equal(("Name", "Name"), (languageBox.DisplayMemberPath, themeBox.DisplayMemberPath));
        AssertSelected(languageBox, 1, setup.ViewModel.SelectedLanguage);
        AssertSelected(themeBox, 2, setup.ViewModel.SelectedTheme);
        Assert.Equal("de", ((UiLanguageOption)languageBox.SelectedItem).Code);
        Assert.Equal(ThemeChoice.Dark, ((ThemeOption)themeBox.SelectedItem).Value);

        // Changes apply at once, so there is nothing to confirm. The logical tree has no template parts (the drop-down toggle of a combo box).
        // Only the general page: the game page has buttons that open the folder dialog and clear a path, which confirm nothing.
        Assert.DoesNotContain(LogicalDescendants(Find<Grid>(window, "GeneralPage")), element => element is ButtonBase);
    });

    [Fact] public void Choosing_in_the_selection_boxes_writes_the_settings() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings());
        var languageBox = Find<ComboBox>(setup.Window, "LanguageBox");
        var themeBox = Find<ComboBox>(setup.Window, "ThemeBox");

        languageBox.SelectedItem = Languages[1];
        themeBox.SelectedItem = setup.ViewModel.ThemeOptions.Single(option => option.Value == ThemeChoice.Dark);

        Assert.Equal(("de", ThemeChoice.Dark), (setup.Settings.Language, setup.Settings.Theme));
    });

    // The language switch replaces the list of theme options. The theme box must not lose its choice on the way: neither by ending up with
    // nothing selected (it takes over the new list, writes null, and reads the property back at once), nor by selecting the first item on
    // its own (which would silently reset the theme to System).
    [Fact] public void Switching_the_language_translates_the_window_and_keeps_the_theme() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings { Theme = ThemeChoice.Dark });
        var languageBox = Find<ComboBox>(setup.Window, "LanguageBox");
        var themeBox = Find<ComboBox>(setup.Window, "ThemeBox");

        languageBox.SelectedItem = Languages[1];
        Pump(setup.Window);

        Assert.Equal("de", setup.Ui.Language);
        Assert.Equal("Einstellungen", setup.Window.Title);
        Assert.Equal(ThemeChoice.Dark, setup.Settings.Theme);
        Assert.Equal(["Wie Windows", "Hell", "Dunkel"], themeBox.Items.Cast<ThemeOption>().Select(option => option.Name));
        AssertSelected(themeBox, 2, setup.ViewModel.SelectedTheme);
        Assert.Equal(("Dunkel", ThemeChoice.Dark), (setup.ViewModel.SelectedTheme.Name, setup.ViewModel.SelectedTheme.Value));
        AssertSelected(languageBox, 1, setup.ViewModel.SelectedLanguage);

        // And back: the list is replaced again, and the choice made in between stays as well.
        themeBox.SelectedItem = setup.ViewModel.ThemeOptions[1];
        languageBox.SelectedItem = Languages[0];
        Pump(setup.Window);

        Assert.Equal(("en", ThemeChoice.Light), (setup.Settings.Language, setup.Settings.Theme));
        Assert.Equal(["Same as Windows", "Light", "Dark"], themeBox.Items.Cast<ThemeOption>().Select(option => option.Name));
        AssertSelected(themeBox, 1, setup.ViewModel.SelectedTheme);
        AssertSelected(languageBox, 0, setup.ViewModel.SelectedLanguage);
    });

    [Fact] public void Game_category_shows_the_game_controls() => Sta.RunAsync(async () =>
    {
        var (game, viewModel, window) = await CreateWithGame();
        using (game)
        {
            Assert.Equal((0, true, false), (viewModel.SelectedCategory, Find<ComboBox>(window, "LanguageBox").IsVisible, Find<TextBox>(window, "GamePathBox").IsVisible));

            Find<ListBox>(window, "CategoryList").SelectedIndex = 1;
            Pump(window);

            Assert.Equal(1, viewModel.SelectedCategory);
            foreach (var name in new[] { "GamePathBox", "BrowseGamePathButton", "ClearGamePathButton", "SchemaFolderBox", "BrowseSchemaFolderButton", "ClearSchemaFolderButton" })
            {
                var control = Assert.IsAssignableFrom<Control>(window.FindName(name));
                Assert.True(control.IsVisible && control.IsEnabled, name);
            }

            Assert.Equal(("Test Game", game.InstallPath), (Find<TextBlock>(window, "GameNameText").Text, Find<TextBox>(window, "GamePathBox").Text));
            Assert.False(Find<TextBlock>(window, "NoGameText").IsVisible);
            Assert.False(Find<ComboBox>(window, "LanguageBox").IsVisible);
            Assert.False(Find<ComboBox>(window, "ThemeBox").IsVisible);
            Assert.Equal(Visibility.Collapsed, Find<Grid>(window, "GeneralPage").Visibility);

            // And back: the category of the view model drives the list as well.
            viewModel.SelectedCategory = 0;
            Pump(window);
            Assert.True(Find<ComboBox>(window, "LanguageBox").IsVisible);
            Assert.False(Find<TextBox>(window, "GamePathBox").IsVisible);
        }
    });

    [Fact] public void Game_path_box_commits_on_enter_and_shows_the_error() => Sta.RunAsync(async () =>
    {
        var (game, _, window) = await CreateWithGame();
        using (game)
        {
            Find<ListBox>(window, "CategoryList").SelectedIndex = 1;
            Pump(window);
            var box = Find<TextBox>(window, "GamePathBox");
            var error = Find<TextBlock>(window, "GamePathErrorText");
            Assert.False(error.IsVisible);

            var wrong = game.Root.File("wrong"); Directory.CreateDirectory(wrong);
            box.Focus();
            box.Text = wrong;
            Assert.Equal(game.InstallPath, game.GameSettings.InstallPath);    // typing alone commits nothing
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Pump(window);

            Assert.Equal((true, "This folder has no \"packs\" folder. Choose the folder that contains it."), (error.IsVisible, error.Text));
            Assert.Equal((game.InstallPath, wrong), (game.GameSettings.InstallPath, box.Text));
        }
    });

    [Fact] public void Text_boxes_are_labeled_for_screen_readers() => Sta.RunAsync(async () =>
    {
        var (game, _, window) = await CreateWithGame();
        using (game)
        {
            var gameLabel = Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(Find<TextBox>(window, "GamePathBox")));
            var schemaLabel = Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(Find<TextBox>(window, "SchemaFolderBox")));
            Assert.Equal(("Game folder", "Schema folder"), (gameLabel.Text, schemaLabel.Text));
        }
    });

    // The automation peer clicks like the user does (also through a bound command) and posts the click through the dispatcher.
    private static void Click(Window window, Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Pump(window);
    }

    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Tree.Descendants<T>(root).Single(element => element.Name == name);

    // The check box of a row; looked up again after every change, because a change of the settings rebuilds the rows.
    private static CheckBox Check(ReorderListBox list, string id, string name) => Named<CheckBox>(list.Row(id), name);

    // Opens the page of the category and returns its code-built content.
    private static ContentControl ShowPage(Setup setup, int category, string hostName)
    {
        setup.ViewModel.SelectedCategory = category;
        Pump(setup.Window);
        var host = Find<ContentControl>(setup.Window, hostName);
        Assert.True(host.IsVisible, hostName);
        return host;
    }

    [Fact] public void The_category_list_has_five_entries_in_order() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings());
        var list = Find<ListBox>(setup.Window, "CategoryList");

        Assert.Equal(["General", "Game", "Layout", "Tile bar", "Toolbar"], list.Items.Cast<ListBoxItem>().Select(item => item.Content as string));
        Assert.Equal((2, 3, 4), (SettingsViewModel.LayoutCategory, SettingsViewModel.TilesCategory, SettingsViewModel.ToolbarCategory));

        // The list drives the page and the choice goes into the settings.
        list.SelectedIndex = SettingsViewModel.TilesCategory;
        Pump(setup.Window);
        Assert.Equal(SettingsViewModel.TilesCategory, setup.Settings.SettingsCategory);
        Assert.True(Find<ContentControl>(setup.Window, "TilesHost").IsVisible);
        foreach (var name in new[] { "LayoutPage", "ToolbarHost" })
        {
            Assert.False(Find<FrameworkElement>(setup.Window, name).IsVisible, name);
        }

        Assert.Equal(Visibility.Collapsed, Find<Grid>(setup.Window, "GeneralPage").Visibility);
    });

    [Fact] public void The_tiles_page_lists_both_tiles_and_a_visibility_check_writes_the_settings() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings());
        var settings = setup.Settings;
        var host = ShowPage(setup, SettingsViewModel.TilesCategory, "TilesHost");
        var list = Tree.Descendants<ReorderListBox>(host).Single();

        Assert.Equal(TileCatalog.DefaultOrder, list.CurrentIds);
        Assert.Contains(Tree.Descendants<TextBlock>(list.Row(TileIds.NewBuilding)), text => text.Text == "New building");
        Assert.True(Check(list, TileIds.NewTrait, "VisibleCheck").IsChecked);
        Assert.False(Check(list, TileIds.NewTrait, "IconOnlyCheck").IsChecked);

        // Hiding a tile writes the settings at once; the tile keeps its place in the list.
        Check(list, TileIds.NewTrait, "VisibleCheck").IsChecked = false;
        Assert.False(settings.IsTileVisible(TileIds.NewTrait));
        Assert.Equal(TileCatalog.DefaultOrder, list.CurrentIds);
        Assert.False(Check(list, TileIds.NewTrait, "VisibleCheck").IsChecked);

        Check(list, TileIds.NewBuilding, "IconOnlyCheck").IsChecked = true;
        Assert.True(settings.IsTileIconOnly(TileIds.NewBuilding));

        // The arrows write the order; a change from outside rebuilds the list.
        Assert.True(list.MoveDown(TileIds.NewBuilding));
        Assert.Equal([TileIds.NewTrait, TileIds.NewBuilding], settings.TileOrder);
        Assert.Equal([TileIds.NewTrait, TileIds.NewBuilding], list.CurrentIds);
        settings.SetTileVisible(TileIds.NewTrait, true);
        Assert.True(Check(list, TileIds.NewTrait, "VisibleCheck").IsChecked);

        // Reset: default order and no tile icon-only.
        Click(setup.Window, Named<Button>(host, "TileResetButton"));
        Assert.Equal(TileCatalog.DefaultOrder, settings.TileOrder);
        Assert.Equal(TileCatalog.DefaultOrder, list.CurrentIds);
        Assert.False(settings.IsTileIconOnly(TileIds.NewBuilding));
        Assert.False(Check(list, TileIds.NewBuilding, "IconOnlyCheck").IsChecked);
    });

    [Fact] public void The_toolbar_page_adds_a_separator_with_the_next_free_number_and_reset_restores_the_default() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings());
        var settings = setup.Settings;
        var host = ShowPage(setup, SettingsViewModel.ToolbarCategory, "ToolbarHost");
        var list = Tree.Descendants<ReorderListBox>(host).Single();
        Assert.Equal(ToolbarCatalog.DefaultOrder, list.CurrentIds);

        // The default order has sep:1 to sep:4, so the next one is sep:5, at the end.
        Click(setup.Window, Named<Button>(host, "AddSeparatorButton"));
        Assert.Equal(ToolbarCatalog.Separator(5), settings.ToolbarOrder[^1]);
        Assert.Equal(settings.ToolbarOrder, list.CurrentIds);

        // A separator row has a remove button and no check box; an entry has a visibility check box.
        var separatorRow = list.Row(ToolbarCatalog.Separator(2));
        Assert.Empty(Tree.Descendants<CheckBox>(separatorRow));
        var remove = Tree.Descendants<Button>(separatorRow).Single(button => Equals(button.ToolTip, "Remove"));
        remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.DoesNotContain(ToolbarCatalog.Separator(2), settings.ToolbarOrder);
        Assert.DoesNotContain(ToolbarCatalog.Separator(2), list.CurrentIds);

        // The next number follows the highest one in use (a removed number is not taken again).
        Click(setup.Window, Named<Button>(host, "AddSeparatorButton"));
        Assert.Equal(ToolbarCatalog.Separator(6), settings.ToolbarOrder[^1]);

        Check(list, ToolbarItemIds.Save, "VisibleCheck").IsChecked = false;
        Assert.False(settings.IsToolbarItemVisible(ToolbarItemIds.Save));
        Assert.False(Check(list, ToolbarItemIds.Save, "VisibleCheck").IsChecked);

        Click(setup.Window, Named<Button>(host, "ToolbarResetButton"));
        Assert.Equal(ToolbarCatalog.DefaultOrder, settings.ToolbarOrder);
        Assert.Equal(ToolbarCatalog.DefaultOrder, list.CurrentIds);
    });

    [Fact] public void The_layout_page_writes_position_and_size_at_once() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings());
        var settings = setup.Settings;
        ShowPage(setup, SettingsViewModel.LayoutCategory, "LayoutPage");
        var window = setup.Window;
        Assert.True(Find<RadioButton>(window, "ToolbarPositionTopRadio").IsChecked);
        Assert.True(Find<RadioButton>(window, "TileSizeLargeRadio").IsChecked);

        Find<RadioButton>(window, "ToolbarPositionLeftRadio").IsChecked = true;
        Assert.Equal(ToolbarPosition.Left, settings.ToolbarPosition);
        Assert.False(Find<RadioButton>(window, "ToolbarPositionTopRadio").IsChecked);

        // The size radios are a group of their own: choosing a size leaves the position alone.
        Find<RadioButton>(window, "TileSizeMediumRadio").IsChecked = true;
        Assert.Equal((TileSize.Medium, ToolbarPosition.Left), (settings.TileSize, settings.ToolbarPosition));
        Assert.True(Find<RadioButton>(window, "ToolbarPositionLeftRadio").IsChecked);

        Find<CheckBox>(window, "ToolbarLabelsCheck").IsChecked = false;
        Find<CheckBox>(window, "TileBarVisibleCheck").IsChecked = false;
        Find<CheckBox>(window, "TileLabelsCheck").IsChecked = false;
        Assert.Equal((false, false, false), (settings.ToolbarLabelsVisible, settings.TileBarVisible, settings.TileLabelsVisible));

        // A change from outside (the grip of the toolbar) moves the radio.
        settings.ToolbarPosition = ToolbarPosition.Bottom;
        Pump(window);
        Assert.True(Find<RadioButton>(window, "ToolbarPositionBottomRadio").IsChecked);
        Assert.False(Find<RadioButton>(window, "ToolbarPositionLeftRadio").IsChecked);
    });
}
