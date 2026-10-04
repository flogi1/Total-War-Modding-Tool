using TotalWarModTool.Projects;

namespace TotalWarModTool.Projects.Tests;

public class ProjectFileTests
{
    [Fact]
    public void CreateNew_then_Load_gives_the_same_project()
    {
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Path, "my mod");
        var created = ProjectFile.CreateNew(folder, "My Mod", "warhammer_3", "my_mod.pack", ["other.pack"]);
        Assert.Equal(ProjectProblem.None, created.Problem);
        Assert.NotEqual(Guid.Empty, created.Project!.Id);
        Assert.True(Directory.Exists(Path.Combine(folder, "pack")));
        var loaded = ProjectFile.Load(folder);
        Assert.Equal(ProjectProblem.None, loaded.Problem);
        Assert.Equal((created.Project.Id, "My Mod", "warhammer_3", "my_mod.pack"), (loaded.Project!.Id, loaded.Project.Name, loaded.Project.GameId, loaded.Project.PackName));
        Assert.Equal(["other.pack"], loaded.Project.Dependencies);
        var text = File.ReadAllText(Path.Combine(folder, "twmt-project.json"));
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("\"game\": \"warhammer_3\"", text);
    }

    [Fact]
    public void CreateNew_accepts_an_existing_empty_folder_and_rejects_the_rest()
    {
        using var dir = new TempDir();
        Assert.Equal(ProjectProblem.None, ProjectFile.CreateNew(dir.Path, "A", "g", "a.pack", []).Problem);
        Assert.Equal(ProjectProblem.FolderNotEmpty, ProjectFile.CreateNew(dir.Path, "A", "g", "a.pack", []).Problem);
        using var other = new TempDir();
        Assert.Equal(ProjectProblem.InvalidPackName, ProjectFile.CreateNew(other.Path, "A", "g", "a.txt", []).Problem);
        Assert.Equal(ProjectProblem.Invalid, ProjectFile.CreateNew(other.Path, " ", "g", "a.pack", []).Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(other.Path));
    }

    [Theory]
    [InlineData(null, ProjectProblem.NotFound)]
    [InlineData("{ broken", ProjectProblem.Corrupt)]
    [InlineData("[1]", ProjectProblem.Corrupt)]
    [InlineData("""{"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.Corrupt)]
    [InlineData("""{"schemaVersion":2,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.NewerVersion)]
    [InlineData("""{"schemaVersion":1,"id":"nope","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"00000000-0000-0000-0000-000000000000","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"","game":"g","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.txt"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack","dependencies":["ok.pack",5]}""", ProjectProblem.Invalid)]
    public void Load_reports_problems_and_never_touches_the_file(string? content, ProjectProblem expected)
    {
        using var dir = new TempDir();
        if (content is not null) File.WriteAllText(dir.File("twmt-project.json"), content);
        var result = ProjectFile.Load(dir.Path);
        Assert.Equal(expected, result.Problem);
        Assert.Null(result.Project);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        if (content is not null) Assert.Equal(content, File.ReadAllText(dir.File("twmt-project.json")));
        Assert.False(File.Exists(dir.File("twmt-project.json.bad")));
    }

    [Fact]
    public void Load_reads_a_file_with_a_BOM_and_rejects_invalid_UTF8_and_an_unpaired_surrogate_escape()
    {
        const string json = """{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""";
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("twmt-project.json"), [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(json)]);
        Assert.Equal(ProjectProblem.None, ProjectFile.Load(dir.Path).Problem);

        var invalid = new byte[] { 0xFF, 0xFE, 0xFD };
        File.WriteAllBytes(dir.File("twmt-project.json"), invalid);
        Assert.Equal(ProjectProblem.Corrupt, ProjectFile.Load(dir.Path).Problem);
        Assert.Equal(invalid, File.ReadAllBytes(dir.File("twmt-project.json")));

        File.WriteAllText(dir.File("twmt-project.json"), json.Replace("\"name\":\"A\"", "\"name\":\"\\uD800\""));
        Assert.Equal(ProjectProblem.Invalid, ProjectFile.Load(dir.Path).Problem);
    }

    [Fact]
    public void Load_tolerates_missing_dependencies_and_Save_rejects_a_bad_pack_name()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("twmt-project.json"), """{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""");
        var project = ProjectFile.Load(dir.Path).Project!;
        Assert.Empty(project.Dependencies);
        Assert.Equal(ProjectProblem.InvalidPackName, ProjectFile.Save(dir.Path, project with { PackName = "x" }).Problem);
        Assert.Equal(ProjectProblem.None, ProjectFile.Save(dir.Path, project with { Name = "B" }).Problem);
        Assert.Equal("B", ProjectFile.Load(dir.Path).Project!.Name);
    }
}
