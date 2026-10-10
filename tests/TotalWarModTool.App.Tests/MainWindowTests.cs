using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

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
        var window = new MainWindow(viewModel);
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        window.Show();
        Pump(window);
        return new Setup(game, controller, viewModel, window, logFolders);
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
        var controller = game.NewController();
        var main = game.NewMain(controller, withDocuments: withDocuments, withFiles: withFiles);
        await controller.LoadAsync();
        var window = new MainWindow(main.Main);
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        window.Show();
        Pump(window);
        return new ProjectSetup(new Setup(game, controller, main.Main, window, []), main);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

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

    [Fact] public void Tools_menu_offers_refresh_settings_and_log_folder() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var tools = Descendants<MenuItem>(setup.Window).First();
        Assert.Equal(setup.Game.Texts.MenuTools, tools.Header);
        var items = tools.Items.OfType<MenuItem>().ToList();
        Assert.Equal([setup.Game.Texts.OperationRefreshPacks, setup.Game.Texts.MenuToolsNewBuilding, setup.Game.Texts.MenuToolsNewTrait,
            setup.Game.Texts.MenuSettings,
            setup.Game.Texts.MenuOpenLogFolder], items.Select(i => i.Header));
        Assert.Same(setup.ViewModel.RefreshCommand, items[0].Command);
        Assert.Same(setup.ViewModel.OpenSettingsCommand, items[3].Command);
        Assert.Same(setup.ViewModel.OpenLogFolderCommand, items[4].Command);

        // The wizard entry is hidden while the composition root has not set the wizards.
        Assert.Null(setup.ViewModel.Wizards);
        Assert.Equal(Visibility.Collapsed, items[1].Visibility);
        Assert.Equal(Visibility.Collapsed, items[2].Visibility);
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

    [Fact] public void The_project_menu_is_second_and_binds_the_commands() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts; var projects = setup.Setup.ViewModel.Projects!;
        var menus = Descendants<MenuItem>(window).ToList();
        Assert.Equal([texts.MenuTools, texts.MenuEdit, texts.ProjectMessageTitle, texts.MenuDebug], menus.Select(m => m.Header));
        var project = menus[2];
        Assert.Equal(Visibility.Visible, project.Visibility);

        // The two entries for new files sit under "Import pack", the two save entries above the separator in front of Build (see
        // The_project_menu_has_new_table_and_new_loc_file and The_project_menu_has_save_and_save_all_with_their_gestures).
        var entries = project.Items.Cast<object>().ToList();
        Assert.Equal(10, entries.Count);
        Assert.IsType<Separator>(entries[7]);
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
        var project = Descendants<MenuItem>(window).ToList()[2];
        var items = project.Items.OfType<MenuItem>().ToList();
        var save = items.Single(item => Equals(item.Header, texts.MenuProjectSave));
        var saveAll = items.Single(item => Equals(item.Header, texts.MenuProjectSaveAll));

        // Above the separator that stands in front of Build, behind the entries that open or create a project or a file.
        var entries = project.Items.Cast<object>().ToList();
        Assert.Equal((5, 6, 7), (entries.IndexOf(save), entries.IndexOf(saveAll), entries.FindIndex(entry => entry is Separator)));
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
        var project = Descendants<MenuItem>(setup.Setup.Window).ToList()[2];
        var texts = setup.Setup.Game.Texts;

        var save = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.MenuProjectSave));
        var saveAll = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.MenuProjectSaveAll));
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (save.Visibility, saveAll.Visibility));
    });

    [Fact] public void The_project_menu_is_hidden_without_projects() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var menus = Descendants<MenuItem>(setup.Window).ToList();
        Assert.Equal(setup.Game.Texts.MenuTools, menus[0].Header);
        Assert.Equal(setup.Game.Texts.MenuEdit, menus[1].Header);
        Assert.Equal(Visibility.Collapsed, menus[1].Visibility);               // no documents: no edit menu either
        Assert.Equal(setup.Game.Texts.ProjectMessageTitle, menus[2].Header);
        Assert.Equal(Visibility.Collapsed, menus[2].Visibility);
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
        var window = new MainWindow(viewModel);
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        window.Show();
        Pump(window);
        var texts = game.Texts;

        Assert.Same(documents, viewModel.Documents);
        Assert.Same(documents.Tabs, viewModel.Tabs);
        var menus = Descendants<MenuItem>(window).ToList();
        Assert.Equal([texts.MenuTools, texts.MenuEdit, texts.ProjectMessageTitle, texts.MenuDebug], menus.Select(m => m.Header));
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

    // The toolbar under the menu: the document commands and the row commands as buttons, so they are found without a right click.
    [Fact] public void The_toolbar_has_nine_buttons_in_groups_directly_under_the_menu() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts; var viewModel = setup.Setup.ViewModel;
        var documents = viewModel.Documents!; var rows = viewModel.RowCommands;
        var toolbar = (Border)window.FindName("Toolbar");
        Assert.Equal(Visibility.Visible, toolbar.Visibility);

        // Directly under the menu and above the explorer and the tabs.
        var dock = Assert.IsType<DockPanel>(toolbar.Parent);
        var menu = Assert.Single(Descendants<Menu>(window));
        Assert.Equal(Dock.Top, DockPanel.GetDock(toolbar));
        Assert.Equal(dock.Children.IndexOf(menu) + 1, dock.Children.IndexOf(toolbar));
        Assert.True(dock.Children.IndexOf(toolbar) < dock.Children.IndexOf((UIElement)window.FindName("Layout")));

        // Buttons and separators in one line: groups of save, undo, rows, copy.
        var line = Assert.IsType<WrapPanel>(Assert.Single(Descendants<WrapPanel>(toolbar)));
        var kinds = line.Children.Cast<UIElement>().Select(child => child is Button ? "B" : child is Separator ? "|" : "?").ToList();
        Assert.Equal("BB|BB|BBB|BB", string.Concat(kinds));

        var buttons = line.Children.OfType<Button>().ToList();
        Assert.Equal([texts.MenuProjectSave, texts.MenuProjectSaveAll, texts.MenuEditUndo, texts.MenuEditRedo, texts.GridInsertRow,
            texts.GridDuplicateRows, texts.GridDeleteRows, texts.GridCopy, texts.GridPaste], buttons.Select(b => b.Content));
        Assert.Equal<ICommand>([documents.SaveCommand, documents.SaveAllCommand, documents.UndoCommand, documents.RedoCommand, rows.InsertRowCommand,
            rows.DuplicateRowsCommand, rows.DeleteRowsCommand, rows.CopyCommand, rows.PasteCommand], buttons.Select(b => b.Command));
        Assert.Equal([texts.ToolbarSaveToolTip, texts.ToolbarSaveAllToolTip, texts.ToolbarUndoToolTip, texts.ToolbarRedoToolTip, rows.InsertRowToolTip,
            rows.DuplicateRowsToolTip, rows.DeleteRowsToolTip, rows.CopyToolTip, rows.PasteToolTip], buttons.Select(b => b.ToolTip));
        Assert.All(buttons, b => Assert.True(ToolTipService.GetShowOnDisabled(b)));
        Assert.Equal("Save (Ctrl+S)", buttons[0].ToolTip);
        Assert.Equal(texts.ToolbarOpenTable, buttons[4].ToolTip);                              // no tab: the row buttons say why they are off
    });

    [Fact] public void The_toolbar_buttons_cannot_take_the_focus_so_the_grid_keeps_its_selection() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var toolbar = (Border)setup.Setup.Window.FindName("Toolbar");
        var buttons = Descendants<Button>(toolbar).ToList();

        Assert.Equal(9, buttons.Count);
        Assert.All(buttons, b => Assert.False(b.Focusable));
        Assert.All(buttons, b => Assert.False(b.IsTabStop && b.Focusable));
    });

    [Fact] public void The_toolbar_is_hidden_without_documents() => Sta.RunAsync(async () =>
    {
        using var without = await Create();
        Assert.Equal(Visibility.Collapsed, ((Border)without.Window.FindName("Toolbar")).Visibility);

        using var noDocuments = await CreateWithProjects(withDocuments: false);
        Assert.Equal(Visibility.Collapsed, ((Border)noDocuments.Setup.Window.FindName("Toolbar")).Visibility);
    });

    [Fact] public void A_row_button_follows_the_selected_tab_and_its_tool_tip_names_the_reason() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects();
        var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts; var viewModel = setup.Setup.ViewModel;
        var delete = Descendants<Button>((Border)window.FindName("Toolbar")).Single(b => Equals(b.Content, texts.GridDeleteRows));
        Assert.False(delete.IsEnabled);

        var document = TotalWarModTool.Editing.RowsDocument.ForLoc(@"text\a.loc.tsv", [new TotalWarModTool.Tables.LocRow("k", "t", false)]);
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "Mod"), "text/a.loc.tsv", texts, _ => { }, document,
            new TotalWarModTool.UndoRedo.ActionHistory(), new FakeClipboard());
        viewModel.Tabs.Tabs.Add(tab);
        viewModel.Tabs.SelectedTab = tab;
        Pump(window);
        Assert.False(delete.IsEnabled);
        Assert.Equal(texts.ToolbarSelectRows, delete.ToolTip);

        tab.SelectedRows = [0];
        Pump(window);
        Assert.True(delete.IsEnabled);
        Assert.Equal("Delete rows (Del)", delete.ToolTip);
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
        var project = Descendants<MenuItem>(window).ToList()[2];
        var entries = project.Items.Cast<object>().ToList();
        var newTable = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewTableTitle));
        var newLoc = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewLocTitle));
        var import = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.MenuProjectImport));

        // Directly under "Import pack", in this order, on the commands of the main view model.
        Assert.Equal((entries.IndexOf(import) + 1, entries.IndexOf(import) + 2), (entries.IndexOf(newTable), entries.IndexOf(newLoc)));
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
        var project = Descendants<MenuItem>(setup.Setup.Window).ToList()[2];
        var texts = setup.Setup.Game.Texts;

        var newTable = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewTableTitle));
        var newLoc = project.Items.OfType<MenuItem>().Single(item => Equals(item.Header, texts.FileNewLocTitle));
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (newTable.Visibility, newLoc.Visibility));
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
