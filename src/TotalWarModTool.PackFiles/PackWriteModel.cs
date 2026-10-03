namespace TotalWarModTool.PackFiles;

/// <summary>
/// One file to put into a pack. <see cref="Path"/> uses '/' or '\'. <see cref="Open"/> is called once, when the entry is written;
/// the stream it returns must give exactly <see cref="Length"/> bytes. The writer closes the stream.
/// </summary>
public sealed record PackWriteEntry(string Path, long Length, Func<Stream> Open);

public enum PackWriteProblem
{
    None,
    InvalidPath,
    PathTooLong,
    DuplicatePath,
    InvalidDependency,
    EntryTooLarge,
    IndexTooLarge,
    SourceUnreadable,
    SourceChanged,
    TargetUnwritable,
}

/// <summary><see cref="Detail"/> is short technical English text, not a UI text; it names the entry path where one is involved.</summary>
public sealed record PackWriteResult(PackWriteProblem Problem, string? Detail);
