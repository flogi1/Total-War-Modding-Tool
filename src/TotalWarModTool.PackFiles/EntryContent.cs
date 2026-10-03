namespace TotalWarModTool.PackFiles;

public enum CompressionFormat
{
    None,
    Zstd,
    Lz4,
    Lzma,
    Unknown,
}

public enum EntryProblem
{
    None,
    PackChanged,
    Unreadable,
    Encrypted,
    UnsupportedCompression,
    Corrupt,
    TooLarge,
}

/// <summary>
/// The bytes of one pack entry or a problem. <see cref="UncompressedSize"/> is set as soon as it is known, even when
/// <see cref="Bytes"/> is null. <see cref="Detail"/> is short technical English text, not a UI text.
/// </summary>
public sealed record EntryContent(byte[]? Bytes, CompressionFormat Format, long UncompressedSize, EntryProblem Problem, string? Detail);

/// <summary>What the first bytes of an entry say, without reading its content.</summary>
public sealed record EntryInfo(CompressionFormat Format, long UncompressedSize, EntryProblem Problem, string? Detail);
