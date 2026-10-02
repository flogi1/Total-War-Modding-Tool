// Root namespace on purpose: resolves unqualified from every TotalWarModTool.<Module>.Tests namespace.
namespace TotalWarModTool;

public static class RepoRoot
{
    /// <summary>Walks up from the test output folder to the folder that contains TotalWarModTool.slnx.</summary>
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "TotalWarModTool.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("TotalWarModTool.slnx not found above " + AppContext.BaseDirectory);
    }
}