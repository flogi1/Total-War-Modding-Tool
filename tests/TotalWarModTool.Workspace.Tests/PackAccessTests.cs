using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace.Tests;

public class PackAccessTests
{
    private static readonly PackIndexResult Unreadable = new(null, PackProblem.Unreadable, "locked");
    private static readonly PackIndexResult Truncated = new(null, PackProblem.Truncated, "short");

    [Fact] public void Unreadable_is_tried_again_until_it_works()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WriteTo(dir.File("a.pack"));
        var calls = 0;
        var result = PackAccess.ReadIndex(path, CancellationToken.None, 3, TimeSpan.Zero, p => ++calls < 3 ? Unreadable : PackReader.ReadIndex(p));
        Assert.Equal((3, PackProblem.None), (calls, result.Problem));
    }

    [Fact] public void Gives_up_after_the_last_attempt()
    {
        var calls = 0;
        var result = PackAccess.ReadIndex("x", CancellationToken.None, 3, TimeSpan.Zero, _ => { calls++; return Unreadable; });
        Assert.Equal((3, PackProblem.Unreadable), (calls, result.Problem));
    }

    [Fact] public void Other_problems_are_not_tried_again()
    {
        var calls = 0;
        var result = PackAccess.ReadIndex("x", CancellationToken.None, 3, TimeSpan.Zero, _ => { calls++; return Truncated; });
        Assert.Equal((1, PackProblem.Truncated), (calls, result.Problem));
    }

    [Fact] public void Cancel_during_the_wait_throws()
    {
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() =>
            PackAccess.ReadIndex("x", cts.Token, 3, TimeSpan.FromMinutes(5), _ => { cts.Cancel(); return Unreadable; }));
    }

    [Fact] public void Public_overload_reads_a_real_pack()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("a", [1]).WriteTo(dir.File("a.pack"));
        Assert.Single(PackAccess.ReadIndex(path, CancellationToken.None).Index!.Entries);
    }
}
