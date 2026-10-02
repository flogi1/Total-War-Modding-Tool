using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>One entry of the theme selection: the setting it stands for and its name in the current UI language.</summary>
public sealed record ThemeOption(ThemeChoice Value, string Name);

/// <summary>
/// View model of the settings window. It keeps no state of its own: the selections are read from and written to
/// <see cref="GlobalSettings"/> (never from the controls), and a change of the settings from outside is passed on to the bound boxes.
/// Applying a change is the job of <see cref="SettingsApplier"/>. One instance serves every settings window of a run, because it
/// subscribes to the settings and the texts for good and is never detached.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly GlobalSettings _settings;
    private readonly UiLanguageOption _fallbackLanguage;
    private IReadOnlyList<ThemeOption> _themeOptions;
    private bool _swappingThemeOptions;

    /// <param name="languages">
    /// The languages to offer, from <see cref="LanguageCatalog.Find"/>. The language of the settings is free text from a file that can be
    /// edited by hand, so the choice is never built from it.
    /// </param>
    public SettingsViewModel(GlobalSettings settings, AppTexts texts, IReadOnlyList<UiLanguageOption> languages)
    {
        if (languages.Count == 0)
        {
            throw new ArgumentException("At least one language has to be offered.", nameof(languages));
        }

        _settings = settings;
        Texts = texts;
        Languages = languages;
        _fallbackLanguage = languages.FirstOrDefault(option => option.Code == UiText.ReferenceLanguage) ?? languages[0];
        _themeOptions = BuildThemeOptions();

        // The names of the theme options are texts: when the language changes, they are built again.
        texts.PropertyChanged += (_, _) => RebuildThemeOptions();
        settings.Changed += OnSettingsChanged;
    }

    public AppTexts Texts { get; }

    public IReadOnlyList<UiLanguageOption> Languages { get; }

    /// <summary>
    /// The offered language of <see cref="GlobalSettings.Language"/>. If the settings hold a code that is not offered, this is English;
    /// the setting itself is left as it is.
    /// </summary>
    public UiLanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(option => option.Code == _settings.Language) ?? _fallbackLanguage;
        set
        {
            // A bound selection box writes null when it loses its selection; that is not a choice of the user.
            if (value is not null)
            {
                _settings.Language = value.Code;
            }
        }
    }

    /// <summary>System, Light, Dark. A new list (with the names in the new language) replaces the old one whenever the language changes.</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions => _themeOptions;

    /// <summary>
    /// The option of <see cref="GlobalSettings.Theme"/> in the current <see cref="ThemeOptions"/>. Empty (null) only for the moment in which
    /// the options are swapped, see <see cref="RebuildThemeOptions"/>.
    /// </summary>
    public ThemeOption SelectedTheme
    {
        get => _swappingThemeOptions ? null! : _themeOptions.FirstOrDefault(option => option.Value == _settings.Theme) ?? _themeOptions[0];
        set
        {
            // Null arrives when the theme box loses its selection because ThemeOptions was replaced (see RebuildThemeOptions).
            if (value is not null)
            {
                _settings.Theme = value.Value;
            }
        }
    }

    private IReadOnlyList<ThemeOption> BuildThemeOptions() =>
    [
        new(ThemeChoice.System, Texts.SettingsThemeSystem),
        new(ThemeChoice.Light, Texts.SettingsThemeLight),
        new(ThemeChoice.Dark, Texts.SettingsThemeDark),
    ];

    private void RebuildThemeOptions()
    {
        _themeOptions = BuildThemeOptions();

        // A bound ComboBox takes over a new list in three steps: it drops its selection, writes null into SelectedTheme and reads the
        // property back at once. If it is given an option in that moment, it keeps it as SelectedItem without selecting it (SelectedIndex -1,
        // an empty box). So SelectedTheme is empty while the list is swapped, and the box gets the real option right afterwards.
        _swappingThemeOptions = true;
        try
        {
            OnPropertyChanged(nameof(ThemeOptions));
        }
        finally
        {
            _swappingThemeOptions = false;
        }

        OnPropertyChanged(nameof(SelectedTheme));
    }

    private void OnSettingsChanged(object? sender, string property)
    {
        switch (property)
        {
            case nameof(GlobalSettings.Language):
                OnPropertyChanged(nameof(SelectedLanguage));
                break;
            case nameof(GlobalSettings.Theme):
                OnPropertyChanged(nameof(SelectedTheme));
                break;
        }
    }
}
