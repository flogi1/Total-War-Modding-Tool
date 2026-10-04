using System.Text;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Settings;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ProjectsControllerOperationsTests
{
    // One row of the table "x_tables" of the test schema, in the text form of a project.
    private const string Tsv = "value\n#x_tables;0;db/x_tables/my_mod\n42\n";

    private static readonly byte[] Utf8Tsv = new UTF8Encoding(false).GetBytes(Tsv);

    private static async Task<WorkspaceController> LoadedAsync(TestGame game)
    {
        var workspace = game.NewController();
        await workspace.LoadAsync();
        return workspace;
    }

    private static string Put(string projectFolder, string relativeFile, byte[] bytes)
    {
        var path = Path.Combine(projectFolder, "pack", relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // A project "My Mod" (pack my_mod.pack) with one valid table file, created through the controller.
    private static ProjectState NewProject(TestGame game, ProjectsController projects, string folderName = "mod", string packName = "my_mod.pack")
    {
        var folder = game.NewFolder(folderName);
        Assert.True(projects.Create(folder, "My Mod", packName).Ok);
        var state = projects.Projects[^1];
        Put(state.Folder, @"db\x_tables\my_mod.tsv", Utf8Tsv);
        return state;
    }

    private static string SourcePack(TestGame game) => new PackBuilder()
        .AddEntry(@"db\x_tables\my_mod", TestGame.TableBytes)
        .AddEntry(@"text\db\my_mod.loc", LocEncoder.Encode([new LocRow("k", "Text", false)]))
        .AddEntry(@"script\a.lua", new UTF8Encoding(false).GetBytes("print(1)"))
        .AddEntry(@"..\..\escape.txt", [1])
        .WriteTo(game.Root.File("source.pack"));

    // ---- import ----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Import_creates_a_project_adds_it_to_the_list_and_reports_counts()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var source = SourcePack(game);
        var before = File.ReadAllBytes(source);

        var preview = await projects.PreviewImportAsync(source);

        Assert.Null(preview!.Message);
        Assert.Equal((1, 1, 1, 1), (preview.Preview!.Tables, preview.Preview.LocFiles, preview.Preview.OtherFiles, preview.Preview.Skipped.Count));
        Assert.Equal(game.Texts.OperationReadPack("source.pack"), Assert.Single(game.Runner.Titles));

        var target = Path.Combine(game.NewFolder("parent"), "imported");
        var changes = 0; projects.Changed += (_, _) => changes++;
        var result = await projects.ImportAsync(source, target, "Imported", "imported.pack");

        Assert.True(result!.Ok);
        Assert.Equal(1, result.Report!.Tables);
        Assert.Contains(game.Texts.ImportSummary(1, 1, 1), result.Message);
        Assert.Contains(game.Texts.ImportSkipped(1), result.Message);
        var state = Assert.Single(projects.Projects);
        Assert.Equal((result.ProjectId, "Imported", 1), (state.Id, state.DisplayName, changes));
        Assert.Equal(state.Id, projects.ActiveId);
        Assert.Equal(game.Texts.OperationImportPack("source.pack"), game.Runner.Titles[1]);
        Assert.True(File.Exists(Path.Combine(target, "pack", "db", "x_tables", "my_mod.tsv")));
        Assert.Equal(state.Id, Assert.Single(game.NewProjects(await LoadedAsync(game)).Projects).Id);   // saved
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact] public async Task Import_problems_leave_the_list_unchanged()
    {
        using var game = new TestGame();
        game.AddVanillaPack("data.pack");
        var projects = game.NewProjects(await LoadedAsync(game));
        var source = SourcePack(game);
        var busy = game.NewFolder("busy");
        File.WriteAllText(Path.Combine(busy, "x.txt"), "x");

        var vanilla = await projects.ImportAsync(source, game.Root.File("p1"), "N", "DATA.pack");
        var notEmpty = await projects.ImportAsync(source, busy, "N", "n.pack");
        var unreadable = await projects.ImportAsync(game.Root.File("missing.pack"), game.Root.File("p2"), "N", "n.pack");

        Assert.False(vanilla!.Ok);
        Assert.StartsWith(game.Texts.ImportProblemText(ImportProblem.InvalidPackName), vanilla.Message);
        Assert.False(notEmpty!.Ok);
        Assert.StartsWith(game.Texts.ImportProblemText(ImportProblem.TargetNotEmpty), notEmpty.Message);
        Assert.False(unreadable!.Ok);
        Assert.StartsWith(game.Texts.ImportProblemText(ImportProblem.SourceUnreadable), unreadable.Message);
        Assert.Empty(projects.Projects);
        Assert.False(Directory.Exists(game.Root.File("p1")));
    }

    [Fact] public async Task Input_the_library_would_throw_on_is_refused_before_the_runner_is_used()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var source = SourcePack(game);
        var target = game.Root.File("p");

        var emptyName = await projects.ImportAsync(source, target, "  ", "n.pack");
        var relativeTarget = await projects.ImportAsync(source, @"relative\x", "N", "n.pack");
        var relativeSource = await projects.ImportAsync(@"relative.pack", target, "N", "n.pack");
        var relativePreview = await projects.PreviewImportAsync(@"relative.pack");

        Assert.Equal((false, game.Texts.ProjectNameEmpty), (emptyName!.Ok, emptyName.Message));
        Assert.Equal((false, game.Texts.ProjectFolderNotAbsolute), (relativeTarget!.Ok, relativeTarget.Message));
        Assert.False(relativeSource!.Ok);
        Assert.Equal(((ImportPreview?)null, game.Texts.ProjectPackPathNotAbsolute), (relativePreview!.Preview, relativePreview.Message));
        Assert.Empty(game.Runner.Titles);
        Assert.Empty(game.Log.Errors);
    }

    [Fact] public async Task Import_refuses_the_folder_of_a_project_that_is_in_the_list_already()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);
        Directory.Delete(state.Folder, recursive: true);

        var result = await projects.ImportAsync(SourcePack(game), state.Folder, "N", "n.pack");

        Assert.Equal((false, game.Texts.ProjectAlreadyInList(state.Folder)), (result!.Ok, result.Message));
        Assert.False(Directory.Exists(state.Folder));
    }

    [Fact] public async Task A_cancelled_import_changes_nothing_and_names_a_leftover_staging_folder()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var source = SourcePack(game);
        var target = Path.Combine(game.NewFolder("parent"), "cancelled");

        game.Runner.CancelNext = true;
        var first = await projects.ImportAsync(source, target, "N", "n.pack");

        Assert.Equal((false, game.Texts.ImportCancelled), (first!.Ok, first.Message));
        Assert.Empty(projects.Projects);
        Assert.False(Directory.Exists(target));

        var leftover = target + ".import-0123abcd";
        Directory.CreateDirectory(leftover);
        game.Runner.CancelNext = true;
        var second = await projects.ImportAsync(source, target, "N", "n.pack");

        Assert.Equal((false, game.Texts.ImportLeftover(leftover)), (second!.Ok, second.Message));
        Assert.Empty(projects.Projects);
        Assert.False(Directory.Exists(target));
    }

    // ---- build -----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Build_writes_the_pack_and_reports_ignored_files()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);
        Put(state.Folder, @"db\x_tables\.~lock.my_mod.tsv#", [1]);

        var report = await projects.BuildAsync(state.Id, install: false);

        Assert.True(report!.Built);
        Assert.False(report.Installed);
        Assert.True(File.Exists(report.PackPath));
        Assert.Equal(Path.Combine(state.Folder, "build", "my_mod.pack"), report.PackPath);
        Assert.Single(report.Ignored);
        Assert.Contains(game.Texts.BuildDone("my_mod.pack", 1), report.Message);
        Assert.Contains(game.Texts.BuildIgnored(1), report.Message);
        Assert.Equal(game.Texts.OperationBuild("My Mod"), Assert.Single(game.Runner.Titles));
        Assert.Empty(Directory.EnumerateFiles(game.DataPath));
    }

    [Fact] public async Task Build_problems_are_returned_and_nothing_is_written()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);
        Put(state.Folder, @"db\x_tables\my_mod.tsv", Encoding.UTF8.GetBytes(Tsv.Replace("42", "many")));

        var report = await projects.BuildAsync(state.Id, install: false);

        Assert.False(report!.Built);
        Assert.Null(report.PackPath);
        Assert.Equal(3, report.Problems[0].Line);
        Assert.False(Directory.Exists(Path.Combine(state.Folder, "build")));
    }

    [Fact] public async Task A_project_that_cannot_be_used_is_not_built()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);
        Directory.Delete(state.Folder, recursive: true);
        projects.Reload();

        var report = await projects.BuildAsync(state.Id, install: false);

        Assert.False(report!.Built);
        Assert.StartsWith(game.Texts.ProjectProblemText(ProjectProblem.NotFound), report.Message);
        Assert.Empty(game.Runner.Titles);
    }

    [Fact] public async Task Nothing_starts_while_another_operation_runs_and_a_cancelled_build_gives_null()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);

        game.Runner.IsRunning = true;
        Assert.False(projects.CanStartOperation);
        Assert.Null(await projects.BuildAsync(state.Id, install: false));
        Assert.Null(await projects.PreviewImportAsync(game.Root.File("x.pack")));
        Assert.Null(await projects.ListFilesAsync(state.Id));
        Assert.Empty(game.Runner.Titles);
        game.Runner.IsRunning = false;

        game.Runner.CancelNext = true;
        Assert.Null(await projects.BuildAsync(state.Id, install: false));
        Assert.False(Directory.Exists(Path.Combine(state.Folder, "build")));
    }

    // ---- install ---------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Build_and_install_copies_the_pack_and_remembers_the_fingerprint()
    {
        using var game = new TestGame();
        var workspace = await LoadedAsync(game);
        var projects = game.NewProjects(workspace);
        var state = NewProject(game, projects);
        var changes = 0; projects.Changed += (_, _) => changes++;

        var report = await projects.BuildAsync(state.Id, install: true);

        var installedPath = Path.Combine(game.DataPath, "my_mod.pack");
        Assert.True(report!.Built);
        Assert.True(report.Installed);
        Assert.Equal(game.Texts.BuildInstalled("my_mod.pack"), report.Message);
        Assert.Equal(File.ReadAllBytes(report.PackPath!), File.ReadAllBytes(installedPath));
        Assert.Equal(game.Texts.OperationBuildAndInstall("My Mod"), Assert.Single(game.Runner.Titles));
        Assert.Equal(("my_mod.pack", new FileInfo(installedPath).Length), (projects.Projects[0].Installed!.FileName, projects.Projects[0].Installed!.Length));
        Assert.Equal(1, changes);

        // A new controller over the same store still owns the file and replaces it.
        var again = game.NewProjects(workspace);
        Assert.NotNull(again.Projects[0].Installed);
        var second = await again.BuildAsync(again.Projects[0].Id, install: true);
        Assert.True(second!.Installed);
    }

    [Fact] public async Task A_foreign_pack_in_the_data_folder_is_never_replaced()
    {
        using var game = new TestGame();
        var foreign = game.AddPack("my_mod.pack", ("a.txt", [9, 9]));
        var before = File.ReadAllBytes(foreign);
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);

        var report = await projects.BuildAsync(state.Id, install: true);

        Assert.True(report!.Built);
        Assert.False(report.Installed);
        Assert.StartsWith(game.Texts.InstallProblemText(InstallProblem.ForeignFile), report.Message);
        Assert.Equal(before, File.ReadAllBytes(foreign));
        Assert.Null(projects.Projects[0].Installed);
    }

    [Fact] public async Task Install_without_a_game_folder_is_refused_with_a_reason()
    {
        using var game = new TestGame();
        var workspace = new WorkspaceController(TestGame.Profile, new GameSettings(), new GlobalSettings(), game.Paths, new NoSteam(), game.Runner,
            game.Log, game.Texts);
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace);
        var state = NewProject(game, projects);

        var report = await projects.BuildAsync(state.Id, install: true);
        var uninstall = await projects.UninstallAsync(state.Id);

        Assert.Equal(game.Texts.InstallNoGameFolder, projects.InstallDisabledReason);
        Assert.Equal((false, game.Texts.InstallNoGameFolder), (report!.Built, report.Message));
        Assert.Equal((false, game.Texts.InstallNoGameFolder), (uninstall!.Ok, uninstall.Message));
        Assert.False(Directory.Exists(Path.Combine(state.Folder, "build")));
        Assert.Empty(game.Runner.Titles);
    }

    [Fact] public async Task Installing_is_possible_when_the_game_was_found()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));

        Assert.Null(projects.InstallDisabledReason);
    }

    [Fact] public async Task A_changed_pack_name_uninstalls_the_old_pack_first()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects, packName: "a.pack");
        Assert.True((await projects.BuildAsync(state.Id, install: true))!.Installed);
        Assert.True(File.Exists(Path.Combine(game.DataPath, "a.pack")));

        Assert.Equal(ProjectProblem.None, ProjectFile.Save(state.Folder, projects.Projects[0].Project! with { PackName = "b.pack" }).Problem);
        projects.Reload();
        var report = await projects.BuildAsync(state.Id, install: true);

        Assert.True(report!.Installed);
        Assert.False(File.Exists(Path.Combine(game.DataPath, "a.pack")));
        Assert.True(File.Exists(Path.Combine(game.DataPath, "b.pack")));
        Assert.Equal("b.pack", projects.Projects[0].Installed!.FileName);
    }

    [Fact] public async Task An_old_pack_that_cannot_be_removed_stops_the_install_with_a_message()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects, packName: "a.pack");
        Assert.True((await projects.BuildAsync(state.Id, install: true))!.Installed);
        var old = Path.Combine(game.DataPath, "a.pack");
        File.AppendAllText(old, "changed by someone else");              // no longer the file the tool installed
        Assert.Equal(ProjectProblem.None, ProjectFile.Save(state.Folder, projects.Projects[0].Project! with { PackName = "b.pack" }).Problem);
        projects.Reload();

        var report = await projects.BuildAsync(state.Id, install: true);

        Assert.True(report!.Built);
        Assert.False(report.Installed);
        Assert.StartsWith(game.Texts.BuildOldPackNotRemoved("a.pack"), report.Message);
        Assert.Contains(game.Texts.InstallProblemText(InstallProblem.ForeignFile), report.Message);
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(Path.Combine(game.DataPath, "b.pack")));
        Assert.Equal("a.pack", projects.Projects[0].Installed!.FileName);   // the record of the old pack stays
    }

    [Fact] public async Task Uninstall_removes_the_pack_and_the_record()
    {
        using var game = new TestGame();
        var workspace = await LoadedAsync(game);
        var projects = game.NewProjects(workspace);
        var state = NewProject(game, projects);

        var none = await projects.UninstallAsync(state.Id);
        Assert.Equal((false, game.Texts.UninstallNotInstalled), (none!.Ok, none.Message));

        Assert.True((await projects.BuildAsync(state.Id, install: true))!.Installed);
        var result = await projects.UninstallAsync(state.Id);

        Assert.Equal((true, (string?)null), (result!.Ok, result.Message));
        Assert.False(File.Exists(Path.Combine(game.DataPath, "my_mod.pack")));
        Assert.Null(projects.Projects[0].Installed);
        Assert.Null(game.NewProjects(workspace).Projects[0].Installed);
        Assert.Equal(game.Texts.OperationUninstall("My Mod"), game.Runner.Titles[^1]);

        var unknown = await projects.UninstallAsync(Guid.NewGuid());
        Assert.Equal((false, game.Texts.ProjectUnknown), (unknown!.Ok, unknown.Message));
    }

    // ---- files -----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task ListFiles_and_OpenFile_serve_the_explorer()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);
        Put(state.Folder, @"script\a.lua", [1]);
        Put(state.Folder, @"script\a.lua.bak", [1]);

        var list = await projects.ListFilesAsync(state.Id);

        Assert.Equal([@"db\x_tables\my_mod.tsv", @"script\a.lua"], list!.Files.Select(f => f.RelativeFile));
        Assert.Equal([@"script\a.lua.bak"], list.Ignored);
        Assert.Equal(game.Texts.OperationOpenProject("My Mod"), Assert.Single(game.Runner.Titles));

        var opened = await projects.OpenFileAsync(state.Id, @"db\x_tables\my_mod.tsv");

        Assert.Equal((EntryKind.Table, TableDecodeStatus.Ok), (opened!.Kind, opened.Table!.Status));
        Assert.Equal(game.Texts.OperationOpenFile("my_mod.tsv"), game.Runner.Titles[1]);
        Assert.Null(await projects.ListFilesAsync(Guid.NewGuid()));
        Assert.Null(await projects.OpenFileAsync(Guid.NewGuid(), @"a.lua"));
    }

    [Fact] public async Task ListFilesSilently_lists_without_the_runner()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(await LoadedAsync(game));
        var state = NewProject(game, projects);

        var list = await projects.ListFilesSilentlyAsync(state.Id);

        Assert.Equal([@"db\x_tables\my_mod.tsv"], list!.Files.Select(f => f.RelativeFile));
        Assert.Empty(game.Runner.Titles);
        Assert.Null(await projects.ListFilesSilentlyAsync(Guid.NewGuid()));
    }
}
