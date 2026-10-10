using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class NewTraitViewModelTests
{
    private static readonly AppTexts Texts = new(new TotalWarModTool.Localization.UiText(Path.Combine(Path.GetTempPath(), "no-such-locales-folder")));

    private static readonly IReadOnlyList<WizardChoice> Templates =
        [new("brave", "Brave"), new("wise", "Wise one"), new("plain", null)];

    private static NewTraitViewModel Create(Func<string, string?>? validate = null, IReadOnlyList<WizardChoice>? templates = null) =>
        new(Texts, new NewTraitInput(templates ?? Templates, validate ?? (_ => null)));

    private static string[] Keys(IEnumerable<WizardChoice> choices) => choices.Select(c => c.Key).ToArray();

    [Fact] public void The_key_follows_the_template_until_the_user_types()
    {
        var viewModel = Create();
        Assert.Equal("brave", viewModel.SelectedTemplate!.Key);
        Assert.Equal("brave_copy", viewModel.Key);

        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "wise");
        Assert.Equal("wise_copy", viewModel.Key);

        // The box giving back the value it shows is no typing.
        viewModel.Key = "wise_copy";
        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "plain");
        Assert.Equal("plain_copy", viewModel.Key);

        viewModel.Key = "flo_trait";
        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "brave");
        Assert.Equal("flo_trait", viewModel.Key);
        Assert.Equal(new NewTraitAnswer("brave", "flo_trait"), viewModel.Result);
    }

    [Fact] public void An_existing_trait_key_is_refused()
    {
        var viewModel = Create(validate: key => Keys(Templates).Contains(key) ? "exists" : null);
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.CanAccept);

        viewModel.Key = "wise";

        Assert.Equal("exists", viewModel.Error);
        Assert.False(viewModel.CanAccept);
        Assert.Null(viewModel.Result);

        viewModel.Key = "flo_trait";
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.CanAccept);
    }

    [Fact] public void The_filter_matches_key_and_name_and_a_filter_without_match_blocks_ok()
    {
        var viewModel = Create();

        viewModel.SelectedTemplate = viewModel.FilteredTemplates.Single(t => t.Key == "plain");
        viewModel.Filter = "PLA";
        Assert.Equal(["plain"], Keys(viewModel.FilteredTemplates));
        Assert.Equal("plain", viewModel.SelectedTemplate!.Key);

        viewModel.Filter = "one";
        Assert.Equal(["wise"], Keys(viewModel.FilteredTemplates));
        Assert.Equal("wise", viewModel.SelectedTemplate!.Key);
        Assert.Equal("wise_copy", viewModel.Key);

        viewModel.Filter = "nothing";
        Assert.Empty(viewModel.FilteredTemplates);
        Assert.Null(viewModel.SelectedTemplate);
        Assert.Equal(Texts.ObjectsWizardNoTemplate, viewModel.Error);
        Assert.False(viewModel.CanAccept);
        Assert.Null(viewModel.Result);

        viewModel.Filter = string.Empty;
        Assert.Equal(["brave", "wise", "plain"], Keys(viewModel.FilteredTemplates));
        Assert.True(viewModel.CanAccept);
    }

    [Fact] public void Without_templates_the_dialog_cannot_be_accepted()
    {
        var viewModel = Create(templates: []);

        Assert.Null(viewModel.SelectedTemplate);
        Assert.Equal(Texts.ObjectsWizardNoTemplate, viewModel.Error);
        Assert.False(viewModel.CanAccept);
    }

    [Fact] public void The_window_shows_the_list_and_answers_with_the_choice() => Sta.Run(() =>
    {
        var window = new NewTraitWindow(Create());
        window.Show();
        try
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var templates = (ListBox)window.FindName("TemplateList");
            var label = new WizardLabelConverter();

            Assert.Equal(Texts.ObjectsWizardTraitTitle, window.Title);
            Assert.Equal(3, templates.Items.Count);
            Assert.Equal("Brave — brave", label.Convert(templates.SelectedItem, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("brave_copy", ((TextBox)window.FindName("KeyBox")).Text);

            ((IInvokeProvider)new ButtonAutomationPeer((Button)window.FindName("OkButton")).GetPattern(PatternInterface.Invoke)!).Invoke();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            Assert.False(window.IsVisible);
            Assert.Equal(new NewTraitAnswer("brave", "brave_copy"), window.Result);
        }
        finally
        {
            window.Close();
        }
    });
}
