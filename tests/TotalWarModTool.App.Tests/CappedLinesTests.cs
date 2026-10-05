namespace TotalWarModTool.App.Tests;

public class CappedLinesTests
{
    private static List<string> Names(int count) => Enumerable.Range(1, count).Select(i => "file" + i).ToList();

    private static string More(int missing) => "more " + missing;

    [Fact] public void No_names_give_no_lines() =>
        Assert.Empty(CappedLines.Of([], More));

    [Fact] public void One_name_is_listed_alone() =>
        Assert.Equal(["file1"], CappedLines.Of(Names(1), More));

    [Fact] public void Ten_names_are_all_listed_without_a_count() =>
        Assert.Equal(Names(10), CappedLines.Of(Names(10), More));

    [Fact] public void Eleven_names_give_ten_and_a_count_of_one()
    {
        var lines = CappedLines.Of(Names(11), More);

        Assert.Equal(Names(10).Append("more 1"), lines);
    }

    [Fact] public void Twenty_five_names_give_ten_and_a_count_of_fifteen()
    {
        var lines = CappedLines.Of(Names(25), More);

        Assert.Equal(Names(10).Append("more 15"), lines);
    }

    [Fact] public void The_count_line_is_built_only_when_names_are_missing()
    {
        var calls = 0;

        CappedLines.Of(Names(10), missing => { calls++; return More(missing); });
        Assert.Equal(0, calls);

        CappedLines.Of(Names(12), missing => { calls++; return More(missing); });
        Assert.Equal(1, calls);
    }
}
