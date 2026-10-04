using System.IO;
using TotalWarModTool.Projects;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// One project of the list as the App sees it. <paramref name="Project"/> is null when the project file could not be loaded
/// (or belongs to another game); <paramref name="Problem"/> and <paramref name="Detail"/> say why. <paramref name="Detail"/> is short
/// technical English, not a UI text.
/// </summary>
public sealed record ProjectState(Guid Id, string Folder, ModProject? Project, ProjectProblem Problem, string? Detail, InstalledPack? Installed)
{
    /// <summary>The name of the project; the last segment of the folder path when the project could not be loaded.</summary>
    public string DisplayName => Project?.Name ?? LastSegment(Folder);

    public bool IsUsable => Project is not null;

    private static string LastSegment(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return name.Length == 0 ? folder : name;
    }
}

/// <summary>
/// Outcome of a change to the project list. With <paramref name="Ok"/> false, <paramref name="Message"/> is a ready UI text; with true it is
/// null or a note. <paramref name="Warning"/> is a ready UI text for a message box when something happened that the user must not miss
/// although the action succeeded: the data folder was changed, but the project list could not be saved.
/// </summary>
public sealed record ProjectActionResult(bool Ok, string? Message, string? Warning = null);

/// <summary>What importing a pack would do. <paramref name="Message"/> is set (and <paramref name="Preview"/> null) when the pack cannot be read.</summary>
public sealed record ImportPreviewResult(ImportPreview? Preview, string? Message);

/// <summary>
/// Outcome of an import. With <paramref name="Ok"/> false, <paramref name="Message"/> says why (a problem, a cancel); the list is unchanged.
/// With true it summarises the import. <paramref name="Report"/> is the report of the library when the import ran to its end.
/// </summary>
public sealed record ImportOutcome(bool Ok, string? Message, ImportReport? Report, Guid? ProjectId);

/// <summary>
/// Outcome of a build, optionally with the install. <paramref name="Built"/> is true when the pack was written; <paramref name="Installed"/>
/// when it was also copied into the data folder. <paramref name="Problems"/> are the build problems (nothing was written then).
/// <paramref name="Message"/> is a ready UI text: the summary on success, otherwise what went wrong. <paramref name="Warning"/> is set
/// when the data folder was changed but the project list could not be saved (a ready UI text for a message box).
/// </summary>
public sealed record BuildReport(bool Built, bool Installed, string? PackPath, IReadOnlyList<BuildProblem> Problems,
    IReadOnlyList<string> Ignored, string? Message, string? Warning = null);

/// <summary>
/// A project file that came back as a tab after a restart. <paramref name="Name"/> is the name of the project, already safe to show
/// (control characters escaped), for <see cref="TabSource.Name"/>.
/// </summary>
public sealed record RestoredProjectTab(Guid ProjectId, string Name, OpenedEntry Entry);

/// <summary>
/// What of the project part of a stored session could be brought back: the file lists of the projects that were open and the tabs of
/// project files, in the order they were stored.
/// </summary>
public sealed record ProjectRestoreResult(IReadOnlyList<(Guid Id, ProjectFileList Files)> Expanded, IReadOnlyList<RestoredProjectTab> Tabs);
