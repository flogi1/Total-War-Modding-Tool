using System.Globalization;
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
        ResetTilesCommand = new RelayCommand(ResetTiles);
        AddToolbarSeparatorCommand = new RelayCommand(AddToolbarSeparator);
        ResetToolbarCommand = new RelayCommand(() => _settings.ToolbarOrder = ToolbarCatalog.DefaultOrder);
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

    /// <summary>The page "Game" of the window, see <see cref="SelectedCategory"/>.</summary>
    public const int GameCategory = 1;

    /// <summary>The page "Layout" of the window, see <see cref="SelectedCategory"/>.</summary>
    public const int LayoutCategory = 2;

    /// <summary>The page "Tile bar" of the window, see <see cref="SelectedCategory"/>.</summary>
    public const int TilesCategory = 3;

    /// <summary>The page "Toolbar" of the window, see <see cref="SelectedCategory"/>.</summary>
    public const int ToolbarCategory = 4;

    /// <summary>The highest page index; a new page raises it.</summary>
    private const int LastCategory = ToolbarCategory;

    /// <summary>
    /// The chosen page of the window: 0 General, 1 Game, 2 Layout, 3 Tile bar, 4 Toolbar. Stored in <see cref="GlobalSettings.SettingsCategory"/>,
    /// so the window opens on the page shown last; a stored value that names no page shows the first page.
    /// </summary>
    public int SelectedCategory
    {
        get => _settings.SettingsCategory is >= 0 and <= LastCategory ? _settings.SettingsCategory : 0;
        set
        {
            // A list box writes -1 when it loses its selection; that is not a choice of the user. The settings raise the change.
            if (value is >= 0 and <= LastCategory)
            {
                _settings.SettingsCategory = value;
            }
        }
    }

    /// <summary>The settings themselves, for the code-built pages (tile bar and toolbar lists).</summary>
    public GlobalSettings Settings => _settings;

    public ToolbarPosition ToolbarPosition
    {
        get => _settings.ToolbarPosition;
        set => _settings.ToolbarPosition = value;
    }

    public bool ToolbarLabelsVisible
    {
        get => _settings.ToolbarLabelsVisible;
        set => _settings.ToolbarLabelsVisible = value;
    }

    public bool TileBarVisible
    {
        get => _settings.TileBarVisible;
        set => _settings.TileBarVisible = value;
    }

    public TileSize TileSize
    {
        get => _settings.TileSize;
        set => _settings.TileSize = value;
    }

    public bool TileLabelsVisible
    {
        get => _settings.TileLabelsVisible;
        set => _settings.TileLabelsVisible = value;
    }

    /// <summary>Default order of the tiles, and no tile icon-only. Hidden tiles stay hidden.</summary>
    public ICommand ResetTilesCommand { get; }

    /// <summary>Appends a new separator (<see cref="NextSeparator"/>) to the toolbar.</summary>
    public ICommand AddToolbarSeparatorCommand { get; }

    /// <summary>Default order of the toolbar with its four separators. Hidden entries stay hidden.</summary>
    public ICommand ResetToolbarCommand { get; }

    /// <summary>Takes the separator <paramref name="id"/> out of the toolbar.</summary>
    public void RemoveToolbarSeparator(string id)
    {
        if (ToolbarCatalog.IsSeparator(id))
        {
            _settings.ToolbarOrder = _settings.ToolbarOrder.Where(entry => !string.Equals(entry, id, StringComparison.Ordinal)).ToList();
        }
    }

    /// <summary>
    /// The id of a new separator: one more than the highest separator number in <paramref name="order"/>, <c>sep:1</c> without one. A number
    /// freed by a removed separator is not taken again unless it was the highest. When the highest number is <see cref="int.MaxValue"/>
    /// (a hand-edited file), the smallest free number is taken instead, so adding a separator always works.
    /// </summary>
    public static string NextSeparator(IEnumerable<string> order)
    {
        var used = order.Where(ToolbarCatalog.IsSeparator)
            .Select(id => int.Parse(id.AsSpan(ToolbarCatalog.SeparatorPrefix.Length), CultureInfo.InvariantCulture))
            .ToHashSet();
        var highest = used.DefaultIfEmpty(0).Max();
        if (highest < int.MaxValue)
        {
            return ToolbarCatalog.Separator(highest + 1);
        }

        var free = 1;
        while (used.Contains(free))
        {
            free++;
        }

        return ToolbarCatalog.Separator(free);
    }

    private void ResetTiles()
    {
        _settings.TileOrder = TileCatalog.DefaultOrder;
        foreach (var id in TileCatalog.DefaultOrder)
        {
            _settings.SetTileIconOnly(id, false);
        }
    }

    private void AddToolbarSeparator() => _settings.ToolbarOrder = [.. _settings.ToolbarOrder, NextSeparator(_settings.ToolbarOrder)];

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

    /// <summary>
    /// Why the last input of <see cref="GamePath"/> was rejected, or why the stored game folder shown in the box has no data folder; null when
    /// there is nothing to say.
    /// </summary>
    public string? GamePathError =>
        (_gamePathInvalid || StoredPathInvalid) && _workspace.Profile is { } profile ? Texts.SettingsGamePathInvalid(profile.DataFolder) : null;

    // The box shows the stored folder (the user has not typed something else) and the workspace found no data folder in it.
    private bool StoredPathInvalid =>
        _workspace.ManualPathInvalid && _gameSettings.InstallPath is { } stored && string.Equals(_gamePath, stored, StringComparison.Ordinal);

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

    /// <summary>
    /// Shows the stored values in the two path boxes and forgets the input that was rejected. Called when a settings window is opened, so it
    /// does not show what was typed into the window before.
    /// </summary>
    public void ResetInputs()
    {
        _gamePathInvalid = false;
        _schemaFolderMissing = false;
        ShowGameSetting();
        ShowSchemaFolderSetting();
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
        else if (Normalize(text) is not { } folder || GameLocator.FromInstallPath(profile, folder) is null)
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
            _gameSettings.InstallPath = folder;
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
        string? folder = null;
        if (text.Length > 0)
        {
            folder = Normalize(text);
            if (folder is null || !Directory.Exists(folder))
            {
                _schemaFolder = text;
                _schemaFolderMissing = true;
                OnPropertyChanged(nameof(SchemaFolder));
                OnPropertyChanged(nameof(SchemaFolderError));
                return;
            }
        }

        _schemaFolderMissing = false;
        _settings.SchemaFolder = folder;
        ShowSchemaFolderSetting();
    }

    // The folder as it is stored: full path, without a trailing separator. Null for text that cannot be a path.
    private static string? Normalize(string text)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
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
            case nameof(GlobalSettings.SettingsCategory):
                OnPropertyChanged(nameof(SelectedCategory));
                break;
            case nameof(GlobalSettings.ToolbarPosition):
            case nameof(GlobalSettings.ToolbarLabelsVisible):
            case nameof(GlobalSettings.TileBarVisible):
            case nameof(GlobalSettings.TileSize):
            case nameof(GlobalSettings.TileLabelsVisible):
                // The pass-through properties have the names of the settings.
                OnPropertyChanged(property);
                break;
        }
    }
}
