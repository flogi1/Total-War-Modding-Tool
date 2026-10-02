// Root namespace on purpose: resolves unqualified from every TotalWarModTool.<Module>.Tests namespace.
namespace TotalWarModTool;

/// <summary>A unique folder under %TEMP%\twmt-tests that is deleted recursively on dispose.</summary>
public sealed class TempDir : IDisposable
{
    private const int DeleteAttempts = 5;

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twmt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>
    /// Best effort: a cleanup error (virus scanner or a test that still holds a file open) must never replace a real assertion failure.
    /// The delete is retried a few times and then given up silently.
    /// </summary>
    public void Dispose()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    return;
                }

                Thread.Sleep(50 * attempt);
            }
        }
    }
}