namespace TotalWarModTool.PackFiles;

public enum PackType
{
    Unknown = -1,
    Boot = 0,
    Release = 1,
    Patch = 2,
    Mod = 3,
    Movie = 4,
}

public enum PackProblem
{
    None,
    NotFound,
    Unreadable,
    Truncated,
    UnknownFormat,
    EncryptedIndex,
    InconsistentIndex,
}

/// <summary>The 28-byte pack header. Counts and sizes are the unsigned 32-bit values of the file, widened to long.</summary>
public sealed record PackHeader(
    string Magic,
    PackType Type,
    bool HasIndexTimestamps,
    bool HasEncryptedIndex,
    bool HasEncryptedData,
    long DependencyCount,
    long DependencyIndexSize,
    long EntryCount,
    long EntryIndexSize);

/// <summary>Header or a problem. <see cref="Detail"/> is short technical English text, not a UI text.</summary>
public sealed record PackHeaderResult(PackHeader? Header, PackProblem Problem, string? Detail);

/// <summary>One file in a pack. <see cref="Offset"/> is the absolute position in the pack file; the path uses '/'.</summary>
public sealed record PackEntry(string Path, long Offset, long StoredSize, bool IsCompressed);
