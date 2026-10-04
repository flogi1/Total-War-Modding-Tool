using TotalWarModTool.Storage;

namespace TotalWarModTool.Projects;

/// <summary>Why <see cref="PackInstaller"/> did not touch the data folder (or <see cref="None"/>).</summary>
public enum InstallProblem
{
    None,
    InvalidPackName,
    VanillaName,
    NoBuiltPack,
    DataFolderMissing,
    ForeignFile,
    TargetUnwritable,
}

/// <summary>
/// Result of an install or uninstall. <paramref name="Installed"/> is the new record for the project list: set after an install,
/// null after an uninstall or on a problem. <paramref name="Detail"/> is a short technical English text on a problem.
/// </summary>
public sealed record InstallResult(InstallProblem Problem, string? Detail, InstalledPack? Installed);

/// <summary>
/// What an install or uninstall acts on. <paramref name="Recorded"/> is what the project list holds for this project (null when
/// the tool never installed it). <paramref name="VanillaPackNames"/> are the pack names that are never allowed.
/// </summary>
public sealed record InstallRequest(string DataFolder, string PackName, InstalledPack? Recorded, IReadOnlySet<string> VanillaPackNames);

/// <summary>
/// The only code that writes into the game's data folder. It creates, replaces or deletes exactly
/// <c>&lt;DataFolder&gt;\&lt;PackName&gt;</c> and <c>&lt;DataFolder&gt;\&lt;PackName&gt;.twmt-tmp</c>, and only after
/// <see cref="CheckOwnership"/> returned <see cref="InstallProblem.None"/>. The ownership is decided once: no later step acts
/// on a fresh look at the disk, except the one re-check right before an owned file is replaced. The window between that
/// check and the replace cannot be closed on Windows. The caller records the result in the project list.
/// </summary>
public static class PackInstaller
{
    private const string TempSuffix = ".twmt-tmp";
    private const int BufferSize = 81920;
    private const int MoveAttempts = 15;
    private const int RemoveAttempts = 3;
    private const int RemovePauseMilliseconds = 100;

    // Win32 error codes that say "the destination exists"; they are told apart by HResult, never by message text.
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;

    private enum CopyOutcome
    {
        Copied,
        SourceUnreadable,
        TempUnwritable,
    }

    private enum MoveOutcome
    {
        Moved,
        TargetExists,
        Failed,
    }

    /// <summary>Pure check: <see cref="InstallProblem.None"/> when <see cref="Install"/> or <see cref="Uninstall"/> may touch the target file.</summary>
    public static InstallProblem CheckOwnership(InstallRequest request)
    {
        return Check(request, out _, out _);
    }

    /// <summary>
    /// Copies <paramref name="builtPack"/> to the target in the data folder (temp file next to it, then move or replace). On a problem
    /// nothing changes and the old target stays. A temp file that cannot be removed is named in the detail. Throws
    /// <see cref="OperationCanceledException"/> after trying to remove the temp file; the caller must check that it is gone.
    /// Throws <see cref="ArgumentException"/> when <paramref name="builtPack"/> is the target or the temp file itself.
    /// </summary>
    public static InstallResult Install(string builtPack, InstallRequest request, CancellationToken cancellationToken)
    {
        return Install(builtPack, request, cancellationToken, null);
    }

    /// <summary>The seam for tests: <paramref name="beforeReplace"/> runs after the copy, right before the target is decided on.</summary>
    internal static InstallResult Install(string builtPack, InstallRequest request, CancellationToken cancellationToken, Action? beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problem = Check(request, out var detail, out var targetExists);
        if (problem != InstallProblem.None)
        {
            return Failed(problem, detail);
        }

        // The name passed PackNameRules.Check, so it holds no separator and both paths stay inside the data folder.
        var target = Path.Combine(request.DataFolder, request.PackName);
        var temp = target + TempSuffix;

        if (string.IsNullOrEmpty(builtPack))
        {
            return Failed(InstallProblem.NoBuiltPack, "No built pack was given.");
        }

        try
        {
            var full = Path.GetFullPath(builtPack);
            if (string.Equals(full, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The built pack must not be the target or the temp file of the install.", nameof(builtPack));
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or PathTooLongException)
        {
            return Failed(InstallProblem.NoBuiltPack, "The path of the built pack is not valid: " + ex.Message);
        }

        if (!File.Exists(builtPack))
        {
            return Failed(InstallProblem.NoBuiltPack, "The built pack does not exist.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var finished = false;
        try
        {
            var outcome = CopyToTemp(builtPack, temp, cancellationToken, out var copyDetail);
            if (outcome == CopyOutcome.SourceUnreadable)
            {
                return FailedAndClean(InstallProblem.NoBuiltPack, "The built pack could not be read: " + copyDetail, temp, ref finished);
            }

            if (outcome == CopyOutcome.TempUnwritable)
            {
                return FailedAndClean(InstallProblem.TargetUnwritable, "The pack could not be copied into the data folder: " + copyDetail, temp, ref finished);
            }

            beforeReplace?.Invoke();

            if (targetExists)
            {
                // An owned file is replaced only if it is still the owned one.
                var again = Check(request, out var againDetail, out var stillThere);
                if (again != InstallProblem.None)
                {
                    return FailedAndClean(again, againDetail, temp, ref finished);
                }

                targetExists = stillThere;
            }

            if (targetExists)
            {
                try
                {
                    AtomicFile.Replace(temp, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return FailedAndClean(InstallProblem.TargetUnwritable, "The target pack could not be replaced (is the game running?): " + ex.Message, temp, ref finished);
                }
            }
            else
            {
                var moved = MoveWithoutOverwrite(temp, target, out var moveDetail);
                if (moved == MoveOutcome.TargetExists)
                {
                    return FailedAndClean(InstallProblem.ForeignFile, "A file with the name of the pack appeared in the data folder during the install.", temp, ref finished);
                }

                if (moved == MoveOutcome.Failed)
                {
                    return FailedAndClean(InstallProblem.TargetUnwritable, "The pack could not be moved into place (is the game running?): " + moveDetail, temp, ref finished);
                }
            }

            // The temp file is gone now; the pack is in place.
            finished = true;
            try
            {
                var info = new FileInfo(target);
                return new InstallResult(InstallProblem.None, null, new InstalledPack(request.PackName, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed(InstallProblem.TargetUnwritable, "The pack is in place but its state could not be read: " + ex.Message);
            }
        }
        finally
        {
            if (!finished)
            {
                // Cancel or an unexpected exception: the result cannot carry a note, so the caller has to check.
                RemoveTemp(temp);
            }
        }
    }

    /// <summary>Deletes the target file from the data folder. A file that is not there is not a problem and is not touched.</summary>
    public static InstallResult Uninstall(InstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problem = Check(request, out var detail, out var targetExists);
        if (problem != InstallProblem.None)
        {
            return Failed(problem, detail);
        }

        if (!targetExists)
        {
            return new InstallResult(InstallProblem.None, null, null);
        }

        try
        {
            File.Delete(Path.Combine(request.DataFolder, request.PackName));
            return new InstallResult(InstallProblem.None, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(InstallProblem.TargetUnwritable, "The pack could not be deleted (is the game running?): " + ex.Message);
        }
    }

    /// <summary>
    /// The one decision about ownership. <paramref name="fileExists"/> is what the check saw: true for an owned file, false when the
    /// target is missing (only meaningful when the result is <see cref="InstallProblem.None"/>).
    /// </summary>
    private static InstallProblem Check(InstallRequest request, out string? detail, out bool fileExists)
    {
        ArgumentNullException.ThrowIfNull(request);
        detail = null;
        fileExists = false;

        if (PackNameRules.Check(request.PackName) is { } reason)
        {
            detail = "The pack name is not valid: " + reason;
            return InstallProblem.InvalidPackName;
        }

        // The caller's set may use any comparer, so the comparison is done here.
        if (request.VanillaPackNames.Any(v => string.Equals(v, request.PackName, StringComparison.OrdinalIgnoreCase)))
        {
            detail = "The pack name belongs to a vanilla pack.";
            return InstallProblem.VanillaName;
        }

        if (string.IsNullOrEmpty(request.DataFolder) || !Path.IsPathFullyQualified(request.DataFolder) || !Directory.Exists(request.DataFolder))
        {
            detail = "The data folder does not exist.";
            return InstallProblem.DataFolderMissing;
        }

        var target = Path.Combine(request.DataFolder, request.PackName);
        if (Directory.Exists(target))
        {
            detail = "A folder with the name of the pack exists in the data folder.";
            return InstallProblem.ForeignFile;
        }

        if (!File.Exists(target))
        {
            return InstallProblem.None;
        }

        try
        {
            var info = new FileInfo(target);
            var recorded = request.Recorded;
            if (recorded is not null
                && string.Equals(recorded.FileName, request.PackName, StringComparison.OrdinalIgnoreCase)
                && recorded.Length == info.Length
                && recorded.LastWriteTimeUtc == info.LastWriteTimeUtc)
            {
                fileExists = true;
                return InstallProblem.None;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            detail = "The file in the data folder could not be inspected: " + ex.Message;
            return InstallProblem.ForeignFile;
        }

        detail = "A file with this name exists in the data folder and was not installed by this tool (or was changed since).";
        return InstallProblem.ForeignFile;
    }

    /// <summary>
    /// Deletes a stale temp file, then copies the built pack into a fresh temp file. The token is checked before every block.
    /// Both streams are closed when this returns, so the caller can remove the temp file.
    /// </summary>
    private static CopyOutcome CopyToTemp(string builtPack, string temp, CancellationToken cancellationToken, out string? detail)
    {
        detail = null;
        FileStream source;
        try
        {
            source = new FileStream(builtPack, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            detail = ex.Message;
            return CopyOutcome.SourceUnreadable;
        }

        using (source)
        {
            try
            {
                // A stale temp file of a crashed run carries the tool's own name. File.Delete removes a link itself, not its target.
                File.Delete(temp);
                using var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[BufferSize];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read;
                    try
                    {
                        read = source.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        detail = ex.Message;
                        return CopyOutcome.SourceUnreadable;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    destination.Write(buffer, 0, read);
                }

                destination.Flush(true);
                return CopyOutcome.Copied;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                detail = ex.Message;
                return CopyOutcome.TempUnwritable;
            }
        }
    }

    /// <summary>
    /// Moves the temp file to a target that the ownership check saw as missing, never overwriting. Lock errors of the temp file are
    /// retried; "the target exists" is not.
    /// </summary>
    private static MoveOutcome MoveWithoutOverwrite(string temp, string target, out string? detail)
    {
        detail = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: false);
                return MoveOutcome.Moved;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is ErrorFileExists or ErrorAlreadyExists)
            {
                return MoveOutcome.TargetExists;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= MoveAttempts)
                {
                    detail = ex.Message;
                    return MoveOutcome.Failed;
                }

                Thread.Sleep(10 * attempt);
            }
        }
    }

    private static InstallResult Failed(InstallProblem problem, string? detail)
    {
        return new InstallResult(problem, detail, null);
    }

    /// <summary>Removes the temp file first, so the detail can name it when it stays, then builds the failure result.</summary>
    private static InstallResult FailedAndClean(InstallProblem problem, string? detail, string temp, ref bool finished)
    {
        finished = true;
        if (!RemoveTemp(temp))
        {
            detail += " The temporary file '" + temp + "' could not be removed; delete it by hand.";
        }

        return Failed(problem, detail);
    }

    /// <summary>Tries three times, 100 ms apart (no pause after the last try). True when the file is gone.</summary>
    private static bool RemoveTemp(string path)
    {
        for (var attempt = 1; attempt <= RemoveAttempts; attempt++)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again below; the result is reported by the final check.
            }

            if (!File.Exists(path))
            {
                return true;
            }

            if (attempt < RemoveAttempts)
            {
                Thread.Sleep(RemovePauseMilliseconds);
            }
        }

        return false;
    }
}
