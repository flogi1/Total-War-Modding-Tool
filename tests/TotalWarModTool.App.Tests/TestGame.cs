using TotalWarModTool.GameProfiles;
using TotalWarModTool.Localization;
using TotalWarModTool.Operations;
using TotalWarModTool.Projects;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

/// <summary>A synthetic game in a temp folder: install folder with a data folder, program folder with a schema, empty roaming and local roots.</summary>
internal sealed class TestGame : IDisposable
{
    public static readonly GameProfile Profile =
        new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["release"], null);

    /// <summary>One row of the table "x_tables": header byte, row count 1, value 42.</summary>
    public static readonly byte[] TableBytes = PackBuilder.Hex("01 | 01 00 00 00 | 2A 00 00 00");

    private const string SchemaText = """
        (
            version: 5,
            definitions: {
                "x_tables": [
                    (
                        version: 0,
                        fields: [
                            (name: "value", field_type: I32, is_key: false, is_reference: None),
                        ],
                        localised_fields: [],
                    ),
                ],
            },
            patches: {},
        )
        """;

    /// <param name="withSchema">Writes the shipped schema into the program folder.</param>
    /// <param name="inSteamLibrary">
    /// Places the install folder in a Steam library below <see cref="SteamPath"/> and leaves the install path of the settings empty, so
    /// the game is found through <see cref="FixedSteam"/> and has a workshop folder (see <see cref="AddWorkshopPack"/>).
    /// </param>
    public TestGame(bool withSchema = true, bool inSteamLibrary = false)
    {
        Root = new TempDir();
        SteamPath = Root.File("steam");
        InstallPath = inSteamLibrary
            ? Path.Combine(SteamPath, "steamapps", "common", Profile.InstallFolderName)
            : Root.File("game");
        DataPath = Path.Combine(InstallPath, Profile.DataFolder);
        Directory.CreateDirectory(DataPath);
        Paths = new AppPaths(Root.File("roaming"), Root.File("local"), Root.File("program"));
        GameSettings = new GameSettings { InstallPath = inSteamLibrary ? null : InstallPath };
        Settings = new GlobalSettings();
        Runner = new FakeOperationRunner();
        Log = new FakeAppLog();
        Texts = new AppTexts(new UiText(Root.File("no-locales")));
        if (withSchema)
        {
            Directory.CreateDirectory(Paths.SchemasDirectory);
            File.WriteAllText(Path.Combine(Paths.SchemasDirectory, Profile.SchemaFile), SchemaText);
        }
    }

    public TempDir Root { get; }

    public string InstallPath { get; }

    /// <summary>The Steam folder of the <c>inSteamLibrary</c> variant; an unused path otherwise.</summary>
    public string SteamPath { get; }

    public string DataPath { get; }

    public AppPaths Paths { get; }

    public GameSettings GameSettings { get; }

    public GlobalSettings Settings { get; }

    public FakeOperationRunner Runner { get; }

    public FakeAppLog Log { get; }

    public AppTexts Texts { get; }

    /// <summary>Writes a PFH5 mod pack (type 3) into the data folder and returns its path.</summary>
    public string AddPack(string name, params (string Path, byte[] Bytes)[] entries)
    {
        var builder = new PackBuilder();
        foreach (var (path, bytes) in entries)
        {
            builder.AddEntry(path, bytes);
        }

        return builder.WriteTo(Path.Combine(DataPath, name));
    }

    /// <summary>Writes a PFH5 mod pack into the workshop folder of the game (<c>steamapps\workshop\content\&lt;appId&gt;\&lt;workshopId&gt;</c>) and returns its path.</summary>
    public string AddWorkshopPack(string workshopId, string name, params (string Path, byte[] Bytes)[] entries)
    {
        var folder = Path.Combine(SteamPath, "steamapps", "workshop", "content", Profile.SteamAppId.ToString(), workshopId);
        Directory.CreateDirectory(folder);
        var builder = new PackBuilder();
        foreach (var (path, bytes) in entries)
        {
            builder.AddEntry(path, bytes);
        }

        return builder.WriteTo(Path.Combine(folder, name));
    }

    /// <summary>Writes a PFH5 pack of type release into the data folder. The test profile counts that type as vanilla.</summary>
    public string AddVanillaPack(string name) =>
        new PackBuilder(packType: 1).AddEntry("db/x_tables/data__", TableBytes).WriteTo(Path.Combine(DataPath, name));

    /// <summary>A project controller over the project list store of the test profile below the roaming root; loads the list as the app does at start.</summary>
    public ProjectsController NewProjects(WorkspaceController workspace, IOperationRunner? runner = null)
    {
        var store = new ProjectListStore(Paths.RoamingRoot, Profile.Id);
        return new ProjectsController(store, store.Load(), workspace, runner ?? Runner, Log, Texts, TimeProvider.System);
    }

    /// <summary>
    /// The main view model of the app with projects: the controller of <see cref="NewProjects"/>, the commands over a fake dialog.
    /// <paramref name="withDocuments"/> adds the <see cref="DocumentsController"/> (editable tabs) the way the app wires it.
    /// <paramref name="withFiles"/> adds the file operations over a fake dialog (they need the documents, so it implies them); the trash is below the temp folder.
    /// </summary>
    public MainSetup NewMain(WorkspaceController workspace, IOperationRunner? runner = null, bool withDocuments = false, bool withFiles = false)
    {
        var projects = NewProjects(workspace, runner);
        var dialogs = new FakeProjectDialogs();
        var commands = new ProjectsViewModel(projects, dialogs, Texts);
        var history = new TotalWarModTool.UndoRedo.ActionHistory();
        DocumentsController? documents = withDocuments || withFiles
            ? new DocumentsController(new TabsViewModel(Texts), projects, history, runner ?? Runner, new FakeEditDialogs(), Log, Texts)
            : null;
        if (documents is not null)
        {
            // The two hooks the composition root sets.
            commands.UnsavedReason = documents.UnsavedReason;
            commands.BeforeBuild = documents.ConfirmSaveBeforeBuildAsync;
        }

        ProjectFilesController? filesController = null;
        ProjectFilesViewModel? files = null;
        FakeFileDialogs? fileDialogs = null;
        if (withFiles)
        {
            filesController = new ProjectFilesController(projects, workspace, documents!, history, runner ?? Runner, Root.File("trash"), Log, Texts, TimeProvider.System);
            fileDialogs = new FakeFileDialogs();
            files = new ProjectFilesViewModel(filesController, projects, fileDialogs, Texts);
        }

        var main = new MainViewModel(Texts, Paths, _ => { }, runner ?? Runner, () => { }, workspace, null, projects, commands, documents, files);
        return new MainSetup(main, projects, dialogs, documents, files, fileDialogs);
    }

    /// <summary>Creates a project (it becomes the active one when it is the first) and writes the given files below its pack folder.</summary>
    public ProjectState NewProject(ProjectsController projects, string name, params (string RelativeFile, string Text)[] files)
    {
        var folder = NewFolder(name.ToLowerInvariant().Replace(' ', '_'));
        var result = projects.Create(folder, name, name.ToLowerInvariant().Replace(' ', '_') + ".pack");
        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Message);
        }

        var state = projects.Projects[^1];
        foreach (var (relative, text) in files)
        {
            var path = Path.Combine(state.Folder, "pack", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return state;
    }

    /// <summary>Creates an empty folder below the temp folder and returns its path.</summary>
    public string NewFolder(string name)
    {
        var folder = Root.File(Path.Combine("projects", name));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public WorkspaceController NewController(ISteamLocationSource? steam = null, IOperationRunner? runner = null) =>
        new(Profile, GameSettings, Settings, Paths, steam ?? new NoSteam(), runner ?? Runner, Log, Texts);

    public void Dispose() => Root.Dispose();
}

/// <summary>What <see cref="TestGame.NewMain"/> built.</summary>
internal sealed record MainSetup(MainViewModel Main, ProjectsController Projects, FakeProjectDialogs Dialogs, DocumentsController? Documents = null,
    ProjectFilesViewModel? Files = null, FakeFileDialogs? FileDialogs = null);

/// <summary>A Steam source that finds no Steam installation.</summary>
internal sealed class NoSteam : ISteamLocationSource
{
    public string? GetSteamPath() => null;
}

/// <summary>A Steam source that returns a fixed folder.</summary>
internal sealed class FixedSteam(string path) : ISteamLocationSource
{
    public string? GetSteamPath() => path;
}
