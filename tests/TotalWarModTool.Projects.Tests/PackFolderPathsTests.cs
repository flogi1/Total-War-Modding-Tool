namespace TotalWarModTool.Projects.Tests;

public class PackFolderPathsTests
{
    [Fact] public void A_plain_relative_file_resolves_below_the_pack_folder()
    {
        using var dir = new TempDir();

        var check = PackFolderPaths.Resolve(dir.Path, @"db\a\x.tsv");

        Assert.Equal(PackPathProblem.None, check.Problem);
        Assert.Equal(Path.Combine(dir.Path, "pack", @"db\a\x.tsv"), check.FullPath);
        Assert.Null(check.LinkPath);
        Assert.Null(check.Detail);
    }

    [Fact] public void Slashes_and_a_trailing_separator_give_the_same_full_path()
    {
        using var dir = new TempDir();

        var plain = PackFolderPaths.Resolve(dir.Path, @"db\a\x.tsv");
        var slashes = PackFolderPaths.Resolve(dir.Path + Path.DirectorySeparatorChar, "db/a/x.tsv");

        Assert.Equal(plain.FullPath, slashes.FullPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\x")]
    [InlineData("a/../x")]
    [InlineData("C:\\x")]
    [InlineData("\\x")]
    [InlineData("/x")]
    public void A_path_that_leaves_the_pack_folder_is_unsafe(string relativeFile)
    {
        using var dir = new TempDir();

        var check = PackFolderPaths.Resolve(dir.Path, relativeFile);

        Assert.Equal(PackPathProblem.UnsafePath, check.Problem);
        Assert.False(string.IsNullOrWhiteSpace(check.Detail));
    }

    [Fact] public void A_linked_folder_on_the_path_is_reported_with_its_relative_path()
    {
        using var dir = new TempDir();
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(dir.Path, "pack", "a"));
        var link = Path.Combine(dir.Path, "pack", "a", "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var check = PackFolderPaths.Resolve(dir.Path, @"a\linked\x.tsv");

            Assert.Equal(PackPathProblem.Link, check.Problem);
            Assert.Equal(@"pack\a\linked", check.LinkPath);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact] public void ResolveForWrite_gives_the_same_full_path_as_Resolve_for_a_plain_file()
    {
        using var dir = new TempDir();

        var read = PackFolderPaths.Resolve(dir.Path, @"db\a\x.tsv");
        var write = PackFolderPaths.ResolveForWrite(dir.Path, "db/a/x.tsv");

        Assert.Equal(PackPathProblem.None, write.Problem);
        Assert.Equal(read.FullPath, write.FullPath);
    }

    // Resolve is the reader's rule and stays as it was; the writer's rule adds the Windows file name rules.
    [Theory]
    [InlineData("a.tsv:stream")]
    [InlineData("a::$DATA")]
    [InlineData("NUL.tsv")]
    [InlineData("x.tsv.")]
    [InlineData("x.tsv ")]
    [InlineData("a\\\\b.tsv")]
    [InlineData(".")]
    public void ResolveForWrite_refuses_names_that_Resolve_leaves_to_the_reader(string relativeFile)
    {
        using var dir = new TempDir();

        var write = PackFolderPaths.ResolveForWrite(dir.Path, relativeFile);

        Assert.Equal(PackPathProblem.UnsafePath, write.Problem);
        Assert.False(string.IsNullOrWhiteSpace(write.Detail));
        Assert.Null(write.FullPath);
    }

    [Fact] public void A_path_that_is_the_pack_folder_itself_is_unsafe()
    {
        using var dir = new TempDir();

        Assert.Equal(PackPathProblem.UnsafePath, PackFolderPaths.Resolve(dir.Path, ".").Problem);
        Assert.Equal(PackPathProblem.UnsafePath, PackFolderPaths.ResolveForWrite(dir.Path, ".").Problem);
    }

    [Fact] public void ResolveForWrite_names_a_linked_folder_like_Resolve()
    {
        using var dir = new TempDir();
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(dir.Path, "pack"));
        var link = Path.Combine(dir.Path, "pack", "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var write = PackFolderPaths.ResolveForWrite(dir.Path, @"linked\x.tsv");

            Assert.Equal(PackPathProblem.Link, write.Problem);
            Assert.Equal(@"pack\linked", write.LinkPath);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact] public void Null_arguments_are_thrown()
    {
        Assert.ThrowsAny<ArgumentException>(() => PackFolderPaths.Resolve(" ", "a"));
        Assert.Throws<ArgumentNullException>(() => PackFolderPaths.Resolve("p", null!));
        Assert.ThrowsAny<ArgumentException>(() => PackFolderPaths.ResolveForWrite(" ", "a"));
        Assert.Throws<ArgumentNullException>(() => PackFolderPaths.ResolveForWrite("p", null!));
    }
}
