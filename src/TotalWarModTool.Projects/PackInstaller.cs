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
/// <see cref="CheckOwnership"/> returned <see cref="InstallProblem.None"/>. The caller records the result in the project list.
/// </summary>
public static class PackInstaller
{
    private const string TempSuffix = ".twmt-tmp";
    private const int BufferSize = 81920;

    /// <summary>Pure check: <see cref="InstallProblem.None"/> when <see cref="Install"/> or <see cref="Uninstall"/> may touch the target file.</summary>
    public static InstallProblem CheckOwnership(InstallRequest request)
    {
        return Check(request, out _);
    }

    /// <summary>
    /// Copies <paramref name="builtPack"/> to the target in the data folder (temp file next to it, then replace). On a problem nothing
    /// changes and the old target stays. Throws <see cref="OperationCanceledException"/> after removing the temp file.
    /// </summary>
    public static InstallResult Install(string builtPack, InstallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problem = Check(request, out var detail);
        if (problem != InstallProblem.None)
        {
            return Failed(problem, detail);
        }

        if (string.IsNullOrEmpty(builtPack) || !File.Exists(builtPack))
        {
            return Failed(InstallProblem.NoBuiltPack, "The built pack does not exist.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The name passed PackNameRules.Check, so it holds no separator and both paths stay inside the data folder.
        var target = Path.Combine(request.DataFolder, request.PackName);
        var temp = target + TempSuffix;
        var moved = false;
        try
        {
            try
            {
                using var source = new FileStream(builtPack, FileMode.Open, FileAccess.Read, FileShare.Read);
                // A stale temp file of a crashed run is the tool's own name and is overwritten.
                using var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[BufferSize];
                int read;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    destination.Write(buffer, 0, read);
                }

                destination.Flush(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed(InstallProblem.TargetUnwritable, "The pack could not be copied into the data folder: " + ex.Message);
            }

            try
            {
                AtomicFile.Replace(temp, target);
                moved = true;
                var info = new FileInfo(target);
                return new InstallResult(InstallProblem.None, null, new InstalledPack(request.PackName, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed(InstallProblem.TargetUnwritable, "The target pack could not be replaced (is the game running?): " + ex.Message);
            }
        }
        finally
        {
            if (!moved)
            {
                TryDelete(temp);
            }
        }
    }

    /// <summary>Deletes the target file from the data folder. A file that is already gone is not a problem.</summary>
    public static InstallResult Uninstall(InstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problem = Check(request, out var detail);
        if (problem != InstallProblem.None)
        {
            return Failed(problem, detail);
        }

        var target = Path.Combine(request.DataFolder, request.PackName);
        if (!File.Exists(target))
        {
            return new InstallResult(InstallProblem.None, null, null);
        }

        try
        {
            File.Delete(target);
            return new InstallResult(InstallProblem.None, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(InstallProblem.TargetUnwritable, "The pack could not be deleted (is the game running?): " + ex.Message);
        }
    }

    private static InstallProblem Check(InstallRequest request, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(request);
        detail = null;

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

    private static InstallResult Failed(InstallProblem problem, string? detail)
    {
        return new InstallResult(problem, detail, null);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the error that led here is the one that is reported. A stale temp file is overwritten by the next install.
        }
    }
}
