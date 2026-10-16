using TotalWarModTool.Objects;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

/// <summary>The game, the project and the apply controller of the apply and copy tests, wired like the composition root wires them.</summary>
internal static class ObjectApplySetup
{
    internal const string XEntry = "db/x_tables/data__";

    /// <summary>The seed of the random integer keys of a copy in the tests.</summary>
    internal const int KeySeed = 4711;

    internal sealed record Setup(TestGame Game, WorkspaceController Workspace, MainSetup Main, ObjectsController Objects, ObjectApplyController Apply,
        FakeObjectDialogs Dialogs, ProjectState Project, string VanillaPath) : IDisposable
    {
        public TabsViewModel Tabs => Main.Main.Tabs;

        public ActionHistory History => Main.History!;

        public FakeOperationRunner Runner => Game.Runner;

        public AppTexts Texts => Game.Texts;

        public string ProjectFile(string relative) => Path.Combine(Project.Folder, "pack", relative);

        public void Dispose() => Game.Dispose();

        /// <summary>How many builds of the object with this key went through the runner.</summary>
        public int Builds(string key) => Runner.Titles.Count(title => title == Texts.ObjectsBuilding(key));

        /// <summary>The rows of a project table file.</summary>
        public IReadOnlyList<object?[]> RowsOf(string relative) =>
            TableTsv.Read(File.ReadAllText(ProjectFile(relative)), Workspace.Schema!.Schema!).Table!.Rows;
    }

    // The game of ObjectsControllerTests plus a project (the active one) and the apply controller wired like the composition root wires it.
    // projectFiles are written below the pack folder of the project before anything is built.
    internal static async Task<Setup> CreateAsync(params (string RelativeFile, string Table, object?[][] Rows)[] projectFiles)
    {
        var game = new TestGame(withSchema: false);
        ObjectsTestSchema.WriteSchema(game);
        var vanilla = ObjectsTestSchema.WriteVanillaPack(game, "data.pack", ObjectsTestSchema.XRows, ObjectsTestSchema.YRows);
        File.WriteAllText(Path.Combine(game.InstallPath, LoadOrderResolver.UsedModsFileName), string.Empty);
        ObjectsTestSchema.WriteLocPack(game);
        var workspace = game.NewController(profile: ObjectsTestSchema.Profile);
        await workspace.LoadAsync();
        var main = game.NewMain(workspace, withDocuments: true);
        var project = game.NewProject(main.Projects, "Flo");
        foreach (var (relative, table, rows) in projectFiles)
        {
            var definition = table == "x_tables" ? ObjectsTestSchema.X : ObjectsTestSchema.Y;
            var path = Path.Combine(project.Folder, "pack", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, TableTsv.Write(table, 0, relative.Replace('\\', '/')[..^".tsv".Length], definition, rows).Text);
        }

        var objects = new ObjectsController(workspace, main.Projects, main.Documents, main.Main.Tabs, game.Runner, game.Paths, game.Log, game.Texts,
            key => key, main.Main.OpenEntryAsync, main.Main.OpenProjectFileAsync);
        main.Main.Tabs.ObjectOpener = objects;
        var dialogs = new FakeObjectDialogs();
        var apply = new ObjectApplyController(objects, main.Projects, main.Documents!, main.History!, game.Runner, dialogs, game.Root.File("trash"),
            game.Log, game.Texts, TimeProvider.System);
        objects.ApplyHandler = apply.ApplyAsync;
        objects.LeaveHandler = apply.LeaveAsync;
        objects.CopyHandler = apply.CopyAsync;
        objects.KeyRandom = new Random(KeySeed);
        return new Setup(game, workspace, main, objects, apply, dialogs, project, vanilla);
    }

    // Opens x_tables of the vanilla pack and shows k1 as an object; the tab is built and selected.
    internal static async Task<ObjectTabViewModel> ShowK1Async(Setup setup)
    {
        var pack = setup.Workspace.Catalog!.Packs.Single(p => string.Equals(p.Path, setup.VanillaPath, StringComparison.OrdinalIgnoreCase));
        await setup.Main.Main.OpenEntryAsync(pack, XEntry);
        var table = Assert.IsType<TableTabViewModel>(setup.Tabs.SelectedTab);
        var index = Enumerable.Range(0, 100).First(i => table.KeyOfRow(i) is { } rowKey && rowKey.KeyValues.SequenceEqual(new[] { "k1" }));
        setup.Objects.Show(table, table.KeyOfRow(index)!);
        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        return tab;
    }

    // Replaces text in the editor text and proves that it matched.
    internal static void Edit(ObjectTabViewModel tab, string from, string to)
    {
        Assert.Contains(from, tab.Text);
        var before = tab.Text;
        tab.Text = before.Replace(from, to);
        Assert.NotEqual(before, tab.Text);
    }

    // Runs the apply the way the button does (through the handler the controller put on the tab) and proves that it could run.
    internal static async Task ApplyAsync(ObjectTabViewModel tab)
    {
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ApplyDisabledReason);
        await tab.ApplyHandler!(tab);
    }
}
