namespace TotalWarModTool.Projects;

/// <summary>
/// One mod project: <paramref name="Id"/> is its stable identity (the folder is only an attribute), <paramref name="GameId"/> the id
/// of the game profile, <paramref name="PackName"/> the file name of the mod pack that is built from the project and
/// <paramref name="Dependencies"/> the file names of the packs the mod depends on.
/// </summary>
public sealed record ModProject(Guid Id, string Name, string GameId, string PackName, IReadOnlyList<string> Dependencies);

public enum ProjectProblem
{
    None,
    NotFound,
    Unreadable,
    Corrupt,
    NewerVersion,
    Invalid,
    FolderNotEmpty,
    InvalidPackName,
    Unwritable,
}

/// <summary>Outcome of a project file operation. <paramref name="Project"/> is null unless <paramref name="Problem"/> is None. <paramref name="Detail"/> is short technical English, not a UI text.</summary>
public sealed record ProjectResult(ModProject? Project, ProjectProblem Problem, string? Detail);
