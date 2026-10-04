using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects.Tests;

public class PackImporterTests
{
    private static readonly IReadOnlySet<string> Vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data.pack" };

    private static string Pack(TempDir dir, Action<PackBuilder> fill)
    {
        var builder = new PackBuilder();
        fill(builder);
        return builder.WriteTo(dir.File("source.pack"));
    }

    private static ImportRequest Request(TempDir dir, string source, string packName = "my_mod.pack") =>
        new(source, Path.Combine(dir.Path, "project"), "My Mod", "warhammer_3", packName, Vanilla);

    [Fact] public void Imports_tables_loc_files_and_other_files()
    {
        using var dir = new TempDir();
        var units = TestData.Units(("unit_a", 100, 0.1f));
        var loc = TestData.Loc(("k", "Text"));
        var source = Pack(dir, p => p.AddDependency("base.pack")
            .AddEntry(@"db\units_tables\my_mod", units).AddEntry(@"text\db\my_mod.loc", loc)
            .AddEntry(@"script\a.lua", TestData.Utf8("print(1)")).AddEntry(@"db\other_tables\x", [1, 2, 3]));
        var before = File.ReadAllBytes(source);
        var seen = new List<OperationProgress>();
        var report = PackImporter.Import(Request(dir, source), TestData.Schema, new SyncProgress(seen.Add), CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal((1, 1, 2), (report.Tables, report.LocFiles, report.OtherFiles));
        Assert.Equal("db/other_tables/x", Assert.Single(report.StayedBinary).PackPath);
        Assert.Empty(report.Skipped);
        var project = Path.Combine(dir.Path, "project");
        Assert.StartsWith("key\tcost\tspeed\n", File.ReadAllText(Path.Combine(project, "pack", "db", "units_tables", "my_mod.tsv")));
        Assert.True(File.Exists(Path.Combine(project, "pack", "text", "db", "my_mod.loc.tsv")));
        Assert.Equal("print(1)", File.ReadAllText(Path.Combine(project, "pack", "script", "a.lua")));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(project, "pack", "db", "other_tables", "x")));
        var loaded = ProjectFile.Load(project).Project!;
        Assert.Equal((report.Project!.Id, "My Mod", "warhammer_3", "my_mod.pack"), (loaded.Id, loaded.Name, loaded.GameId, loaded.PackName));
        Assert.Equal(["base.pack"], loaded.Dependencies);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal([1L, 2L, 3L, 4L], seen.Select(p => p.Done));
        Assert.All(seen, p => Assert.Equal(4L, p.Total));
        Assert.Empty(Directory.EnumerateDirectories(dir.Path, "*.import-*"));
    }

    [Fact] public void Unsafe_and_colliding_entries_are_skipped_and_reported()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p
            .AddEntry(@"..\..\escape.txt", [1]).AddEntry(@"C:\abs.txt", [1]).AddEntry(@"a\con", [1]).AddEntry(@"a\b.", [1])
            .AddEntry(@"db\units_tables\x.tsv", [1]).AddEntry(@"text\x.loc.tsv", [1])
            .AddEntry(@"keep\File.txt", [1]).AddEntry(@"KEEP\file.TXT", [2])
            .AddEntry(@"node", [1]).AddEntry(@"node\child.txt", [1]));
        var preview = PackImporter.Preview(PackReader.ReadIndex(source).Index!);
        var report = PackImporter.Import(Request(dir, source), TestData.Schema, null, CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal(["../../escape.txt", "C:/abs.txt", "a/con", "a/b.", "db/units_tables/x.tsv", "text/x.loc.tsv", "KEEP/file.TXT", "node/child.txt"], report.Skipped.Select(s => s.PackPath));
        Assert.Equal(report.Skipped, preview.Skipped);
        Assert.All(report.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Equal((0, 0, 2, 2L), (preview.Tables, preview.LocFiles, preview.OtherFiles, preview.StoredBytes));
        var files = Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir.Path, f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal([@"project\pack\keep\File.txt", @"project\pack\node", @"project\twmt-project.json", "source.pack"], files);
    }

    [Fact] public void An_unreadable_entry_is_skipped_and_the_import_goes_on()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry(@"a\broken.bin", [1, 2, 3], compressed: true).AddEntry(@"a\fine.txt", [7]));
        var report = PackImporter.Import(Request(dir, source), null, null, CancellationToken.None);
        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal("a/broken.bin", Assert.Single(report.Skipped).PackPath);
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "a", "fine.txt")));
    }

    [Fact] public void Problems_before_the_first_write()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        Assert.Equal(ImportProblem.InvalidPackName, PackImporter.Import(Request(dir, source, "nope"), null, null, CancellationToken.None).Problem);
        Assert.Equal(ImportProblem.InvalidPackName, PackImporter.Import(Request(dir, source, "DATA.pack"), null, null, CancellationToken.None).Problem);
        Assert.Equal(ImportProblem.SourceUnreadable, PackImporter.Import(Request(dir, dir.File("missing.pack")), null, null, CancellationToken.None).Problem);
        Directory.CreateDirectory(Path.Combine(dir.Path, "project"));
        File.WriteAllText(Path.Combine(dir.Path, "project", "mine.txt"), "x");
        Assert.Equal(ImportProblem.TargetNotEmpty, PackImporter.Import(Request(dir, source), null, null, CancellationToken.None).Problem);
        Assert.Equal(["mine.txt"], Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "project")).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateDirectories(dir.Path, "*.import-*"));
    }

    [Fact] public void An_existing_empty_target_folder_is_used()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        Directory.CreateDirectory(Path.Combine(dir.Path, "project"));
        Assert.Equal(ImportProblem.None, PackImporter.Import(Request(dir, source), null, null, CancellationToken.None).Problem);
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "a.txt")));
    }

    [Fact] public void Cancel_leaves_no_trace()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]).AddEntry("b.txt", [2]).AddEntry("c.txt", [3]));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Done == 2) cts.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => PackImporter.Import(Request(dir, source), null, progress, cts.Token));
        Assert.Equal(["source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public void A_failed_import_leaves_no_trace()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        // The parent of the target is a file, so nothing can be created below it.
        File.WriteAllText(dir.File("blocker"), "x");
        var request = Request(dir, source) with { TargetFolder = Path.Combine(dir.Path, "blocker", "project") };
        var report = PackImporter.Import(request, null, null, CancellationToken.None);
        Assert.Equal(ImportProblem.TargetUnwritable, report.Problem);
        Assert.Null(report.Project);
        Assert.Equal(["blocker", "source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact] public void A_path_that_would_be_too_long_is_skipped()
    {
        using var dir = new TempDir();
        var tooLong = new string('a', 120) + "/" + new string('b', 120) + ".txt";
        var source = Pack(dir, p => p.AddEntry(tooLong, [1]).AddEntry("a.txt", [2]));
        var report = PackImporter.Import(Request(dir, source), null, null, CancellationToken.None);
        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal(tooLong, Assert.Single(report.Skipped).PackPath);
        Assert.Equal(["pack", "twmt-project.json"], Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "project")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "a.txt")));
    }

    [Fact] public void An_empty_project_name_or_game_id_is_an_argument_error()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        Assert.Throws<ArgumentException>(() => PackImporter.Import(Request(dir, source) with { ProjectName = " " }, null, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PackImporter.Import(Request(dir, source) with { GameId = "" }, null, null, CancellationToken.None));
        Assert.Equal(["source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public void A_table_whose_text_file_name_is_a_folder_of_a_later_entry_skips_that_entry()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p
            .AddEntry(@"db\units_tables\x", TestData.Units(("unit_a", 100, 0.1f))).AddEntry(@"db\units_tables\x.tsv\y", [1]));
        var report = PackImporter.Import(Request(dir, source), TestData.Schema, null, CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        var skipped = Assert.Single(report.Skipped);
        Assert.Equal("db/units_tables/x.tsv/y", skipped.PackPath);
        Assert.Contains("db/units_tables/x.tsv", skipped.Reason);
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "db", "units_tables", "x.tsv")));
        Assert.Equal((1, 0, 0), (report.Tables, report.LocFiles, report.OtherFiles));
    }

    [Fact] public void A_table_whose_text_file_name_is_a_folder_of_an_earlier_entry_is_skipped()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p
            .AddEntry(@"db\units_tables\x.tsv\y", [1]).AddEntry(@"db\units_tables\x", TestData.Units(("unit_a", 100, 0.1f))));
        var report = PackImporter.Import(Request(dir, source), TestData.Schema, null, CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        var skipped = Assert.Single(report.Skipped);
        Assert.Equal("db/units_tables/x", skipped.PackPath);
        Assert.Contains("db/units_tables/x.tsv/y", skipped.Reason);
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "db", "units_tables", "x.tsv", "y")));
        Assert.Equal((0, 0, 1), (report.Tables, report.LocFiles, report.OtherFiles));
    }

    [Fact] public void Unusable_dependency_names_of_the_source_are_dropped_and_reported()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddDependency("").AddDependency("  ").AddDependency("base.pack").AddEntry("a.txt", [1]));
        var report = PackImporter.Import(Request(dir, source), null, null, CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal(["base.pack"], report.Project!.Dependencies);
        Assert.Equal(["", "  "], report.DroppedDependencies);
        Assert.Equal(["base.pack"], ProjectFile.Load(Path.Combine(dir.Path, "project")).Project!.Dependencies);
    }

    [Fact] public void A_missing_parent_folder_of_the_target_is_not_created()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        var request = Request(dir, source) with { TargetFolder = Path.Combine(dir.Path, "missing", "project") };
        var report = PackImporter.Import(request, null, null, CancellationToken.None);
        Assert.Equal(ImportProblem.TargetUnwritable, report.Problem);
        Assert.Contains("parent folder", report.Detail);
        Assert.Equal(["source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public void A_relative_source_or_target_is_an_argument_error()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        Assert.Throws<ArgumentException>(() => PackImporter.Import(Request(dir, source) with { SourcePack = "source.pack" }, null, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PackImporter.Import(Request(dir, source) with { TargetFolder = @"project" }, null, null, CancellationToken.None));
        Assert.Equal(["source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public void The_vanilla_name_check_ignores_case_whatever_the_set_does()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        var request = Request(dir, source, "DATA.pack") with { VanillaPackNames = new HashSet<string>(StringComparer.Ordinal) { "data.pack" } };
        Assert.Equal(ImportProblem.InvalidPackName, PackImporter.Import(request, null, null, CancellationToken.None).Problem);
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
