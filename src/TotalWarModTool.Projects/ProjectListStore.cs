using System.Globalization;
using TotalWarModTool.Storage;

namespace TotalWarModTool.Projects;

public sealed record ProjectListLoadResult(ProjectList List, LoadStatus Status, string? BadCopyPath);

/// <summary>
/// Loads and saves the <see cref="ProjectList"/> of one game as projects.json in the folder of that game below the roaming root.
/// A broken file never blocks the start: it is copied to a .bad file and the empty list is used. Unusable values in a readable
/// file are repaired silently on load.
/// </summary>
public sealed class ProjectListStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "projects.json";

    public ProjectListStore(string roamingRoot, string profileId)
    {
        FilePath = Path.Combine(roamingRoot, "games", profileId, FileName);
    }

    public string FilePath { get; }

    public ProjectListLoadResult Load()
    {
        var result = JsonFileStore.Load(FilePath, SchemaVersion, () => new ProjectListFile());
        return new ProjectListLoadResult(Repair(result.Value), result.Status, result.BadCopyPath);
    }

    /// <summary>Writes the list as given (repairs happen on load). Creates the game folder when it is missing.</summary>
    public void Save(ProjectList list)
    {
        var file = new ProjectListFile
        {
            ActiveId = list.ActiveId?.ToString("D", CultureInfo.InvariantCulture),
            Projects = list.Projects.Select(p => (ProjectEntryFile?)new ProjectEntryFile
            {
                Id = p.Id.ToString("D", CultureInfo.InvariantCulture),
                Path = p.Path,
                Installed = p.Installed is null ? null : new InstalledFile
                {
                    FileName = p.Installed.FileName,
                    Length = p.Installed.Length,
                    LastWriteTimeUtc = FormatTime(p.Installed.LastWriteTimeUtc),
                },
            }).ToList(),
        };
        JsonFileStore.Save(FilePath, SchemaVersion, file);
    }

    // The time is stored as round-trip text ("O", 7 fractional digits = every tick) and always in UTC, so it comes back exactly and as UTC.
    private static string FormatTime(DateTime time)
    {
        var utc = time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
            _ => time,
        };
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    private static bool TryParseTime(string? text, out DateTime time)
    {
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time)
            && time.Kind == DateTimeKind.Utc;
    }

    // Every member of the file is nullable: System.Text.Json delivers JSON null and missing properties as null, whatever the annotations say.
    private static ProjectList Repair(ProjectListFile file)
    {
        var projects = new List<ProjectListEntry>();
        var seenIds = new HashSet<Guid>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in file.Projects ?? [])
        {
            if (raw is null
                || !Guid.TryParse(raw.Id, out var id)
                || id == Guid.Empty
                || ProjectListEdits.NormalizePath(raw.Path) is not { } path
                || seenIds.Contains(id)
                || seenPaths.Contains(path))
            {
                continue;
            }

            seenIds.Add(id);
            seenPaths.Add(path);
            projects.Add(new ProjectListEntry(id, path, RepairInstalled(raw.Installed)));
        }

        Guid? active = Guid.TryParse(file.ActiveId, out var storedActive) && seenIds.Contains(storedActive)
            ? storedActive
            : projects.Count == 0 ? null : projects[0].Id;
        return new ProjectList(projects, active);
    }

    private static InstalledPack? RepairInstalled(InstalledFile? raw)
    {
        // The file name later decides which file in the game folder the tool may replace or delete, so a hand-edited path never passes.
        if (raw is { FileName: { } fileName, Length: >= 0 }
            && PackNameRules.Check(fileName) is null
            && TryParseTime(raw.LastWriteTimeUtc, out var time))
        {
            return new InstalledPack(fileName, raw.Length.Value, time);
        }

        return null;
    }

    // The file format. Separate from the model so that the file can be read leniently. schemaVersion is added by JsonFileStore.
    private sealed class ProjectListFile
    {
        public string? ActiveId { get; set; }
        public List<ProjectEntryFile?>? Projects { get; set; }
    }

    private sealed class ProjectEntryFile
    {
        public string? Id { get; set; }
        public string? Path { get; set; }
        public InstalledFile? Installed { get; set; }
    }

    private sealed class InstalledFile
    {
        public string? FileName { get; set; }
        public long? Length { get; set; }
        public string? LastWriteTimeUtc { get; set; }
    }
}
