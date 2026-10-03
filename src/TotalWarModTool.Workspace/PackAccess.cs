using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace;

/// <summary>Reads pack indexes for the app. A pack that another program (the game, a virus scanner) holds locked is tried again.</summary>
public static class PackAccess
{
    public const int DefaultAttempts = 3;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Reads the index. Only <see cref="PackProblem.Unreadable"/> (locked or access denied) is tried again, up to
    /// <paramref name="attempts"/> times in total, waiting <paramref name="retryDelay"/> on the token between tries.
    /// Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static PackIndexResult ReadIndex(string path, CancellationToken cancellationToken, int attempts = DefaultAttempts, TimeSpan? retryDelay = null) =>
        ReadIndex(path, cancellationToken, attempts, retryDelay ?? DefaultRetryDelay, PackReader.ReadIndex);

    internal static PackIndexResult ReadIndex(string path, CancellationToken cancellationToken, int attempts, TimeSpan retryDelay,
        Func<string, PackIndexResult> read)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = read(path);
            if (result.Problem != PackProblem.Unreadable || attempt >= attempts)
            {
                return result;
            }

            // The wait ends early when the token is cancelled; the check right after turns that into the exception.
            cancellationToken.WaitHandle.WaitOne(retryDelay);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
