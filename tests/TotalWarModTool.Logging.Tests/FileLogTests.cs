namespace TotalWarModTool.Logging.Tests;

public class FileLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 14, 3, 7, 123, TimeSpan.Zero);

    [Fact] public void Info_writes_timestamped_line()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null);
        log.Info("started");
        Assert.Equal(["2026-10-02 14:03:07.123 [INFO] started"], File.ReadAllLines(log.CurrentFilePath));
    }

    [Fact] public void Error_writes_exception_and_redacts_profile_path()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), @"C:\Users\alice");
        log.Error(@"save failed for C:\Users\alice\s.json", new IOException(@"denied: C:\Users\alice\s.json"));
        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains(@"[ERROR] save failed for %USERPROFILE%\s.json", text);
        Assert.Contains(@"System.IO.IOException: denied: %USERPROFILE%\s.json", text);
        Assert.DoesNotContain("alice", text);
    }

    [Fact] public void Rotates_and_keeps_at_most_maxFiles()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null, maxFileBytes: 200, maxFiles: 3);
        for (var i = 0; i < 40; i++) log.Info("line " + i.ToString("D3") + new string('x', 40));
        var names = Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order();
        Assert.Equal(["twmt.1.log", "twmt.2.log", "twmt.log"], names);
        Assert.All(Directory.GetFiles(dir.Path), f => Assert.True(new FileInfo(f).Length <= 200));
        Assert.Contains("line 039", File.ReadAllText(log.CurrentFilePath));
    }

    [Fact] public void Does_not_throw_when_directory_cannot_be_created()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("blocked"), "");                  // a file where the log folder should be
        Assert.Null(Record.Exception(() =>
        {
            var log = new FileLog(dir.File("blocked"), new FakeTimeProvider(T0), null);
            log.Info("x"); log.Error("y", new Exception("z"));
        }));
    }

    [Fact] public void Does_not_throw_when_log_file_is_locked()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null);
        log.Info("first");
        using (new FileStream(log.CurrentFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(Record.Exception(() => log.Info("lost")));
        Assert.Null(Record.Exception(() => log.Info("third")));
        Assert.Contains("third", File.ReadAllText(log.CurrentFilePath));
    }

    [Fact] public void Concurrent_writes_produce_complete_lines()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null);
        Parallel.For(0, 8, t => { for (var i = 0; i < 200; i++) log.Info($"t{t} i{i}"); });
        var lines = File.ReadAllLines(log.CurrentFilePath);
        Assert.Equal(1600, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^2026-10-02 14:03:07\.123 \[INFO\] t\d i\d+$", l));
    }
}
