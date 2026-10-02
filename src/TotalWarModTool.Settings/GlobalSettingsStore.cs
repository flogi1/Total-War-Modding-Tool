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
        };
        JsonFileStore.Save(FilePath, SchemaVersion, file);
    }

    private static GlobalSettings ToSettings(SettingsFile file)
    {
        // Every member is nullable: System.Text.Json delivers JSON null and missing properties as null, whatever the annotations say.
        return new GlobalSettings
        {
            Language = file.Language ?? string.Empty,
            Theme = ParseTheme(file.Theme),
            MainWindow = ToPlacement(file.MainWindow),
        };
    }

    private static ThemeChoice ParseTheme(string? text)
    {
        // Compares the names instead of Enum.TryParse, which would also accept numbers and comma-separated lists.
        foreach (var theme in Enum.GetValues<ThemeChoice>())
        {
            if (string.Equals(theme.ToString(), text?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return ThemeChoice.System;
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