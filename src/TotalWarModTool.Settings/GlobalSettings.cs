namespace TotalWarModTool.Settings;

public enum ThemeChoice { System, Light, Dark }

public enum TileSize { Large, Medium, Compact }

public enum ToolbarPosition { Top, Left, Right, Bottom }

/// <summary>
/// Global (per user, not per project) settings. Every setting is changed through its property, which raises <see cref="Changed"/>
/// only when the value really changes, so the UI and other services can react immediately.
/// </summary>
public sealed class GlobalSettings
{
    private const string DefaultLanguage = "en";

    private string _language = DefaultLanguage;
    private ThemeChoice _theme = ThemeChoice.System;
    private WindowPlacement? _mainWindow;
    private string? _selectedGame;
    private string? _schemaFolder;
    private IReadOnlyList<string> _tileOrder = TileCatalog.DefaultOrder.ToList();
    private readonly HashSet<string> _hiddenTiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _iconOnlyTiles = new(StringComparer.Ordinal);
    private TileSize _tileSize = TileSize.Large;
    private bool _tileLabelsVisible = true;
    private bool _tileBarVisible = true;
    private IReadOnlyList<string> _toolbarOrder = ToolbarCatalog.DefaultOrder.ToList();
    private readonly HashSet<string> _hiddenToolbarItems = new(StringComparer.Ordinal);
    private ToolbarPosition _toolbarPosition = ToolbarPosition.Top;
    private bool _toolbarLabelsVisible = true;
    private int _settingsCategory;

    /// <summary>Argument: the name of the changed property.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Language code. The setter trims and lower-cases it; a blank value becomes <see cref="DefaultLanguage"/>.</summary>
    public string Language
    {
        get => _language;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultLanguage : value.Trim().ToLowerInvariant();
            if (normalized == _language)
            {
                return;
            }

            _language = normalized;
            Changed?.Invoke(this, nameof(Language));
        }
    }

    public ThemeChoice Theme
    {
        get => _theme;
        set
        {
            if (value == _theme)
            {
                return;
            }

            _theme = value;
            Changed?.Invoke(this, nameof(Theme));
        }
    }

    public WindowPlacement? MainWindow
    {
        get => _mainWindow;
        set
        {
            if (Equals(value, _mainWindow))
            {
                return;
            }

            _mainWindow = value;
            Changed?.Invoke(this, nameof(MainWindow));
        }
    }

    /// <summary>Profile id of the selected game. The setter trims; a blank value becomes null (no game chosen yet).</summary>
    public string? SelectedGame
    {
        get => _selectedGame;
        set
        {
            var normalized = NormalizeText(value);
            if (normalized == _selectedGame)
            {
                return;
            }

            _selectedGame = normalized;
            Changed?.Invoke(this, nameof(SelectedGame));
        }
    }

    /// <summary>Override folder for schema files. The setter trims; a blank value becomes null (use the default schema source).</summary>
    public string? SchemaFolder
    {
        get => _schemaFolder;
        set
        {
            var normalized = NormalizeText(value);
            if (normalized == _schemaFolder)
            {
                return;
            }

            _schemaFolder = normalized;
            Changed?.Invoke(this, nameof(SchemaFolder));
        }
    }

    /// <summary>Order of the tiles. The setter normalizes it: unknown ids and duplicates drop out, missing ids are appended.</summary>
    public IReadOnlyList<string> TileOrder
    {
        get => _tileOrder;
        set
        {
            var normalized = ReorderList.Complete(value, TileCatalog.DefaultOrder);
            if (normalized.SequenceEqual(_tileOrder))
            {
                return;
            }

            _tileOrder = normalized;
            Changed?.Invoke(this, nameof(TileOrder));
        }
    }

    public IReadOnlyCollection<string> HiddenTiles => _hiddenTiles;

    public bool IsTileVisible(string id) => !_hiddenTiles.Contains(id);

    public void SetTileVisible(string id, bool visible) => SetMember(_hiddenTiles, id, !visible, nameof(HiddenTiles));

    public IReadOnlyCollection<string> IconOnlyTiles => _iconOnlyTiles;

    public bool IsTileIconOnly(string id) => _iconOnlyTiles.Contains(id);

    public void SetTileIconOnly(string id, bool iconOnly) => SetMember(_iconOnlyTiles, id, iconOnly, nameof(IconOnlyTiles));

    public TileSize TileSize
    {
        get => _tileSize;
        set
        {
            if (value == _tileSize)
            {
                return;
            }

            _tileSize = value;
            Changed?.Invoke(this, nameof(TileSize));
        }
    }

    public bool TileLabelsVisible
    {
        get => _tileLabelsVisible;
        set
        {
            if (value == _tileLabelsVisible)
            {
                return;
            }

            _tileLabelsVisible = value;
            Changed?.Invoke(this, nameof(TileLabelsVisible));
        }
    }

    public bool TileBarVisible
    {
        get => _tileBarVisible;
        set
        {
            if (value == _tileBarVisible)
            {
                return;
            }

            _tileBarVisible = value;
            Changed?.Invoke(this, nameof(TileBarVisible));
        }
    }

    /// <summary>Order of the toolbar including separators (<c>sep:n</c>). The setter normalizes it like <see cref="TileOrder"/>.</summary>
    public IReadOnlyList<string> ToolbarOrder
    {
        get => _toolbarOrder;
        set
        {
            var normalized = ReorderList.Complete(value, ToolbarCatalog.All, ToolbarCatalog.IsSeparator);
            if (normalized.SequenceEqual(_toolbarOrder))
            {
                return;
            }

            _toolbarOrder = normalized;
            Changed?.Invoke(this, nameof(ToolbarOrder));
        }
    }

    public IReadOnlyCollection<string> HiddenToolbarItems => _hiddenToolbarItems;

    public bool IsToolbarItemVisible(string id) => !_hiddenToolbarItems.Contains(id);

    public void SetToolbarItemVisible(string id, bool visible) => SetMember(_hiddenToolbarItems, id, !visible, nameof(HiddenToolbarItems));

    public ToolbarPosition ToolbarPosition
    {
        get => _toolbarPosition;
        set
        {
            if (value == _toolbarPosition)
            {
                return;
            }

            _toolbarPosition = value;
            Changed?.Invoke(this, nameof(ToolbarPosition));
        }
    }

    public bool ToolbarLabelsVisible
    {
        get => _toolbarLabelsVisible;
        set
        {
            if (value == _toolbarLabelsVisible)
            {
                return;
            }

            _toolbarLabelsVisible = value;
            Changed?.Invoke(this, nameof(ToolbarLabelsVisible));
        }
    }

    /// <summary>Index of the selected page in the settings window. The setter turns negative values into 0.</summary>
    public int SettingsCategory
    {
        get => _settingsCategory;
        set
        {
            var normalized = Math.Max(0, value);
            if (normalized == _settingsCategory)
            {
                return;
            }

            _settingsCategory = normalized;
            Changed?.Invoke(this, nameof(SettingsCategory));
        }
    }

    private void SetMember(HashSet<string> set, string id, bool present, string propertyName)
    {
        if (present ? set.Add(id) : set.Remove(id))
        {
            Changed?.Invoke(this, propertyName);
        }
    }

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}