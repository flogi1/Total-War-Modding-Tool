namespace TotalWarModTool.Logging;

/// <summary>Keeps the user name out of the log by replacing the user profile path with a placeholder.</summary>
public static class PathRedactor
{
    private const string Placeholder = "%USERPROFILE%";

    /// <summary>
    /// Replaces every occurrence of <paramref name="userProfilePath"/> (case-insensitive) with <c>%USERPROFILE%</c>.
    /// A null or empty profile path leaves the text unchanged.
    /// </summary>
    public static string Redact(string text, string? userProfilePath)
    {
        if (string.IsNullOrEmpty(userProfilePath))
        {
            return text;
        }

        return text.Replace(userProfilePath, Placeholder, StringComparison.OrdinalIgnoreCase);
    }
}
