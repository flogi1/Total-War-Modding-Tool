// Root namespace on purpose: resolves unqualified from every TotalWarModTool.<Module>.Tests namespace.
namespace TotalWarModTool;

/// <summary>A unique folder under %TEMP%\twmt-tests that is deleted recursively on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twmt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }
}