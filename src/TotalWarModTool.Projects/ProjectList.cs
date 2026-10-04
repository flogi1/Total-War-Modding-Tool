namespace TotalWarModTool.Projects;

/// <summary>What the tool installed into the data folder for a project: the file name and the state of the file right after the install.</summary>
public sealed record InstalledPack(string FileName, long Length, DateTime LastWriteTimeUtc);

/// <summary>One project of the list: its stable id, the normalised absolute path of its folder and the last install, if any.</summary>
public sealed record ProjectListEntry(Guid Id, string Path, InstalledPack? Installed);

/// <summary>
/// The projects of one game. <paramref name="ActiveId"/> is null only when the list is empty. Record equality compares
/// <paramref name="Projects"/> by reference, not by content.
/// </summary>
public sealed record ProjectList(IReadOnlyList<ProjectListEntry> Projects, Guid? ActiveId)
{
    public static ProjectList Empty { get; } = new([], null);
}

public enum ProjectListProblem { None, DuplicateId, DuplicatePath, InvalidPath, UnknownId }

/// <summary>Result of an edit. On a problem <paramref name="List"/> is the unchanged input.</summary>
public sealed record ProjectListEdit(ProjectList List, ProjectListProblem Problem);

/// <summary>Pure edits of a <see cref="ProjectList"/>. On a problem the returned list is the unchanged input.</summary>
public static class ProjectListEdits
{
    /// <summary>Adds a project at the end. The first project of an empty list becomes active. Throws <see cref="ArgumentException"/> for <see cref="Guid.Empty"/>.</summary>
    public static ProjectListEdit Add(ProjectList list, Guid id, string path)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The id of a project must not be empty.", nameof(id));
        }

        var normalised = NormalizePath(path);
        if (normalised is null)
        {
            return new ProjectListEdit(list, ProjectListProblem.InvalidPath);
        }

        if (list.Projects.Any(p => p.Id == id))
        {
            return new ProjectListEdit(list, ProjectListProblem.DuplicateId);
        }

        if (list.Projects.Any(p => string.Equals(p.Path, normalised, StringComparison.OrdinalIgnoreCase)))
        {
            return new ProjectListEdit(list, ProjectListProblem.DuplicatePath);
        }

        var projects = list.Projects.Append(new ProjectListEntry(id, normalised, null)).ToList();
        return new ProjectListEdit(new ProjectList(projects, list.ActiveId ?? id), ProjectListProblem.None);
    }

    /// <summary>Removes a project. When it was the active one, the first remaining project becomes active (null if none remains).</summary>
    public static ProjectListEdit Remove(ProjectList list, Guid id)
    {
        if (list.Projects.All(p => p.Id != id))
        {
            return new ProjectListEdit(list, ProjectListProblem.UnknownId);
        }

        var projects = list.Projects.Where(p => p.Id != id).ToList();
        var active = list.ActiveId == id ? (projects.Count == 0 ? (Guid?)null : projects[0].Id) : list.ActiveId;
        return new ProjectListEdit(new ProjectList(projects, active), ProjectListProblem.None);
    }

    public static ProjectListEdit SetActive(ProjectList list, Guid id)
    {
        return list.Projects.Any(p => p.Id == id)
            ? new ProjectListEdit(list with { ActiveId = id }, ProjectListProblem.None)
            : new ProjectListEdit(list, ProjectListProblem.UnknownId);
    }

    /// <summary>
    /// Records (or, with null, clears) what was installed for a project. Throws <see cref="ArgumentException"/> for a record that could not
    /// survive save and load: a file name that is not a valid pack name, a negative length or a time that is not of kind UTC.
    /// </summary>
    public static ProjectListEdit SetInstalled(ProjectList list, Guid id, InstalledPack? installed)
    {
        if (installed is not null)
        {
            if (PackNameRules.Check(installed.FileName) is { } reason)
            {
                throw new ArgumentException("The installed file name is not a valid pack name: " + reason, nameof(installed));
            }

            if (installed.Length < 0)
            {
                throw new ArgumentException("The installed length must not be negative.", nameof(installed));
            }

            if (installed.LastWriteTimeUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The installed time must be of kind UTC.", nameof(installed));
            }
        }

        if (list.Projects.All(p => p.Id != id))
        {
            return new ProjectListEdit(list, ProjectListProblem.UnknownId);
        }

        var projects = list.Projects.Select(p => p.Id == id ? p with { Installed = installed } : p).ToList();
        return new ProjectListEdit(list with { Projects = projects }, ProjectListProblem.None);
    }

    /// <summary>
    /// The full path without a trailing separator (with ".." and "." resolved), or null if <paramref name="path"/> is null, empty, not an
    /// absolute path or not a valid path. UNC paths are absolute. This is the one rule by which the list compares and stores folders.
    /// </summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
