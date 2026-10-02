using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsAutoSaverTests
{
    [Fact] public void Flush_saves_immediately() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var settings = new GlobalSettings();
        var saver = new SettingsAutoSaver(settings, store, new FakeAppLog());

        settings.Theme = ThemeChoice.Dark;
        saver.Flush();

        Assert.Equal(ThemeChoice.Dark, new GlobalSettingsStore(dir.Path).Load().Settings.Theme);
    });

    [Fact] public void Flush_saves_even_when_nothing_changed() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var saver = new SettingsAutoSaver(new GlobalSettings(), store, new FakeAppLog());

        saver.Flush();

        Assert.True(File.Exists(store.FilePath));
    });

    [Fact] public void A_change_is_saved_after_the_delay_without_Flush() => Sta.RunAsync(async () =>
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        var settings = new GlobalSettings();
        _ = new SettingsAutoSaver(settings, store, new FakeAppLog());

        settings.Language = "de";
        Assert.False(File.Exists(store.FilePath));

        for (var waited = 0; !File.Exists(store.FilePath) && waited < 5000; waited += 50)
        {
            await Task.Delay(50);
        }

        Assert.Equal("de", new GlobalSettingsStore(dir.Path).Load().Settings.Language);
    });

    [Fact] public void A_failing_save_is_logged_as_warning_and_does_not_throw() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var blocker = dir.File("not-a-folder");
        File.WriteAllText(blocker, "a file where the settings folder should be");
        var log = new FakeAppLog();
        var saver = new SettingsAutoSaver(new GlobalSettings(), new GlobalSettingsStore(blocker), log);

        saver.Flush();

        Assert.Single(log.Warnings);
        Assert.Empty(log.Errors);
    });
}
