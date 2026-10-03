namespace TotalWarModTool.App.Tests;

public class DebouncedSaverTests
{
    [Fact] public void Flush_saves_once_and_only_when_dirty() => Sta.Run(() =>
    {
        var saves = 0;
        var saver = new DebouncedSaver(() => saves++, new FakeAppLog(), "Session");
        saver.Flush(); Assert.Equal(0, saves);
        saver.MarkDirty(); saver.MarkDirty(); saver.Flush(); saver.Flush();
        Assert.Equal(1, saves);
    });

    [Fact] public void Failed_save_is_logged_and_stays_pending() => Sta.Run(() =>
    {
        var log = new FakeAppLog(); var fail = true; var saves = 0;
        var saver = new DebouncedSaver(() => { if (fail) throw new IOException("locked"); saves++; }, log, "Session");
        saver.MarkDirty(); saver.Flush();
        Assert.Contains(log.Warnings, w => w.Contains("Session") && w.Contains("locked"));
        fail = false; saver.Flush();
        Assert.Equal(1, saves);
    });

    [Fact] public void Saves_by_itself_after_the_delay() => Sta.RunAsync(async () =>
    {
        var saves = 0;
        var saver = new DebouncedSaver(() => saves++, new FakeAppLog(), "Session", TimeSpan.FromMilliseconds(20));
        saver.MarkDirty();
        var deadline = Environment.TickCount64 + 5000;
        while (saves == 0 && Environment.TickCount64 < deadline) await Task.Delay(10);
        Assert.Equal(1, saves);
    });
}
