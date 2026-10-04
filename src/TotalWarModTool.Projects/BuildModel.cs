using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects;

/// <summary>
/// A problem that stops a build. <paramref name="RelativeFile"/> is relative to the pack folder (with '\'), empty when the problem is
/// not tied to one file. <paramref name="Line"/> is 0 when it has none. <paramref name="Detail"/> is short technical English, not a UI text.
/// </summary>
public sealed record BuildProblem(string RelativeFile, int Line, string Detail);

/// <summary>
/// Entries are ready for <see cref="PackWriter"/>. An empty <paramref name="Problems"/> list means the plan can be written.
/// <paramref name="Ignored"/> names the files of the pack folder that were left out on purpose (relative to the pack folder, with '\').
/// </summary>
public sealed record BuildPlan(IReadOnlyList<PackWriteEntry> Entries, IReadOnlyList<string> Dependencies, IReadOnlyList<BuildProblem> Problems,
    IReadOnlyList<string> Ignored);

/// <summary>How a build ended: the pack was written, the plan had problems (nothing was written), or writing failed (the previous pack is untouched).</summary>
public enum BuildOutcome
{
    Built,
    HasProblems,
    WriteFailed,
}

/// <summary><paramref name="PackPath"/> is the built pack when <paramref name="Outcome"/> is Built, otherwise null. <paramref name="Detail"/> says why a write failed.</summary>
public sealed record BuildResult(BuildOutcome Outcome, string? PackPath, string? Detail);
