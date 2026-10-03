namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderRealDataTests
{
    [RealDataFact] public void Every_pack_in_the_data_folder_has_a_consistent_index()
    {
        var packs = Directory.GetFiles(RealData.Wh3Data, "*.pack");
        Assert.NotEmpty(packs);
        Assert.All(packs, p => { var r = PackReader.ReadIndex(p); Assert.True(r.Problem == PackProblem.None, $"{Path.GetFileName(p)}: {r.Problem} {r.Detail}"); });
    }
}
