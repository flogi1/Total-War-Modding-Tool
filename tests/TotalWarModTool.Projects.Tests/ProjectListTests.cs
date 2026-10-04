namespace TotalWarModTool.Projects.Tests;

public class ProjectListTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Add_makes_the_first_project_active_and_normalises_the_path()
    {
        var one = ProjectListEdits.Add(ProjectList.Empty, A, @"C:\mods\a\");
        Assert.Equal(ProjectListProblem.None, one.Problem);
        Assert.Equal((A, @"C:\mods\a"), (one.List.ActiveId!.Value, one.List.Projects[0].Path));
        var two = ProjectListEdits.Add(one.List, B, @"C:\mods\x\..\b");
        Assert.Equal((A, @"C:\mods\b", 2), (two.List.ActiveId!.Value, two.List.Projects[1].Path, two.List.Projects.Count));
    }

    [Fact]
    public void Add_rejects_duplicates_and_relative_paths()
    {
        var list = ProjectListEdits.Add(ProjectList.Empty, A, @"C:\mods\a").List;
        Assert.Equal(ProjectListProblem.DuplicateId, ProjectListEdits.Add(list, A, @"C:\mods\copy").Problem);
        Assert.Equal(ProjectListProblem.DuplicatePath, ProjectListEdits.Add(list, B, @"c:\MODS\A\").Problem);
        Assert.Equal(ProjectListProblem.InvalidPath, ProjectListEdits.Add(list, B, @"mods\b").Problem);
        Assert.Same(list, ProjectListEdits.Add(list, B, "").List);
        Assert.Throws<ArgumentException>(() => ProjectListEdits.Add(list, Guid.Empty, @"C:\mods\b"));
    }

    [Fact]
    public void Remove_moves_the_active_mark_and_edits_reject_unknown_ids()
    {
        var list = ProjectListEdits.Add(ProjectListEdits.Add(ProjectList.Empty, A, @"C:\a").List, B, @"C:\b").List;
        Assert.Equal(B, ProjectListEdits.SetActive(list, B).List.ActiveId);
        Assert.Equal(B, ProjectListEdits.Remove(list, A).List.ActiveId);
        Assert.Null(ProjectListEdits.Remove(ProjectListEdits.Remove(list, A).List, B).List.ActiveId);
        var installed = new InstalledPack("a.pack", 5, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        Assert.Equal(installed, ProjectListEdits.SetInstalled(list, A, installed).List.Projects[0].Installed);
        Assert.Null(ProjectListEdits.SetInstalled(ProjectListEdits.SetInstalled(list, A, installed).List, A, null).List.Projects[0].Installed);
        var unknown = Guid.NewGuid();
        Assert.All(new[] { ProjectListEdits.Remove(list, unknown), ProjectListEdits.SetActive(list, unknown), ProjectListEdits.SetInstalled(list, unknown, null) },
            e => Assert.Equal((ProjectListProblem.UnknownId, list), (e.Problem, e.List)));
    }
}
