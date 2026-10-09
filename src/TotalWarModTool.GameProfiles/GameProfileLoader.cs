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
        catch (IOException ex)
        {
            problems.Add(new ProfileProblem(profilesFolder, "Profile folder cannot be listed: " + ex.Message));
            return new ProfileLoadResult(profiles, problems);
        }
        catch (UnauthorizedAccessException ex)
        {
            problems.Add(new ProfileProblem(profilesFolder, "Profile folder cannot be listed: " + ex.Message));
            return new ProfileLoadResult(profiles, problems);
        }

        var names = files.Select(Path.GetFileName).OfType<string>().ToList();
        names.Sort(StringComparer.Ordinal);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        catch (IOException ex)
        {
            detail = "File cannot be read: " + ex.Message;
            return null;
        }
        catch (UnauthorizedAccessException ex)
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
        catch (InvalidOperationException ex)
        {
            // JsonElement.GetString() throws this for a lone escaped surrogate, which JsonDocument.Parse accepts.
            detail = "Contains text that is not valid Unicode: " + ex.Message;
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

        if (!IsValidId(id))
        {
            detail = "id must consist of the characters a-z, 0-9 and _ only.";
            return null;
        }

        foreach (var (name, value) in new[] { ("installFolderName", installFolderName), ("dataFolder", dataFolder), ("schemaFile", schemaFile) })
        {
            if (!IsSingleSafePathSegment(value))
            {
                detail = name + " must be a single plain file or folder name.";
                return null;
            }
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

            if (!IsSingleSafePathSegment(manifest))
            {
                detail = "vanillaManifestFile must be a single plain file name.";
                return null;
            }
        }

        if (!TryReadLocPacks(root, out var locPacks, out detail))
        {
            return null;
        }

        string? defaultLocPack = null;
        if (root.TryGetProperty("defaultLocPack", out var defaultElement) && defaultElement.ValueKind != JsonValueKind.Null)
        {
            defaultLocPack = defaultElement.ValueKind == JsonValueKind.String ? defaultElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(defaultLocPack) || !IsSingleSafePathSegment(defaultLocPack))
            {
                detail = "defaultLocPack must be a single plain file name when present.";
                return null;
            }
        }

        return new GameProfile(id, displayName, steamAppId, installFolderName, dataFolder, packFormat, schemaFile, packTypes, manifest, locPacks, defaultLocPack);
    }

    // Language codes are normalized to upper case; lookups ignore case.
    private static bool TryReadLocPacks(JsonElement root, out IReadOnlyDictionary<string, string> locPacks, out string detail)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        locPacks = map;
        detail = string.Empty;
        if (!root.TryGetProperty("locPacks", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            detail = "locPacks must be an object mapping language codes to loc pack file names.";
            return false;
        }

        foreach (var entry in element.EnumerateObject())
        {
            var code = entry.Name.Trim().ToUpperInvariant();
            var file = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
            if (code.Length == 0 || string.IsNullOrWhiteSpace(file) || !IsSingleSafePathSegment(file))
            {
                detail = "locPacks must map non-empty language codes to single plain file names.";
                return false;
            }

            if (!map.TryAdd(code, file))
            {
                detail = "locPacks lists the language code '" + code + "' more than once.";
                return false;
            }
        }

        return true;
    }

    private static bool IsValidId(string id)
    {
        foreach (var c in id)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return id.Length > 0;
    }

    // Profile values are joined to the install and profile folders, so they must never leave them.
    private static bool IsSingleSafePathSegment(string value) =>
        value.Length > 0
        && !Path.IsPathRooted(value)
        && value.IndexOfAny(['/', '\\']) < 0
        && value is not ("." or "..")
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

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
