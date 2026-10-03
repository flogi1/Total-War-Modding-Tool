using TotalWarModTool.Storage;

namespace TotalWarModTool.Settings;

public sealed record GameSettingsLoadResult(GameSettings Settings, LoadStatus Status, string? BadCopyPath);

/// <summary>
/// Loads and saves <see cref="GameSettings"/> as game-settings.json in the folder of one game below the roaming root.
/// A broken file never blocks the start: it is copied to a .bad file and defaults are used.
/// </summary>
public sealed class GameSettingsStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "game-settings.json";

    public GameSettingsStore(string roamingRoot, string profileId)
    {
        FilePath = Path.Combine(roamingRoot, "games", profileId, FileName);
    }

    public string FilePath { get; }

    public GameSettingsLoadResult Load()
    {
        var result = JsonFileStore.Load(FilePath, SchemaVersion, () => new GameSettingsFile());
        return new GameSettingsLoadResult(ToSettings(result.Value), result.Status, result.BadCopyPath);
    }

    /// <summary>Creates the game folder when it is missing.</summary>
    public void Save(GameSettings settings)
    {
        JsonFileStore.Save(FilePath, SchemaVersion, new GameSettingsFile { InstallPath = settings.InstallPath });
    }

    private static GameSettings ToSettings(GameSettingsFile file) => new() { InstallPath = file.InstallPath };

    // The file format: all members nullable so that the file can be read leniently. schemaVersion is added by JsonFileStore.
    private sealed class GameSettingsFile
    {
        public string? InstallPath { get; set; }
    }
}
