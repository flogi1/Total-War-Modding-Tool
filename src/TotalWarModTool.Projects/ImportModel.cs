namespace TotalWarModTool.Projects;

/// <summary>An entry of the pack that is not part of the project. <paramref name="Reason"/> is short technical English, not a UI text.</summary>
public sealed record SkippedEntry(string PackPath, string Reason);

/// <summary>A table or loc entry that is in the project as a binary file. <paramref name="Reason"/> says why it could not become text.</summary>
public sealed record BinaryTable(string PackPath, string Reason);

/// <summary>What an import would do, counted by path only; nothing is read from the data section of the pack.</summary>
public sealed record ImportPreview(int Tables, int LocFiles, int OtherFiles, long StoredBytes, IReadOnlyList<SkippedEntry> Skipped);

public enum ImportProblem
{
    None,
    SourceUnreadable,
    InvalidPackName,
    TargetNotEmpty,
    TargetUnwritable,

    /// <summary>Tables of the pack are in another version than the game uses; nothing was written. Detail lists them.</summary>
    OutdatedTables,
}

/// <summary>
/// Outcome of an import. <paramref name="Project"/> is null unless <paramref name="Problem"/> is None. <paramref name="Tables"/> and
/// <paramref name="LocFiles"/> count only the files written as text, <paramref name="OtherFiles"/> every file written as it is.
/// <paramref name="StayedBinary"/> lists the tables and loc files that are in the project as binary files.
/// <paramref name="DroppedDependencies"/> are the dependency names of the source that are not in the project because they are empty,
/// blank or not valid Unicode.
/// </summary>
public sealed record ImportReport(
    ModProject? Project,
    ImportProblem Problem,
    string? Detail,
    int Tables,
    int LocFiles,
    int OtherFiles,
    IReadOnlyList<BinaryTable> StayedBinary,
    IReadOnlyList<SkippedEntry> Skipped,
    IReadOnlyList<string> DroppedDependencies);

/// <summary>
/// What to import. <paramref name="TargetFolder"/> must not exist or must be empty. <paramref name="VanillaPackNames"/> are the pack
/// names the mod must not use; the caller supplies a set that ignores case.
/// </summary>
public sealed record ImportRequest(
    string SourcePack,
    string TargetFolder,
    string ProjectName,
    string GameId,
    string PackName,
    IReadOnlySet<string> VanillaPackNames);
