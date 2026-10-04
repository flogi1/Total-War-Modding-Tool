using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects.Tests;

public class ProjectBuilderTests
{
    private static readonly DateTimeOffset BuildTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static string NewGuid() => TestData.HeaderGuid;

    private static (string Folder, ModProject Project) NewProject(TempDir dir)
    {
        var folder = Path.Combine(dir.Path, "project");
        return (folder, ProjectFile.CreateNew(folder, "My Mod", "warhammer_3", "my_mod.pack", ["base.pack"]).Project!);
    }

    private static void Put(string folder, string relative, byte[] bytes)
    {
        var file = Path.Combine(folder, "pack", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
    }

    private const string UnitsTsv = "key\tcost\tspeed\n#units_tables;2;db/units_tables/my_mod\t\t\nunit_a\t100\t0.1\n";

    [Fact] public void Builds_a_pack_from_text_and_raw_files()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"db\units_tables\my_mod.tsv", TestData.Utf8("\uFEFF" + UnitsTsv.Replace("\n", "\r\n")));
        Put(folder, @"text\db\my_mod.loc.tsv", TestData.Utf8("key\ttext\ttooltip\n#Loc;1;x\t\t\nk\tText\tfalse\n"));
        Put(folder, @"script\a.lua", TestData.Utf8("print(1)"));
        Put(folder, @"db\other_tables\binary", [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(folder, "pack", "empty folder"));

        var seen = new List<OperationProgress>();
        var plan = ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, new SyncProgress(seen.Add), CancellationToken.None);
        Assert.Empty(plan.Problems);
        Assert.Equal(4, plan.Entries.Count);
        Assert.Equal(4L, seen[^1].Done);
        Assert.False(Directory.Exists(Path.Combine(folder, "build")));

        var result = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, result.Outcome);
        Assert.Equal(Path.Combine(folder, "build", "my_mod.pack"), result.PackPath);
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(Path.Combine(folder, "build")).Select(Path.GetFileName));

        var index = PackReader.ReadIndex(result.PackPath!).Index!;
        Assert.Equal(["base.pack"], index.Dependencies);
        Assert.Equal(["db/other_tables/binary", "db/units_tables/my_mod", "script/a.lua", "text/db/my_mod.loc"], index.Entries.Select(e => e.Path));
        Assert.Equal(TestData.Units(("unit_a", 100, 0.1f)), PackReader.ReadEntry(index, index.Find("db/units_tables/my_mod")!, CancellationToken.None).Bytes);
        Assert.Equal(TestData.Loc(("k", "Text")), PackReader.ReadEntry(index, index.Find("text/db/my_mod.loc")!, CancellationToken.None).Bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, PackReader.ReadEntry(index, index.Find("db/other_tables/binary")!, CancellationToken.None).Bytes);
    }

    [Fact] public void Every_problem_is_reported_and_nothing_is_written()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"db\units_tables\bad_value.tsv", TestData.Utf8(UnitsTsv.Replace("100", "many")));
        Put(folder, @"db\other_tables\wrong_folder.tsv", TestData.Utf8(UnitsTsv));
        Put(folder, @"db\units_tables\not_utf8.tsv", [0x6B, 0xFF]);
        Put(folder, @"text\broken.loc.tsv", TestData.Utf8("nothing"));
        Put(folder, @"db\units_tables\twice.tsv", TestData.Utf8(UnitsTsv));
        Put(folder, @"db\units_tables\twice", [1]);
        Put(folder, @"script\fine.lua", [1]);
        Directory.CreateDirectory(Path.Combine(folder, "build"));
        File.WriteAllBytes(Path.Combine(folder, "build", "my_mod.pack"), [9, 9]);

        var plan = ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, null, CancellationToken.None);
        Assert.Equal([(@"db\other_tables\wrong_folder.tsv", 2), (@"db\units_tables\bad_value.tsv", 3), (@"db\units_tables\not_utf8.tsv", 0), (@"db\units_tables\twice.tsv", 0), (@"text\broken.loc.tsv", 2)],
            plan.Problems.Select(p => (p.RelativeFile, p.Line)));
        Assert.All(plan.Problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Detail)));
        Assert.Contains(@"db\units_tables\twice", plan.Problems[3].Detail);

        var result = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.HasProblems, result.Outcome);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(folder, "build", "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(Path.Combine(folder, "build")).Select(Path.GetFileName));
    }

    [Fact] public void Tables_need_a_schema_and_a_missing_pack_folder_is_a_problem()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Assert.Empty(ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None).Problems);
        Put(folder, @"db\units_tables\my_mod.tsv", TestData.Utf8(UnitsTsv));
        Assert.Equal(@"db\units_tables\my_mod.tsv", Assert.Single(ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None).Problems).RelativeFile);
        Directory.Delete(Path.Combine(folder, "pack"), recursive: true);
        Assert.Equal("", Assert.Single(ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, null, CancellationToken.None).Problems).RelativeFile);
    }

    [Fact] public void A_failed_or_cancelled_write_keeps_the_old_pack_and_leaves_no_temp_file()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"a.bin", [1, 2, 3]);
        Put(folder, @"b.bin", [4]);
        var build = Path.Combine(folder, "build");
        Directory.CreateDirectory(build);
        File.WriteAllBytes(Path.Combine(build, "my_mod.pack"), [9, 9]);

        var plan = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
        File.WriteAllBytes(Path.Combine(folder, "pack", "a.bin"), [1]);   // the file shrank after the plan was made
        var failed = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.WriteFailed, failed.Outcome);
        Assert.Contains("SourceChanged", failed.Detail);

        using var cts = new CancellationTokenSource();
        var again = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ProjectBuilder.WriteBuild(folder, project, again, BuildTime, new SyncProgress(_ => cts.Cancel()), cts.Token));

        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(build, "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(build).Select(Path.GetFileName));
    }

    [Fact] public void A_second_file_for_the_same_pack_path_ignoring_case_is_a_problem_naming_both_files()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"db\units_tables\Mixed.tsv", TestData.Utf8(UnitsTsv));
        Put(folder, @"db\units_tables\mixed", [1]);

        var plan = ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, null, CancellationToken.None);
        var problem = Assert.Single(plan.Problems);
        Assert.Equal(@"db\units_tables\mixed", problem.RelativeFile);
        Assert.Contains(@"db\units_tables\Mixed.tsv", problem.Detail);
        Assert.Contains("db/units_tables/mixed", problem.Detail);
        Assert.Single(plan.Entries);
    }

    [Fact] public void A_link_to_a_folder_is_not_followed_and_is_a_problem_naming_it()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, "a.bin", [1]);
        Put(folder, "z.bin", [2]);
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "secret.bin"), [7]);
        var link = Path.Combine(folder, "pack", "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var plan = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
            var problem = Assert.Single(plan.Problems);
            Assert.Equal("linked", problem.RelativeFile);
            Assert.Contains("links to folders are not supported", problem.Detail);
            Assert.Equal(["a.bin", "z.bin"], plan.Entries.Select(e => e.Path));
        }
        finally
        {
            Directory.Delete(link);   // the link itself, never the target behind it
        }

        Assert.True(File.Exists(Path.Combine(outside, "secret.bin")));
    }

    [Fact] public void A_cancelled_token_stops_the_listing()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"sub\a.bin", [1]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, cts.Token));
    }

    [Fact] public void An_invalid_pack_name_is_rejected_before_the_disk_is_touched()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        var plan = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
        foreach (var name in new[] { "bad", @"..\x.pack", "" })
        {
            var broken = project with { PackName = name };
            Assert.Throws<ArgumentException>(() => ProjectBuilder.ComputeBuild(folder, broken, null, NewGuid, null, CancellationToken.None));
            Assert.Throws<ArgumentException>(() => ProjectBuilder.WriteBuild(folder, broken, plan, BuildTime, null, CancellationToken.None));
        }

        Assert.False(Directory.Exists(Path.Combine(folder, "build")));
    }

    [Fact] public void Stale_temp_files_of_earlier_runs_are_removed_and_other_files_stay()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, "a.bin", [1]);
        var build = Path.Combine(folder, "build");
        Directory.CreateDirectory(build);
        File.WriteAllBytes(Path.Combine(build, "my_mod.pack.0123abcd.tmp"), [1]);
        File.WriteAllBytes(Path.Combine(build, "my_mod.pack.notes.tmp"), [2]);

        var plan = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None).Outcome);
        Assert.Equal(["my_mod.pack", "my_mod.pack.notes.tmp"], Directory.EnumerateFileSystemEntries(build).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact] public void Ignored_files_are_not_packed_and_are_named_in_the_plan()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"db\units_tables\my_mod.tsv", TestData.Utf8(UnitsTsv));
        Put(folder, @"db\units_tables\.~lock.my_mod.tsv#", TestData.Utf8("LibreOffice lock"));

        var plan = ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, null, CancellationToken.None);

        Assert.Empty(plan.Problems);
        Assert.Single(plan.Entries);
        Assert.Equal([@"db\units_tables\.~lock.my_mod.tsv#"], plan.Ignored);

        var result = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, result.Outcome);
        var index = PackReader.ReadIndex(result.PackPath!).Index!;
        Assert.Equal(["db/units_tables/my_mod"], index.Entries.Select(e => e.Path));
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
