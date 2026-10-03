using TotalWarModTool.GameProfiles;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace;

/// <summary>Lists the packs of the game's data folder and the workshop folder and labels each with its origin. Read only.</summary>
public static class PackCatalog
{
    private const string PackExtension = ".pack";

    private readonly record struct Candidate(string Path, string Name, string? WorkshopId);

    /// <summary>
    /// Scans the data folder (top level only) and the workshop folder (one level deep, sub folder name = workshop id).
    /// Missing or unreadable folders give an empty list. A pack with a broken header stays in the list with its problem.
    /// Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static PackCatalogResult Scan(GameProfile profile, string installPath, string? workshopPath,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(installPath);
        cancellationToken.ThrowIfCancellationRequested();

        var dataFolder = Path.Combine(installPath, profile.DataFolder);
        var manifestPacks = ReadManifestPacks(profile, dataFolder, cancellationToken);

        var folderProblems = new List<CatalogFolderProblem>();
        var dataCandidates = ListPacks(dataFolder, workshopId: null, folderProblems, cancellationToken);
        var workshopCandidates = ListWorkshopPacks(workshopPath, folderProblems, cancellationToken);
        var total = dataCandidates.Count + workshopCandidates.Count;

        var vanillaTypes = new HashSet<string>(profile.VanillaPackTypes, StringComparer.OrdinalIgnoreCase);
        var packs = new List<PackSource>(total);
        foreach (var candidate in dataCandidates.Concat(workshopCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fingerprint = FileFingerprint.TryRead(candidate.Path);
            var header = PackReader.ReadHeader(candidate.Path);
            var origin = candidate.WorkshopId is not null
                ? PackOrigin.Workshop
                : IsVanilla(candidate.Name, header, manifestPacks, vanillaTypes) ? PackOrigin.Vanilla : PackOrigin.DataFolderMod;
            packs.Add(new PackSource(candidate.Path, candidate.Name, origin, candidate.WorkshopId, fingerprint, header.Header, header.Problem, header.Detail));
            progress?.Report(new OperationProgress(packs.Count, total, candidate.Name));
        }

        packs.Sort(Compare);
        return new PackCatalogResult(packs, manifestPacks is not null, folderProblems);
    }

    private static bool IsVanilla(string name, PackHeaderResult header, HashSet<string>? manifestPacks, HashSet<string> vanillaTypes) =>
        manifestPacks is not null
            ? manifestPacks.Contains(name)
            : header.Header is { } h && vanillaTypes.Contains(h.Type.ToString());

    private static int Compare(PackSource a, PackSource b)
    {
        var byOrigin = a.Origin.CompareTo(b.Origin);
        if (byOrigin != 0)
        {
            return byOrigin;
        }

        var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        return byName != 0 ? byName : string.CompareOrdinal(a.Path, b.Path);
    }

    // A folder that cannot be listed completely contributes no packs: a partial list would be a half result.
    // A missing folder is not a problem; a folder that exists but cannot be listed is reported in folderProblems.
    private static List<Candidate> ListPacks(string folder, string? workshopId, List<CatalogFolderProblem> folderProblems,
        CancellationToken cancellationToken)
    {
        var result = new List<Candidate>();
        try
        {
            // The pattern is not used on purpose: its case handling depends on the platform, so ".PACK" is filtered here.
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (name.EndsWith(PackExtension, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new Candidate(file, name, workshopId));
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // A missing folder counts as empty and is no problem.
            return [];
        }
        catch (IOException ex)
        {
            return NoPacks(folder, ex, folderProblems);
        }
        catch (UnauthorizedAccessException ex)
        {
            return NoPacks(folder, ex, folderProblems);
        }

        return result;
    }

    private static List<Candidate> NoPacks(string folder, Exception reason, List<CatalogFolderProblem> folderProblems)
    {
        folderProblems.Add(new CatalogFolderProblem(folder, reason.Message));
        return [];
    }

    private static List<Candidate> ListWorkshopPacks(string? workshopPath, List<CatalogFolderProblem> folderProblems,
        CancellationToken cancellationToken)
    {
        var result = new List<Candidate>();
        if (string.IsNullOrEmpty(workshopPath))
        {
            return result;
        }

        List<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(workshopPath).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            // A missing workshop folder counts as empty and is no problem.
            return result;
        }
        catch (IOException ex)
        {
            return NoPacks(workshopPath, ex, folderProblems);
        }
        catch (UnauthorizedAccessException ex)
        {
            return NoPacks(workshopPath, ex, folderProblems);
        }

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.AddRange(ListPacks(folder, Path.GetFileName(folder), folderProblems, cancellationToken));
        }

        return result;
    }

    // Null when the profile names no manifest or the file is not readable. Each line is "path<TAB>size<TAB>flag";
    // only the file name of a path that ends in .pack counts.
    private static HashSet<string>? ReadManifestPacks(GameProfile profile, string dataFolder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(profile.VanillaManifestFile))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(Path.Combine(dataFolder, profile.VanillaManifestFile), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tab = line.IndexOf('\t');
                var path = tab >= 0 ? line[..tab] : line;
                if (path.EndsWith(PackExtension, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(path[(path.LastIndexOfAny(['/', '\\']) + 1)..]);
                }
            }

            return names;
        }
        catch (IOException)
        {
            // An unreadable manifest means: no manifest, the pack type decides.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
            return null;
        }
    }
}
