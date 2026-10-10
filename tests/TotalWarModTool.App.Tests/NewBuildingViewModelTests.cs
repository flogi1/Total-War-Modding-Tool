using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class NewBuildingViewModelTests
{
    private static readonly AppTexts Texts = new(new TotalWarModTool.Localization.UiText(Path.Combine(Path.GetTempPath(), "no-such-locales-folder")));

    private static readonly BuildingCatalog Catalog = ObjectsTestSchema.NewBuildingCatalog();

    private static NewBuildingViewModel Create(string? preselected = null, Func<string, string?>? validate = null) =>
        new(Texts, new NewBuildingInput(Catalog, preselected, validate ?? (_ => null)));

    private static string[] Keys(IEnumerable<WizardChoice> choices) => choices.Select(c => c.Key).ToArray();

    [Fact] public void The_preselected_subculture_lists_its_templates()
    {
        var preselected = Create("sc3");

        Assert.Equal(["sc1", "sc2", "sc3"], Keys(preselected.Subcultures));
        Assert.Equal("sc3", preselected.SelectedSubculture!.Key);
        Assert.Equal(["o_1"], Keys(preselected.FilteredTemplates));
        Assert.Equal("o_1", preselected.SelectedTemplate!.Key);

        // Without a preselection the first subculture is chosen; another subculture lists its own templates.
        var viewModel = Create();
        Assert.Equal("sc1", viewModel.SelectedSubculture!.Key);
        Assert.Equal(["b_1", "b_2", "solo"], Keys(viewModel.FilteredTemplates));
        Assert.Equal("b_1", viewModel.SelectedTemplate!.Key);

        viewModel.SelectedSubculture = viewModel.Subcultures.Single(s => s.Key == "sc2");
        Assert.Equal(["b_2"], Keys(viewModel.FilteredTemplates));
        Assert.Equal("b_2", viewModel.SelectedTemplate!.Key);

        // The filter matches key and name anywhere, ignoring case; the selection stays while it is listed.
        viewModel.SelectedSubculture = viewModel.Subcultures[0];
        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "solo");
        viewModel.Filter = "SHR";
        Assert.Equal(["solo"], Keys(viewModel.FilteredTemplates));
        viewModel.Filter = "b_";
        Assert.Equal(["b_1", "b_2"], Keys(viewModel.FilteredTemplates));
        Assert.Equal("b_1", viewModel.SelectedTemplate!.Key);
        viewModel.Filter = "BIG";
        Assert.Equal("b_2", viewModel.SelectedTemplate!.Key);

        // A filter that lets nothing through leaves no template, and OK is blocked.
        viewModel.Filter = "nothing";
        Assert.Empty(viewModel.FilteredTemplates);
        Assert.Null(viewModel.SelectedTemplate);
        Assert.Equal(Texts.ObjectsWizardNoTemplate, viewModel.Error);
        Assert.False(viewModel.CanAccept);
        Assert.Null(viewModel.Result);
    }

    [Fact] public void The_key_follows_the_template_until_the_user_types()
    {
        var viewModel = Create();
        Assert.Equal("b_1_copy", viewModel.Key);

        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "b_2");
        Assert.Equal("b_2_copy", viewModel.Key);

        // The box giving back the value it shows is no typing.
        viewModel.Key = "b_2_copy";
        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "solo");
        Assert.Equal("solo_copy", viewModel.Key);

        viewModel.Key = "flo_shrine";
        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "b_1");
        Assert.Equal("flo_shrine", viewModel.Key);
    }

    [Fact] public void The_stage_list_preselects_the_last_stage()
    {
        var viewModel = Create();

        Assert.Equal(["b_1", "b_2"], viewModel.Stages.Select(s => s.LevelKey));
        Assert.Equal("b_2", viewModel.SelectedStage!.LevelKey);
        Assert.True(viewModel.CanChooseExistingChain);
        Assert.True(viewModel.NewChain);
    }

    [Fact] public void A_template_without_chain_allows_only_a_new_chain()
    {
        var viewModel = Create();
        viewModel.NewChain = false;
        Assert.False(viewModel.NewChain);

        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "solo");

        Assert.Empty(viewModel.Stages);
        Assert.Null(viewModel.SelectedStage);
        Assert.False(viewModel.CanChooseExistingChain);
        Assert.True(viewModel.NewChain);
        viewModel.NewChain = false;
        Assert.True(viewModel.NewChain);
        Assert.Equal(new NewBuildingAnswer("solo", "solo_copy", null), viewModel.Result);
    }

    [Fact] public void An_existing_level_key_is_refused()
    {
        var viewModel = Create(validate: key => Catalog.HasLevel(key) ? "exists" : null);
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.CanAccept);

        viewModel.Key = "b_2";

        Assert.Equal("exists", viewModel.Error);
        Assert.False(viewModel.CanAccept);
        Assert.Null(viewModel.Result);

        viewModel.Key = "flo_b";
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.CanAccept);
    }

    [Fact] public void The_result_names_template_key_and_stage()
    {
        var viewModel = Create();
        viewModel.Key = "flo_b";

        Assert.Equal(new NewBuildingAnswer("b_1", "flo_b", null), viewModel.Result);

        viewModel.NewChain = false;
        Assert.Equal(new NewBuildingAnswer("b_1", "flo_b", viewModel.Stages[1]), viewModel.Result);

        viewModel.SelectedStage = viewModel.Stages[0];
        Assert.Equal(new NewBuildingAnswer("b_1", "flo_b", new BuildingStage("b_1", 0, "Barracks")), viewModel.Result);
    }

    [Fact] public void The_window_shows_the_lists_and_answers_with_the_choice() => Sta.Run(() =>
    {
        var window = new NewBuildingWindow(Create());
        window.Show();
        try
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var templates = (ListBox)window.FindName("TemplateList");
            var subcultures = (ComboBox)window.FindName("SubcultureBox");
            var stages = (ComboBox)window.FindName("StageBox");
            var label = new WizardLabelConverter();

            Assert.Equal(Texts.ObjectsWizardBuildingTitle, window.Title);
            Assert.Equal(3, templates.Items.Count);
            Assert.Equal("Barracks — b_1", label.Convert(templates.SelectedItem, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("Empire", label.Convert(subcultures.SelectedItem, typeof(string), WizardLabelConverter.NameOnly, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("1: Big barracks", label.Convert(stages.SelectedItem, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.False(stages.IsEnabled);
            Assert.Equal("b_1_copy", ((TextBox)window.FindName("KeyBox")).Text);

            ((RadioButton)window.FindName("ExistingChainButton")).IsChecked = true;
            window.UpdateLayout();
            Assert.True(stages.IsEnabled);
            ((IInvokeProvider)new ButtonAutomationPeer((Button)window.FindName("OkButton")).GetPattern(PatternInterface.Invoke)!).Invoke();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            Assert.False(window.IsVisible);
            Assert.Equal(new NewBuildingAnswer("b_1", "b_1_copy", new BuildingStage("b_2", 1, "Big barracks")), window.Result);
        }
        finally
        {
            window.Close();
        }
    });
}
