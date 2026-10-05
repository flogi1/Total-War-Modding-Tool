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

/// <summary>What <see cref="ProjectFileWriter.Probe"/> found.</summary>
public enum FileProbeState
{
    /// <summary>The file exists; the fingerprint is set.</summary>
    Present,

    /// <summary>The file does not exist (nor a folder on the way).</summary>
    Missing,

    /// <summary>It cannot be told: unsafe path, a link on the way, a folder in the place of the file, or a file system error.</summary>
    Unknown,
}

/// <summary>The outcome of <see cref="ProjectFileWriter.Probe"/>. <paramref name="Fingerprint"/> is set for Present only; <paramref name="Detail"/> (short technical English) for Unknown only.</summary>
public sealed record FileProbe(FileProbeState State, FileFingerprint? Fingerprint, string? Detail);

/// <summary>
/// Writes one text file below the pack folder of a mod project: atomically, never outside the pack folder and never through a link.
/// This is the only door by which the editing features write a project file.
/// </summary>
public static class ProjectFileWriter
{
    // Strict on purpose: a lone surrogate is an error, never a U+FFFD in the file.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Writes <paramref name="text"/> as UTF-8 without BOM to pack\&lt;relativeFile&gt; of the project, atomically (temp file next to the
    /// target, then replace). The text is written as it comes, line endings included; text that is not well-formed UTF-16 (a lone
    /// surrogate) is refused as <see cref="FileWriteProblem.Failed"/>. The path is checked by <see cref="PackFolderPaths.ResolveForWrite"/>.
    /// The folder of the file must exist; no folder is created. A missing file is created. The temp file is created new (a file that
    /// already has that name is never overwritten or deleted; another name is tried). Never throws for the file system; only argument
    /// errors are thrown. In every case other than success the old file is unchanged and no temp file of this write is left.
    /// </summary>
    public static FileWriteResult WriteText(string projectFolder, string relativeFile, string text) =>
        WriteText(projectFolder, relativeFile, text, ProjectTempFiles.NewSuffix);

    /// <summary>The overload with the source of the 8-character temp name part, so that a test can provoke a name collision.</summary>
    internal static FileWriteResult WriteText(string projectFolder, string relativeFile, string text, Func<string> tempSuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);
        ArgumentNullException.ThrowIfNull(text);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (check.Problem == PackPathProblem.UnsafePath)
        {
            return Failure(FileWriteProblem.UnsafePath, check.Detail);
        }

        if (check.Problem == PackPathProblem.Link)
        {
            return Failure(FileWriteProblem.Link, check.Detail ?? $"links are not supported: the path passes through the link '{check.LinkPath}'");
        }

        byte[] bytes;
        try
        {
            bytes = Utf8NoBom.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            return Failure(FileWriteProblem.Failed, "the text is not well-formed UTF-16 (it has an unpaired surrogate character); nothing was written");
        }

        var target = check.FullPath!;
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory))
        {
            return Failure(FileWriteProblem.Failed, "the folder of the file does not exist: " + Path.GetRelativePath(projectFolder, directory));
        }

        string? temp = null;   // set only for a temp file that this call created: only that one is ever deleted
        var replaced = false;
        try
        {
            try
            {
                temp = ProjectTempFiles.Create(target, bytes, tempSuffix);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failure(FileWriteProblem.Failed, "the temp file could not be written: " + ex.Message);
            }

            if (temp is null)
            {
                return Failure(FileWriteProblem.Failed, "no free name for the temp file was found");
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
            if (temp is not null && !replaced)
            {
                ProjectTempFiles.TryDelete(temp);   // best effort; the problem that is reported is the one that matters
            }
        }

        return new FileWriteResult(FileWriteProblem.None, null, FileFingerprint.TryRead(target));
    }

    /// <summary>
    /// The fingerprint of pack\&lt;relativeFile&gt;, or null when the file does not exist or cannot be asked. Also null for an unsafe path
    /// (<see cref="PackFolderPaths.ResolveForWrite"/>) or a path through a link: nothing is read through a link.
    /// </summary>
    public static FileFingerprint? Fingerprint(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        return check.Problem == PackPathProblem.None ? FileFingerprint.TryRead(check.FullPath!) : null;
    }

    /// <summary>
    /// Like <see cref="Fingerprint"/>, but tells "the file is not there" from "it cannot be told". Only argument errors are thrown.
    /// </summary>
    /// <returns>
    /// <see cref="FileProbeState.Present"/> with the fingerprint; <see cref="FileProbeState.Missing"/> when the file, a folder on the way or the
    /// pack folder does not exist; <see cref="FileProbeState.Unknown"/> for an unsafe path, a path through a link, a folder in the place of the
    /// file, or any file system error (access denied, other IO error), with a short technical English detail.
    /// </returns>
    public static FileProbe Probe(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (check.Problem == PackPathProblem.UnsafePath)
        {
            return new FileProbe(FileProbeState.Unknown, null, check.Detail ?? "the path is not a plain file path below the pack folder");
        }

        if (check.Problem == PackPathProblem.Link)
        {
            return new FileProbe(FileProbeState.Unknown, null, check.Detail ?? $"links are not supported: the path passes through the link '{check.LinkPath}'");
        }

        var path = check.FullPath!;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                return new FileProbe(FileProbeState.Present, new FileFingerprint(path, info.Length, info.LastWriteTimeUtc), null);
            }

            // FileInfo.Exists is false for every error too, so the reason is asked for: only "not found" means missing.
            File.GetAttributes(path);
            return Directory.Exists(path)
                ? new FileProbe(FileProbeState.Unknown, null, "a folder is in the place of the file")
                : new FileProbe(FileProbeState.Unknown, null, "the file state changed while it was asked");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new FileProbe(FileProbeState.Missing, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new FileProbe(FileProbeState.Unknown, null, "the file cannot be asked: " + ex.Message);
        }
    }

    private static FileWriteResult Failure(FileWriteProblem problem, string? detail) => new(problem, detail, null);
}
