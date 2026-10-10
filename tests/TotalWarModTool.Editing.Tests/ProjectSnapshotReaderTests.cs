using TotalWarModTool.Objects;
using TotalWarModTool.Tables;
using static TotalWarModTool.Editing.Tests.WriteFixture;

namespace TotalWarModTool.Editing.Tests;

public class ProjectSnapshotReaderTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static SnapshotRequest Request(string table, bool loc = false, params PackEntryRequest[] entries) =>
        new(new HashSet<string>([table], StringComparer.OrdinalIgnoreCase), loc, entries);

    private static ProjectSnapshot Read(string folder, SnapshotRequest request, PackEntryExists? exists = null) =>
        ProjectSnapshotReader.Read(folder, MiniSchema, request, exists ?? (static _ => false), None);

    [Fact]
    public void The_request_lists_tables_loc_and_the_pack_entries_of_foreign_deletes()
    {
        var vanilla = Vanilla("v.pack");
        var mod = Mod("m.pack");
        var changes = Changes(
        [
            Change(RowChangeKind.Set, X, ["k1", "b1", "n", "1"], ["k1", "b1", "n", "0"], From(Project("proj"), @"pack\db\x_tables\a.tsv"), 3, "note"),
            Change(RowChangeKind.Delete, Y, ["b1", "a", "b"], ["b1", "a", "b"], From(vanilla, "db/y_tables/data__"), 4),
            Change(RowChangeKind.Delete, X, ["k2", "b1", "n", "1"], ["k2", "b1", "n", "1"],
                From(mod, "db/x_tables/m", new OverriddenLayer(vanilla, "db/x_tables/data__")), 5),
            Change(RowChangeKind.Delete, X, ["k3", "b1", "n", "1"], ["k3", "b1", "n", "1"],
                From(mod, "db/x_tables/m", new OverriddenLayer(vanilla, "db/x_tables/data__")), 6),
        ], [new TextChange(TextChangeKind.Insert, "x_name_k1", "T", null, 7)]);

        var request = SnapshotRequest.For(changes);

        Assert.True(request.Tables.SetEquals([X, Y]));
        Assert.True(request.Loc);
        Assert.Equal(
            [new PackEntryRequest(vanilla, "db/y_tables/data__"), new PackEntryRequest(mod, "db/x_tables/m"), new PackEntryRequest(vanilla, "db/x_tables/data__")],
            request.PackEntries);
    }

    [Fact]
    public void Every_project_file_of_a_requested_table_is_read_with_text_and_fingerprint()
    {
        using var tmp = new TempDir();
        var a = WriteProjectTable(tmp.Path, X, 2, "a", XRow("k1"));
        var b = WriteProjectTable(tmp.Path, X, 1, "b", ["k2", "b1", "n"], ["k3", "b1", "n"]);
        WriteProjectTable(tmp.Path, Y, 1, "c", ["b1", "a", "b"]);

        var snapshot = Read(tmp.Path, Request(X));

        Assert.Empty(snapshot.Problems);
        Assert.Equal([@"db\x_tables\a.tsv", @"db\x_tables\b.tsv"], snapshot.Tables.Select(t => t.RelativeFile).ToArray());
        Assert.Equal(new FileInfo(a).Length, snapshot.Tables[0].Fingerprint.Length);
        Assert.Equal(File.ReadAllText(b), snapshot.Tables[1].Text);
        Assert.Equal([2, 1], snapshot.Tables.Select(t => t.Table.Version).ToArray());
        Assert.Equal([1, 2], snapshot.Tables.Select(t => t.Table.Rows.Count).ToArray());
        Assert.All(snapshot.Tables, t => Assert.False(t.IsCopy));
    }

    [Fact]
    public void A_project_file_whose_pack_path_is_in_a_pack_is_a_copy()
    {
        using var tmp = new TempDir();
        WriteProjectTable(tmp.Path, X, 2, "data__", XRow("k1"));
        WriteProjectTable(tmp.Path, X, 2, "a", XRow("k2"));

        var snapshot = Read(tmp.Path, Request(X), static p => p == "db/x_tables/data__");

        Assert.True(snapshot.Tables.Single(t => t.RelativeFile == @"db\x_tables\data__.tsv").IsCopy);
        Assert.False(snapshot.Tables.Single(t => t.RelativeFile == @"db\x_tables\a.tsv").IsCopy);
    }

    [Fact]
    public void A_missing_pack_folder_gives_an_empty_snapshot()
    {
        using var tmp = new TempDir();

        var snapshot = Read(tmp.Path, Request(X, loc: true));

        Assert.Empty(snapshot.Tables);
        Assert.Empty(snapshot.Locs);
        Assert.Empty(snapshot.Problems);
    }

    [Fact]
    public void An_invalid_project_file_is_a_problem_not_an_exception()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "pack", "db", X, "a.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "garbage");

        var snapshot = Read(tmp.Path, Request(X));

        var problem = Assert.Single(snapshot.Problems);
        Assert.Equal(SnapshotProblemKind.ProjectFileInvalid, problem.Kind);
        Assert.Equal(@"db\x_tables\a.tsv", problem.File);
        Assert.Empty(snapshot.Tables);
    }

    [Fact]
    public void Loc_files_are_read_only_when_requested()
    {
        using var tmp = new TempDir();
        WriteProjectLoc(tmp.Path, "p", new LocRow("k1", "one", false), new LocRow("k2", "two", true));

        var without = Read(tmp.Path, Request(X, loc: false));
        var with = Read(tmp.Path, Request(X, loc: true));

        Assert.Empty(without.Locs);
        var loc = Assert.Single(with.Locs);
        Assert.Equal(2, loc.Loc.Rows.Count);
        Assert.Equal("text/db/p.loc", loc.PackPath);
        Assert.Equal(@"text\db\p.loc.tsv", loc.RelativeFile);
    }

    [Fact]
    public void A_pack_entry_is_adopted_in_its_own_version()
    {
        using var tmp = new TempDir();
        var pack = WritePack(tmp.File("v.pack"), ("data__", X, 1, [["k1", "b1", "n"], ["k2", "b1", "n"], ["k3", "b1", "n"]]));
        var request = new PackEntryRequest(Vanilla(pack), "db/x_tables/data__");

        var snapshot = Read(tmp.Path, Request(X, false, request));

        Assert.Empty(snapshot.Problems);
        var entry = Assert.Single(snapshot.PackEntries);
        Assert.Equal(@"db\x_tables\data__.tsv", entry.RelativeFile);
        Assert.Equal(1, entry.Table.Version);
        Assert.Equal(3, entry.Table.Rows.Count);
        Assert.Equal("db/x_tables/data__", entry.Table.PackPath);
    }

    [Fact]
    public void A_missing_pack_or_entry_is_a_problem()
    {
        using var tmp = new TempDir();
        var pack = WritePack(tmp.File("v.pack"), ("data__", X, 1, [["k1", "b1", "n"]]));
        var gone = Vanilla(tmp.File("gone.pack"));
        var present = Vanilla(pack);

        var snapshot = Read(tmp.Path, Request(X, false, new PackEntryRequest(gone, "db/x_tables/data__"), new PackEntryRequest(present, "db/x_tables/nope")));

        Assert.Equal([SnapshotProblemKind.PackUnreadable, SnapshotProblemKind.PackEntryMissing], snapshot.Problems.Select(p => p.Kind).ToArray());
        Assert.Equal(gone, snapshot.Problems[0].Source);
        Assert.Equal("db/x_tables/nope", snapshot.Problems[1].File);
        Assert.Empty(snapshot.PackEntries);
    }
}
