using System.IO;

namespace TotalWarModTool.App;

/// <summary>
/// The folders the app works in. All file access of the app starts from one of these roots, so tests can use a temp folder instead.
/// </summary>
/// <param name="RoamingRoot">User data (settings): <c>%APPDATA%\TotalWarModTool</c>.</param>
/// <param name="LocalRoot">Large or machine-bound data (logs): <c>%LocalAppData%\TotalWarModTool</c>.</param>
/// <param name="ProgramRoot">The program folder, read-only at run time (locale files).</param>
public sealed record AppPaths(string RoamingRoot, string LocalRoot, string ProgramRoot)
{
    public const string AppFolderName = "TotalWarModTool";

    public static AppPaths Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName),
        AppContext.BaseDirectory);

    public string LogDirectory => Path.Combine(LocalRoot, "logs");

    public string LocalesDirectory => Path.Combine(ProgramRoot, "locales");
}
