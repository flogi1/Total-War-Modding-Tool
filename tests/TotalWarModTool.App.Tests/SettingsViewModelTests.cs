using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsViewModelTests
{
    private static readonly UiLanguageOption[] Languages = [new("en", "English"), new("de", "Deutsch")];
    private static string Locales => Path.Combine(RepoRoot.Find(), "locales");

    [Fact] public void Selection_reflects_and_writes_settings()
    {
        var settings = new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark };
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);
        Assert.Equal(("de", ThemeChoice.Dark), (vm.SelectedLanguage.Code, vm.SelectedTheme.Value));
        vm.SelectedLanguage = Languages[0]; vm.SelectedTheme = vm.ThemeOptions.Single(o => o.Value == ThemeChoice.Light);
        Assert.Equal(("en", ThemeChoice.Light), (settings.Language, settings.Theme));
    }

    [Fact] public void Theme_options_are_System_Light_Dark_with_translated_names()
    {
        var ui = new UiText(Locales);
        var vm = new SettingsViewModel(new GlobalSettings(), new AppTexts(ui), Languages);
        Assert.Equal(["Same as Windows", "Light", "Dark"], vm.ThemeOptions.Select(o => o.Name));
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        ui.Load("de");
        Assert.Equal(["Wie Windows", "Hell", "Dunkel"], vm.ThemeOptions.Select(o => o.Name));
        Assert.Contains(nameof(SettingsViewModel.ThemeOptions), changed);
        Assert.Contains(nameof(SettingsViewModel.SelectedTheme), changed);
    }

    [Fact] public void Selection_follows_external_settings_change()
    {
        var settings = new GlobalSettings();
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);
        settings.Language = "de"; settings.Theme = ThemeChoice.Dark;
        Assert.Equal(("de", ThemeChoice.Dark), (vm.SelectedLanguage.Code, vm.SelectedTheme.Value));
    }

    // The getters read the settings anyway; this checks that the bound selection boxes are told to look again.
    [Fact] public void External_settings_change_raises_PropertyChanged_for_the_selection()
    {
        var settings = new GlobalSettings();
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Language = "de";
        Assert.Equal([nameof(SettingsViewModel.SelectedLanguage)], changed);

        changed.Clear();
        settings.Theme = ThemeChoice.Dark;
        Assert.Equal([nameof(SettingsViewModel.SelectedTheme)], changed);

        // Not a setting of this window: no notification.
        changed.Clear();
        settings.MainWindow = new WindowPlacement(1, 2, 300, 400, false);
        Assert.Empty(changed);
    }

    // The language of a hand-edited file may not be in the list (after the start-up repair it normally is). Showing English is only a
    // display fallback: the setting is left alone, so nothing is written behind the user's back.
    [Fact] public void Language_that_is_not_offered_shows_English_and_leaves_the_settings_alone()
    {
        var settings = new GlobalSettings { Language = "xx" };
        var changes = new List<string>(); settings.Changed += (_, property) => changes.Add(property);
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);

        Assert.Equal("en", vm.SelectedLanguage.Code);
        Assert.Equal("xx", settings.Language);
        Assert.Empty(changes);
    }

    // WPF writes null into the bound property when a selection box loses its selection, e.g. when the list of theme options is replaced
    // after a language switch. That must neither throw nor reset the setting.
    [Fact] public void Null_selection_is_ignored()
    {
        var settings = new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark };
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);

        vm.SelectedLanguage = null!;
        vm.SelectedTheme = null!;

        Assert.Equal(("de", ThemeChoice.Dark), (settings.Language, settings.Theme));
    }
}
