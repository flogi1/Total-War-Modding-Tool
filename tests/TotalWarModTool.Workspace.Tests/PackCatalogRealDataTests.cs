using TotalWarModTool.GameProfiles;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace.Tests;

public class PackCatalogRealDataTests
{
    [RealDataFact] public void Catalog_of_the_real_install_is_plausible()
    {
        var profile = GameProfileLoader.LoadAll(Path.Combine(RepoRoot.Find(), "profiles")).Profiles.Single(p => p.Id == "warhammer_3");
        var r = PackCatalog.Scan(profile, RealData.Wh3Dir!, RealData.Wh3Workshop, null, CancellationToken.None);
        Assert.True(r.UsedManifest);
        var vanilla = r.Packs.Where(p => p.Origin == PackOrigin.Vanilla).ToList();
        Assert.True(vanilla.Count > 100);
        Assert.Contains(vanilla, p => p.Name.Equals("db.pack", StringComparison.OrdinalIgnoreCase));
        Assert.All(vanilla, p => Assert.True(p.Problem == PackProblem.None, $"{p.Name}: {p.Problem}"));
        Assert.All(vanilla, p => Assert.Contains(p.Header!.Type, new[] { PackType.Boot, PackType.Release, PackType.Patch }));
        Assert.All(r.Packs.Where(p => p.Origin == PackOrigin.Workshop), p => Assert.Matches("^[0-9]+$", p.WorkshopId));
        Assert.All(r.Packs.Where(p => p.Origin != PackOrigin.Workshop), p => Assert.Null(p.WorkshopId));
    }
}
