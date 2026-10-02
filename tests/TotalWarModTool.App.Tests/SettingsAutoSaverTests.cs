using TotalWarModTool.Settings;
using TotalWarModTool.Storage;

namespace TotalWarModTool.App.Tests;

public class SettingsAutoSaverTests
{
    // What a newer app version might have written: a higher schema version and a field this build does not know.
    private const string ForeignFile = "{\"schemaVersion\":2,\"language\":\"de\",\"futureField\":true}";

    [Fact] public void Flush_writes_after_a_change() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, store, new FakeAppLog(), LoadStatus.Loaded);

        settings.Theme = ThemeChoice.Dark;
        saver.Flush();

        Assert.Equal(ThemeChoice.Dark, new GlobalSettingsStore(dir.Path).Load().Settings.Theme);
    });

    [Fact] public void Flush_does_not_write_when_nothing_changed() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var saver = new SettingsAutoSaver(new GlobalSettings(), store, new FakeAppLog(), LoadStatus.Missing);

        saver.Flush();

        Assert.False(File.Exists(store.FilePath));
    });

    [Fact] public void Flush_does_not_rewrite_an_existing_file_when_nothing_changed() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, "{\"schemaVersion\":1,\"language\":\"en\"}");
        var saver = new SettingsAutoSaver(store.Load().Settings, store, new FakeAppLog(), LoadStatus.Loaded);

        saver.Flush();

        Assert.Equal("{\"schemaVersion\":1,\"language\":\"en\"}", File.ReadAllText(store.FilePath));
    });

    [Fact] public void A_saved_change_is_not_saved_again_by_the_next_Flush() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, store, new FakeAppLog(), LoadStatus.Loaded);
        settings.Theme = ThemeChoice.Dark;
        saver.Flush();
        File.Delete(store.FilePath);

        saver.Flush();

        Assert.False(File.Exists(store.FilePath));
    });

    [Fact] public void A_change_is_saved_after_the_delay_without_Flush() => Sta.RunAsync(async () =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var settings = new GlobalSettings();
        _ = new SettingsAutoSaver(settings, store, new FakeAppLog(), LoadStatus.Missing);

        settings.Language = "de";
        Assert.False(File.Exists(store.FilePath));

        for (var waited = 0; !File.Exists(store.FilePath) && waited < 5000; waited += 50)
        {
            await Task.Delay(50);
        }

        Assert.Equal("de", new GlobalSettingsStore(dir.Path).Load().Settings.Language);
    });

    [Fact] public void A_corrupt_file_status_still_saves_changes() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, store, new FakeAppLog(), LoadStatus.Corrupt);

        settings.Theme = ThemeChoice.Light;
        saver.Flush();

        Assert.True(File.Exists(store.FilePath));
    });

    [Fact] public void Never_writes_when_load_status_is_NewerVersion() => Sta.Run(() =>
    {
        var warnings = AssertNothingIsWritten(LoadStatus.NewerVersion);

        Assert.Contains("will not be saved in this session", warnings[0]);
        Assert.Contains("newer version", warnings[0]);
    });

    [Fact] public void Never_writes_when_load_status_is_Unreadable() => Sta.Run(() =>
    {
        var warnings = AssertNothingIsWritten(LoadStatus.Unreadable);

        Assert.Contains("will not be saved in this session", warnings[0]);
        Assert.Contains("could not be read", warnings[0]);
    });

    [Fact] public void The_debounce_timer_does_not_write_either_when_saving_is_disabled() => Sta.RunAsync(async () =>
    {
        foreach (var status in new[] { LoadStatus.NewerVersion, LoadStatus.Unreadable })
        {
            using var dir = new TempDir();
            var store = new GlobalSettingsStore(dir.Path);
            File.WriteAllText(store.FilePath, ForeignFile);
            var settings = new GlobalSettings();
            _ = new SettingsAutoSaver(settings, store, new FakeAppLog(), status);

            settings.Theme = ThemeChoice.Dark;
            await Task.Delay(900);

            Assert.Equal(ForeignFile, File.ReadAllText(store.FilePath));
        }
    });

    [Fact] public void A_failing_save_is_logged_as_warning_and_does_not_throw() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var log = new FakeAppLog();
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, new GlobalSettingsStore(BlockedDirectory(dir)), log, LoadStatus.Loaded);

        settings.Theme = ThemeChoice.Dark;
        saver.Flush();

        Assert.Single(log.Warnings);
        Assert.Empty(log.Errors);
    });

    [Fact] public void Failed_save_keeps_changes_pending() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var blocker = BlockedDirectory(dir);
        var store = new GlobalSettingsStore(blocker);
        var log = new FakeAppLog();
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, store, log, LoadStatus.Loaded);
        settings.Theme = ThemeChoice.Dark;
        saver.Flush();
        Assert.Single(log.Warnings);

        File.Delete(blocker);
        saver.Flush();

        Assert.Equal(ThemeChoice.Dark, new GlobalSettingsStore(blocker).Load().Settings.Theme);
        Assert.Single(log.Warnings);
    });

    // A file where the settings folder should be, so that every save fails with an IOException.
    private static string BlockedDirectory(TempDir dir)
    {
        var blocker = dir.File("not-a-folder");
        File.WriteAllText(blocker, "a file where the settings folder should be");
        return blocker;
    }

    // With an existing settings file and a status that forbids saving: one warning when the saver is created, a change and a Flush
    // later neither write nor warn again. Returns the warnings.
    private static IReadOnlyList<string> AssertNothingIsWritten(LoadStatus status)
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, ForeignFile);
        var before = File.ReadAllBytes(store.FilePath);
        var log = new FakeAppLog();
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, store, log, status);
        Assert.Single(log.Warnings);

        settings.Theme = ThemeChoice.Dark;
        saver.Flush();

        Assert.Equal(before, File.ReadAllBytes(store.FilePath));
        Assert.Single(log.Warnings);
        Assert.Empty(log.Errors);
        return log.Warnings.ToList();
    }
}
