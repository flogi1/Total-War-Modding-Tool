using TotalWarModTool.Storage;

namespace TotalWarModTool.App;

/// <summary>Builds the log warning for a stored file (settings, game settings, session) that could not be used as it is.</summary>
public static class SettingsLoadMessage
{
    /// <summary>The warning for <paramref name="status"/>, or null when there is nothing to report (loaded, or no file yet).</summary>
    /// <param name="badCopyPath">Where the broken file was copied to; null if no copy was made. Only a corrupt file is ever copied.</param>
    /// <param name="what">Names the kind of file at the start of the message.</param>
    public static string? Build(string filePath, LoadStatus status, string? badCopyPath, string what = "Settings file")
    {
        if (status is LoadStatus.Loaded or LoadStatus.Missing)
        {
            return null;
        }

        var message = $"{what} {filePath}: {status}.";
        if (status != LoadStatus.Corrupt)
        {
            // Unreadable and NewerVersion are never copied and, in this session, never overwritten (the auto-saver does not write).
            return message;
        }

        // A corrupt file is replaced by the first change of a setting, so the log must say whether the user still has a copy.
        return badCopyPath is null
            ? $"{message} No backup copy of the broken file could be created. The file will be overwritten on the first change."
            : $"{message} The broken file was copied to {badCopyPath}.";
    }
}
