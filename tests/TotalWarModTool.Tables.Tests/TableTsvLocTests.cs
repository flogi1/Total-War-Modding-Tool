namespace TotalWarModTool.Tables.Tests;

public class TableTsvLocTests
{
    [Fact] public void Writes_and_reads_loc_rows()
    {
        LocRow[] rows = [new("key_a", "Text\twith tab\nand break", true), new("key_ü", "", false)];
        var text = TableTsv.WriteLoc("text/db/mod.loc", rows).Text!;
        Assert.Equal("key\ttext\ttooltip\n#Loc;1;text/db/mod.loc\t\t\nkey_a\tText\\\\twith tab\\\\nand break\ttrue\nkey_ü\t\tfalse\n", text);
        var read = TableTsv.ReadLoc(text);
        Assert.Null(read.Problem);
        Assert.Equal("text/db/mod.loc", read.Loc!.PackPath);
        Assert.Equal(rows, read.Loc.Rows);
    }

    [Fact] public void A_text_with_carriage_return_cannot_be_written() =>
        Assert.Null(TableTsv.WriteLoc("p", [new LocRow("k", "a\rb", false)]).Text);

    [Fact] public void A_text_with_an_unpaired_surrogate_cannot_be_written()
    {
        var r = TableTsv.WriteLoc("p", [new LocRow("k", "a\uDC00b", false)]);
        Assert.Null(r.Text);
        Assert.False(string.IsNullOrWhiteSpace(r.Detail));
    }

    [Fact] public void ReadLoc_rejects_a_text_with_an_unpaired_surrogate()
    {
        var read = TableTsv.ReadLoc("key\ttext\ttooltip\n#Loc;1;p\t\t\nk\ta\uD800b\tfalse\n");
        Assert.Null(read.Loc);
        Assert.Equal((TsvProblem.InvalidValue, 3), (read.Problem!.Problem, read.Problem.Line));
    }

    [Theory]
    [InlineData("key\ttext\ttooltip\n#Loc;2;p\t\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("key\ttext\ttooltip\n#units_tables;1;p\t\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("key\ttext\n#Loc;1;p\t\n", TsvProblem.MissingColumn, 1)]
    [InlineData("key\ttext\ttooltip\textra\n#Loc;1;p\t\t\t\n", TsvProblem.UnknownColumn, 1)]
    [InlineData("key\ttext\ttooltip\n#Loc;1;p\t\t\nk\tt\n", TsvProblem.WrongCellCount, 3)]
    [InlineData("key\ttext\ttooltip\n#Loc;1;p\t\t\nk\tt\tmaybe\n", TsvProblem.InvalidValue, 3)]
    public void ReadLoc_reports_the_problem_with_its_line(string text, TsvProblem problem, int line)
    {
        var read = TableTsv.ReadLoc(text);
        Assert.Null(read.Loc);
        Assert.Equal((problem, line), (read.Problem!.Problem, read.Problem.Line));
    }
}
