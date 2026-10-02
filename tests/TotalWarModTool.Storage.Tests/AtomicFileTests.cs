namespace TotalWarModTool.Storage.Tests;

public class AtomicFileTests
{
    [Fact] public void WriteAllText_creates_missing_directory_and_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a", "b", "f.json");
        AtomicFile.WriteAllText(path, "x");
        Assert.Equal("x", File.ReadAllText(path));
    }

    [Fact] public void WriteAllText_replaces_content_and_leaves_no_temp_files()
    {
        using var dir = new TempDir();
        AtomicFile.WriteAllText(dir.File("f.json"), "old");
        AtomicFile.WriteAllText(dir.File("f.json"), "new");
        Assert.Equal("new", File.ReadAllText(dir.File("f.json")));
        Assert.Equal(["f.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public async Task WriteAllText_retries_while_target_is_locked()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("f.json"), "old");
        var locked = new FileStream(dir.File("f.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(150); locked.Dispose(); });
        AtomicFile.WriteAllText(dir.File("f.json"), "new");
        await release;
        Assert.Equal("new", File.ReadAllText(dir.File("f.json")));
    }

    [Fact] public void WriteAllText_throws_and_removes_temp_when_target_stays_locked()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("f.json"), "old");
        using (new FileStream(dir.File("f.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(dir.File("f.json"), "new"));
        Assert.Equal(["f.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
        Assert.Equal("old", File.ReadAllText(dir.File("f.json")));
    }
}