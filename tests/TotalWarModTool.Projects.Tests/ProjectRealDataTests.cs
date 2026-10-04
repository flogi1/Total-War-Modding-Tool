using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects.Tests;

public class ProjectRealDataTests
{
    [RealDataFact] public void A_workshop_pack_imported_and_rebuilt_has_the_same_content()
    {
        if (RealData.Wh3Workshop is not { } workshop) return;
        var source = Directory.EnumerateFiles(workshop, "*.pack", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).Where(f => f.Length is > 1_000_000 and < 30_000_000).OrderBy(f => f.Length).ThenBy(f => f.FullName, StringComparer.Ordinal)
            .Select(f => f.FullName).FirstOrDefault(f => PackReader.ReadIndex(f).Index is { } i && i.Entries.Any(e => DbTablePath.TryGetTableName(e.Path, out _)));
        if (source is null) return;

        using var dir = new TempDir();
        var schema = SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron")).Schema!;
        var request = new ImportRequest(source, Path.Combine(dir.Path, "project"), "Round Trip", "warhammer_3", "twmt_round_trip.pack", new HashSet<string>());
        var report = PackImporter.Import(request, schema, null, CancellationToken.None);
        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.True(report.Tables > 0, "no table was imported as text");

        var folder = request.TargetFolder;
        var plan = ProjectBuilder.ComputeBuild(folder, report.Project!, schema, () => Guid.NewGuid().ToString(), null, CancellationToken.None);
        Assert.True(plan.Problems.Count == 0, string.Join("; ", plan.Problems.Take(5)));
        var built = ProjectBuilder.WriteBuild(folder, report.Project!, plan, DateTimeOffset.UnixEpoch, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, built.Outcome);

        var original = PackReader.ReadIndex(source).Index!;
        var rebuilt = PackReader.ReadIndex(built.PackPath!).Index!;
        var skipped = report.Skipped.Select(s => s.PackPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(original.Dependencies, rebuilt.Dependencies);
        Assert.Equal(original.Entries.Count - skipped.Count, rebuilt.Entries.Count);
        foreach (var entry in original.Entries.Where(e => !skipped.Contains(e.Path)))
        {
            var before = PackReader.ReadEntry(original, entry, CancellationToken.None).Bytes!;
            var after = PackReader.ReadEntry(rebuilt, rebuilt.Find(entry.Path)!, CancellationToken.None).Bytes!;
            if (DbTablePath.TryGetTableName(entry.Path, out var table)
                && DbTableDecoder.Decode(before, table, schema) is { Status: TableDecodeStatus.Ok } a && DbTableDecoder.Decode(after, table, schema) is { Status: TableDecodeStatus.Ok } b)
            {
                // A rebuilt table differs only in the GUID of its header.
                Assert.True(DbTableEncoder.Encode(a.Definition!, a.Version, null, a.Rows).AsSpan().SequenceEqual(DbTableEncoder.Encode(b.Definition!, b.Version, null, b.Rows)), entry.Path);
            }
            else
            {
                Assert.True(before.AsSpan().SequenceEqual(after), entry.Path);
            }
        }
    }
}
