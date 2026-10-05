namespace TotalWarModTool.Projects;

/// <summary>
/// The temp file and move steps that <see cref="ProjectFileWriter"/> and <see cref="ProjectFileOps"/> share. Knows nothing about the
/// pack folder: the caller has already decided that the target is a safe place.
/// </summary>
internal static class ProjectTempFiles
{
    private const int TempNameAttempts = 5;

    // About one second in total: long enough for a virus scanner to let go, short enough that a held file is reported soon.
    private static readonly int[] MovePausesMilliseconds = [50, 100, 200, 300, 400];

    /// <summary>A fresh 8-character name part for a temp file.</summary>
    public static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Creates <c>target.suffix.tmp</c> with <see cref="FileMode.CreateNew"/> and writes the bytes. Returns its path, or null when every
    /// name was taken. A name that is taken (file or folder) is never touched. A write error after the creation deletes the new file.
    /// </summary>
    public static string? Create(string target, ReadOnlyMemory<byte> content, Func<string> tempSuffix, Action<string>? onUndeletable = null)
    {
        for (var attempt = 0; attempt < TempNameAttempts; attempt++)
        {
            var temp = target + "." + tempSuffix() + ".tmp";
            FileStream stream;
            try
            {
                stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && (File.Exists(temp) || Directory.Exists(temp)))
            {
                continue;   // the name is taken by something that is not ours
            }

            try
            {
                using (stream)
                {
                    stream.Write(content.Span);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (!TryDelete(temp))
                {
                    onUndeletable?.Invoke(temp);   // the caller can name the file that is left
                }

                throw;
            }

            return temp;
        }

        return null;
    }

    /// <summary>Deletes a file that the caller created. True when it is gone; false when it could not be deleted (best effort, never throws).</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Moves <paramref name="source"/> to <paramref name="target"/> and never overwrites. A sharing or lock violation (another program,
    /// for example a virus scanner, holds a file open) is retried a few times with a short pause, but only while
    /// <paramref name="targetTaken"/> says the target is still free: a taken target fails at once. The error of the last try is thrown.
    /// <paramref name="stillSafe"/> (optional) is asked after every pause, right before the next try: when it says false the move is not
    /// tried again. Returns true when the file was moved, false when <paramref name="stillSafe"/> stopped it (nothing was moved).
    /// </summary>
    public static bool MoveWithoutOverwrite(string source, string target, Func<bool> targetTaken, CancellationToken cancellationToken,
        Func<bool>? stillSafe = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(source, target, overwrite: false);
                return true;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < MovePausesMilliseconds.Length && !targetTaken())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(MovePausesMilliseconds[attempt]);
                if (stillSafe is not null && !stillSafe())
                {
                    return false;
                }
            }
        }
    }

    // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33) are the low word of the HResult.
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;
}
