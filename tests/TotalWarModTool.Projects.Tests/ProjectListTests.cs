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
    public void NormalizePath_gives_the_full_path_without_a_trailing_separator_and_null_for_unusable_paths()
    {
        Assert.Equal(@"C:\mods\b", ProjectListEdits.NormalizePath(@"C:\mods\x\..\b\"));
        Assert.Equal(@"\\server\share\mods", ProjectListEdits.NormalizePath(@"\\server\share\mods\"));
        Assert.Null(ProjectListEdits.NormalizePath(null));
        Assert.Null(ProjectListEdits.NormalizePath(""));
        Assert.Null(ProjectListEdits.NormalizePath(@"mods\b"));
        Assert.Null(ProjectListEdits.NormalizePath(@"C:mods"));
        Assert.Null(ProjectListEdits.NormalizePath("C:\\mods\\a\0b"));
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

    [Fact]
    public void Add_keeps_a_root_path_and_treats_a_unc_share_with_and_without_a_trailing_separator_as_one_path()
    {
        var root = ProjectListEdits.Add(ProjectList.Empty, A, @"C:\");
        Assert.Equal((ProjectListProblem.None, @"C:\"), (root.Problem, root.List.Projects[0].Path));

        var share = ProjectListEdits.Add(ProjectList.Empty, A, @"\\server\share");
        Assert.Equal(ProjectListProblem.None, share.Problem);
        Assert.Equal(ProjectListProblem.DuplicatePath, ProjectListEdits.Add(share.List, B, @"\\server\share\").Problem);

        var shareWithSeparator = ProjectListEdits.Add(ProjectList.Empty, A, @"\\server\share\");
        Assert.Equal(share.List.Projects[0].Path, shareWithSeparator.List.Projects[0].Path);
    }

    [Fact]
    public void Remove_of_a_project_that_is_not_active_keeps_the_active_mark()
    {
        var list = ProjectListEdits.Add(ProjectListEdits.Add(ProjectList.Empty, A, @"C:\a").List, B, @"C:\b").List;
        var removed = ProjectListEdits.Remove(list, B);
        Assert.Equal((ProjectListProblem.None, A, 1), (removed.Problem, removed.List.ActiveId!.Value, removed.List.Projects.Count));
    }

    [Fact]
    public void SetInstalled_stores_a_valid_record_and_rejects_one_that_could_not_survive_save_and_load()
    {
        var list = ProjectListEdits.Add(ProjectList.Empty, A, @"C:\a").List;
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var valid = new InstalledPack("a.pack", 0, utc);
        Assert.Equal((ProjectListProblem.None, valid), (ProjectListEdits.SetInstalled(list, A, valid).Problem, ProjectListEdits.SetInstalled(list, A, valid).List.Projects[0].Installed));

        Assert.Throws<ArgumentException>(() => ProjectListEdits.SetInstalled(list, A, new InstalledPack("a.txt", 1, utc)));
        Assert.Throws<ArgumentException>(() => ProjectListEdits.SetInstalled(list, A, new InstalledPack(@"..\x.pack", 1, utc)));
        Assert.Throws<ArgumentException>(() => ProjectListEdits.SetInstalled(list, A, new InstalledPack("", 1, utc)));
        Assert.Throws<ArgumentException>(() => ProjectListEdits.SetInstalled(list, A, new InstalledPack("a.pack", -1, utc)));
        Assert.Throws<ArgumentException>(() => ProjectListEdits.SetInstalled(list, A, new InstalledPack("a.pack", 1, DateTime.SpecifyKind(utc, DateTimeKind.Local))));
        Assert.Throws<ArgumentException>(() => ProjectListEdits.SetInstalled(list, A, new InstalledPack("a.pack", 1, DateTime.SpecifyKind(utc, DateTimeKind.Unspecified))));
    }
}
