using TotalWarModTool.GameProfiles;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.Objects;

public sealed record LoadOrderRequest(
    GameProfile Profile,
    string InstallPath,
    PackCatalogResult Catalog,
    string? ProjectFolder,
    string? ProjectName,
    /// <summary>Pack of the root row when it might not be active; null when the root comes from vanilla or the project.</summary>
    string? RootPackPath);

/// <summary>Works out which packs and which project feed the object view, in which order, plus the loc pack of the game language. Reads only.</summary>
public static class LoadOrderResolver
{
    public const string UsedModsFileName = "used_mods.txt";
    public const string LanguageFileName = "language.txt";

    public static LoadOrder Resolve(LoadOrderRequest request)
    {
        var profile = request.Profile;
        var dataFolder = Path.Combine(request.InstallPath, profile.DataFolder);
        var sources = new List<TableSource>();
        var warnings = new List<LoadOrderWarning>();

        foreach (var pack in request.Catalog.Packs)
        {
            if (pack.Origin == PackOrigin.Vanilla)
            {
                sources.Add(new TableSource(SourceKind.Vanilla, pack.Name, pack.Path));
            }
        }

        AddUsedMods(request, dataFolder, sources, warnings);

        if (request.RootPackPath is { } rootPath && !Contains(sources, rootPath))
        {
            sources.Add(new TableSource(SourceKind.Mod, Path.GetFileName(rootPath), rootPath));
            warnings.Add(new LoadOrderWarning(LoadOrderProblem.RootPackAdded, Path.GetFileName(rootPath), "The pack of the root row is not in the active mod list; it was added behind the mods."));
        }

        if (request.ProjectFolder is { } projectFolder)
        {
            sources.Add(new TableSource(SourceKind.Project, request.ProjectName ?? Path.GetFileName(projectFolder), projectFolder));
        }

        var locPack = ResolveLocPack(profile, dataFolder, sources, warnings);
        return new LoadOrder(sources, locPack, warnings);
    }

    private static void AddUsedMods(LoadOrderRequest request, string dataFolder, List<TableSource> sources, List<LoadOrderWarning> warnings)
    {
        var usedModsPath = Path.Combine(request.InstallPath, UsedModsFileName);
        if (!File.Exists(usedModsPath))
        {
            warnings.Add(new LoadOrderWarning(LoadOrderProblem.UsedModsMissing, UsedModsFileName, "used_mods.txt was not found in the game folder."));
            return;
        }

        var text = TryReadAllText(usedModsPath, out var error);
        if (text is null)
        {
            warnings.Add(new LoadOrderWarning(LoadOrderProblem.UsedModsUnreadable, UsedModsFileName, "used_mods.txt cannot be read: " + error));
            return;
        }

        var used = UsedModsFile.Parse(text);
        var searchFolders = used.WorkingDirectories.Where(Directory.Exists).Append(dataFolder).ToList();
        foreach (var modName in used.ModNames)
        {
            var path = FindMod(searchFolders, modName);
            if (path is null)
            {
                warnings.Add(new LoadOrderWarning(LoadOrderProblem.ModPackMissing, modName, "The pack of an active mod was not found."));
                continue;
            }

            if (!Contains(sources, path))
            {
                sources.Add(new TableSource(SourceKind.Mod, Path.GetFileName(path), path));
            }
        }
    }

    /// <summary>A name with a path separator or a parent reference is never combined into a path.</summary>
    private static string? FindMod(List<string> searchFolders, string modName)
    {
        if (modName.IndexOfAny(['/', '\\']) >= 0 || modName.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var folder in searchFolders)
        {
            var path = Path.Combine(folder, modName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static TableSource? ResolveLocPack(GameProfile profile, string dataFolder, List<TableSource> sources, List<LoadOrderWarning> warnings)
    {
        var languagePath = Path.Combine(dataFolder, LanguageFileName);
        string? packName = profile.DefaultLocPack;
        if (File.Exists(languagePath))
        {
            var text = TryReadAllText(languagePath, out var error);
            if (text is null)
            {
                warnings.Add(new LoadOrderWarning(LoadOrderProblem.LanguageFileUnreadable, LanguageFileName, "language.txt cannot be read: " + error));
            }
            else if (profile.LocPacks is { } locPacks && locPacks.TryGetValue(text.Trim().ToUpperInvariant(), out var mapped))
            {
                packName = mapped;
            }
        }

        if (string.IsNullOrEmpty(packName) || packName.IndexOfAny(['/', '\\']) >= 0 || packName.Contains("..", StringComparison.Ordinal))
        {
            warnings.Add(new LoadOrderWarning(LoadOrderProblem.LocPackMissing, packName, "No loc pack is defined for the game language."));
            return null;
        }

        var packPath = Path.Combine(dataFolder, packName);
        if (!File.Exists(packPath))
        {
            warnings.Add(new LoadOrderWarning(LoadOrderProblem.LocPackMissing, packName, "The loc pack of the game language was not found in the data folder."));
            return null;
        }

        var existing = sources.Find(s => s.Kind == SourceKind.Vanilla && string.Equals(s.Path, packPath, StringComparison.OrdinalIgnoreCase));
        return existing ?? new TableSource(SourceKind.Vanilla, packName, packPath);
    }

    private static bool Contains(List<TableSource> sources, string path) =>
        sources.Exists(s => s.Kind != SourceKind.Project && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    private static string? TryReadAllText(string path, out string error)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            error = "";
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }
}
