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

    [Fact] public void Null_arguments_are_thrown()
    {
        Assert.ThrowsAny<ArgumentException>(() => PackFolderPaths.Resolve(" ", "a"));
        Assert.Throws<ArgumentNullException>(() => PackFolderPaths.Resolve("p", null!));
    }
}
