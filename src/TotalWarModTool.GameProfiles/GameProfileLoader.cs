using System.Text.Json;

namespace TotalWarModTool.GameProfiles;

/// <summary>Loads game profiles from JSON files. A broken file is skipped and reported, it never throws.</summary>
public static class GameProfileLoader
{
    public const int SchemaVersion = 1;

    /// <summary>Loads every top-level *.json file of the folder. Profiles are sorted by id, files are read in ordinal name order.</summary>
    public static ProfileLoadResult LoadAll(string profilesFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesFolder);

        var profiles = new List<GameProfile>();
        var problems = new List<ProfileProblem>();
        if (!Directory.Exists(profilesFolder))
        {
            return new ProfileLoadResult(profiles, problems);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(profilesFolder, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new ProfileProblem(profilesFolder, "Profile folder cannot be listed: " + ex.Message));
            return new ProfileLoadResult(profiles, problems);
        }

        var names = files.Select(Path.GetFileName).OfType<string>().ToList();
        names.Sort(StringComparer.Ordinal);

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var profile = TryLoad(Path.Combine(profilesFolder, name), out var detail);
            if (profile is null)
            {
                problems.Add(new ProfileProblem(name, detail));
            }
            else if (!seenIds.Add(profile.Id))
            {
                problems.Add(new ProfileProblem(name, "Profile id '" + profile.Id + "' is already used by an earlier file."));
            }
            else
            {
                profiles.Add(profile);
            }
        }

        profiles.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return new ProfileLoadResult(profiles, problems);
    }

    private static GameProfile? TryLoad(string path, out string detail)
    {
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            detail = "File cannot be read: " + ex.Message;
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return Parse(document.RootElement, out detail);
        }
        catch (JsonException ex)
        {
            detail = "Not valid JSON: " + ex.Message;
            return null;
        }
    }

    private static GameProfile? Parse(JsonElement root, out string detail)
    {
        detail = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            detail = "The profile must be a JSON object.";
            return null;
        }

        if (!root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var versionNumber)
            || versionNumber != SchemaVersion)
        {
            detail = "schemaVersion is missing or not " + SchemaVersion + ".";
            return null;
        }

        if (!TryGetString(root, "id", out var id, out detail)
            || !TryGetString(root, "displayName", out var displayName, out detail)
            || !TryGetString(root, "installFolderName", out var installFolderName, out detail)
            || !TryGetString(root, "dataFolder", out var dataFolder, out detail)
            || !TryGetString(root, "packFormat", out var packFormat, out detail)
            || !TryGetString(root, "schemaFile", out var schemaFile, out detail))
        {
            return null;
        }

        if (!root.TryGetProperty("steamAppId", out var appIdElement)
            || appIdElement.ValueKind != JsonValueKind.Number
            || !appIdElement.TryGetInt32(out var steamAppId)
            || steamAppId <= 0)
        {
            detail = "steamAppId is missing or not a positive integer.";
            return null;
        }

        var packTypes = new List<string>();
        if (!root.TryGetProperty("vanillaPackTypes", out var typesElement) || typesElement.ValueKind != JsonValueKind.Array)
        {
            detail = "vanillaPackTypes is missing or not an array.";
            return null;
        }

        foreach (var item in typesElement.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                detail = "vanillaPackTypes must contain only non-empty strings.";
                return null;
            }

            packTypes.Add(value);
        }

        if (packTypes.Count == 0)
        {
            detail = "vanillaPackTypes must not be empty.";
            return null;
        }

        string? manifest = null;
        if (root.TryGetProperty("vanillaManifestFile", out var manifestElement) && manifestElement.ValueKind != JsonValueKind.Null)
        {
            manifest = manifestElement.ValueKind == JsonValueKind.String ? manifestElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(manifest))
            {
                detail = "vanillaManifestFile must be a non-empty string when present.";
                return null;
            }
        }

        return new GameProfile(id, displayName, steamAppId, installFolderName, dataFolder, packFormat, schemaFile, packTypes, manifest);
    }

    private static bool TryGetString(JsonElement root, string name, out string value, out string detail)
    {
        value = string.Empty;
        detail = string.Empty;
        if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        detail = name + " is missing or empty.";
        return false;
    }
}
