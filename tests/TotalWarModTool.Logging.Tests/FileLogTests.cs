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

    // Two lines of 79 characters plus line break fit into 200 bytes, a third one does not: every third write rotates.
    private static void WriteLines(FileLog log, int from, int to)
    {
        for (var i = from; i < to; i++) log.Info("line " + i.ToString("D3") + new string('x', 40));
    }

    private static List<string> ArchiveSnapshot(string directory) =>
        Directory.GetFiles(directory, "twmt.*.log").Select(f => Path.GetFileName(f) + "=" + File.ReadAllText(f)).Order().ToList();

    [Fact] public void Failed_rotation_keeps_existing_archives()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null, maxFileBytes: 200, maxFiles: 3);
        WriteLines(log, 0, 6);                                       // current + twmt.1.log + twmt.2.log, current is nearly full
        var archive1 = File.ReadAllText(dir.File("twmt.1.log"));

        // A viewer that keeps the current file open without delete sharing: every rotation attempt fails.
        // The first attempt may lose the oldest archive (a successful rotation would have dropped it anyway) ...
        List<string> afterFirstAttempt;
        using (new FileStream(log.CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.Null(Record.Exception(() => WriteLines(log, 6, 7)));
            afterFirstAttempt = ArchiveSnapshot(dir.Path);

            // ... but every further attempt must not delete or change anything.
            Assert.Null(Record.Exception(() => WriteLines(log, 7, 13)));
        }

        Assert.Equal(afterFirstAttempt, ArchiveSnapshot(dir.Path));
        Assert.Contains(afterFirstAttempt, a => a.EndsWith("=" + archive1, StringComparison.Ordinal));
    }

    [Fact] public void Rotation_succeeds_while_a_reader_with_delete_sharing_holds_the_file()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null, maxFileBytes: 200, maxFiles: 3);
        WriteLines(log, 0, 6);                                       // current + twmt.1.log + twmt.2.log, current is nearly full
        var archive1 = File.ReadAllText(dir.File("twmt.1.log"));
        var current = File.ReadAllText(log.CurrentFilePath);

        // A reader that allows renaming the file it holds open: the rotation must go through.
        using (new FileStream(log.CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            WriteLines(log, 6, 7);

        Assert.Equal(current, File.ReadAllText(dir.File("twmt.1.log")));
        Assert.Equal(archive1, File.ReadAllText(dir.File("twmt.2.log")));
        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains("line 006", text);
        Assert.DoesNotContain("line 005", text);
    }

    [Fact] public void Failed_rotation_keeps_archives_when_an_archive_is_locked()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null, maxFileBytes: 200, maxFiles: 4);
        WriteLines(log, 0, 8);                                       // current + twmt.1.log .. twmt.3.log, current is nearly full
        var archive1 = File.ReadAllText(dir.File("twmt.1.log"));
        var archive2 = File.ReadAllText(dir.File("twmt.2.log"));

        // The newest archive cannot be renamed, so the shift stops half way through. Repeated attempts must not eat more archives.
        using (new FileStream(dir.File("twmt.1.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.Null(Record.Exception(() => WriteLines(log, 8, 14)));

        var archives = Directory.GetFiles(dir.Path, "twmt.*.log").Select(File.ReadAllText).ToList();
        Assert.Contains(archive1, archives);
        Assert.Contains(archive2, archives);

        // Once the lock is gone the archives close up again and rotation works as before.
        WriteLines(log, 14, 20);
        Assert.Equal(["twmt.1.log", "twmt.2.log", "twmt.3.log", "twmt.log"], Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order());
        Assert.Contains("line 019", File.ReadAllText(log.CurrentFilePath));
    }
}
