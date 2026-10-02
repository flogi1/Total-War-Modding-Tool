using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsWindowTests
{
    private static readonly UiLanguageOption[] Languages = [new("en", "English"), new("de", "Deutsch")];

    private sealed class FakeSystemTheme : ISystemThemeSource { public bool IsDark() => false; }

    private sealed record Setup(GlobalSettings Settings, UiText Ui, SettingsViewModel ViewModel, SettingsWindow Window);

    // The same chain as in the app: window (shown, as in the app), view model, settings, applier, texts. Everything runs on the STA thread of the test.
    private static Setup Create(GlobalSettings settings)
    {
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var texts = new AppTexts(ui);
        var theme = new ThemeManager(new ResourceDictionary(), new FakeSystemTheme());
        theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme, new FakeAppLog());

        var viewModel = new SettingsViewModel(settings, texts, Languages);
        var window = new SettingsWindow(viewModel);
        window.Show();
        Pump(window);
        return new Setup(settings, ui, viewModel, window);
    }

    // Bindings to the DataContext are delivered by the dispatcher at DataBind priority; a window nobody pumps needs this nudge.
    private static void Pump(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // What the box really has selected, not only what its SelectedItem property says: a box can hold a SelectedItem that it has not selected
    // (SelectedIndex -1, nothing shown, the items of the drop-down are not marked), which is what the user would see as an empty box.
    private static void AssertSelected(ComboBox box, int index, object item)
    {
        Assert.Equal(index, box.SelectedIndex);
        Assert.Same(item, box.SelectedItem);
        Assert.Same(item, box.SelectionBoxItem);
    }

    private static T Find<T>(Window window, string name) where T : class => Assert.IsType<T>(window.FindName(name));

    private static IEnumerable<object> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            yield return child;
            if (child is DependencyObject element)
            {
                foreach (var descendant in LogicalDescendants(element))
                {
                    yield return descendant;
                }
            }
        }
    }

    [Fact] public void Shows_the_two_selection_boxes_bound_to_the_view_model_and_no_buttons() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark });
        var window = setup.Window;
        var languageBox = Find<ComboBox>(window, "LanguageBox");
        var themeBox = Find<ComboBox>(window, "ThemeBox");

        Assert.Equal(("Settings", 640d, 420d), (window.Title, window.Width, window.Height));
        Assert.Same(setup.ViewModel.Languages, languageBox.ItemsSource);
        Assert.Same(setup.ViewModel.ThemeOptions, themeBox.ItemsSource);
        Assert.Equal(("Name", "Name"), (languageBox.DisplayMemberPath, themeBox.DisplayMemberPath));
        AssertSelected(languageBox, 1, setup.ViewModel.SelectedLanguage);
        AssertSelected(themeBox, 2, setup.ViewModel.SelectedTheme);
        Assert.Equal("de", ((UiLanguageOption)languageBox.SelectedItem).Code);
        Assert.Equal(ThemeChoice.Dark, ((ThemeOption)themeBox.SelectedItem).Value);

        // Changes apply at once, so there is nothing to confirm. The logical tree has no template parts (the drop-down toggle of a combo box).
        Assert.DoesNotContain(LogicalDescendants(window), element => element is ButtonBase);
    });

    [Fact] public void Choosing_in_the_selection_boxes_writes_the_settings() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings());
        var languageBox = Find<ComboBox>(setup.Window, "LanguageBox");
        var themeBox = Find<ComboBox>(setup.Window, "ThemeBox");

        languageBox.SelectedItem = Languages[1];
        themeBox.SelectedItem = setup.ViewModel.ThemeOptions.Single(option => option.Value == ThemeChoice.Dark);

        Assert.Equal(("de", ThemeChoice.Dark), (setup.Settings.Language, setup.Settings.Theme));
    });

    // The language switch replaces the list of theme options. The theme box must not lose its choice on the way: neither by ending up with
    // nothing selected (it takes over the new list, writes null, and reads the property back at once), nor by selecting the first item on
    // its own (which would silently reset the theme to System).
    [Fact] public void Switching_the_language_translates_the_window_and_keeps_the_theme() => Sta.Run(() =>
    {
        var setup = Create(new GlobalSettings { Theme = ThemeChoice.Dark });
        var languageBox = Find<ComboBox>(setup.Window, "LanguageBox");
        var themeBox = Find<ComboBox>(setup.Window, "ThemeBox");

        languageBox.SelectedItem = Languages[1];
        Pump(setup.Window);

        Assert.Equal("de", setup.Ui.Language);
        Assert.Equal("Einstellungen", setup.Window.Title);
        Assert.Equal(ThemeChoice.Dark, setup.Settings.Theme);
        Assert.Equal(["Wie Windows", "Hell", "Dunkel"], themeBox.Items.Cast<ThemeOption>().Select(option => option.Name));
        AssertSelected(themeBox, 2, setup.ViewModel.SelectedTheme);
        Assert.Equal(("Dunkel", ThemeChoice.Dark), (setup.ViewModel.SelectedTheme.Name, setup.ViewModel.SelectedTheme.Value));
        AssertSelected(languageBox, 1, setup.ViewModel.SelectedLanguage);

        // And back: the list is replaced again, and the choice made in between stays as well.
        themeBox.SelectedItem = setup.ViewModel.ThemeOptions[1];
        languageBox.SelectedItem = Languages[0];
        Pump(setup.Window);

        Assert.Equal(("en", ThemeChoice.Light), (setup.Settings.Language, setup.Settings.Theme));
        Assert.Equal(["Same as Windows", "Light", "Dark"], themeBox.Items.Cast<ThemeOption>().Select(option => option.Name));
        AssertSelected(themeBox, 1, setup.ViewModel.SelectedTheme);
        AssertSelected(languageBox, 0, setup.ViewModel.SelectedLanguage);
    });
}
