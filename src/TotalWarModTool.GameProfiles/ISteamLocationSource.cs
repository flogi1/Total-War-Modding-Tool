namespace TotalWarModTool.GameProfiles;

/// <summary>Finds the Steam installation folder. The real implementation (registry) is provided by the app.</summary>
public interface ISteamLocationSource
{
    /// <summary>The Steam installation folder, or null when Steam is not installed.</summary>
    string? GetSteamPath();
}
