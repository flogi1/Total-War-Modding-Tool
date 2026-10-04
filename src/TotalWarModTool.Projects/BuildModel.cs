using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects;

/// <summary>
/// A problem that stops a build. <paramref name="RelativeFile"/> is relative to the pack folder (with '\'), empty when the problem is
/// not tied to one file. <paramref name="Line"/> is 0 when it has none. <paramref name="Detail"/> is short technical English, not a UI text.
/// </summary>
public sealed record BuildProblem(string RelativeFile, int Line, string Detail);

/// <summary>Entries are ready for <see cref="PackWriter"/>. An empty <paramref name="Problems"/> list means the plan can be written.</summary>
public sealed record BuildPlan(IReadOnlyList<PackWriteEntry> Entries, IReadOnlyList<string> Dependencies, IReadOnlyList<BuildProblem> Problems);

public enum BuildOutcome
{
    Built,
    HasProblems,
    WriteFailed,
}

/// <summary><paramref name="PackPath"/> is the built pack when <paramref name="Outcome"/> is Built, otherwise null. <paramref name="Detail"/> says why a write failed.</summary>
public sealed record BuildResult(BuildOutcome Outcome, string? PackPath, string? Detail);
