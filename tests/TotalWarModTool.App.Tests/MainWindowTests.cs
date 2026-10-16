using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TotalWarModTool.App.Shell;
using TotalWarModTool.Objects;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class MainWindowTests
{
    private sealed record Setup(TestGame Game, WorkspaceController Controller, MainViewModel ViewModel, MainWindow Window, List<string> LogFolders) : IDisposable
    {
        public void Dispose() => Game.Dispose();
    }

    // A MainWindow with a MainViewModel of a loaded TestGame, shown. The test host has no Application, so the styles of Controls.xaml go into
    // the window's resources (the real app has them in its application resources).
    private static async Task<Setup> Create(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var controller = game.NewController();
        var logFolders = new List<string>();
        var viewModel = new MainViewModel(game.Texts, game.Paths, logFolders.Add, game.Runner, () => { }, controller);
        await controller.LoadAsync();
        var window = Show(new MainWindow(viewModel, game.Settings));
        return new Setup(game, controller, viewModel, window, logFolders);
    }

    // Adds the styles of Controls.xaml, shows the window and lets it lay out.
    private static MainWindow Show(MainWindow window)
    {
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        window.Show();
        Pump(window);
        return window;
    }

    private sealed record ProjectSetup(Setup Setup, MainSetup Main) : IDisposable
    {
        public void Dispose() => Setup.Dispose();
    }

    // Like Create, over a main view model with projects (the fake dialogs are in Main) and, by default, with the editable documents.
    // withFiles adds the file operations (and with them the documents).
    private static async Task<ProjectSetup> CreateWithProjects(bool withDocuments = true, bool withFiles = false)
    {
        var game = new TestGame();
        game.EnsureTableVersions();
        var controller = game.NewController();
        var main = game.NewMain(controller, withDocuments: withDocuments, withFiles: withFiles);
        await controller.LoadAsync();
        var window = Show(new MainWindow(main.Main, game.Settings));
        return new ProjectSetup(new Setup(game, controller, main.Main, window, []), main);
    }

    // The game of the building wizard (as in ObjectWizardControllerTests) with documents and no project yet. The window is shown first and the
    // wizards are set afterwards, so the window has to pick them up through the setter, as it would if the composition root set them late.
    private static async Task<ProjectSetup> CreateWithWizards()
    {
        var game = new TestGame(withSchema: false);
        ObjectsTestSchema.WriteBuildingSchema(game);
        ObjectsTestSchema.WriteBuildingVanilla(game);
        File.WriteAllText(Path.Combine(game.InstallPath, LoadOrderResolver.UsedModsFileName), string.Empty);
        ObjectsTestSchema.WriteWizards(game, ObjectsTestSchema.WizardsJson);
        var controller = game.NewController(profile: ObjectsTestSchema.Profile);
        await controller.LoadAsync();
        var main = game.NewMain(controller, withDocuments: true);
        var window = Show(new MainWindow(main.Main, game.Settings));

        var objects = new ObjectsController(controller, main.Projects, main.Documents, main.Main.Tabs, game.Runner, game.Paths, game.Log, game.Texts,
            key => key, main.Main.OpenEntryAsync, main.Main.OpenProjectFileAsync);
        var dialogs = new FakeObjectDialogs();
        var apply = new ObjectApplyController(objects, main.Projects, main.Documents!, main.History!, game.Runner, dialogs, game.Root.File("trash"),
            game.Log, game.Texts, TimeProvider.System);
        main.Main.Wizards = new ObjectWizardController(objects, apply, main.Projects, game.Runner, dialogs, game.Log, game.Texts);
        Pump(window);
        return new ProjectSetup(new Setup(game, controller, main.Main, window, []), main);
    }

    // The top-level menu items in their order; collapsed ones too (the visual tree has them).
    private static List<MenuItem> TopMenus(Window window) => Descendants<Menu>(window).Single().Items.OfType<MenuItem>().ToList();

    // The headers of all top-level menus as they would be in this build.
    private static object[] AllMenuHeaders(AppTexts texts) => [texts.ProjectMessageTitle, texts.MenuEdit, texts.MenuSettings, texts.MenuHelp, texts.MenuDebug];

    private static Border ToolbarHost(Window window) => (Border)window.FindName("ToolbarHost");

    private static Button ToolbarButton(Window window, string id) =>
        Descendants<Button>(ToolbarHost(window)).Single(b => b is not ToolbarPositionGrip && Equals(b.Tag, id));

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => Tree.VisualDescendants<T>(root);

    [Fact] public void Window_shows_explorer_tabs_and_status_line() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => g.AddPack("a.pack"));
        var window = setup.Window;
        Assert.Same(setup.ViewModel.Explorer, Assert.Single(Descendants<ExplorerView>(window)).DataContext);
        Assert.Same(setup.ViewModel.Tabs, Assert.Single(Descendants<TabsView>(window)).DataContext);

        var bar = Assert.Single(Descendants<StatusBar>(window));
        var first = Descendants<TextBlock>(bar).First();
        Assert.Equal(setup.ViewModel.StatusGame, first.Text);
        Assert.Equal("Game: Test Game", first.Text);
        Assert.Equal(setup.Game.InstallPath, first.ToolTip);
        Assert.Contains(Descendants<TextBlock>(bar), t => t.Text == setup.ViewModel.StatusSchema);

        // Explorer, splitter and tabs side by side in this order, the explorer 320 wide to start with.
        var grid = (Grid)window.FindName("Layout");
        Assert.Equal([320.0, 5.0], [grid.ColumnDefinitions[0].Width.Value, grid.ColumnDefinitions[1].Width.Value]);
        Assert.Equal(150.0, grid.ColumnDefinitions[0].MinWidth);
        Assert.Single(Descendants<GridSplitter>(window));
    });

    [Fact] public void The_menu_has_project_edit_settings_help_and_no_tools_menu() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects(withFiles: true);
        var texts = setup.Setup.Game.Texts; var vm = setup.Setup.ViewModel;
        var menus = TopMenus(setup.Setup.Window);
        object[] visible = vm.IsDebugBuild
            ? [texts.ProjectMessageTitle, texts.MenuEdit, texts.MenuSettings, texts.MenuHelp, texts.MenuDebug]
            : [texts.ProjectMessageTitle, texts.MenuEdit, texts.MenuSettings, texts.MenuHelp];
        Assert.Equal(visible, menus.Where(m => m.Visibility == Visibility.Visible).Select(m => m.Header));
        Assert.Equal(AllMenuHeaders(texts), menus.Select(m => m.Header));
        Assert.DoesNotContain(texts.MenuToolsNewBuilding, Descendants<MenuItem>(setup.Setup.Window).Select(m => m.Header));

        // "Settings" opens the window directly (no submenu), "Help" has the log folder.
        var settings = menus[2];
        Assert.Empty(settings.Items);
        Assert.Same(vm.OpenSettingsCommand, settings.Command);
        var help = Assert.Single(menus[3].Items.OfType<MenuItem>());
        Assert.Equal(texts.MenuOpenLogFolder, help.Header);
        Assert.Same(vm.OpenLogFolderCommand, help.Command);
    });

    [Fact] public void The_toolbar_shows_the_default_order_with_separators_and_binds_the_commands() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects(withFiles: true);
        var vm = setup.Setup.ViewModel; var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var host = ToolbarHost(window);
        Assert.Equal(Dock.Top, DockPanel.GetDock(host));
        var panel = Assert.IsType<WrapPanel>(host.Child);
        Assert.Equal(ToolbarCatalog.DefaultOrder, ToolbarBuilder.PlacedIds(panel));
        Assert.Single(Descendants<ToolbarPositionGrip>(panel));

        var build = ToolbarButton(window, "build");
        Assert.Same(vm.Projects!.BuildActiveCommand, build.Command);
        Assert.False(build.Focusable);
        Assert.True(ToolTipService.GetShowOnDisabled(build));
        Assert.Equal(Visibility.Visible, ToolbarItemView.LabelOf((FrameworkElement)build.Content)!.Visibility);
        Assert.Same(vm.Documents!.SaveCommand, ToolbarButton(window, "save").Command);
        Assert.Same(vm.NewTableCommand, ToolbarButton(window, "newTable").Command);
        Assert.Same(vm.RefreshCommand, ToolbarButton(window, "refreshPacks").Command);

        // No active project: name and gesture, then the reason. With a project the reason goes and the tool tip follows without a rebuild.
        Assert.False(build.IsEnabled);
        Assert.Equal("Build (Ctrl+B)\n" + texts.ProjectNoneActive, build.ToolTip);
        setup.Setup.Game.NewProject(setup.Main.Projects, "Alpha");
        Pump(window);
        Assert.Same(build, ToolbarButton(window, "build"));
        Assert.Equal("Build (Ctrl+B)", build.ToolTip);
        Assert.Equal("Save (Ctrl+S)", ToolbarButton(window, "save").ToolTip);
    });

    // Review focus 3: without projects, documents and files there is no NullReferenceException; the missing entries are left out.
    [Fact] public void Without_projects_only_the_available_entries_are_placed_and_separators_collapse() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var panel = (Panel)ToolbarHost(setup.Window).Child;
        Assert.Equal(["refreshPacks"], ToolbarBuilder.PlacedIds(panel));
    });

    // Review focus 4: at the left the toolbar is vertical and never has labels, the tool tip still names the entry and its gesture.
    [Fact] public void Hiding_an_entry_and_moving_the_toolbar_left_rebuild_it_vertically_without_labels() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects(withFiles: true);
        var settings = setup.Setup.Game.Settings; var window = setup.Setup.Window; var host = ToolbarHost(window);
        settings.SetToolbarItemVisible("undo", false); Pump(window);
        Assert.DoesNotContain("undo", ToolbarBuilder.PlacedIds((Panel)host.Child));

        settings.ToolbarPosition = ToolbarPosition.Left; Pump(window);
        Assert.Equal(Dock.Left, DockPanel.GetDock(host));
        var panel = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(host.Child).Content);
        Assert.DoesNotContain("undo", ToolbarBuilder.PlacedIds(panel));
        var buttons = Descendants<Button>(panel).Where(b => b is not ToolbarPositionGrip).ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.Equal(Visibility.Collapsed, ToolbarItemView.LabelOf((FrameworkElement)b.Content)!.Visibility));
        Assert.Equal("Save (Ctrl+S)", ToolbarButton(window, "save").ToolTip);

        // Labels switched off and on again do not show at the left; back at the top they come back.
        settings.ToolbarLabelsVisible = false; Pump(window);
        settings.ToolbarLabelsVisible = true; Pump(window);
        Assert.Equal(Visibility.Collapsed, ToolbarItemView.LabelOf((FrameworkElement)ToolbarButton(window, "save").Content)!.Visibility);
        settings.ToolbarPosition = ToolbarPosition.Top; Pump(window);
        Assert.IsType<WrapPanel>(host.Child);
        Assert.Equal(Visibility.Visible, ToolbarItemView.LabelOf((FrameworkElement)ToolbarButton(window, "save").Content)!.Visibility);
    });

    // Review focus 5: a tile is disabled with its reason and enabled again by CanExecuteChanged, without a rebuild.
    [Fact] public void The_tiles_show_the_wizards_and_follow_their_enabled_state() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithWizards();
        var window = setup.Setup.Window; var vm = setup.Setup.ViewModel;
        var bar = (TileBar)window.FindName("TileBarControl");
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(["newBuilding", "newTrait"], bar.VisibleIds);
        var tile = bar.TileButton("newBuilding");
        Assert.False(tile.IsEnabled);
        Assert.NotNull(vm.Wizards!.NewBuildingDisabledReason);
        Assert.Equal(vm.Wizards.NewBuildingDisabledReason, tile.ToolTip);

        // The window takes command and reason from the view model, not from a mapping of its own.
        var binding = vm.TileBindingFor("newBuilding")!;
        Assert.Same(vm.Wizards.NewBuildingCommand, binding.Command);
        Assert.Equal(vm.Texts.TileLabel("newBuilding"), binding.Label);
        Assert.Equal(vm.Wizards.NewBuildingDisabledReason, binding.Reason!());
        Assert.Same(vm.Wizards.NewTraitCommand, vm.TileBindingFor("newTrait")!.Command);

        setup.Setup.Game.NewProject(setup.Main.Projects, "Flo");               // the wizards raise CanExecuteChanged
        Pump(window);
        Assert.Same(tile, bar.TileButton("newBuilding"));
        Assert.True(tile.IsEnabled);
        Assert.Equal(setup.Setup.Game.Texts.TileNewBuildingTip, tile.ToolTip);
    });

    [Fact] public void The_tile_bar_is_collapsed_without_wizards_or_when_switched_off() => Sta.RunAsync(async () =>
    {
        using (var setup = await Create())
        {
            Assert.Equal(Visibility.Collapsed, ((TileBar)setup.Window.FindName("TileBarControl")).Visibility);
        }

        using var withWizards = await CreateWithWizards();
        var bar = (TileBar)withWizards.Setup.Window.FindName("TileBarControl");
        Assert.Equal(Visibility.Visible, bar.Visibility);
        withWizards.Setup.Game.Settings.TileBarVisible = false;
        Pump(withWizards.Setup.Window);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        withWizards.Setup.Game.Settings.TileBarVisible = true;
        Assert.Equal(Visibility.Visible, bar.Visibility);
    });

    [Fact] public void Status_hint_is_hidden_without_a_hint() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var hint = (FrameworkElement)setup.Window.FindName("StatusHintItem");
        Assert.Null(setup.ViewModel.StatusHint);
        Assert.Equal(Visibility.Collapsed, hint.Visibility);

        setup.Game.Settings.SchemaFolder = setup.Game.Root.File("no-schemas");         // the override fails: the shipped schema is used, with a hint
        Pump(setup.Window);
        Assert.NotNull(setup.ViewModel.StatusHint);
        Assert.Equal(Visibility.Visible, hint.Visibility);
        Assert.Contains(Descendants<TextBlock>(hint), t => t.Text == setup.ViewModel.StatusHint);
    });

    // Review item 3: Ctrl+W and F5 are bound on the window, so they work wherever the focus is.
    [Fact] public void Ctrl_W_and_F5_are_bound_on_the_window() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var bindings = setup.Window.InputBindings.OfType<KeyBinding>().ToList();
        Assert.Same(setup.ViewModel.Tabs.CloseSelectedCommand, Assert.Single(bindings, b => b.Key == Key.W && b.Modifiers == ModifierKeys.Control).Command);
        Assert.Same(setup.ViewModel.RefreshCommand, Assert.Single(bindings, b => b.Key == Key.F5 && b.Modifiers == ModifierKeys.None).Command);
    });

    [Fact] public void The_project_menu_is_first_and_binds_the_commands() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts; var projects = setup.Setup.ViewModel.Projects!;
        var menus = TopMenus(window);
        Assert.Equal(AllMenuHeaders(texts), menus.Select(m => m.Header));
        var project = menus[0];
        Assert.Equal(Visibility.Visible, project.Visibility);

        // Separators after "Import pack", after the two entries for new files and in front of Build (see
        // The_project_menu_has_new_table_and_new_loc_file and The_project_menu_has_save_and_save_all_with_their_gestures).
        var entries = project.Items.Cast<object>().ToList();
        Assert.Equal(12, entries.Count);
        Assert.Equal([3, 6, 9], entries.Select((entry, index) => (entry, index)).Where(x => x.entry is Separator).Select(x => x.index));
        var items = entries.OfType<MenuItem>().ToList();
        var main = setup.Setup.ViewModel;
        Assert.Equal([texts.ProjectNewTitle, texts.MenuProjectOpen, texts.MenuProjectImport, texts.FileNewTableTitle, texts.FileNewLocTitle, texts.MenuProjectSave,
            texts.MenuProjectSaveAll, texts.BuildMessageTitle, texts.ProjectActionBuildAndInstall], items.Select(i => i.Header));
        Assert.Equal([projects.NewProjectCommand, projects.OpenProjectCommand, projects.ImportPackCommand, main.NewTableCommand, main.NewLocCommand,
            main.Documents!.SaveCommand, main.Documents!.SaveAllCommand, projects.BuildActiveCommand, projects.BuildAndInstallActiveCommand], items.Select(i => i.Command));
        Assert.Equal(["", "", "", "", "", texts.MenuProjectSaveGesture, texts.MenuProjectSaveAllGesture, texts.MenuProjectBuildGesture, texts.MenuProjectBuildAndInstallGesture],
            items.Select(i => i.InputGestureText ?? ""));

        // A menu item asks its command when the menu opens.
        project.IsSubmenuOpen = true;
        Pump(window);
        Assert.False(items[7].Command.CanExecute(null));
        Assert.False(items[7].IsEnabled);
        project.IsSubmenuOpen = false;
        Assert.Equal(texts.ProjectNoneActive, items[7].ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(items[7]));

        var bindings = window.InputBindings.OfType<KeyBinding>().ToList();
        Assert.Same(projects.BuildActiveCommand, Assert.Single(bindings, b => b.Key == Key.B && b.Modifiers == ModifierKeys.Control).Command);
        Assert.Same(projects.BuildAndInstallActiveCommand, Assert.Single(bindings, b => b.Key == Key.B && b.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)).Command);
    });

    [Fact] public void The_project_menu_has_save_and_save_all_with_their_gestures() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts; var viewModel = setup.Setup.ViewModel;
        var documents = viewModel.Documents!;
        var project = TopMenus(window)[0];
        var items = project.Items.OfType<MenuItem>().ToList();
        var save = items.Single(item => Equals(item.Header, texts.MenuProjectSave));
        var saveAll = items.Single(item => Equals(item.Header, texts.MenuProjectSaveAll));

        // Above the separator that stands in front of Build, behind the entries that open or create a project or a file.
        var entries = project.Items.Cast<object>().ToList();
        Assert.Equal((7, 8, 9), (entries.IndexOf(save), entries.IndexOf(saveAll), entries.FindLastIndex(entry => entry is Separator)));
        Assert.Same(documents.SaveCommand, save.Command);
        Assert.Same(documents.SaveAllCommand, saveAll.Command);
        Assert.Equal(("Ctrl+S", "Ctrl+Shift+S"), (texts.MenuProjectSaveGesture, texts.MenuProjectSaveAllGesture));
        Assert.Equal((texts.MenuProjectSaveGesture, texts.MenuProjectSaveAllGesture), (save.InputGestureText, saveAll.InputGestureText));

        var bindings = window.InputBindings.OfType<KeyBinding>().ToList();
        Assert.Same(documents.SaveCommand, Assert.Single(bindings, b => b.Key == Key.S && b.Modifiers == ModifierKeys.Control).Command);
        Assert.Same(documents.SaveAllCommand, Assert.Single(bindings, b => b.Key == Key.S && b.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)).Command);

        // Nothing to save: disabled, and no tool tip, because "nothing to save" needs no explanation. While an operation runs: the busy text.
        project.IsSubmenuOpen = true;
        Pump(window);
        Assert.False(save.IsEnabled);
        Assert.False(saveAll.IsEnabled);
        Assert.Null(save.ToolTip);
        Assert.Null(saveAll.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(save));
        Assert.True(ToolTipService.GetShowOnDisabled(saveAll));
        project.IsSubmenuOpen = false;

        setup.Setup.Game.Runner.IsRunning = true;
        Assert.Equal(texts.ProjectBusy, viewModel.ProjectSaveToolTip);
        setup.Setup.Game.Runner.IsRunning = false;
        Assert.Null(viewModel.ProjectSaveToolTip);
    });

    [Fact] public void The_save_entries_are_collapsed_without_documents() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects(withDocuments: false);
        var project = TopMenus(setup.Setup.Window)[0];
        var texts = setup.Setup.Game.Texts;

        var save = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.MenuProjectSave));
        var saveAll = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.MenuProjectSaveAll));
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (save.Visibility, saveAll.Visibility));

        // No two separators in a row: only the one after "Import pack" is left.
        Assert.Equal(1, project.Items.OfType<Separator>().Count(separator => separator.Visibility == Visibility.Visible));
    });

    [Fact] public void The_project_menu_is_hidden_without_projects() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var menus = TopMenus(setup.Window);
        Assert.Equal(AllMenuHeaders(setup.Game.Texts), menus.Select(m => m.Header));
        Assert.Equal(Visibility.Collapsed, menus[0].Visibility);
        Assert.Equal(Visibility.Collapsed, menus[1].Visibility);               // no documents: no edit menu either
        Assert.Equal((Visibility.Visible, Visibility.Visible), (menus[2].Visibility, menus[3].Visibility));
    });

    [Fact] public void The_edit_menu_has_undo_and_redo_with_their_gestures() => Sta.RunAsync(async () =>
    {
        using var game = new TestGame();
        var controller = game.NewController();
        var projects = game.NewProjects(controller);
        var commands = new ProjectsViewModel(projects, new FakeProjectDialogs(), game.Texts);
        var documents = new DocumentsController(new TabsViewModel(game.Texts), projects, new TotalWarModTool.UndoRedo.ActionHistory(),
            game.Runner, new FakeEditDialogs(), game.Log, game.Texts);
        var viewModel = new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => { }, controller, null, projects, commands, documents);
        await controller.LoadAsync();
        var window = Show(new MainWindow(viewModel, game.Settings));
        var texts = game.Texts;

        Assert.Same(documents, viewModel.Documents);
        Assert.Same(documents.Tabs, viewModel.Tabs);
        var menus = TopMenus(window);
        Assert.Equal(AllMenuHeaders(texts), menus.Select(m => m.Header));
        var edit = menus[1];
        Assert.Equal(Visibility.Visible, edit.Visibility);
        var items = edit.Items.OfType<MenuItem>().ToList();
        Assert.Equal(2, edit.Items.Count);
        Assert.Equal([texts.MenuEditUndo, texts.MenuEditRedo], items.Select(i => i.Header));
        Assert.Equal([documents.UndoCommand, documents.RedoCommand], items.Select(i => i.Command));
        Assert.Equal([texts.MenuEditUndoGesture, texts.MenuEditRedoGesture], items.Select(i => i.InputGestureText));
        Assert.Equal("Ctrl+Z", texts.MenuEditUndoGesture);
        Assert.Equal("Ctrl+Y", texts.MenuEditRedoGesture);

        var bindings = window.InputBindings.OfType<KeyBinding>().ToList();
        Assert.Same(documents.UndoCommand, Assert.Single(bindings, b => b.Key == Key.Z && b.Modifiers == ModifierKeys.Control).Command);
        Assert.Same(documents.RedoCommand, Assert.Single(bindings, b => b.Key == Key.Y && b.Modifiers == ModifierKeys.Control).Command);
    });

    [Fact] public void The_status_bar_shows_the_last_project_message() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var item = (FrameworkElement)window.FindName("ProjectStatusItem");
        Assert.Equal(Visibility.Collapsed, item.Visibility);

        setup.Main.Dialogs.ProjectAnswer = new ProjectInput("Alpha", setup.Setup.Game.NewFolder("alpha"), "alpha.pack");
        setup.Setup.ViewModel.Projects!.NewProjectCommand.Execute(null);
        Pump(window);

        Assert.Equal(Visibility.Visible, item.Visibility);
        var text = Assert.Single(Descendants<TextBlock>(item));
        Assert.Equal(texts.ProjectCreated("Alpha"), text.Text);
        Assert.Equal(texts.ProjectCreated("Alpha"), text.ToolTip);
    });

    [Fact] public void The_project_menu_has_new_table_and_new_loc_file() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects(withFiles: true);
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts; var main = setup.Setup.ViewModel;
        var project = TopMenus(window)[0];
        var entries = project.Items.Cast<object>().ToList();
        var newTable = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewTableTitle));
        var newLoc = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewLocTitle));
        var import = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.MenuProjectImport));

        // Under "Import pack" and a separator, in this order, on the commands of the main view model; a separator follows them.
        Assert.IsType<Separator>(entries[entries.IndexOf(import) + 1]);
        Assert.Equal((entries.IndexOf(import) + 2, entries.IndexOf(import) + 3), (entries.IndexOf(newTable), entries.IndexOf(newLoc)));
        Assert.IsType<Separator>(entries[entries.IndexOf(newLoc) + 1]);
        Assert.Equal(3, project.Items.OfType<Separator>().Count(separator => separator.Visibility == Visibility.Visible));
        Assert.Same(main.NewTableCommand, newTable.Command);
        Assert.Same(main.NewLocCommand, newLoc.Command);
        Assert.Equal((Visibility.Visible, Visibility.Visible), (newTable.Visibility, newLoc.Visibility));

        // No active project: both are off and say why; a disabled entry shows its tool tip.
        project.IsSubmenuOpen = true;
        Pump(window);
        Assert.Equal((false, false), (newTable.IsEnabled, newLoc.IsEnabled));
        project.IsSubmenuOpen = false;
        Assert.Equal((texts.ProjectNoneActive, texts.ProjectNoneActive), (newTable.ToolTip, newLoc.ToolTip));
        Assert.True(ToolTipService.GetShowOnDisabled(newTable));
        Assert.True(ToolTipService.GetShowOnDisabled(newLoc));

        // With an active project they can run, and the tool tip follows.
        setup.Setup.Game.NewProject(setup.Main.Projects, "Alpha");
        project.IsSubmenuOpen = true;
        Pump(window);
        Assert.Equal((true, true), (newTable.IsEnabled, newLoc.IsEnabled));
        project.IsSubmenuOpen = false;
        Assert.Null(newTable.ToolTip);
        Assert.Null(newLoc.ToolTip);
    });

    [Fact] public void The_new_file_entries_are_collapsed_without_the_file_operations() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var project = TopMenus(setup.Setup.Window)[0];
        var texts = setup.Setup.Game.Texts;

        var newTable = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewTableTitle));
        var newLoc = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewLocTitle));
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (newTable.Visibility, newLoc.Visibility));

        // The separator after them goes with them, so no two separators stand in a row.
        Assert.Equal(2, project.Items.OfType<Separator>().Count(separator => separator.Visibility == Visibility.Visible));
    });

    [Fact] public void The_status_bar_shows_the_last_file_message() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects(withFiles: true);
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var item = (FrameworkElement)window.FindName("ProjectStatusItem");
        setup.Setup.Game.NewProject(setup.Main.Projects, "Alpha");
        Assert.Equal(Visibility.Collapsed, item.Visibility);

        setup.Main.FileDialogs!.NameAnswers.Enqueue("mine");
        setup.Setup.ViewModel.NewLocCommand.Execute(null);
        Pump(window);

        var created = texts.FileCreatedStatus(@"text\db\mine.loc.tsv");
        Assert.Equal(Visibility.Visible, item.Visibility);
        var text = Assert.Single(Descendants<TextBlock>(item));
        Assert.Equal((created, created), (text.Text, text.ToolTip));
    });
}
