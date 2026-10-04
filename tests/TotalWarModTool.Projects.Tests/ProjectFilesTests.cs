namespace TotalWarModTool.Projects.Tests;

public class ProjectFilesTests
{
    private static string Put(string folder, string relative, byte[]? bytes = null)
    {
        var file = Path.Combine(folder, "pack", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes ?? [1]);
        return file;
    }

    [Fact] public void Lists_files_sorted_with_kind_and_pack_path()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"text\x.loc.tsv");
        Put(dir.Path, @"script\b.lua");
        Put(dir.Path, @"db\units_tables\a.tsv");
        Directory.CreateDirectory(Path.Combine(dir.Path, "pack", "e"));

        var list = ProjectFiles.List(dir.Path, CancellationToken.None);

        Assert.True(list.PackFolderExists);
        Assert.Equal(
            [
                new ProjectFileItem(@"db\units_tables\a.tsv", "db/units_tables/a", ProjectFileKind.Table),
                new ProjectFileItem(@"script\b.lua", "script/b.lua", ProjectFileKind.Raw),
                new ProjectFileItem(@"text\x.loc.tsv", "text/x.loc", ProjectFileKind.Loc),
            ],
            list.Files);
        Assert.Empty(list.Ignored);
        Assert.Empty(list.Problems);
    }

    [Fact] public void Lock_hidden_and_system_files_are_ignored_and_listed()
    {
        using var dir = new TempDir();
        foreach (var name in new[] { ".~lock.a.tsv#", "~$a.tsv", "a.tsv.bak", "notes.TMP", "x~", "keep.lua" })
        {
            Put(dir.Path, name);
        }

        var hidden = Put(dir.Path, "desktop.ini");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        var list = ProjectFiles.List(dir.Path, CancellationToken.None);

        Assert.Equal(["keep.lua"], list.Files.Select(f => f.RelativeFile));
        Assert.Equal([".~lock.a.tsv#", "a.tsv.bak", "desktop.ini", "notes.TMP", "x~", "~$a.tsv"], list.Ignored);
        Assert.Empty(list.Problems);
    }

    [Fact] public void A_hidden_folder_is_listed_and_the_rule_looks_at_files_only()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"visible\a.lua");
        var inHidden = Put(dir.Path, @"hidden\b.lua");
        File.SetAttributes(Path.GetDirectoryName(inHidden)!, FileAttributes.Directory | FileAttributes.Hidden);

        var list = ProjectFiles.List(dir.Path, CancellationToken.None);

        Assert.Equal([@"hidden\b.lua", @"visible\a.lua"], list.Files.Select(f => f.RelativeFile));
        Assert.Empty(list.Ignored);
    }

    [Theory]
    [InlineData(".~LOCK.a.ods#", FileAttributes.Normal, true)]
    [InlineData("~$doc.docx", FileAttributes.Normal, true)]
    [InlineData("a.BAK", FileAttributes.Normal, true)]
    [InlineData("a.Tmp", FileAttributes.Normal, true)]
    [InlineData("a.lua~", FileAttributes.Normal, true)]
    [InlineData("a.lua", FileAttributes.Hidden, true)]
    [InlineData("a.lua", FileAttributes.System, true)]
    [InlineData("a.lua", FileAttributes.Archive, false)]
    [InlineData("tmp.lua", FileAttributes.Normal, false)]
    [InlineData("a~b.lua", FileAttributes.Normal, false)]
    [InlineData("bak", FileAttributes.Normal, false)]
    public void IsIgnored_decides_by_name_and_attributes(string name, FileAttributes attributes, bool expected) =>
        Assert.Equal(expected, ProjectFiles.IsIgnored(name, attributes));

    [Fact] public void A_missing_pack_folder_is_reported()
    {
        using var dir = new TempDir();

        var list = ProjectFiles.List(dir.Path, CancellationToken.None);

        Assert.False(list.PackFolderExists);
        Assert.Empty(list.Files);
        Assert.Empty(list.Ignored);
        Assert.Empty(list.Problems);
    }

    [Fact] public void A_junction_is_a_problem_and_not_followed()
    {
        using var dir = new TempDir();
        Put(dir.Path, "a.bin");
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "secret.bin"), [7]);
        var link = Path.Combine(dir.Path, "pack", "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var list = ProjectFiles.List(dir.Path, CancellationToken.None);
            var problem = Assert.Single(list.Problems);
            Assert.Equal("linked", problem.RelativePath);
            Assert.Contains("links to folders are not supported", problem.Detail);
            Assert.Equal(["a.bin"], list.Files.Select(f => f.RelativeFile));
        }
        finally
        {
            Directory.Delete(link);   // the link itself, never the target behind it
        }

        Assert.True(File.Exists(Path.Combine(outside, "secret.bin")));
    }

    [Fact] public void A_cancelled_token_stops_the_listing()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"sub\a.bin");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ProjectFiles.List(dir.Path, cts.Token));
    }
}
