using System.IO;
using System.Windows.Input;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Localization;
using TotalWarModTool.Schema;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>One entry of the theme selection: the setting it stands for and its name in the current UI language.</summary>
public sealed record ThemeOption(ThemeChoice Value, string Name);

/// <summary>
/// View model of the settings window. It keeps no state of its own: the selections are read from and written to
/// <see cref="GlobalSettings"/> (never from the controls), and a change of the settings from outside is passed on to the bound boxes.
/// Applying a change is the job of <see cref="SettingsApplier"/>. One instance serves every settings window of a run, because it
/// subscribes to the settings and the texts for good and is never detached.
/// The only state besides the settings is what is being typed into the two path boxes and the error of the last rejected input; both are
/// replaced by the value of the setting as soon as it changes.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly GlobalSettings _settings;
    private readonly GameSettings _gameSettings;
    private readonly WorkspaceController _workspace;
    private readonly Func<string?, string?> _pickFolder;
    private readonly UiLanguageOption _fallbackLanguage;
    private IReadOnlyList<ThemeOption> _themeOptions;
    private bool _swappingThemeOptions;
    private int _selectedCategory;
    private string _gamePath;
    private bool _gamePathInvalid;
    private string _schemaFolder;
    private bool _schemaFolderMissing;

    /// <param name="languages">
    /// The languages to offer, from <see cref="LanguageCatalog.Find"/>. The language of the settings is free text from a file that can be
    /// edited by hand, so the choice is never built from it.
    /// </param>
    /// <param name="pickFolder">Shows a folder dialog starting at the given folder and returns the choice, or null when cancelled.</param>
    public SettingsViewModel(GlobalSettings settings, AppTexts texts, IReadOnlyList<UiLanguageOption> languages,
        GameSettings gameSettings, WorkspaceController workspace, Func<string?, string?> pickFolder)
    {
        ArgumentNullException.ThrowIfNull(gameSettings);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(pickFolder);

        if (languages.Count == 0)
        {
            throw new ArgumentException("At least one language has to be offered.", nameof(languages));
        }

        _settings = settings;
        _gameSettings = gameSettings;
        _workspace = workspace;
        _pickFolder = pickFolder;
        _gamePath = gameSettings.InstallPath ?? string.Empty;
        _schemaFolder = settings.SchemaFolder ?? string.Empty;
        BrowseGamePathCommand = new RelayCommand(BrowseGamePath, () => HasGame);
        ClearGamePathCommand = new RelayCommand(ClearGamePath, () => HasGame);
        BrowseSchemaFolderCommand = new RelayCommand(BrowseSchemaFolder);
        ClearSchemaFolderCommand = new RelayCommand(ClearSchemaFolder);
        Texts = texts;
        Languages = languages;
        _fallbackLanguage = languages.FirstOrDefault(option => option.Code == UiText.ReferenceLanguage) ?? languages[0];
        _themeOptions = BuildThemeOptions();

        // The names of the theme options are texts: when the language changes, they are built again. So are the messages of this window.
        texts.PropertyChanged += (_, _) =>
        {
            RebuildThemeOptions();
            RaiseGameTexts();
        };
        settings.Changed += OnSettingsChanged;
        gameSettings.Changed += OnGameSettingsChanged;
        workspace.Changed += (_, _) => RaiseGameTexts();
    }

    public AppTexts Texts { get; }

    public IReadOnlyList<UiLanguageOption> Languages { get; }

    /// <summary>The chosen page of the window: 0 General, 1 Game.</summary>
    public int SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            // A list box writes -1 when it loses its selection; that is not a choice of the user.
            if (value >= 0)
            {
                SetProperty(ref _selectedCategory, value);
            }
        }
    }

    /// <summary>False without a game profile: there is nothing to set for a game, and the game controls are disabled.</summary>
    public bool HasGame => _workspace.Profile is not null;

    /// <summary>Display name of the game profile (data, not a UI text); empty without one.</summary>
    public string GameName => _workspace.Profile?.DisplayName ?? string.Empty;

    /// <summary>
    /// The text of the game folder box: what is being typed, until the input is committed or the setting changes. Setting it commits:
    /// blank means automatic detection, a folder without the data folder of the game is rejected (see <see cref="GamePathError"/>) and
    /// the setting stays as it was.
    /// </summary>
    public string GamePath
    {
        get => _gamePath;
        set => CommitGamePath(value);
    }

    /// <summary>Why the last input of <see cref="GamePath"/> was rejected; null when it was not.</summary>
    public string? GamePathError =>
        _gamePathInvalid && _workspace.Profile is { } profile ? Texts.SettingsGamePathInvalid(profile.DataFolder) : null;

    /// <summary>What the automatic detection found, or that it found nothing; empty while a manual folder is set.</summary>
    public string GamePathInfo
    {
        get
        {
            if (!HasGame || _gameSettings.InstallPath is not null)
            {
                return string.Empty;
            }

            return _workspace.Location is { } location
                ? Texts.SettingsGamePathDetected(location.InstallPath)
                : Texts.SettingsGamePathNotDetected;
        }
    }

    public ICommand BrowseGamePathCommand { get; }

    public ICommand ClearGamePathCommand { get; }

    /// <summary>The text of the schema folder box. Setting it commits: blank means the shipped schema, a folder that does not exist is rejected.</summary>
    public string SchemaFolder
    {
        get => _schemaFolder;
        set => CommitSchemaFolder(value);
    }

    /// <summary>Why the last input of <see cref="SchemaFolder"/> was rejected; null when it was not.</summary>
    public string? SchemaFolderError => _schemaFolderMissing ? Texts.SettingsSchemaFolderMissing : null;

    /// <summary>The schema in use with its date, and why the folder of the settings is not used when it has no usable file.</summary>
    public string SchemaStatus
    {
        get
        {
            var schema = _workspace.Schema;
            var source = Texts.SchemaSourceText(schema);
            return schema?.OverrideAttempt is { Problem: not SchemaProblem.None } failed && _workspace.Profile is { } profile
                ? Texts.SettingsSchemaStatusOverrideFailed(source, profile.SchemaFile, Texts.SchemaProblemText(failed.Problem))
                : Texts.SettingsSchemaStatus(source);
        }
    }

    public ICommand BrowseSchemaFolderCommand { get; }

    public ICommand ClearSchemaFolderCommand { get; }

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

    // The commit of the game folder. Without a profile there is nothing to check the folder against, so nothing is committed.
    private void CommitGamePath(string? input)
    {
        if (_workspace.Profile is not { } profile)
        {
            return;
        }

        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            _gamePathInvalid = false;
            _gameSettings.InstallPath = null;
        }
        else if (GameLocator.FromInstallPath(profile, text) is null)
        {
            // The box keeps what was typed, the setting stays: the user can see and correct the input.
            _gamePath = text;
            _gamePathInvalid = true;
            OnPropertyChanged(nameof(GamePath));
            OnPropertyChanged(nameof(GamePathError));
            return;
        }
        else
        {
            _gamePathInvalid = false;
            _gameSettings.InstallPath = text;
        }

        ShowGameSetting();
    }

    private void BrowseGamePath()
    {
        if (_pickFolder(_gameSettings.InstallPath ?? _workspace.Location?.InstallPath) is { } folder)
        {
            CommitGamePath(folder);
        }
    }

    private void ClearGamePath()
    {
        _gamePathInvalid = false;
        _gameSettings.InstallPath = null;
        ShowGameSetting();
    }

    // The box shows the value of the setting (empty without a manual folder), and the input that was rejected is forgotten.
    private void ShowGameSetting()
    {
        _gamePath = _gameSettings.InstallPath ?? string.Empty;
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(GamePathError));
        OnPropertyChanged(nameof(GamePathInfo));
    }

    private void CommitSchemaFolder(string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length > 0 && !Directory.Exists(text))
        {
            _schemaFolder = text;
            _schemaFolderMissing = true;
            OnPropertyChanged(nameof(SchemaFolder));
            OnPropertyChanged(nameof(SchemaFolderError));
            return;
        }

        _schemaFolderMissing = false;
        _settings.SchemaFolder = text.Length == 0 ? null : text;
        ShowSchemaFolderSetting();
    }

    private void BrowseSchemaFolder()
    {
        if (_pickFolder(_settings.SchemaFolder) is { } folder)
        {
            CommitSchemaFolder(folder);
        }
    }

    private void ClearSchemaFolder()
    {
        _schemaFolderMissing = false;
        _settings.SchemaFolder = null;
        ShowSchemaFolderSetting();
    }

    private void ShowSchemaFolderSetting()
    {
        _schemaFolder = _settings.SchemaFolder ?? string.Empty;
        OnPropertyChanged(nameof(SchemaFolder));
        OnPropertyChanged(nameof(SchemaFolderError));
        OnPropertyChanged(nameof(SchemaStatus));
    }

    // What depends on the language or on the state of the workspace (detected folder, schema in use).
    private void RaiseGameTexts()
    {
        OnPropertyChanged(nameof(GamePathError));
        OnPropertyChanged(nameof(GamePathInfo));
        OnPropertyChanged(nameof(SchemaFolderError));
        OnPropertyChanged(nameof(SchemaStatus));
    }

    private void OnGameSettingsChanged(object? sender, string property)
    {
        if (property == nameof(GameSettings.InstallPath))
        {
            _gamePathInvalid = false;
            ShowGameSetting();
        }
    }

    private void OnSettingsChanged(object? sender, string property)
    {
        switch (property)
        {
            case nameof(GlobalSettings.SchemaFolder):
                _schemaFolderMissing = false;
                ShowSchemaFolderSetting();
                break;
            case nameof(GlobalSettings.Language):
                OnPropertyChanged(nameof(SelectedLanguage));
                break;
            case nameof(GlobalSettings.Theme):
                OnPropertyChanged(nameof(SelectedTheme));
                break;
        }
    }
}
