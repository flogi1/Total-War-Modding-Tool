using TotalWarModTool.Projects;
using TotalWarModTool.Storage;

namespace TotalWarModTool.App.Tests;

public class ProjectsControllerTests
{
    private static readonly string[] NoDependencies = [];

    // A valid project folder of the test game, made by the library.
    private static ModProject NewProjectIn(string folder, string name = "Existing", string gameId = "testgame")
    {
        var created = ProjectFile.CreateNew(folder, name, gameId, "existing.pack", NoDependencies);
        Assert.Equal(ProjectProblem.None, created.Problem);
        return created.Project!;
    }

    private static string ProjectFilePath(string folder) => Path.Combine(folder, ProjectFile.FileName);

    [Fact] public void Create_adds_an_active_project_and_saves_the_list()
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        var projects = game.NewProjects(workspace);
        var changes = 0; projects.Changed += (_, _) => changes++;
        var folder = game.NewFolder("my-mod");

        var result = projects.Create(folder, "My Mod", "my_mod.pack");

        Assert.Equal((true, (string?)null), (result.Ok, result.Message));
        Assert.Single(projects.Projects);
        Assert.Equal("My Mod", projects.Active!.DisplayName);
        Assert.Equal(1, changes);
        Assert.True(File.Exists(ProjectFilePath(folder)));
        var second = game.NewProjects(workspace);
        Assert.Equal(("My Mod", projects.ActiveId), (second.Projects.Single().DisplayName, second.ActiveId));
    }

    [Fact] public async Task Create_refuses_a_vanilla_or_invalid_pack_name_and_a_relative_folder()
    {
        using var game = new TestGame();
        game.AddVanillaPack("data.pack");
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace);
        var folder = game.NewFolder("refused");

        var vanilla = projects.Create(folder, "Mod", "DATA.pack");
        var vanillaReason = PackNameRules.Check("DATA.pack", ["data.pack"])!;
        var invalid = projects.Create(folder, "Mod", "nope");
        var invalidReason = PackNameRules.Check("nope")!;
        var relative = projects.Create(@"relative\x", "Mod", "mod.pack");

        Assert.Equal((false, game.Texts.ProjectPackNameInvalid(vanillaReason)), (vanilla.Ok, vanilla.Message));
        Assert.Equal((false, game.Texts.ProjectPackNameInvalid(invalidReason)), (invalid.Ok, invalid.Message));
        Assert.Equal((false, game.Texts.ProjectFolderNotAbsolute), (relative.Ok, relative.Message));
        Assert.Empty(projects.Projects);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.False(Directory.Exists(Path.GetFullPath(@"relative\x")));
        Assert.Equal(game.Texts.ProjectPackNameInvalid(vanillaReason), projects.CheckPackName("data.PACK"));
        Assert.Null(projects.CheckPackName("mine.pack"));
    }

    [Fact] public void Create_reports_a_library_problem_with_its_detail_on_a_second_line()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());
        var folder = game.NewFolder("busy");
        File.WriteAllText(Path.Combine(folder, "other.txt"), "x");

        var result = projects.Create(folder, "Mod", "mod.pack");

        Assert.False(result.Ok);
        Assert.Equal(game.Texts.ProjectProblemText(ProjectProblem.FolderNotEmpty) + "\n\n" + "The project folder is not empty.", result.Message);
        Assert.Empty(projects.Projects);
    }

    [Fact] public void Create_refuses_the_folder_of_a_project_that_is_already_in_the_list()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());
        var folder = game.NewFolder("twice");
        Assert.True(projects.Create(folder, "One", "one.pack").Ok);
        Directory.Delete(folder, recursive: true);                          // the folder is gone, the entry stays

        var result = projects.Create(folder + Path.DirectorySeparatorChar, "Two", "two.pack");

        Assert.Equal((false, game.Texts.ProjectAlreadyInList(folder)), (result.Ok, result.Message));
        Assert.False(Directory.Exists(folder));                             // nothing was created behind the list's back
    }

    [Fact] public void AddExisting_rejects_broken_foreign_and_duplicate_projects()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());

        var broken = game.NewFolder("broken");
        File.WriteAllText(ProjectFilePath(broken), "{ not json");
        var foreign = game.NewFolder("foreign");
        NewProjectIn(foreign, "Foreign", gameId: "other");
        var valid = game.NewFolder("valid");
        NewProjectIn(valid);

        var brokenResult = projects.AddExisting(broken);
        var foreignResult = projects.AddExisting(foreign);
        var first = projects.AddExisting(valid);
        var second = projects.AddExisting(valid);
        var relative = projects.AddExisting(@"relative\x");

        Assert.False(brokenResult.Ok);
        Assert.StartsWith(game.Texts.ProjectProblemText(ProjectProblem.Corrupt) + "\n\n", brokenResult.Message);
        Assert.Equal((false, game.Texts.ProjectOtherGame), (foreignResult.Ok, foreignResult.Message));
        Assert.Equal((true, (string?)null), (first.Ok, first.Message));
        Assert.Equal((false, game.Texts.ProjectAlreadyInList(valid)), (second.Ok, second.Message));
        Assert.Equal((false, game.Texts.ProjectFolderNotAbsolute), (relative.Ok, relative.Message));
        Assert.Single(projects.Projects);
        Assert.Equal("{ not json", File.ReadAllText(ProjectFilePath(broken)));
    }

    [Fact] public void A_missing_or_broken_project_stays_in_the_list_with_its_problem()
    {
        using var game = new TestGame();
        var gone = game.Root.File(Path.Combine("projects", "gone"));       // never created
        var broken = game.NewFolder("broken");
        File.WriteAllText(ProjectFilePath(broken), "{ not json");
        var brokenBytes = File.ReadAllBytes(ProjectFilePath(broken));
        var valid = game.NewFolder("valid");
        var project = NewProjectIn(valid, "Valid");
        var entries = new List<ProjectListEntry>
        {
            new(Guid.NewGuid(), gone, null),
            new(Guid.NewGuid(), broken, null),
            new(project.Id, valid, null),
        };
        new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id).Save(new ProjectList(entries, entries[0].Id));

        var projects = game.NewProjects(game.NewController());

        Assert.Equal(3, projects.Projects.Count);
        Assert.Equal((ProjectProblem.NotFound, false, "gone"), (projects.Projects[0].Problem, projects.Projects[0].IsUsable, projects.Projects[0].DisplayName));
        Assert.Equal((ProjectProblem.Corrupt, false, "broken"), (projects.Projects[1].Problem, projects.Projects[1].IsUsable, projects.Projects[1].DisplayName));
        Assert.Equal((ProjectProblem.None, true, "Valid"), (projects.Projects[2].Problem, projects.Projects[2].IsUsable, projects.Projects[2].DisplayName));
        Assert.Equal(entries[0].Id, projects.ActiveId);
        Assert.Equal(brokenBytes, File.ReadAllBytes(ProjectFilePath(broken)));
        Assert.Equal(2, game.Log.Warnings.Count);                           // one per project that cannot be loaded
    }

    [Fact] public void A_project_of_another_game_in_the_list_is_not_usable()
    {
        using var game = new TestGame();
        var folder = game.NewFolder("foreign");
        var project = NewProjectIn(folder, "Foreign", gameId: "Other");
        new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id).Save(
            new ProjectList([new ProjectListEntry(project.Id, folder, null)], project.Id));

        var state = game.NewProjects(game.NewController()).Projects.Single();

        Assert.Equal((ProjectProblem.Invalid, false, "foreign"), (state.Problem, state.IsUsable, state.DisplayName));
        Assert.Equal("the project belongs to the game 'Other'", state.Detail);
    }

    [Fact] public void A_project_file_with_another_id_than_its_list_entry_is_not_usable()
    {
        using var game = new TestGame();
        var folder = game.NewFolder("swapped");
        var project = NewProjectIn(folder, "Swapped");
        var listId = Guid.NewGuid();
        new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id).Save(
            new ProjectList([new ProjectListEntry(listId, folder, null)], listId));

        var state = game.NewProjects(game.NewController()).Projects.Single();

        Assert.Equal((ProjectProblem.Invalid, false, "swapped"), (state.Problem, state.IsUsable, state.DisplayName));
        Assert.Equal($"the project file has the id '{project.Id}', the list expects '{listId}'", state.Detail);
    }

    [Fact] public void A_game_id_that_differs_only_in_case_is_the_same_game()
    {
        using var game = new TestGame();
        var folder = game.NewFolder("shouting");
        var project = NewProjectIn(folder, "Shouting", gameId: "TESTGAME");

        var result = game.NewProjects(game.NewController()).AddExisting(folder);

        Assert.Equal((true, (string?)null), (result.Ok, result.Message));
        Assert.Equal(project.Id, game.NewProjects(game.NewController()).ActiveId);
    }

    [Fact] public void Remove_keeps_the_folder_and_SetActive_moves_the_mark()
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        var projects = game.NewProjects(workspace);
        var folderA = game.NewFolder("a"); var folderB = game.NewFolder("b");
        Assert.True(projects.Create(folderA, "A", "a.pack").Ok);
        Assert.True(projects.Create(folderB, "B", "b.pack").Ok);
        var (idA, idB) = (projects.Projects[0].Id, projects.Projects[1].Id);
        Assert.Equal(idA, projects.ActiveId);                               // the first project of a list is the active one
        var changes = 0; projects.Changed += (_, _) => changes++;

        Assert.True(projects.SetActive(idB).Ok);
        Assert.Equal((idB, 1), (projects.ActiveId, changes));
        Assert.Equal(idB, game.NewProjects(workspace).ActiveId);
        var unknown = projects.SetActive(Guid.NewGuid());
        Assert.Equal((false, game.Texts.ProjectUnknown), (unknown.Ok, unknown.Message));

        Assert.True(projects.Remove(idB).Ok);
        Assert.Equal((idA, 1, 2), (projects.ActiveId, projects.Projects.Count, changes));
        Assert.True(File.Exists(ProjectFilePath(folderB)));                 // only the entry is removed, never the folder
        Assert.Single(game.NewProjects(workspace).Projects);
        var gone = projects.Remove(idB);
        Assert.Equal((false, game.Texts.ProjectUnknown), (gone.Ok, gone.Message));
        Assert.Equal(2, changes);
    }

    [Fact] public void A_list_that_must_not_be_saved_is_never_written()
    {
        using var game = new TestGame();
        var store = new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ \"schemaVersion\": 99, \"projects\": [] }");
        var before = File.ReadAllBytes(store.FilePath);
        var projects = game.NewProjects(game.NewController());
        Assert.Single(game.Log.Warnings);                                   // the warning at the start

        var result = projects.Create(game.NewFolder("m"), "Mod", "mod.pack");

        Assert.True(result.Ok);
        Assert.Equal(game.Texts.ProjectListNotSaved, result.Message);      // the user is told, the change stays in memory
        Assert.Single(projects.Projects);
        Assert.Equal(before, File.ReadAllBytes(store.FilePath));
        Assert.Single(game.Log.Warnings);
    }

    [Fact] public void A_corrupt_list_without_a_bad_copy_is_the_only_copy_and_is_never_written()
    {
        using var game = new TestGame();
        var store = new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ not json");
        var before = File.ReadAllBytes(store.FilePath);
        var projects = new ProjectsController(store, new ProjectListLoadResult(ProjectList.Empty, LoadStatus.Corrupt, null),
            game.NewController(), game.Runner, game.Log, game.Texts, TimeProvider.System);
        Assert.Single(game.Log.Warnings);

        var result = projects.Create(game.NewFolder("m"), "Mod", "mod.pack");

        Assert.Equal((true, game.Texts.ProjectListNotSaved), (result.Ok, result.Message));
        Assert.Equal(before, File.ReadAllBytes(store.FilePath));
        Assert.Single(game.Log.Warnings);
    }

    [Fact] public void A_corrupt_list_with_a_bad_copy_is_replaced_and_leaves_a_trace_in_the_log()
    {
        using var game = new TestGame();
        var store = new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ not json");

        var loaded = store.Load();
        Assert.NotNull(loaded.BadCopyPath);
        var projects = new ProjectsController(store, loaded, game.NewController(), game.Runner, game.Log, game.Texts, TimeProvider.System);

        Assert.Contains(loaded.BadCopyPath, Assert.Single(game.Log.Warnings));
        var result = projects.Create(game.NewFolder("m"), "Mod", "mod.pack");
        Assert.Equal((true, (string?)null), (result.Ok, result.Message));
        Assert.Single(game.NewProjects(game.NewController()).Projects);
        Assert.Single(game.Log.Warnings);
    }

    [Fact] public void Reload_warns_once_when_a_usable_project_becomes_unusable()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());
        var folder = game.NewFolder("m");
        Assert.True(projects.Create(folder, "Mod", "mod.pack").Ok);
        Assert.Empty(game.Log.Warnings);

        File.Delete(ProjectFilePath(folder));
        projects.Reload();
        Assert.Contains("Mod", Assert.Single(game.Log.Warnings));
        Assert.Equal(ProjectProblem.NotFound, projects.Projects[0].Problem);

        projects.Reload();                                                  // nothing changed since: nothing new to say
        Assert.Single(game.Log.Warnings);
    }

    [Fact] public void The_game_id_of_a_foreign_project_is_shown_with_control_characters_escaped()
    {
        using var game = new TestGame();
        var folder = game.NewFolder("foreign");
        var project = NewProjectIn(folder, "Foreign", gameId: "x" + (char)7 + "\u001Fy");
        new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id).Save(
            new ProjectList([new ProjectListEntry(project.Id, folder, null)], project.Id));

        var state = game.NewProjects(game.NewController()).Projects.Single();

        Assert.Equal(@"the project belongs to the game 'x\u0007\u001Fy'", state.Detail);
    }

    [Fact] public void A_list_that_cannot_be_written_is_kept_in_memory_with_a_note_and_a_warning()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());
        // The folder "games" of the store cannot be created because a file has its name.
        Directory.CreateDirectory(game.Paths.RoamingRoot);
        File.WriteAllText(Path.Combine(game.Paths.RoamingRoot, "games"), "x");

        var result = projects.Create(game.NewFolder("m"), "Mod", "mod.pack");

        Assert.Equal((true, game.Texts.ProjectListNotSaved), (result.Ok, result.Message));
        Assert.Single(projects.Projects);
        Assert.Single(game.Log.Warnings);
    }

    [Fact] public void Reload_picks_up_a_changed_project_file()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());
        var folder = game.NewFolder("m");
        Assert.True(projects.Create(folder, "Old", "mod.pack").Ok);
        var changes = 0; projects.Changed += (_, _) => changes++;

        projects.Reload();
        Assert.Equal(0, changes);                                           // nothing changed on disk, nothing is announced

        Assert.Equal(ProjectProblem.None, ProjectFile.Save(folder, projects.Active!.Project! with { Name = "New" }).Problem);
        projects.Reload();

        Assert.Equal(("New", 1), (projects.Active!.DisplayName, changes));
    }

    [Fact] public async Task Vanilla_names_follow_the_catalog_and_Changed_comes_only_when_they_change()
    {
        using var game = new TestGame();
        game.AddVanillaPack("data.pack");
        game.AddPack("mod.pack");
        var workspace = game.NewController();
        var projects = game.NewProjects(workspace);
        Assert.Empty(projects.VanillaPackNames);
        var changes = 0; projects.Changed += (_, _) => changes++;

        await workspace.LoadAsync();

        Assert.Equal(["data.pack"], projects.VanillaPackNames);
        Assert.True(projects.VanillaPackNames.Contains("DATA.PACK"));
        Assert.Equal(1, changes);                                           // the scan raised several workspace changes, one of them changed the names
    }

    [Fact] public void CheckName_answers_for_empty_blank_and_broken_names_and_Create_uses_it()
    {
        using var game = new TestGame();
        var projects = game.NewProjects(game.NewController());
        var broken = "bad" + (char)0xD800;

        Assert.Equal(game.Texts.ProjectNameEmpty, projects.CheckName(""));
        Assert.Equal(game.Texts.ProjectNameEmpty, projects.CheckName("  "));
        Assert.Equal(game.Texts.ProjectNameInvalid, projects.CheckName(broken));
        Assert.Null(projects.CheckName("My Mod"));

        var empty = projects.Create(game.NewFolder("a"), " ", "a.pack");
        var invalid = projects.Create(game.NewFolder("b"), broken, "b.pack");

        Assert.Equal((false, game.Texts.ProjectNameEmpty), (empty.Ok, empty.Message));
        Assert.Equal((false, game.Texts.ProjectNameInvalid), (invalid.Ok, invalid.Message));
        Assert.Empty(projects.Projects);
        Assert.False(File.Exists(ProjectFilePath(game.Root.File(Path.Combine("projects", "a")))));
    }

    [Fact] public async Task OperationStateChanged_is_raised_on_every_change_of_the_workspace()
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        var projects = game.NewProjects(workspace);
        var workspaceChanges = 0; workspace.Changed += (_, _) => workspaceChanges++;
        var stateChanges = 0; projects.OperationStateChanged += (_, _) => stateChanges++;
        var listChanges = 0; projects.Changed += (_, _) => listChanges++;

        await workspace.LoadAsync();

        Assert.True(workspaceChanges > 0);
        Assert.Equal(workspaceChanges, stateChanges);          // without comparing states: a load flips CanStartOperation only
        Assert.True(stateChanges >= listChanges);
    }

    [Fact] public void Without_a_game_profile_the_controller_is_empty_and_every_action_fails()
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        var projects = new ProjectsController(null, new ProjectListLoadResult(ProjectList.Empty, LoadStatus.Missing, null), workspace,
            game.Runner, game.Log, game.Texts, TimeProvider.System);

        Assert.Equal((false, 0, (Guid?)null), (projects.IsAvailable, projects.Projects.Count, projects.ActiveId));
        Assert.All(new[]
        {
            projects.Create(game.NewFolder("n"), "Mod", "mod.pack"),
            projects.AddExisting(game.NewFolder("e")),
            projects.Remove(Guid.NewGuid()),
            projects.SetActive(Guid.NewGuid()),
        }, r => Assert.Equal((false, game.Texts.ProjectUnknown), (r.Ok, r.Message)));
        projects.Reload();
    }
}
