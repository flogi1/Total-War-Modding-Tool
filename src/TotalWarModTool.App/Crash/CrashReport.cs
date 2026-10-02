using System.Globalization;
using System.Runtime.InteropServices;
using TotalWarModTool.Logging;

namespace TotalWarModTool.App;

/// <summary>The text of the crash dialog: what a user can copy into a bug report.</summary>
public static class CrashReport
{
    /// <summary>
    /// Lines: product and version, ISO time, source, OS description, .NET description, an empty line, then the exception with stack trace.
    /// The whole text is passed through <see cref="PathRedactor"/>, so the user name never leaves the machine in a bug report.
    /// </summary>
    /// <param name="source">Where the exception came from, e.g. <see cref="CrashHandler.SourceUi"/>.</param>
    /// <param name="userProfilePath">Replaced by <c>%USERPROFILE%</c> in the text. Null disables the redaction.</param>
    public static string Build(Exception exception, string source, string appVersion, DateTimeOffset now, string? userProfilePath)
    {
        string[] lines =
        [
            $"{AppTexts.AppName} {appVersion}",
            now.ToString("O", CultureInfo.InvariantCulture),
            $"Source: {source}",
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            string.Empty,
            exception.ToString(),
        ];
        return PathRedactor.Redact(string.Join(Environment.NewLine, lines), userProfilePath);
    }
}
