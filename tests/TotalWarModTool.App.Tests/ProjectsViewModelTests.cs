using System.Text;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Settings;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ProjectsViewModelTests
{
    // One row of the table "x_tables" of the test schema, in the text form of a project.
    private const string Tsv = "value\n#x_tables;0;db/x_tables/my_mod\n42\n";

    private const string Backslash = "\\";

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, FakeProjectDialogs Dialogs,
        ProjectsViewModel ViewModel) : IDisposable
    {
        public AppTexts Texts => Game.Texts;

        public void Dispose() => Game.Dispose();
    }

    // The view model over a loaded TestGame, as the app wires it; the dialogs are the fake.
    private static async Task<Setup> CreateAsync(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var workspace = game.NewController();
        await workspace.LoadAsync();
        return Wire(game, workspace);
    }

    private static Setup Wire(TestGame game, WorkspaceController workspace)
    {
        var projects = game.NewProjects(workspace);
        var dialogs = new FakeProjectDialogs();
        return new Setup(game, workspace, projects, dialogs, new ProjectsViewModel(projects, dialogs, game.Texts));
    }

    private static void Put(string projectFolder, string relativeFile, byte[] bytes)
    {
        var path = Path.Combine(projectFolder, "pack", relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    // A project "My Mod" (pack my_mod.pack) with one valid table file, created through the controller; it becomes the active project.
    private static ProjectState NewProject(Setup setup, string folderName = "mod")
    {
        var folder = setup.Game.NewFolder(folderName);
        Assert.True(setup.Projects.Create(folder, "My Mod", "my_mod.pack").Ok);
        var state = setup.Projects.Projects[^1];
        Put(state.Folder, @"db\x_tables\my_mod.tsv", new UTF8Encoding(false).GetBytes(Tsv));
        return state;
    }

    // A pack with a table, a loc file and a script, plus an entry that leaves the project folder and one with a control character in its
    // path (both skipped).
    private static string SourcePack(TestGame game) => new PackBuilder()
        .AddEntry(@"db\x_tables\my_mod", TestGame.TableBytes)
        .AddEntry(@"text\db\my_mod.loc", LocEncoder.Encode([new LocRow("k", "Text", false)]))
        .AddEntry(@"script\a.lua", new UTF8Encoding(false).GetBytes("print(1)"))
        .AddEntry(@"..\..\escape.txt", [1])
        .AddEntry(@"script\bad" + (char)7 + ".lua", [1])
        .WriteTo(game.Root.File("source.pack"));

    // ---- new project, open project -----------------------------------------------------------------------------------------------

    [Fact] public async Task New_project_asks_validates_and_creates()
    {
        using var setup = await CreateAsync(game => game.AddVanillaPack("data.pack"));
        var folder = setup.Game.Root.File("new-mod");
        setup.Dialogs.ProjectAnswer = new ProjectInput("My Mod", folder, "my_mod.pack");
        var changed = new List<string?>(); setup.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        setup.ViewModel.NewProjectCommand.Execute(null);

        var state = Assert.Single(setup.Projects.Projects);
        Assert.Equal(("My Mod", folder), (state.DisplayName, state.Folder));
        Assert.Equal(setup.Texts.ProjectCreated("My Mod"), setup.ViewModel.StatusMessage);
        Assert.Contains(nameof(ProjectsViewModel.StatusMessage), changed);
        Assert.Empty(setup.Dialogs.Messages);

        var ask = Assert.Single(setup.Dialogs.Asks);
        Assert.Equal((setup.Texts.ProjectNewTitle, new ProjectInput("", "", "")), (ask.Title, ask.Defaults));
        Assert.Empty(ask.Info);
        Assert.NotNull(ask.Validate(new ProjectInput("", folder, "x.pack")));
        Assert.NotNull(ask.Validate(new ProjectInput("  ", folder, "x.pack")));
        Assert.Equal(setup.Texts.ProjectFolderNotAbsolute, ask.Validate(new ProjectInput("N", @"relative\x", "x.pack")));
        Assert.Equal(setup.Texts.ProjectFolderNotAbsolute, ask.Validate(new ProjectInput("N", "", "x.pack")));
        var vanilla = ask.Validate(new ProjectInput("N", folder, "DATA.pack"));          // the name of a vanilla pack, whatever the case
        Assert.NotNull(vanilla);
        Assert.Equal(setup.Projects.CheckPackName("DATA.pack"), vanilla);
        Assert.Equal(setup.Projects.CheckPackName("data"), ask.Validate(new ProjectInput("N", folder, "data")));
        Assert.Null(ask.Validate(new ProjectInput("My Mod", folder, "my_mod.pack")));
    }

    [Fact] public async Task A_project_the_controller_refuses_is_shown_as_a_message()
    {
        using var setup = await CreateAsync();
        var busy = setup.Game.NewFolder("busy");
        File.WriteAllText(Path.Combine(busy, "x.txt"), "x");
        setup.Dialogs.ProjectAnswer = new ProjectInput("N", busy, "n.pack");

        setup.ViewModel.NewProjectCommand.Execute(null);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.ProjectProblemText(ProjectProblem.FolderNotEmpty), message);
        Assert.Empty(setup.Projects.Projects);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task A_cancelled_dialog_changes_nothing()
    {
        using var setup = await CreateAsync();
        var changes = 0; setup.Projects.Changed += (_, _) => changes++;

        setup.ViewModel.NewProjectCommand.Execute(null);          // no answer prepared: the user cancels
        setup.ViewModel.OpenProjectCommand.Execute(null);
        setup.ViewModel.ImportPackCommand.Execute(null);

        Assert.Single(setup.Dialogs.Asks);
        Assert.Equal([setup.Texts.ProjectOpenTitle], setup.Dialogs.FolderTitles);
        Assert.Equal(1, setup.Dialogs.PackFileCalls);
        Assert.Empty(setup.Projects.Projects);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Game.Runner.Titles);
        Assert.Equal((0, (string?)null), (changes, setup.ViewModel.StatusMessage));

        // A cancelled import dialog after a readable pack: the pack was read, nothing was imported.
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        Assert.Equal(setup.Texts.OperationReadPack("source.pack"), Assert.Single(setup.Game.Runner.Titles));
        Assert.Empty(setup.Projects.Projects);
    }

    [Fact] public async Task The_status_message_is_cleared_when_a_command_starts()
    {
        using var setup = await CreateAsync();
        setup.Dialogs.ProjectAnswer = new ProjectInput("My Mod", setup.Game.Root.File("a"), "my_mod.pack");
        setup.ViewModel.NewProjectCommand.Execute(null);
        Assert.NotNull(setup.ViewModel.StatusMessage);
        var changed = new List<string?>(); setup.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        setup.Dialogs.ProjectAnswer = null;
        setup.ViewModel.NewProjectCommand.Execute(null);

        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Contains(nameof(ProjectsViewModel.StatusMessage), changed);
    }

    [Fact] public async Task Open_project_shows_a_message_for_a_broken_folder()
    {
        using var setup = await CreateAsync();
        setup.Dialogs.FolderAnswers.Enqueue(setup.Game.NewFolder("empty"));          // no project file in it

        setup.ViewModel.OpenProjectCommand.Execute(null);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.ProjectProblemText(ProjectProblem.NotFound), message);
        Assert.Empty(setup.Projects.Projects);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Open_project_adds_an_existing_project()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        setup.ViewModel.Remove(state.Id);
        Assert.Empty(setup.Projects.Projects);
        setup.Dialogs.FolderAnswers.Enqueue(state.Folder);

        setup.ViewModel.OpenProjectCommand.Execute(null);

        Assert.Equal("My Mod", Assert.Single(setup.Projects.Projects).DisplayName);
        Assert.Equal(setup.Texts.ProjectAdded("My Mod"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
    }

    // ---- import ----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Import_shows_the_preview_and_reports_entries_that_stayed_binary_or_were_skipped()
    {
        using var setup = await CreateAsync(game => game.AddVanillaPack("data.pack"));
        var source = SourcePack(setup.Game);
        var skipped = (await setup.Projects.PreviewImportAsync(source))!.Preview!.Skipped;
        Assert.Equal(2, skipped.Count);
        var target = Path.Combine(setup.Game.NewFolder("parent"), "imported");
        setup.Dialogs.ProjectAnswer = new ProjectInput("Imported", target, "imported.pack");

        await setup.ViewModel.ImportAsync(source, "source.pack");

        var ask = Assert.Single(setup.Dialogs.Asks);
        Assert.Equal(setup.Texts.ImportTitle("source.pack"), ask.Title);
        Assert.Equal(new ProjectInput("source", "", "source.pack"), ask.Defaults);
        Assert.Contains(setup.Texts.ImportPreviewLineTables(1), ask.Info);
        Assert.Contains(setup.Texts.ImportPreviewLineLoc(1), ask.Info);
        Assert.Contains(setup.Texts.ImportPreviewLineOther(1), ask.Info);
        Assert.Contains(setup.Texts.ImportPreviewLineSkipped(2), ask.Info);
        Assert.Contains(ask.Info, line => line.StartsWith(setup.Texts.ImportPreviewLineSize(""), StringComparison.Ordinal));
        Assert.Equal(setup.Texts.ProjectFolderNotAbsolute, ask.Validate(new ProjectInput("Imported", "", "imported.pack")));
        Assert.Null(ask.Validate(new ProjectInput("Imported", target, "imported.pack")));

        Assert.Equal("Imported", Assert.Single(setup.Projects.Projects).DisplayName);
        Assert.Contains(setup.Texts.ImportSummary(1, 1, 1), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);

        var report = Assert.Single(setup.Dialogs.Reports);
        Assert.Equal(setup.Texts.ImportReportTitle, report.Title);
        Assert.Equal(setup.ViewModel.StatusMessage, report.Summary);
        var section = Assert.Single(report.Sections);
        Assert.Equal(setup.Texts.ImportReportSkippedHeading, section.Heading);
        Assert.Equal(2, section.Lines.Count);
        foreach (var entry in skipped)
        {
            Assert.Contains(section.Lines, line => line.StartsWith(DisplayText.Safe(entry.PackPath) + ": ", StringComparison.Ordinal));
        }

        Assert.Contains(section.Lines, line => line.StartsWith("script/bad" + Backslash + "u0007.lua: ", StringComparison.Ordinal));
        Assert.DoesNotContain(section.Lines, line => line.Any(char.IsControl));
    }

    [Fact] public async Task The_import_report_lists_tables_that_stayed_binary_and_dropped_dependencies()
    {
        using var setup = await CreateAsync();
        var source = new PackBuilder()
            .AddEntry(@"db\unknown_tables\t", [1, 2, 3])          // a table the schema does not know: kept as a binary file
            .AddDependency("   ")                                  // a blank dependency name: not taken over
            .WriteTo(setup.Game.Root.File("odd.pack"));
        setup.Dialogs.ProjectAnswer = new ProjectInput("Odd", setup.Game.Root.File("odd"), "odd.pack");

        await setup.ViewModel.ImportAsync(source, "odd.pack");

        var report = Assert.Single(setup.Dialogs.Reports);
        Assert.Equal([setup.Texts.ImportReportBinaryHeading, setup.Texts.ImportReportDroppedHeading], report.Sections.Select(section => section.Heading));
        Assert.StartsWith("db/unknown_tables/t: ", Assert.Single(report.Sections[0].Lines));          // pack paths are written with slashes
        Assert.Single(report.Sections[1].Lines);
    }

    [Fact] public async Task An_import_without_notable_entries_shows_no_report()
    {
        using var setup = await CreateAsync();
        var source = new PackBuilder().AddEntry(@"script\a.lua", new UTF8Encoding(false).GetBytes("print(1)")).WriteTo(setup.Game.Root.File("plain.pack"));
        setup.Dialogs.ProjectAnswer = new ProjectInput("Plain", setup.Game.Root.File("plain"), "plain.pack");

        await setup.ViewModel.ImportAsync(source, "plain.pack");

        Assert.Single(setup.Projects.Projects);
        Assert.Contains(setup.Texts.ImportSummary(0, 0, 1), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Reports);
    }

    [Fact] public async Task The_import_command_asks_for_a_pack_file_and_names_the_dialog_after_it()
    {
        using var setup = await CreateAsync();
        var source = SourcePack(setup.Game);
        setup.Dialogs.PackFileAnswers.Enqueue(source);
        setup.Dialogs.ProjectAnswer = new ProjectInput("Imported", setup.Game.Root.File("imported"), "imported.pack");

        setup.ViewModel.ImportPackCommand.Execute(null);

        Assert.Equal(setup.Texts.ImportTitle("source.pack"), Assert.Single(setup.Dialogs.Asks).Title);
        Assert.Single(setup.Projects.Projects);
    }

    [Fact] public async Task An_unreadable_pack_or_a_refused_import_is_shown_as_a_message()
    {
        using var setup = await CreateAsync();
        var busy = setup.Game.NewFolder("busy");
        File.WriteAllText(Path.Combine(busy, "x.txt"), "x");

        await setup.ViewModel.ImportAsync(@"relative.pack", "relative.pack");
        await setup.ViewModel.ImportAsync(setup.Game.Root.File("missing.pack"), "missing.pack");

        Assert.Empty(setup.Dialogs.Asks);
        Assert.Equal(2, setup.Dialogs.Messages.Count);
        Assert.All(setup.Dialogs.Messages, message => Assert.Equal(setup.Texts.ImportMessageTitle, message.Title));
        Assert.Equal(setup.Texts.ProjectPackPathNotAbsolute, setup.Dialogs.Messages[0].Message);

        setup.Dialogs.Messages.Clear();
        setup.Dialogs.ProjectAnswer = new ProjectInput("N", busy, "n.pack");
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ImportMessageTitle, title);
        Assert.StartsWith(setup.Texts.ImportProblemText(ImportProblem.TargetNotEmpty), message);
        Assert.Empty(setup.Projects.Projects);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public void A_hostile_path_is_shown_escaped()
    {
        var hostile = "a" + (char)7 + "b" + (char)0xD800;
        var expected = string.Concat("a", Backslash, "u0007b", Backslash, "uD800");

        Assert.Equal(expected, DisplayText.Safe(hostile));
        Assert.Equal(string.Concat("x", Backslash, "u000A", Backslash, "u007Fy", Backslash, "uDC00"), DisplayText.Safe("x\n" + (char)0x7F + "y" + (char)0xDC00));

        // Well-formed text stays as it is, including a surrogate pair and a path.
        var smile = char.ConvertFromUtf32(0x1F600);
        Assert.Equal(@"db\tables\" + smile + ".tsv", DisplayText.Safe(@"db\tables\" + smile + ".tsv"));
        Assert.Equal(string.Empty, DisplayText.Safe(string.Empty));

        // Long text is cut after maxChars characters (counted before the escaping) and marked.
        Assert.Equal(new string('a', 300) + " (shortened)", DisplayText.Safe(new string('a', 1000)));
        Assert.Equal(new string('a', 300), DisplayText.Safe(new string('a', 300)));
        Assert.Equal("abc (shortened)", DisplayText.Safe("abcdef", 3));
        Assert.Equal(string.Concat(Backslash, "u0007", Backslash, "u0007 (shortened)"), DisplayText.Safe("\a\a\a\a", 2));

        // A pair is never split by the cut.
        Assert.Equal("a (shortened)", DisplayText.Safe("a" + smile, 2));
        Assert.Equal("a" + smile + " (shortened)", DisplayText.Safe("a" + smile + "b", 3));
    }

    // ---- build -----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Build_problems_open_a_report_with_file_and_line()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        Put(state.Folder, @"db\x_tables\my_mod.tsv", Encoding.UTF8.GetBytes(Tsv.Replace("42", "many")));
        Put(state.Folder, @"db\x_tables\.~lock.my_mod.tsv#", [1]);
        var problem = (await setup.Projects.BuildAsync(state.Id, install: false))!.Problems[0];

        await setup.ViewModel.BuildAsync(state.Id, install: false);

        var report = Assert.Single(setup.Dialogs.Reports);
        Assert.Equal(setup.Texts.BuildReportTitle, report.Title);
        Assert.Equal(setup.Texts.BuildProblemsSummary(1), report.Summary);
        Assert.Equal([setup.Texts.BuildProblemsHeading, setup.Texts.BuildIgnoredHeading], report.Sections.Select(section => section.Heading));
        var line = Assert.Single(report.Sections[0].Lines);
        Assert.Equal(3, problem.Line);
        Assert.Equal(@"db\x_tables\my_mod.tsv(3): " + DisplayText.Safe(problem.Detail), line);
        Assert.Equal([@"db\x_tables\.~lock.my_mod.tsv#"], report.Sections[1].Lines);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.False(Directory.Exists(Path.Combine(state.Folder, "build")));
    }

    [Fact] public async Task A_successful_build_only_sets_the_status_message()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);

        await setup.ViewModel.BuildAsync(state.Id, install: false);

        Assert.Equal(setup.Texts.BuildDone("my_mod.pack", 1), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Dialogs.Messages);

        await setup.ViewModel.BuildAsync(state.Id, install: true);

        Assert.Equal(setup.Texts.BuildInstalled("my_mod.pack"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.True(File.Exists(Path.Combine(setup.Game.DataPath, "my_mod.pack")));
    }

    [Fact] public async Task A_build_that_could_not_be_done_is_shown_as_a_message()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        Directory.Delete(state.Folder, recursive: true);
        setup.Projects.Reload();

        await setup.ViewModel.BuildAsync(state.Id, install: false);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.BuildMessageTitle, title);
        Assert.StartsWith(setup.Texts.ProjectProblemText(ProjectProblem.NotFound), message);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task An_install_that_was_refused_is_shown_as_a_message_too()
    {
        using var setup = await CreateAsync(game => game.AddPack("my_mod.pack", ("a.txt", [9, 9])));          // a foreign file with the name of the pack
        var state = NewProject(setup);

        await setup.ViewModel.BuildAsync(state.Id, install: true);

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.BuildMessageTitle, message.Title);
        Assert.StartsWith(setup.Texts.InstallProblemText(InstallProblem.ForeignFile), message.Message);
        Assert.Equal(message.Message, setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Nothing_is_shown_when_no_operation_could_start()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        await setup.ViewModel.BuildAsync(state.Id, install: true);          // so that there is something to uninstall
        var runs = setup.Game.Runner.Titles.Count;
        setup.Game.Runner.IsRunning = true;

        await setup.ViewModel.BuildAsync(state.Id, install: false);
        await setup.ViewModel.UninstallAsync(state.Id);
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Dialogs.Asks);
        Assert.Equal(runs, setup.Game.Runner.Titles.Count);
    }

    // ---- uninstall, set active, remove -------------------------------------------------------------------------------------------

    [Fact] public async Task Uninstall_removes_the_pack_and_says_so()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);

        await setup.ViewModel.UninstallAsync(state.Id);

        Assert.Equal(setup.Texts.UninstallNotInstalled, Assert.Single(setup.Dialogs.Messages).Message);
        Assert.Equal(setup.Texts.UninstallMessageTitle, setup.Dialogs.Messages[0].Title);

        await setup.ViewModel.BuildAsync(state.Id, install: true);
        Assert.NotNull(setup.Projects.Projects[0].Installed);
        await setup.ViewModel.UninstallAsync(state.Id);

        Assert.Null(setup.Projects.Projects[0].Installed);
        Assert.False(File.Exists(Path.Combine(setup.Game.DataPath, "my_mod.pack")));
        Assert.Equal(setup.Texts.UninstallDone("My Mod"), setup.ViewModel.StatusMessage);
        Assert.Single(setup.Dialogs.Messages);
    }

    [Fact] public async Task Set_active_and_remove_change_the_list_without_asking()
    {
        using var setup = await CreateAsync();
        var first = NewProject(setup, "one");
        var second = NewProject(setup, "two");
        Assert.Equal(first.Id, setup.Projects.ActiveId);

        setup.ViewModel.SetActive(second.Id);

        Assert.Equal(second.Id, setup.Projects.ActiveId);

        setup.ViewModel.Remove(second.Id);

        Assert.Equal([first.Id], setup.Projects.Projects.Select(state => state.Id));
        Assert.True(Directory.Exists(second.Folder));          // the folder stays
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Asks);

        setup.ViewModel.Remove(Guid.NewGuid());
        setup.ViewModel.SetActive(Guid.NewGuid());

        Assert.Equal(2, setup.Dialogs.Messages.Count);
        Assert.All(setup.Dialogs.Messages, message => Assert.Equal((setup.Texts.ProjectMessageTitle, setup.Texts.ProjectUnknown), message));
    }

    // ---- disabled commands -------------------------------------------------------------------------------------------------------

    [Fact] public async Task Commands_for_the_active_project_are_disabled_with_a_reason()
    {
        using var setup = await CreateAsync();
        var vm = setup.ViewModel;
        var raised = 0; ((RelayCommand)vm.BuildActiveCommand).CanExecuteChanged += (_, _) => raised++;

        Assert.Equal(setup.Texts.ProjectNoneActive, vm.BuildDisabledReason(null));
        Assert.Equal(setup.Texts.ProjectNoneActive, vm.InstallDisabledReason(null));
        Assert.False(vm.BuildActiveCommand.CanExecute(null));
        Assert.False(vm.BuildAndInstallActiveCommand.CanExecute(null));
        Assert.Equal(setup.Texts.ProjectUnknown, vm.BuildDisabledReason(Guid.NewGuid()));
        Assert.Equal(setup.Texts.ProjectUnknown, vm.UninstallDisabledReason(Guid.NewGuid()));

        var state = NewProject(setup);

        Assert.True(raised > 0);              // the list changed: the commands are asked again
        Assert.Null(vm.BuildDisabledReason(state.Id));
        Assert.Null(vm.InstallDisabledReason(state.Id));
        Assert.Equal(setup.Texts.UninstallNothing, vm.UninstallDisabledReason(state.Id));
        Assert.True(vm.BuildActiveCommand.CanExecute(null));
        Assert.True(vm.BuildAndInstallActiveCommand.CanExecute(null));

        // A running operation disables the commands without giving a reason: the progress window already shows what runs.
        setup.Game.Runner.IsRunning = true;
        Assert.Null(vm.BuildDisabledReason(state.Id));
        Assert.False(vm.BuildActiveCommand.CanExecute(null));
        Assert.False(vm.BuildAndInstallActiveCommand.CanExecute(null));
        setup.Game.Runner.IsRunning = false;

        // After an install there is something to uninstall.
        await vm.BuildAsync(state.Id, install: true);
        Assert.Null(vm.UninstallDisabledReason(state.Id));

        // A project that cannot be used says why.
        Directory.Delete(state.Folder, recursive: true);
        setup.Projects.Reload();
        var problem = setup.Texts.ProjectProblemText(ProjectProblem.NotFound);
        Assert.Equal(problem, vm.BuildDisabledReason(state.Id));
        Assert.Equal(problem, vm.InstallDisabledReason(state.Id));
        Assert.False(vm.BuildActiveCommand.CanExecute(null));
        Assert.False(vm.BuildAndInstallActiveCommand.CanExecute(null));

        // The installed pack can still be removed: it is removed by the record of the list, not by the project file.
        Assert.Null(vm.UninstallDisabledReason(state.Id));
    }

    [Fact] public async Task Installing_is_disabled_without_a_game_folder()
    {
        using var game = new TestGame();
        var workspace = new WorkspaceController(TestGame.Profile, new GameSettings(), new GlobalSettings(), game.Paths, new NoSteam(), game.Runner,
            game.Log, game.Texts);
        await workspace.LoadAsync();
        var setup = Wire(game, workspace);
        var state = NewProject(setup);

        Assert.Null(setup.ViewModel.BuildDisabledReason(state.Id));
        Assert.Equal(game.Texts.InstallNoGameFolder, setup.ViewModel.InstallDisabledReason(state.Id));
        Assert.Equal(game.Texts.InstallNoGameFolder, setup.ViewModel.UninstallDisabledReason(state.Id));
        Assert.True(setup.ViewModel.BuildActiveCommand.CanExecute(null));
        Assert.False(setup.ViewModel.BuildAndInstallActiveCommand.CanExecute(null));
    }

    [Fact] public async Task The_active_build_commands_build_the_active_project()
    {
        using var setup = await CreateAsync();
        var first = NewProject(setup, "one");
        var second = NewProject(setup, "two");
        setup.ViewModel.SetActive(second.Id);

        setup.ViewModel.BuildAndInstallActiveCommand.Execute(null);

        Assert.Equal(setup.Texts.OperationBuildAndInstall("My Mod"), Assert.Single(setup.Game.Runner.Titles));
        Assert.Equal(setup.Texts.BuildInstalled("my_mod.pack"), setup.ViewModel.StatusMessage);
        Assert.Null(setup.Projects.Projects.Single(state => state.Id == first.Id).Installed);
        Assert.NotNull(setup.Projects.Projects.Single(state => state.Id == second.Id).Installed);

        setup.ViewModel.BuildActiveCommand.Execute(null);

        Assert.Equal(setup.Texts.OperationBuild("My Mod"), setup.Game.Runner.Titles[^1]);
    }

}
