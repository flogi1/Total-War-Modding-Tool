using TotalWarModTool.Editing;
using TotalWarModTool.Objects;
using TotalWarModTool.Workspace;
using static TotalWarModTool.App.Tests.ObjectApplySetup;

namespace TotalWarModTool.App.Tests;

public class ObjectWizardControllerTests
{
    // A new building goes into new files named after the project: there is no project file of any table in these tests.
    private const string LevelsFile = @"db\levels_tables\Flo.tsv";
    private const string VariantsFile = @"db\variants_tables\Flo.tsv";
    private const string ChainsFile = @"db\chains_tables\Flo.tsv";
    private const string UpgradesFile = @"db\upgrades_tables\Flo.tsv";
    private const string TraitsFile = @"db\traits_tables\Flo.tsv";
    private const string TraitLevelsFile = @"db\trait_levels_tables\Flo.tsv";
    private const string EffectsFile = @"db\level_effects_tables\Flo.tsv";

    private sealed record WizardSetup(Setup Setup, ObjectWizardController Wizard) : IDisposable
    {
        public void Dispose() => Setup.Dispose();
    }

    // The game of the building wizard (mini building schema, vanilla pack, loc pack, wizards.json unless told otherwise), the project "Flo"
    // (the active one), the objects, apply and wizard controllers wired like the composition root wires them.
    private static async Task<WizardSetup> CreateAsync(string? wizardsJson = ObjectsTestSchema.WizardsJson)
    {
        var game = new TestGame(withSchema: false);
        ObjectsTestSchema.WriteBuildingSchema(game);
        var vanilla = ObjectsTestSchema.WriteBuildingVanilla(game);
        File.WriteAllText(Path.Combine(game.InstallPath, LoadOrderResolver.UsedModsFileName), string.Empty);
        if (wizardsJson is not null)
        {
            ObjectsTestSchema.WriteWizards(game, wizardsJson);
        }

        var workspace = game.NewController(profile: ObjectsTestSchema.Profile);
        await workspace.LoadAsync();
        var main = game.NewMain(workspace, withDocuments: true);
        var project = game.NewProject(main.Projects, "Flo");
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
        var wizard = new ObjectWizardController(objects, apply, main.Projects, game.Runner, dialogs, game.Log, game.Texts);
        return new WizardSetup(new Setup(game, workspace, main, objects, apply, dialogs, project, vanilla), wizard);
    }

    private static void AssertNothingWritten(Setup setup)
    {
        Assert.False(setup.History.CanUndo);
        Assert.False(Directory.Exists(Path.Combine(setup.Project.Folder, "pack", "db")));
        Assert.DoesNotContain(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
    }

    private static string[] Cells(object?[] row) => row.Select(cell => Convert.ToString(cell, System.Globalization.CultureInfo.InvariantCulture)!).ToArray();

    [Fact] public async Task A_new_building_with_a_new_chain_writes_level_variant_and_chain_in_one_step_and_opens_it()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;
        setup.Dialogs.BuildingAnswer = _ => new NewBuildingAnswer("b_1", "flo_b", null);
        var recorded = 0;
        setup.History.Changed += () => recorded++;
        var changedFor = new List<Guid>();
        setup.Apply.FilesChanged += (_, id) => changedFor.Add(id);

        Assert.Null(wizard.Wizard.NewBuildingDisabledReason);
        Assert.True(wizard.Wizard.NewBuildingCommand.CanExecute(null));
        await wizard.Wizard.NewBuildingAsync();

        Assert.Single(setup.Dialogs.BuildingAsks);
        Assert.Contains(setup.Texts.ObjectsWizardLoading, setup.Runner.Titles);
        Assert.Equal(["flo_b", "flo_b_chain", "0"], Cells(Assert.Single(setup.RowsOf(LevelsFile))));
        Assert.Equal(["flo_b", "", "sc1", ""], Cells(Assert.Single(setup.RowsOf(VariantsFile))));
        Assert.Equal(["flo_b_chain"], Cells(Assert.Single(setup.RowsOf(ChainsFile))));
        Assert.False(File.Exists(setup.ProjectFile(UpgradesFile)));
        var step = Assert.IsType<FileContentsStep>(setup.History.NextUndo);
        Assert.Equal(1, recorded);
        Assert.Contains(LevelsFile, step.Files);
        Assert.Contains(ChainsFile, step.Files);
        Assert.Equal([setup.Project.Id], changedFor);

        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal(new ObjectRef("levels_tables", ["flo_b"], null), tab.Current);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(SourceKind.Project, tab.Result!.View.Root!.Provenance.Source.Kind);
        Assert.NotNull(tab.ResultLine);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("flo_b", StringComparison.Ordinal) && entry.Contains("b_1", StringComparison.Ordinal));
    }

    [Fact] public async Task A_new_building_as_a_stage_writes_one_upgrade_row_from_the_chosen_stage()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;
        setup.Dialogs.BuildingAnswer = input => new NewBuildingAnswer("b_2", "flo_b", input.Catalog.StagesOf("b_chain")[0]);

        await wizard.Wizard.NewBuildingAsync();

        Assert.Equal(["flo_b", "b_chain", "1"], Cells(Assert.Single(setup.RowsOf(LevelsFile))));
        Assert.Equal(["b_1", "flo_b"], Cells(Assert.Single(setup.RowsOf(UpgradesFile))));
        Assert.Equal(["flo_b", "c1", "", ""], Cells(Assert.Single(setup.RowsOf(VariantsFile))));
        Assert.False(File.Exists(setup.ProjectFile(ChainsFile)));
        Assert.IsType<FileContentsStep>(setup.History.NextUndo);
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task The_name_of_the_new_building_ends_with_the_suffix()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;
        setup.Dialogs.BuildingAnswer = _ => new NewBuildingAnswer("b_1", "flo_b", null);

        await wizard.Wizard.NewBuildingAsync();

        var locFile = Assert.Single(Directory.GetFiles(Path.Combine(setup.Project.Folder, "pack"), "*.loc.tsv", SearchOption.AllDirectories));
        Assert.Contains("variants_name_flo_bsc1\tBarracks (copy)\t", File.ReadAllText(locFile));
        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Contains("Barracks (copy)", tab.Text);
    }

    [Fact] public async Task Undo_of_a_new_building_removes_the_level_and_the_chain()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;
        setup.Dialogs.BuildingAnswer = _ => new NewBuildingAnswer("b_1", "flo_b", null);
        await wizard.Wizard.NewBuildingAsync();
        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal(1, setup.Builds("flo_b"));
        await setup.Main.Main.OpenProjectFileAsync(setup.Project.Id, LevelsFile);
        Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.SelectedTab);

        setup.Main.Documents!.UndoCommand.Execute(null);

        Assert.False(File.Exists(setup.ProjectFile(LevelsFile)));
        Assert.False(File.Exists(setup.ProjectFile(VariantsFile)));
        Assert.False(File.Exists(setup.ProjectFile(ChainsFile)));
        Assert.False(setup.History.CanUndo);
        Assert.Same(tab, setup.Tabs.SelectedTab);
        Assert.Equal(2, setup.Builds("flo_b"));
        Assert.Null(tab.Result!.View.Root);
    }

    [Fact] public async Task Cancelling_the_dialog_writes_nothing()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;

        await wizard.Wizard.NewBuildingAsync();

        Assert.Single(setup.Dialogs.BuildingAsks);
        Assert.DoesNotContain(setup.Texts.ObjectsCopyAsChecking, setup.Runner.Titles);
        AssertNothingWritten(setup);
        Assert.Empty(setup.Main.Main.Tabs.Tabs.OfType<ObjectTabViewModel>());
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact] public async Task The_wizard_is_disabled_without_a_project_and_without_a_wizard_file()
    {
        using (var wizard = await CreateAsync())
        {
            var setup = wizard.Setup;
            var changed = new List<string?>();
            wizard.Wizard.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            var canExecuteChanged = 0;
            wizard.Wizard.NewBuildingCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
            Assert.Null(wizard.Wizard.NewBuildingDisabledReason);

            Assert.True(setup.Main.Projects.Remove(setup.Project.Id).Ok);

            Assert.Equal(setup.Texts.ObjectsApplyNoProject, wizard.Wizard.NewBuildingDisabledReason);
            Assert.False(wizard.Wizard.NewBuildingCommand.CanExecute(null));
            Assert.Contains(nameof(ObjectWizardController.NewBuildingDisabledReason), changed);
            Assert.True(canExecuteChanged > 0);
            await wizard.Wizard.NewBuildingAsync();
            Assert.Empty(setup.Dialogs.BuildingAsks);
            Assert.DoesNotContain(setup.Texts.ObjectsWizardLoading, setup.Runner.Titles);
        }

        using (var wizard = await CreateAsync(wizardsJson: null))
        {
            Assert.Equal(wizard.Setup.Texts.ObjectsWizardMissing, wizard.Wizard.NewBuildingDisabledReason);
            Assert.False(wizard.Wizard.NewBuildingCommand.CanExecute(null));
            await wizard.Wizard.NewBuildingAsync();
            Assert.Empty(wizard.Setup.Dialogs.BuildingAsks);
        }
    }

    [Fact] public async Task A_wizard_with_names_the_schema_does_not_know_is_refused()
    {
        using var wizard = await CreateAsync(ObjectsTestSchema.WizardsJson.Replace("\"levelField\": \"level\"", "\"levelField\": \"stage\"", StringComparison.Ordinal));
        var setup = wizard.Setup;
        var expected = setup.Texts.ObjectsWizardSchemaMismatch("levels_tables.stage");

        // The schema is checked once the stores exist; the first try makes them and says why it stops.
        await wizard.Wizard.NewBuildingAsync();

        Assert.Empty(setup.Dialogs.BuildingAsks);
        Assert.Equal(expected, Assert.Single(setup.Dialogs.Messages).Message);
        Assert.Equal(expected, wizard.Wizard.NewBuildingDisabledReason);
        Assert.False(wizard.Wizard.NewBuildingCommand.CanExecute(null));
        AssertNothingWritten(setup);
    }

    [Fact] public async Task The_shown_building_preselects_its_subculture()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;

        await wizard.Wizard.NewBuildingAsync();
        Assert.Null(setup.Dialogs.BuildingAsks[^1].PreselectedSubculture);

        // o_1 has a variant of the culture c2 only: its first subculture is sc3.
        var tab = setup.Objects.OpenObject(new ObjectRef("levels_tables", ["o_1"], null));
        Assert.Equal(ObjectTabState.Ready, tab.State);
        await wizard.Wizard.NewBuildingAsync();

        Assert.Equal("sc3", setup.Dialogs.BuildingAsks[^1].PreselectedSubculture);
        Assert.Equal(["sc1", "sc2", "sc3"], setup.Dialogs.BuildingAsks[^1].Catalog.Subcultures.Select(s => s.Key));
    }

    [Fact] public async Task A_key_whose_chain_key_exists_is_refused()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;

        await wizard.Wizard.NewBuildingAsync();

        var validate = Assert.Single(setup.Dialogs.BuildingAsks).ValidateKey;
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyEmpty, validate(string.Empty));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyWhitespace, validate("a b"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyWhitespace, validate("a\tb"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyExists("levels"), validate("b_1"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyExists("chains"), validate("taken"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyExists("chains"), validate("b"));
        Assert.Null(validate("flo_b"));
        Assert.Null(validate("b_1_copy"));
    }

    // ---- the trait wizard ----

    // The wizard file without its "trait" section (parsed, so the line endings of the source do not matter).
    private static string WithoutTraitSection(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("trait"));
        return node.ToJsonString();
    }

    [Fact] public async Task A_new_trait_writes_trait_levels_and_effects_in_one_step_and_opens_it()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;
        setup.Dialogs.TraitAnswer = _ => new NewTraitAnswer("t1", "flo_t");
        var recorded = 0;
        setup.History.Changed += () => recorded++;
        var changedFor = new List<Guid>();
        setup.Apply.FilesChanged += (_, id) => changedFor.Add(id);

        Assert.Null(wizard.Wizard.NewTraitDisabledReason);
        Assert.True(wizard.Wizard.NewTraitCommand.CanExecute(null));
        await wizard.Wizard.NewTraitAsync();

        Assert.Single(setup.Dialogs.TraitAsks);
        Assert.Contains(setup.Texts.ObjectsWizardLoading, setup.Runner.Titles);
        Assert.Equal(["flo_t"], Cells(Assert.Single(setup.RowsOf(TraitsFile))));
        Assert.Equal([["flo_t_1", "flo_t", "1"], ["flo_t_2", "flo_t", "2"]], setup.RowsOf(TraitLevelsFile).Select(Cells));
        Assert.Equal([["flo_t_1", "e_a"], ["flo_t_1", "e_b"], ["flo_t_2", "e_a"]], setup.RowsOf(EffectsFile).Select(Cells));
        var step = Assert.IsType<FileContentsStep>(setup.History.NextUndo);
        Assert.Equal(1, recorded);
        Assert.Contains(TraitsFile, step.Files);
        Assert.Contains(TraitLevelsFile, step.Files);
        Assert.Contains(EffectsFile, step.Files);
        Assert.Equal([setup.Project.Id], changedFor);

        var locFile = Assert.Single(Directory.GetFiles(Path.Combine(setup.Project.Folder, "pack"), "*.loc.tsv", SearchOption.AllDirectories));
        var loc = File.ReadAllText(locFile);
        Assert.Contains("trait_levels_onscreen_name_flo_t_1\tBrave (copy)\t", loc);
        Assert.Contains("trait_levels_onscreen_name_flo_t_2\tBraver (copy)\t", loc);

        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal(new ObjectRef("traits_tables", ["flo_t"], null), tab.Current);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(SourceKind.Project, tab.Result!.View.Root!.Provenance.Source.Kind);
        Assert.NotNull(tab.ResultLine);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("New trait", StringComparison.Ordinal) && entry.Contains("flo_t", StringComparison.Ordinal)
            && entry.Contains("t1", StringComparison.Ordinal));
    }

    [Fact] public async Task Undo_of_a_new_trait_removes_the_trait_its_levels_and_effects()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;
        setup.Dialogs.TraitAnswer = _ => new NewTraitAnswer("t1", "flo_t");
        await wizard.Wizard.NewTraitAsync();
        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        await setup.Main.Main.OpenProjectFileAsync(setup.Project.Id, TraitsFile);
        Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.SelectedTab);

        setup.Main.Documents!.UndoCommand.Execute(null);

        Assert.False(File.Exists(setup.ProjectFile(TraitsFile)));
        Assert.False(File.Exists(setup.ProjectFile(TraitLevelsFile)));
        Assert.False(File.Exists(setup.ProjectFile(EffectsFile)));
        Assert.False(setup.History.CanUndo);
        Assert.Same(tab, setup.Tabs.SelectedTab);
        Assert.Null(tab.Result!.View.Root);
    }

    [Fact] public async Task The_trait_dialog_lists_every_trait_with_the_name_of_its_first_level_and_refuses_taken_keys()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;

        await wizard.Wizard.NewTraitAsync();

        var input = Assert.Single(setup.Dialogs.TraitAsks);
        Assert.Equal([("t1", "Brave"), ("t2", null)], input.Templates.Select(t => (t.Key, t.Name)));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyEmpty, input.ValidateKey(string.Empty));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyWhitespace, input.ValidateKey("a b"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyExists("traits"), input.ValidateKey("t1"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyExists("traits"), input.ValidateKey("t2"));
        Assert.Null(input.ValidateKey("flo_t"));
        Assert.Null(input.ValidateKey("t1_copy"));
    }

    [Fact] public async Task Cancelling_the_trait_dialog_writes_nothing()
    {
        using var wizard = await CreateAsync();
        var setup = wizard.Setup;

        await wizard.Wizard.NewTraitAsync();

        Assert.Single(setup.Dialogs.TraitAsks);
        Assert.DoesNotContain(setup.Texts.ObjectsCopyAsChecking, setup.Runner.Titles);
        AssertNothingWritten(setup);
        Assert.Empty(setup.Main.Main.Tabs.Tabs.OfType<ObjectTabViewModel>());
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("new trait wizard was cancelled", StringComparison.Ordinal));
    }

    [Fact] public async Task The_trait_wizard_is_disabled_without_its_section_and_the_building_wizard_stays_usable()
    {
        using var wizard = await CreateAsync(WithoutTraitSection(ObjectsTestSchema.WizardsJson));
        var setup = wizard.Setup;

        Assert.Equal(setup.Texts.ObjectsWizardMissing, wizard.Wizard.NewTraitDisabledReason);
        Assert.False(wizard.Wizard.NewTraitCommand.CanExecute(null));
        Assert.Null(wizard.Wizard.NewBuildingDisabledReason);
        await wizard.Wizard.NewTraitAsync();

        Assert.Empty(setup.Dialogs.TraitAsks);
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("new trait wizard was refused: the game profile has no trait wizard", StringComparison.Ordinal));
    }

    [Fact] public async Task A_trait_wizard_with_names_the_schema_does_not_know_is_refused_and_logged_in_english()
    {
        using var wizard = await CreateAsync(
            ObjectsTestSchema.WizardsJson.Replace("\"traitTable\": \"traits_tables\"", "\"traitTable\": \"no_traits_tables\"", StringComparison.Ordinal));
        var setup = wizard.Setup;
        var expected = setup.Texts.ObjectsWizardSchemaMismatch("no_traits_tables");

        await wizard.Wizard.NewTraitAsync();

        Assert.Empty(setup.Dialogs.TraitAsks);
        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(expected, message.Message);
        Assert.Equal(setup.Texts.ObjectsWizardTraitTitle, message.Title);
        Assert.Equal(expected, wizard.Wizard.NewTraitDisabledReason);
        Assert.Null(wizard.Wizard.NewBuildingDisabledReason);
        Assert.Contains(setup.Game.Log.Warnings, entry => entry.Contains("the schema does not know no_traits_tables", StringComparison.Ordinal));
        Assert.DoesNotContain(setup.Game.Log.Warnings.Concat(setup.Game.Log.Infos), entry => entry.Contains(expected, StringComparison.Ordinal));
        AssertNothingWritten(setup);
    }
}
