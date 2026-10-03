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

    // The dirty flag is cleared before the save runs, so a change that arrives during the save stays pending.
    [Fact] public void A_change_during_the_save_is_not_lost() => Sta.Run(() =>
    {
        var saves = 0; DebouncedSaver? saver = null;
        saver = new DebouncedSaver(() => { saves++; if (saves == 1) saver!.MarkDirty(); }, new FakeAppLog(), "Session");
        saver.MarkDirty(); saver.Flush();
        Assert.Equal(1, saves);
        saver.Flush();
        Assert.Equal(2, saves);
        saver.Flush();
        Assert.Equal(2, saves);
    });

    [Fact] public void A_failed_save_stays_pending_also_when_a_change_arrived_during_it() => Sta.Run(() =>
    {
        var saves = 0; var fail = true; DebouncedSaver? saver = null;
        saver = new DebouncedSaver(() => { if (fail) { saver!.MarkDirty(); throw new IOException("locked"); } saves++; }, new FakeAppLog(), "Session");
        saver.MarkDirty(); saver.Flush();
        fail = false; saver.Flush();
        Assert.Equal(1, saves);
    });

    // Any exception keeps the change pending; only I/O and access errors are swallowed and logged, the rest reaches the caller.
    [Fact] public void Another_exception_is_thrown_and_the_change_stays_pending() => Sta.Run(() =>
    {
        var saves = 0; var fail = true;
        var saver = new DebouncedSaver(() => { if (fail) throw new InvalidOperationException("boom"); saves++; }, new FakeAppLog(), "Session");
        saver.MarkDirty();
        Assert.Throws<InvalidOperationException>(saver.Flush);
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
