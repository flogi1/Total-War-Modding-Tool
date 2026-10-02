namespace TotalWarModTool.Storage;

/// <summary>Atomic text file writes: a crash while writing never leaves a half-written target file.</summary>
public static class AtomicFile
{
    private const int MoveAttempts = 15;

    /// <summary>
    /// Writes <paramref name="contents"/> to a temp file next to <paramref name="path"/> and then replaces the target in one step.
    /// Missing directories are created. If the replace still fails after all retries, the temp file is deleted and the error is rethrown.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        var moved = false;
        try
        {
            File.WriteAllText(temp, contents);
            MoveWithRetry(temp, path);
            moved = true;
        }
        finally
        {
            if (!moved)
            {
                TryDelete(temp);
            }
        }
    }

    /// <summary>
    /// On Windows the replace fails briefly with <see cref="UnauthorizedAccessException"/> or <see cref="IOException"/> while another
    /// process (virus scanner, indexer) holds the target open without delete sharing. The state is over after a few milliseconds,
    /// so the move is retried with a growing pause before the error is passed on.
    /// </summary>
    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException && attempt < MoveAttempts)
            {
                Thread.Sleep(10 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Cleanup is best effort; the original error is the one that matters.
        }
    }
}