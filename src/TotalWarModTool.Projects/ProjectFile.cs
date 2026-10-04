using System.Text;
using System.Text.Json;
using TotalWarModTool.Storage;

namespace TotalWarModTool.Projects;

/// <summary>
/// Reads and writes the project file (twmt-project.json) in the folder of a mod project. A broken file is reported as a problem and is
/// never changed, copied or replaced: it is the user's file, so unlike the settings files it is not repaired behind the user's back.
/// </summary>
public static class ProjectFile
{
    public const int SchemaVersion = 1;
    public const string FileName = "twmt-project.json";
    public const string PackFolder = "pack";
    public const string BuildFolder = "build";

    // The text is decoded strictly: an invalid byte is a problem, not a replacement character.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The largest project file that is read (1 MB). A real project file is a few hundred bytes.</summary>
    private const int MaxFileBytes = 1_048_576;

    /// <summary>Reads the project file of <paramref name="projectFolder"/>. Changes nothing on disk. A file over 1 MB is Corrupt and is not read.</summary>
    public static ProjectResult Load(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        string text;
        try
        {
            using var stream = new FileStream(Path.Combine(projectFolder, FileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes)
            {
                return Fail(ProjectProblem.Corrupt, $"{FileName} is larger than the limit of {MaxFileBytes} bytes (1 MB).");
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            text = StrictUtf8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fail(ProjectProblem.NotFound, FileName + " does not exist in the project folder.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ProjectProblem.Unreadable, FileName + " cannot be read (" + ex.GetType().Name + ").");
        }
        catch (DecoderFallbackException)
        {
            return Fail(ProjectProblem.Corrupt, FileName + " is not valid UTF-8.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text.TrimStart('\uFEFF'));
        }
        catch (JsonException)
        {
            return Fail(ProjectProblem.Corrupt, FileName + " is not valid JSON.");
        }

        using (document)
        {
            return Read(document.RootElement);
        }
    }

    /// <summary>Writes the project file atomically. Problems: Invalid, InvalidPackName, Unwritable.</summary>
    public static ProjectResult Save(string projectFolder, ModProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        if (Validate(project) is { } problem)
        {
            return Fail(problem.Problem, problem.Detail);
        }

        var file = new ProjectFileData
        {
            Id = project.Id.ToString("D"),
            Name = project.Name,
            Game = project.GameId,
            PackName = project.PackName,
            Dependencies = [.. project.Dependencies],
        };

        try
        {
            JsonFileStore.Save(Path.Combine(projectFolder, FileName), SchemaVersion, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ProjectProblem.Unwritable, FileName + " cannot be written (" + ex.GetType().Name + ").");
        }

        return new ProjectResult(project, ProjectProblem.None, null);
    }

    /// <summary>
    /// Creates the folder (if missing), the pack folder and the project file with a new id. The folder may be missing or empty.
    /// Problems: FolderNotEmpty, InvalidPackName, Invalid (empty name or game id), Unwritable.
    /// A failure removes what this call created (the project folder if it was new, else only the pack folder), except parent folders that
    /// had to be created above the project folder: those stay.
    /// </summary>
    public static ProjectResult CreateNew(string projectFolder, string name, string gameId, string packName, IReadOnlyList<string> dependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var project = new ModProject(Guid.NewGuid(), name, gameId, packName, dependencies);
        if (Validate(project) is { } problem)
        {
            return Fail(problem.Problem, problem.Detail);
        }

        var folderExisted = Directory.Exists(projectFolder);
        try
        {
            if (folderExisted && Directory.EnumerateFileSystemEntries(projectFolder).Any())
            {
                return Fail(ProjectProblem.FolderNotEmpty, "The project folder is not empty.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ProjectProblem.Unwritable, "The project folder cannot be read (" + ex.GetType().Name + ").");
        }

        var succeeded = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(projectFolder, PackFolder));
            var saved = Save(projectFolder, project);
            succeeded = saved.Problem == ProjectProblem.None;
            return saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ProjectProblem.Unwritable, "The project folder cannot be created (" + ex.GetType().Name + ").");
        }
        finally
        {
            if (!succeeded)
            {
                RemoveCreated(projectFolder, folderExisted);
            }
        }
    }

    private static ProjectResult Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Fail(ProjectProblem.Corrupt, FileName + " is not a JSON object.");
        }

        if (!root.TryGetProperty("schemaVersion", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out var version)
            || version < 1)
        {
            return Fail(ProjectProblem.Corrupt, "schemaVersion is missing or is not a whole number of at least 1.");
        }

        if (version > SchemaVersion)
        {
            return Fail(ProjectProblem.NewerVersion, $"{FileName} has schemaVersion {version}, this version reads up to {SchemaVersion}.");
        }

        try
        {
            if (!TryGetString(root, "id", out var idText, out var detail)
                || !TryGetString(root, "name", out var name, out detail)
                || !TryGetString(root, "game", out var game, out detail)
                || !TryGetString(root, "packName", out var packName, out detail)
                || !TryGetDependencies(root, out var dependencies, out detail))
            {
                return Fail(ProjectProblem.Invalid, detail);
            }

            if (!Guid.TryParse(idText, out var id))
            {
                return Fail(ProjectProblem.Invalid, "id is not a GUID.");
            }

            var project = new ModProject(id, name, game, packName, dependencies);
            if (Validate(project) is { } problem)
            {
                // A bad pack name in a file is a bad file, not a bad input of the caller.
                return Fail(ProjectProblem.Invalid, problem.Detail);
            }

            return new ProjectResult(project, ProjectProblem.None, null);
        }
        catch (InvalidOperationException)
        {
            // JsonElement refuses to turn an unpaired surrogate escape into a string.
            return Fail(ProjectProblem.Invalid, FileName + " contains text that is not valid Unicode.");
        }
    }

    private static bool TryGetString(JsonElement root, string property, out string value, out string detail)
    {
        value = "";
        detail = "";
        if (!root.TryGetProperty(property, out var element))
        {
            detail = property + " is missing.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            detail = property + " is not a string.";
            return false;
        }

        value = element.GetString() ?? "";
        return true;
    }

    private static bool TryGetDependencies(JsonElement root, out List<string> dependencies, out string detail)
    {
        dependencies = [];
        detail = "";
        if (!root.TryGetProperty("dependencies", out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            detail = "dependencies is not a list.";
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                detail = "dependencies contains an entry that is not a string.";
                return false;
            }

            dependencies.Add(item.GetString() ?? "");
        }

        return true;
    }

    /// <summary>The rules every project has to follow, whether it is read, written or created. Null when the project is fine.</summary>
    private static (ProjectProblem Problem, string Detail)? Validate(ModProject project)
    {
        if (project.Id == Guid.Empty)
        {
            return (ProjectProblem.Invalid, "id is empty.");
        }

        if (string.IsNullOrWhiteSpace(project.Name) || !PackNameRules.IsWellFormed(project.Name))
        {
            return (ProjectProblem.Invalid, "name is empty or not valid Unicode.");
        }

        if (string.IsNullOrWhiteSpace(project.GameId) || !PackNameRules.IsWellFormed(project.GameId))
        {
            return (ProjectProblem.Invalid, "game is empty or not valid Unicode.");
        }

        if (PackNameRules.Check(project.PackName) is { } reason)
        {
            return (ProjectProblem.InvalidPackName, "packName: " + reason);
        }

        if (project.Dependencies.Any(d => string.IsNullOrWhiteSpace(d) || !PackNameRules.IsWellFormed(d)))
        {
            return (ProjectProblem.Invalid, "dependencies contains an empty entry or text that is not valid Unicode.");
        }

        return null;
    }

    /// <summary>Takes back what <see cref="CreateNew"/> created: the whole folder if it was new, else only the pack folder (it was empty).</summary>
    private static void RemoveCreated(string projectFolder, bool folderExisted)
    {
        try
        {
            if (!folderExisted)
            {
                Directory.Delete(projectFolder, recursive: true);
            }
            else
            {
                Directory.Delete(Path.Combine(projectFolder, PackFolder), recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; the original problem is the one that is reported.
        }
    }

    private static ProjectResult Fail(ProjectProblem problem, string detail) => new(null, problem, detail);

    // The file format. Separate from the model so that the file can be read and written under its own names.
    // schemaVersion is added by JsonFileStore.
    private sealed class ProjectFileData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Game { get; set; } = "";
        public string PackName { get; set; } = "";
        public List<string> Dependencies { get; set; } = [];
    }
}
