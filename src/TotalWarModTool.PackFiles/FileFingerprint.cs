namespace TotalWarModTool.PackFiles;

/// <summary>Identity of a file's state: path, size and last write time. A cache is stale when any of them changes.</summary>
public sealed record FileFingerprint(string Path, long Length, DateTime LastWriteTimeUtc)
{
    /// <summary>Returns null when the file is missing or not accessible.</summary>
    public static FileFingerprint? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileFingerprint(path, info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
