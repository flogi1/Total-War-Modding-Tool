using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Storage;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects.Tests;

/// <summary>The lossless round trip pack -> project folder -> pack, and the install chain, on small synthetic packs (temp folders only).</summary>
public class ProjectRoundTripTests
{
    private static readonly IReadOnlySet<string> Vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data.pack" };

    // A well-formed table that the test schema does not know: it has to stay binary.
    private static byte[] UnknownTable() =>
        DbTableEncoder.Encode(new TableDefinition(1, TestData.UnitFields), 1, TestData.HeaderGuid, new List<object?[]> { new object?[] { "k", 1, 1.5f } });

    private static string ImportAndBuild(TempDir dir, string source, out ImportReport report, out BuildPlan plan)
    {
        var request = new ImportRequest(source, Path.Combine(dir.Path, "project"), "Round Trip", "warhammer_3", "round_trip.pack", Vanilla);
        report = PackImporter.Import(request, TestData.Schema, null, CancellationToken.None);
        Assert.Equal(ImportProblem.None, report.Problem);

        plan = ProjectBuilder.ComputeBuild(request.TargetFolder, report.Project!, TestData.Schema, () => Guid.NewGuid().ToString(), null, CancellationToken.None);
        Assert.True(plan.Problems.Count == 0, string.Join("; ", plan.Problems.Take(5).Select(p => p.RelativeFile + ": " + p.Detail)));
        var built = ProjectBuilder.WriteBuild(request.TargetFolder, report.Project!, plan, DateTimeOffset.UnixEpoch, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, built.Outcome);
        return built.PackPath!;
    }

    [Fact] public void A_mixed_pack_is_imported_and_rebuilt_without_loss()
    {
        using var dir = new TempDir();
        var source = new PackBuilder()
            .AddDependency("base.pack").AddDependency("other.pack")
            .AddEntry(@"db\units_tables\my_mod", TestData.Units(("unit_a", 100, 0.1f), ("unit_b", -5, 2.5f)))
            .AddEntry(@"db\mystery_tables\data", UnknownTable())
            .AddEntry(@"text\db\my_mod.loc", TestData.Loc(("k", "Text"), ("k2", "More\ttext")))
            .AddEntry(@"text\db\broken.loc", [1, 2, 3, 4, 5])
            .AddEntry(@"text\.loc", TestData.Loc(("only", "name")))
            .AddEntry(@"empty\zero.bin", [])
            .AddEntry(@"Keep\a.txt", [1, 2])
            .AddEntry(@"keep\b.txt", [3, 4, 5])
            .AddEntry(@"..\escape.txt", [9])
            .WriteTo(dir.File("source.pack"));

        var rebuiltPath = ImportAndBuild(dir, source, out var report, out var plan);

        Assert.Equal("../escape.txt", Assert.Single(report.Skipped).PackPath);
        Assert.Equal((1, 1, 6), (report.Tables, report.LocFiles, report.OtherFiles));
        Assert.Equal(
            ["db/mystery_tables/data", "text/.loc", "text/db/broken.loc"],
            report.StayedBinary.Select(b => b.PackPath).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(8, plan.Entries.Count);

        var original = PackReader.ReadIndex(source).Index!;
        var rebuilt = PackReader.ReadIndex(rebuiltPath).Index!;
        var skipped = report.Skipped.Select(s => s.PackPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["base.pack", "other.pack"], rebuilt.Dependencies);
        Assert.Equal(original.Dependencies, rebuilt.Dependencies);
        Assert.Equal(original.Entries.Count - skipped.Count, rebuilt.Entries.Count);

        foreach (var entry in original.Entries.Where(e => !skipped.Contains(e.Path)))
        {
            var match = Assert.Single(rebuilt.Entries, e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            var before = PackReader.ReadEntry(original, entry, CancellationToken.None).Bytes!;
            var after = PackReader.ReadEntry(rebuilt, match, CancellationToken.None).Bytes!;
            if (DbTablePath.TryGetTableName(entry.Path, out var table)
                && DbTableDecoder.Decode(before, table, TestData.Schema) is { Status: TableDecodeStatus.Ok } a
                && DbTableDecoder.Decode(after, table, TestData.Schema) is { Status: TableDecodeStatus.Ok } b)
            {
                // A table that became text is built again with a new header GUID; everything else is the same.
                Assert.NotEqual(before, after);
                Assert.True(DbTableEncoder.Encode(a.Definition!, a.Version, null, a.Rows).AsSpan().SequenceEqual(DbTableEncoder.Encode(b.Definition!, b.Version, null, b.Rows)), entry.Path);
            }
            else
            {
                Assert.True(before.AsSpan().SequenceEqual(after), entry.Path);
            }
        }
    }

    [Fact] public void A_pack_without_entries_is_imported_and_rebuilt_as_a_pack_without_entries()
    {
        using var dir = new TempDir();
        var source = new PackBuilder().WriteTo(dir.File("source.pack"));

        var rebuiltPath = ImportAndBuild(dir, source, out var report, out var plan);

        Assert.Empty(report.Skipped);
        Assert.Equal((0, 0, 0), (report.Tables, report.LocFiles, report.OtherFiles));
        Assert.Empty(plan.Entries);
        var rebuilt = PackReader.ReadIndex(rebuiltPath).Index!;
        Assert.Empty(rebuilt.Entries);
        Assert.Empty(rebuilt.Dependencies);
    }

    [Fact] public void An_install_recorded_in_the_project_list_is_owned_after_save_and_load()
    {
        using var dir = new TempDir();
        var data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        var built = new PackBuilder().AddEntry("a.txt", [1]).WriteTo(dir.File("built.pack"));

        var installed = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        Assert.Equal(InstallProblem.None, installed.Problem);
        Assert.NotNull(installed.Installed);

        var id = Guid.NewGuid();
        var list = ProjectListEdits.Add(ProjectList.Empty, id, Path.Combine(dir.Path, "project")).List;
        list = ProjectListEdits.SetInstalled(list, id, installed.Installed).List;
        var store = new ProjectListStore(Path.Combine(dir.Path, "roaming"), "warhammer_3");
        store.Save(list);
        var loaded = store.Load();

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var record = Assert.Single(loaded.List.Projects).Installed;
        Assert.Equal(installed.Installed, record);
        Assert.Equal(InstallProblem.None, PackInstaller.CheckOwnership(new InstallRequest(data, "my_mod.pack", record, Vanilla)));
        Assert.Equal(InstallProblem.ForeignFile, PackInstaller.CheckOwnership(new InstallRequest(data, "my_mod.pack", null, Vanilla)));
    }
}
