namespace TotalWarModTool.Projects.Tests;

public class ProjectTempFilesTests
{
    [Fact] public void MoveWithoutOverwrite_moves_the_file_and_returns_true()
    {
        using var dir = new TempDir();
        var source = dir.File("a.tmp");
        var target = dir.File("a.tsv");
        File.WriteAllBytes(source, [1, 2]);

        var moved = ProjectTempFiles.MoveWithoutOverwrite(source, target, () => File.Exists(target), CancellationToken.None);

        Assert.True(moved);
        Assert.False(File.Exists(source));
        Assert.Equal([1, 2], File.ReadAllBytes(target));
    }

    [Fact] public void A_stillSafe_that_says_no_stops_the_retries_and_nothing_is_moved()
    {
        using var dir = new TempDir();
        var source = dir.File("a.tmp");
        var target = dir.File("a.tsv");
        File.WriteAllBytes(source, [1, 2]);
        var asked = 0;

        bool moved;
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))   // a sharing violation on every try
        {
            moved = ProjectTempFiles.MoveWithoutOverwrite(source, target, () => File.Exists(target), CancellationToken.None, () => ++asked < 2);
        }

        Assert.False(moved);
        Assert.Equal(2, asked);   // asked after the first pause (yes) and after the second (no), then no further try
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(target));
    }

    [Fact] public void A_taken_target_fails_at_once_without_a_retry_and_without_asking()
    {
        using var dir = new TempDir();
        var source = dir.File("a.tmp");
        var target = dir.File("a.tsv");
        File.WriteAllBytes(source, [1]);
        File.WriteAllBytes(target, [9]);
        var asked = 0;

        Assert.ThrowsAny<IOException>(() =>
            ProjectTempFiles.MoveWithoutOverwrite(source, target, () => File.Exists(target), CancellationToken.None, () => ++asked > 0));

        Assert.Equal(0, asked);
        Assert.Equal([9], File.ReadAllBytes(target));
        Assert.Equal([1], File.ReadAllBytes(source));
    }
}
