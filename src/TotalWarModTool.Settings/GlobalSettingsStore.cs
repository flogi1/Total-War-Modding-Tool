using TotalWarModTool.Storage;

namespace TotalWarModTool.Settings;

public sealed record SettingsLoadResult(GlobalSettings Settings, LoadStatus Status, string? BadCopyPath);

/// <summary>
/// Loads and saves <see cref="GlobalSettings"/> as settings.json in one directory. A broken file never blocks the start:
/// it is copied to a .bad file and defaults are used; unusable values in a readable file are repaired silently.
/// </summary>
public sealed class GlobalSettingsStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "settings.json";

    public GlobalSettingsStore(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    public SettingsLoadResult Load()
    {
        var result = JsonFileStore.Load(FilePath, SchemaVersion, () => new SettingsFile());
        return new SettingsLoadResult(ToSettings(result.Value), result.Status, result.BadCopyPath);
    }

    public void Save(GlobalSettings settings)
    {
        var file = new SettingsFile
        {
            Language = settings.Language,
            Theme = settings.Theme.ToString(),
            MainWindow = ToFile(settings.MainWindow),
            SelectedGame = settings.SelectedGame,
            SchemaFolder = settings.SchemaFolder,
            TileOrder = settings.TileOrder.ToList(),
            HiddenTiles = Sorted(settings.HiddenTiles),
            IconOnlyTiles = Sorted(settings.IconOnlyTiles),
            TileSize = settings.TileSize.ToString(),
            TileLabelsVisible = settings.TileLabelsVisible,
            TileBarVisible = settings.TileBarVisible,
            ToolbarOrder = settings.ToolbarOrder.ToList(),
            HiddenToolbarItems = Sorted(settings.HiddenToolbarItems),
            ToolbarPosition = settings.ToolbarPosition.ToString(),
            ToolbarLabelsVisible = settings.ToolbarLabelsVisible,
            SettingsCategory = settings.SettingsCategory,
        };
        JsonFileStore.Save(FilePath, SchemaVersion, file);
    }

    private static GlobalSettings ToSettings(SettingsFile file)
    {
        // Every member is nullable: System.Text.Json delivers JSON null and missing properties as null, whatever the annotations say.
        var settings = new GlobalSettings
        {
            Language = file.Language ?? string.Empty,
            Theme = ParseEnum(file.Theme, ThemeChoice.System),
            MainWindow = ToPlacement(file.MainWindow),
            SelectedGame = file.SelectedGame,
            SchemaFolder = file.SchemaFolder,
            TileSize = ParseEnum(file.TileSize, TileSize.Large),
            TileLabelsVisible = file.TileLabelsVisible ?? true,
            TileBarVisible = file.TileBarVisible ?? true,
            ToolbarPosition = ParseEnum(file.ToolbarPosition, ToolbarPosition.Top),
            ToolbarLabelsVisible = file.ToolbarLabelsVisible ?? true,
            SettingsCategory = file.SettingsCategory ?? 0,
        };
        // A missing field keeps the default order (the toolbar default has separators). Present lists go through the setters,
        // which normalize them: unknown ids drop out, missing ones are appended.
        if (file.TileOrder is not null)
        {
            settings.TileOrder = file.TileOrder;
        }

        if (file.ToolbarOrder is not null)
        {
            settings.ToolbarOrder = file.ToolbarOrder;
        }

        foreach (var id in file.HiddenTiles ?? [])
        {
            if (id is not null && TileCatalog.IsKnown(id))
            {
                settings.SetTileVisible(id, false);
            }
        }

        foreach (var id in file.IconOnlyTiles ?? [])
        {
            if (id is not null && TileCatalog.IsKnown(id))
            {
                settings.SetTileIconOnly(id, true);
            }
        }

        foreach (var id in file.HiddenToolbarItems ?? [])
        {
            if (id is not null && ToolbarCatalog.IsKnown(id))
            {
                settings.SetToolbarItemVisible(id, false);
            }
        }

        return settings;
    }

    private static List<string> Sorted(IEnumerable<string> ids) => ids.OrderBy(id => id, StringComparer.Ordinal).ToList();

    private static T ParseEnum<T>(string? text, T fallback) where T : struct, Enum
    {
        // Compares the names instead of Enum.TryParse, which would also accept numbers and comma-separated lists.
        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(value.ToString(), text?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return fallback;
    }

    private static WindowPlacement? ToPlacement(WindowPlacementFile? file)
    {
        if (file is not { Left: { } left, Top: { } top, Width: { } width, Height: { } height } || !IsValidPlacement(left, top, width, height))
        {
            return null;
        }

        return new WindowPlacement(left, top, width, height, file.Maximized ?? false);
    }

    private static WindowPlacementFile? ToFile(WindowPlacement? placement)
    {
        if (placement is null || !IsValidPlacement(placement.Left, placement.Top, placement.Width, placement.Height))
        {
            return null;
        }

        return new WindowPlacementFile
        {
            Left = placement.Left,
            Top = placement.Top,
            Width = placement.Width,
            Height = placement.Height,
            Maximized = placement.Maximized,
        };
    }

    private static bool IsValidPlacement(double left, double top, double width, double height) =>
        double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0;

    // The file format. Separate from the model so that the file can be read leniently: all members nullable, theme as text.
    // schemaVersion is added by JsonFileStore and must not be a member here.
    private sealed class SettingsFile
    {
        public string? Language { get; set; }
        public string? Theme { get; set; }
        public WindowPlacementFile? MainWindow { get; set; }
        public string? SelectedGame { get; set; }
        public string? SchemaFolder { get; set; }
        public List<string>? TileOrder { get; set; }
        public List<string>? HiddenTiles { get; set; }
        public List<string>? IconOnlyTiles { get; set; }
        public string? TileSize { get; set; }
        public bool? TileLabelsVisible { get; set; }
        public bool? TileBarVisible { get; set; }
        public List<string>? ToolbarOrder { get; set; }
        public List<string>? HiddenToolbarItems { get; set; }
        public string? ToolbarPosition { get; set; }
        public bool? ToolbarLabelsVisible { get; set; }
        public int? SettingsCategory { get; set; }
    }

    private sealed class WindowPlacementFile
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool? Maximized { get; set; }
    }
}