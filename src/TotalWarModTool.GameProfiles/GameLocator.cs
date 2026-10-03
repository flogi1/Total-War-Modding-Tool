using System.Text.RegularExpressions;

namespace TotalWarModTool.GameProfiles;

public sealed record GameLocation(string InstallPath, string DataPath, string? WorkshopPath);

/// <summary>Finds an installed game through the Steam libraries. Read only.</summary>
public static partial class GameLocator
{
    [GeneratedRegex("\"path\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();

    /// <summary>The first Steam library that contains the game's data folder, or null.</summary>
    public static GameLocation? Find(GameProfile profile, ISteamLocationSource steam)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(steam);

        var steamPath = steam.GetSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            return null;
        }

        foreach (var library in ReadLibraryFolders(steamPath))
        {
            if (!Directory.Exists(library))
            {
                continue;
            }

            var location = FromInstallPath(profile, Path.Combine(library, "steamapps", "common", profile.InstallFolderName));
            if (location is not null)
            {
                return location;
            }
        }

        return null;
    }

    /// <summary>Builds the location for an install folder, or null when its data folder is missing.</summary>
    public static GameLocation? FromInstallPath(GameProfile profile, string installPath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(installPath);

        var dataPath = Path.Combine(installPath, profile.DataFolder);
        if (!Directory.Exists(dataPath))
        {
            return null;
        }

        return new GameLocation(installPath, dataPath, FindWorkshopPath(profile, installPath));
    }

    /// <summary>The Steam library folders: the Steam folder itself first, then every "path" entry of libraryfolders.vdf, without duplicates.</summary>
    public static IReadOnlyList<string> ReadLibraryFolders(string steamPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamPath);

        var folders = new List<string> { steamPath };
        string text;
        try
        {
            var file = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return folders;
        }

        foreach (Match match in PathEntry().Matches(text))
        {
            var folder = match.Groups[1].Value.Replace(@"\\", @"\");
            if (folder.Length > 0 && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    // <library>\steamapps\common\<game> -> <library>\steamapps\workshop\content\<appId>
    private static string? FindWorkshopPath(GameProfile profile, string installPath)
    {
        var common = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(installPath));
        var steamApps = common is null ? null : Path.GetDirectoryName(common);
        var library = steamApps is null ? null : Path.GetDirectoryName(steamApps);
        if (library is null
            || !string.Equals(Path.GetFileName(common), "common", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(steamApps), "steamapps", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var workshop = Path.Combine(library, "steamapps", "workshop", "content", profile.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Directory.Exists(workshop) ? workshop : null;
    }
}
