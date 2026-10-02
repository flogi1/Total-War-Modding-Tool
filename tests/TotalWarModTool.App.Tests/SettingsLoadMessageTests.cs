using TotalWarModTool.Storage;

namespace TotalWarModTool.App.Tests;

public class SettingsLoadMessageTests
{
    private const string SettingsFile = @"C:\data\settings.json";

    [Fact] public void Corrupt_file_with_a_backup_names_the_copy()
    {
        var message = SettingsLoadMessage.Build(SettingsFile, LoadStatus.Corrupt, SettingsFile + ".bad");
        Assert.Contains(SettingsFile, message);
        Assert.Contains("Corrupt", message);
        Assert.Contains(SettingsFile + ".bad", message);
        Assert.DoesNotContain("No backup copy", message);
    }

    // Without a copy the first change of a setting overwrites the broken file, so the log has to say that nothing of it is kept.
    [Fact] public void Corrupt_file_without_a_backup_says_that_no_copy_could_be_created()
    {
        var message = SettingsLoadMessage.Build(SettingsFile, LoadStatus.Corrupt, null);
        Assert.Contains(SettingsFile, message);
        Assert.Contains("Corrupt", message);
        Assert.Contains("No backup copy of the broken file could be created", message);
        Assert.Contains("overwritten on the first change", message);
    }

    // These two never get a copy, and the auto-saver does not write in such a session: they must not claim an overwrite.
    [Theory]
    [InlineData(LoadStatus.Unreadable)]
    [InlineData(LoadStatus.NewerVersion)]
    public void Other_problems_name_the_status_and_make_no_claim_about_a_backup(LoadStatus status)
    {
        var message = SettingsLoadMessage.Build(SettingsFile, status, null);
        Assert.Contains(SettingsFile, message);
        Assert.Contains(status.ToString(), message);
        Assert.DoesNotContain("backup", message);
        Assert.DoesNotContain("overwritten", message);
    }

    [Theory]
    [InlineData(LoadStatus.Loaded)]
    [InlineData(LoadStatus.Missing)]
    public void Nothing_to_report_when_the_file_was_loaded_or_does_not_exist(LoadStatus status) =>
        Assert.Null(SettingsLoadMessage.Build(SettingsFile, status, null));
}
