namespace TotalWarModTool.Storage.Tests;

public class JsonFileStoreTests
{
    public sealed record Sample(string Name, int Count);
    private static Sample Default() => new("default", 0);

    [Fact] public void Save_then_Load_round_trips_and_writes_schemaVersion_first()
    {
        using var dir = new TempDir();
        JsonFileStore.Save(dir.File("s.json"), 1, new Sample("a", 2));
        var text = File.ReadAllText(dir.File("s.json"));
        Assert.True(text.IndexOf("\"schemaVersion\": 1") < text.IndexOf("\"name\": \"a\""));
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(new Sample("a", 2), result.Value);
        Assert.Null(result.BadCopyPath);
    }

    [Fact] public void Load_missing_file_returns_default_and_creates_nothing()
    {
        using var dir = new TempDir();
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.Missing, Default()), (result.Status, result.Value));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Theory]
    [InlineData("{ not json")]                                   // invalid JSON
    [InlineData("[1,2]")]                                        // not an object
    [InlineData("{\"name\":\"a\",\"count\":2}")]                 // schemaVersion missing
    [InlineData("{\"schemaVersion\":\"x\",\"name\":\"a\"}")]     // schemaVersion not a number
    [InlineData("{\"schemaVersion\":1,\"name\":\"a\",\"count\":\"many\"}")]   // wrong property type
    public void Load_corrupt_file_copies_to_bad_and_returns_default(string content)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), content);
        File.WriteAllText(dir.File("s.json.bad"), "older bad copy");
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.Corrupt, Default()), (result.Status, result.Value));
        Assert.Equal(dir.File("s.json.bad"), result.BadCopyPath);
        Assert.Equal(content, File.ReadAllText(dir.File("s.json.bad")));
        Assert.Equal(content, File.ReadAllText(dir.File("s.json")));   // original stays until the next save
    }

    [Fact] public void Load_newer_schemaVersion_reads_known_fields_without_bad_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), "{\"schemaVersion\":7,\"name\":\"a\",\"count\":2,\"future\":true}");
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.NewerVersion, new Sample("a", 2)), (result.Status, result.Value));
        Assert.False(File.Exists(dir.File("s.json.bad")));
    }

    [Fact] public void Load_locked_file_is_unreadable_without_bad_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), "{\"schemaVersion\":1,\"name\":\"a\",\"count\":2}");
        using var locked = new FileStream(dir.File("s.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.Unreadable, Default()), (result.Status, result.Value));
        Assert.False(File.Exists(dir.File("s.json.bad")));
    }
}