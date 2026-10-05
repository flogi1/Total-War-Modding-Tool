using System.Text;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Storage;

namespace TotalWarModTool.Projects;

/// <summary>Why <see cref="ProjectFileWriter.WriteText"/> did not write.</summary>
public enum FileWriteProblem
{
    /// <summary>The file was written.</summary>
    None,

    /// <summary>The path is empty, rooted, has a '..' segment or is otherwise not a plain file path below the pack folder. Nothing was touched.</summary>
    UnsafePath,

    /// <summary>The pack folder, a folder on the way or the file itself is a link. Nothing was written.</summary>
    Link,

    /// <summary>The existing file could not be replaced (held open by another program, or read-only). The old file is unchanged.</summary>
    Locked,

    /// <summary>Anything else: the folder of the file is missing, the temp file could not be written, the target is a folder.</summary>
    Failed,
}

/// <summary>
/// The outcome of a write. <paramref name="Fingerprint"/> is the state of the file right after the write (Problem None), otherwise null.
/// <paramref name="Detail"/> is short technical English and null on success.
/// </summary>
public sealed record FileWriteResult(FileWriteProblem Problem, string? Detail, FileFingerprint? Fingerprint);

/// <summary>
/// Writes one text file below the pack folder of a mod project: atomically, never outside the pack folder and never through a link.
/// This is the only door by which the editing features write a project file.
/// </summary>
public static class ProjectFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="text"/> as UTF-8 without BOM to pack\&lt;relativeFile&gt; of the project, atomically (temp file next to the
    /// target, then replace). The text is written as it comes, line endings included. The folder of the file must exist; no folder is
    /// created. A missing file is created. Never throws for the file system; only argument errors are thrown. In every case other
    /// than success the old file is unchanged and no temp file of this write is left.
    /// </summary>
    public static FileWriteResult WriteText(string projectFolder, string relativeFile, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);
        ArgumentNullException.ThrowIfNull(text);

        var check = Check(projectFolder, relativeFile);
        if (check.Problem == PackPathProblem.UnsafePath)
        {
            return Failure(FileWriteProblem.UnsafePath, check.Detail);
        }

        if (check.Problem == PackPathProblem.Link)
        {
            return Failure(FileWriteProblem.Link, $"links are not supported: the path passes through the link '{check.LinkPath}'");
        }

        var target = check.FullPath!;
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory))
        {
            return Failure(FileWriteProblem.Failed, "the folder of the file does not exist: " + Path.GetRelativePath(projectFolder, directory));
        }

        var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        var replaced = false;
        try
        {
            try
            {
                File.WriteAllText(temp, text, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failure(FileWriteProblem.Failed, "the temp file could not be written: " + ex.Message);
            }

            try
            {
                AtomicFile.Replace(temp, target);
                replaced = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open or read-only: the file exists and cannot be replaced. Anything else (a folder in its place) is a failure.
                return Failure(File.Exists(target) ? FileWriteProblem.Locked : FileWriteProblem.Failed, "the file could not be replaced: " + ex.Message);
            }
        }
        finally
        {
            if (!replaced)
            {
                TryDelete(temp);
            }
        }

        return new FileWriteResult(FileWriteProblem.None, null, FileFingerprint.TryRead(target));
    }

    /// <summary>
    /// The fingerprint of pack\&lt;relativeFile&gt;, or null when the file does not exist or cannot be asked. Also null for an unsafe path
    /// or a path through a link: nothing is read through a link.
    /// </summary>
    public static FileFingerprint? Fingerprint(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = Check(projectFolder, relativeFile);
        return check.Problem == PackPathProblem.None ? FileFingerprint.TryRead(check.FullPath!) : null;
    }

    // The writer is stricter than the reader: a file name that Windows would not accept as it is (a stream suffix such as "a:b",
    // a reserved device name, a trailing dot or space) is no place to write.
    private static PackFolderPathCheck Check(string projectFolder, string relativeFile)
    {
        var reason = ProjectLayout.UnsafePathReason(relativeFile.Replace('\\', '/'));
        return reason is null
            ? PackFolderPaths.Resolve(projectFolder, relativeFile)
            : new PackFolderPathCheck(PackPathProblem.UnsafePath, null, null, reason);
    }

    private static FileWriteResult Failure(FileWriteProblem problem, string? detail) => new(problem, detail, null);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; the problem that is reported is the one that matters.
        }
    }
}
