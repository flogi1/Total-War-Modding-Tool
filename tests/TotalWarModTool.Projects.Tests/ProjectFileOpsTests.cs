using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects.Tests;

public class ProjectFileOpsTests
{
    private static string PackFile(string project, string relative) => Path.Combine(project, "pack", relative);

    private static string Put(string project, string relative, byte[] bytes)
    {
        var file = PackFile(project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    /// <summary>Every file and folder below <paramref name="folder"/> with its size, to prove that nothing was touched or created.</summary>
    private static string[] Snapshot(string folder) =>
        Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(p => File.Exists(p) ? p + "|" + new FileInfo(p).Length : p)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] TempFilesBelow(string folder) =>
        Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories);

    /// <summary>A project folder (a sub folder of the temp dir, so that every '..' row stays inside it) with an empty pack folder.</summary>
    private static string NewProject(TempDir dir)
    {
        var project = Path.Combine(dir.Path, "project");
        Directory.CreateDirectory(Path.Combine(project, "pack"));
        return project;
    }

    [Fact] public void Create_writes_the_bytes_and_returns_the_fingerprint()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Directory.CreateDirectory(PackFile(project, "db"));

        var result = ProjectFileOps.Create(project, @"db\a.tsv", new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Null(result.Detail);
        Assert.Empty(result.CreatedFolders);
        var file = PackFile(project, @"db\a.tsv");
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
        Assert.Equal(FileFingerprint.TryRead(file), result.Fingerprint);
        Assert.Equal(3, result.Fingerprint!.Length);
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public void Create_accepts_slashes_and_empty_content()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);

        var result = ProjectFileOps.Create(project, "text/a.loc.tsv", ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal(["text"], result.CreatedFolders);
        Assert.Empty(File.ReadAllBytes(PackFile(project, @"text\a.loc.tsv")));
        Assert.Equal(0, result.Fingerprint!.Length);
    }

    [Fact] public void Create_makes_missing_folders_and_reports_them_outermost_first()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);

        var result = ProjectFileOps.Create(project, @"db\x_tables\a.tsv", new byte[] { 5 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal(["db", @"db\x_tables"], result.CreatedFolders);
        Assert.Equal([5], File.ReadAllBytes(PackFile(project, @"db\x_tables\a.tsv")));

        // Only the folders that were missing are reported, in the spelling of the call.
        var second = ProjectFileOps.Create(project, "db/x_tables/deeper/b.tsv", new byte[] { 6 }, CancellationToken.None);
        Assert.Equal([@"db\x_tables\deeper"], second.CreatedFolders);
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public void Create_makes_a_missing_pack_folder_but_does_not_report_it()
    {
        using var dir = new TempDir();
        var project = Path.Combine(dir.Path, "project");
        Directory.CreateDirectory(project);

        var result = ProjectFileOps.Create(project, @"db\a.tsv", new byte[] { 1 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal(["db"], result.CreatedFolders);
        Assert.Equal([1], File.ReadAllBytes(PackFile(project, @"db\a.tsv")));
    }

    [Fact] public void Create_without_a_project_folder_or_with_a_file_named_pack_is_Failed_and_creates_nothing()
    {
        using var dir = new TempDir();
        var before = Snapshot(dir.Path);

        var noProject = ProjectFileOps.Create(Path.Combine(dir.Path, "missing"), "a.tsv", new byte[] { 1 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.Failed, noProject.Problem);
        Assert.False(string.IsNullOrWhiteSpace(noProject.Detail));
        Assert.Equal(before, Snapshot(dir.Path));

        var project = Path.Combine(dir.Path, "project");
        Directory.CreateDirectory(project);
        File.WriteAllBytes(Path.Combine(project, "pack"), [9]);
        before = Snapshot(dir.Path);

        var packIsFile = ProjectFileOps.Create(project, "a.tsv", new byte[] { 1 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.Failed, packIsFile.Problem);
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(project, "pack")));
    }

    [Fact] public void Create_never_overwrites_a_file_a_folder_or_a_name_that_differs_in_case()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var file = Put(project, @"db\a.tsv", [1, 2, 3]);
        Directory.CreateDirectory(PackFile(project, @"db\folder"));
        var before = Snapshot(dir.Path);

        foreach (var taken in new[] { @"db\a.tsv", @"db\A.TSV", "DB/a.tsv", @"db\folder", @"db\FOLDER", "db" })
        {
            var result = ProjectFileOps.Create(project, taken, new byte[] { 9, 9, 9, 9 }, CancellationToken.None);

            Assert.Equal(FileOpProblem.TargetExists, result.Problem);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            Assert.Null(result.Fingerprint);
            Assert.Empty(result.CreatedFolders);
            Assert.Equal(before, Snapshot(dir.Path));
        }

        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    // "{tmp}" stands for the temp dir of the test, "{tmpNoDrive}" for the same path without its drive letter and colon (a rooted path
    // on the current drive). The project folder is the sub folder "project" of the temp dir, its pack folder is empty, and the temp dir
    // holds an "outside.tsv" that would be "taken" if a row got through. If the refusal were missing, Path.Combine(packFolder,
    // relativeFile) would point as follows for each row:
    //   ""                          the pack folder itself (a folder: the move onto it fails)
    //   "..\x", "a\..\..\x"         "project\x": inside the temp dir
    //   "x.", "x "                  pack\x (Windows trims the dot and the space): inside the temp dir
    //   "NUL.tsv"                   the NUL device, which discards everything; it names no location
    //   "a::$DATA"                  the default stream of pack\a: inside the temp dir
    //   "a\\b"                      pack\a\b (doubled separators collapse): inside the temp dir
    //   {tmp}\outside.tsv           the existing "outside.tsv" of the temp dir (a rooted path replaces the pack folder)
    //   {tmpNoDrive}\outside.tsv    the same file when the current drive is the drive of the temp dir. On another drive it would be
    //                               a place that is not under the temp dir, so that row is skipped there (nothing is called).
    // Forms that could name a place outside the temp dir (a drive-relative path such as "C:x", a UNC path) are not part of this
    // theory; PackFolderPathsTests covers them on ResolveForWrite, which writes nothing.
    [Theory]
    [InlineData("")]
    [InlineData("..\\x")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("x.")]
    [InlineData("x ")]
    [InlineData("NUL.tsv")]
    [InlineData("a::$DATA")]
    [InlineData("a\\\\b")]
    [InlineData("{tmp}\\outside.tsv")]
    [InlineData("{tmpNoDrive}\\outside.tsv")]
    public void Unsafe_and_taken_targets_are_refused_and_nothing_is_written(string relativeFile)
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, @"a\keep.tsv", [1]);
        var outside = Path.Combine(dir.Path, "outside.tsv");
        File.WriteAllBytes(outside, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        Assert.Equal(':', dir.Path[1]);
        var noDrive = dir.Path[2..];
        Assert.Equal('\\', noDrive[0]);
        if (relativeFile.Contains("{tmpNoDrive}")
            && !string.Equals(Path.GetPathRoot(Environment.CurrentDirectory), Path.GetPathRoot(dir.Path), StringComparison.OrdinalIgnoreCase))
        {
            return;   // see the comment above: only run where the row cannot leave the temp dir
        }

        relativeFile = relativeFile.Replace("{tmpNoDrive}", noDrive).Replace("{tmp}", dir.Path);
        var before = Snapshot(dir.Path);

        var result = ProjectFileOps.Create(project, relativeFile, new byte[] { 7, 7 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.UnsafePath, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.Empty(result.CreatedFolders);
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], File.ReadAllBytes(outside));
        Assert.False(ProjectFileOps.Exists(project, relativeFile));
    }

    [Fact] public void A_linked_pack_folder_and_a_linked_sub_folder_are_refused()
    {
        using var dir = new TempDir();
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "x.tsv");
        File.WriteAllBytes(outsideFile, [7, 7, 7]);
        var before = Snapshot(outside);

        // A linked pack folder.
        var linkedProject = Path.Combine(dir.Path, "linkedProject");
        Directory.CreateDirectory(linkedProject);
        var packLink = Path.Combine(linkedProject, "pack");
        if (!JunctionHelper.TryCreate(packLink, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var viaPack = ProjectFileOps.Create(linkedProject, "new.tsv", new byte[] { 1 }, CancellationToken.None);
            Assert.Equal(FileOpProblem.Link, viaPack.Problem);
            Assert.Contains("pack", viaPack.Detail);
            Assert.Null(viaPack.Fingerprint);
            Assert.Equal(FileOpProblem.Link, ProjectFileOps.Rename(linkedProject, "x.tsv", "y.tsv").Problem);
            Assert.False(ProjectFileOps.Exists(linkedProject, "x.tsv"));
        }
        finally
        {
            Directory.Delete(packLink);   // the link itself, never the target behind it
        }

        // A linked folder below the pack folder: files and missing folders behind it.
        var project = Path.Combine(dir.Path, "project");
        Directory.CreateDirectory(PackFile(project, "plain"));
        Put(project, @"plain\own.tsv", [4]);
        var folderLink = PackFile(project, "linked");
        Assert.True(JunctionHelper.TryCreate(folderLink, outside));
        try
        {
            var viaFolder = ProjectFileOps.Create(project, @"linked\new.tsv", new byte[] { 1 }, CancellationToken.None);
            Assert.Equal(FileOpProblem.Link, viaFolder.Problem);
            Assert.Contains("linked", viaFolder.Detail);
            Assert.Null(viaFolder.Fingerprint);

            var viaMissingFolder = ProjectFileOps.Create(project, @"linked\sub\new.tsv", new byte[] { 1 }, CancellationToken.None);
            Assert.Equal(FileOpProblem.Link, viaMissingFolder.Problem);

            Assert.Equal(FileOpProblem.Link, ProjectFileOps.Rename(project, @"linked\x.tsv", @"plain\x.tsv").Problem);   // out of the link
            Assert.Equal(FileOpProblem.Link, ProjectFileOps.Rename(project, @"plain\own.tsv", @"linked\own.tsv").Problem);   // into the link
            Assert.False(ProjectFileOps.Exists(project, @"linked\x.tsv"));
            Assert.True(File.Exists(PackFile(project, @"plain\own.tsv")));
            Assert.False(File.Exists(PackFile(project, @"plain\x.tsv")));
        }
        finally
        {
            Directory.Delete(folderLink);
        }

        Assert.Equal([7, 7, 7], File.ReadAllBytes(outsideFile));   // the link target is byte-identical
        Assert.Equal(before, Snapshot(outside));
    }

    [Fact] public void A_failed_create_leaves_no_temp_file_and_no_new_folder()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, "db", [1, 2]);   // a FILE where the folder of the target would be
        var before = Snapshot(dir.Path);

        var result = ProjectFileOps.Create(project, @"db\sub\a.tsv", new byte[] { 5 }, CancellationToken.None);

        Assert.Equal(FileOpProblem.TargetExists, result.Problem);
        Assert.Contains("db", result.Detail);
        Assert.Empty(result.CreatedFolders);
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([1, 2], File.ReadAllBytes(PackFile(project, "db")));

        // A file appears at the target after the folders were made: the move fails, the temp file goes, the new file is not touched.
        var target = PackFile(project, @"new\sub\a.tsv");
        var raced = ProjectFileOps.Create(project, @"new\sub\a.tsv", new byte[] { 5 }, CancellationToken.None,
            new ProjectFileOps.OpsHooks { AfterFolders = () => File.WriteAllBytes(target, [9]) });

        Assert.Equal(FileOpProblem.TargetExists, raced.Problem);
        Assert.Empty(raced.CreatedFolders);
        Assert.Equal([9], File.ReadAllBytes(target));
        Assert.Empty(TempFilesBelow(dir.Path));
        // The folders now hold someone else's file: they stay, and the detail names them so that the caller can tell the user.
        Assert.Contains("left behind", raced.Detail);
        Assert.Contains(@"new\sub", raced.Detail);
    }

    [Fact] public void A_cancelled_create_leaves_nothing_behind()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var before = Snapshot(dir.Path);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ProjectFileOps.Create(project, @"db\x_tables\a.tsv", new byte[] { 1 }, cancelled.Token));
        Assert.Equal(before, Snapshot(dir.Path));

        // Cancelled while the folders already exist: they are removed again.
        using var late = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() =>
            ProjectFileOps.Create(project, @"db\x_tables\a.tsv", new byte[] { 1 }, late.Token, new ProjectFileOps.OpsHooks
            {
                AfterFolders = () =>
                {
                    Assert.True(Directory.Exists(PackFile(project, @"db\x_tables")));
                    late.Cancel();
                },
            }));
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public void Rename_moves_the_file_and_creates_target_folders()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, "a.tsv", [1, 2, 3]);
        Put(project, @"db\b.tsv", [4]);

        var sameFolder = ProjectFileOps.Rename(project, @"db\b.tsv", @"db\c.tsv");
        Assert.Equal(FileOpProblem.None, sameFolder.Problem);
        Assert.Empty(sameFolder.CreatedFolders);
        Assert.False(File.Exists(PackFile(project, @"db\b.tsv")));
        Assert.Equal([4], File.ReadAllBytes(PackFile(project, @"db\c.tsv")));

        var result = ProjectFileOps.Rename(project, "a.tsv", @"db\x_tables\z.tsv");

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Null(result.Detail);
        Assert.Equal([@"db\x_tables"], result.CreatedFolders);
        Assert.False(File.Exists(PackFile(project, "a.tsv")));
        var moved = PackFile(project, @"db\x_tables\z.tsv");
        Assert.Equal([1, 2, 3], File.ReadAllBytes(moved));
        Assert.Equal(FileFingerprint.TryRead(moved), result.Fingerprint);
    }

    [Fact] public void Rename_refuses_a_taken_target_and_keeps_both_files()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var first = Put(project, "a.tsv", [1]);
        var second = Put(project, "b.tsv", [2, 2]);
        Directory.CreateDirectory(PackFile(project, "folder"));
        var before = Snapshot(dir.Path);

        foreach (var target in new[] { "b.tsv", "B.TSV", "folder", "FOLDER", "a.tsv" })
        {
            var result = ProjectFileOps.Rename(project, "a.tsv", target);

            Assert.Equal(FileOpProblem.TargetExists, result.Problem);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            Assert.Null(result.Fingerprint);
            Assert.Empty(result.CreatedFolders);
            Assert.Equal(before, Snapshot(dir.Path));
        }

        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2, 2], File.ReadAllBytes(second));

        // A file in the place of a folder of the target path: refused, no folder made.
        var blocked = ProjectFileOps.Rename(project, "a.tsv", @"b.tsv\sub\a.tsv");
        Assert.Equal(FileOpProblem.TargetExists, blocked.Problem);
        Assert.Equal(before, Snapshot(dir.Path));
    }

    [Fact] public void Rename_that_only_changes_the_case_works()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, "a.tsv", [1, 2]);
        Put(project, @"db\x.tsv", [3]);

        var top = ProjectFileOps.Rename(project, "a.tsv", "A.tsv");
        var inFolder = ProjectFileOps.Rename(project, @"db\x.tsv", @"db\X.TSV");

        Assert.Equal(FileOpProblem.None, top.Problem);
        Assert.Equal(FileOpProblem.None, inFolder.Problem);
        Assert.Equal(["A.tsv"], Directory.GetFiles(PackFile(project, ""), "*.tsv").Select(Path.GetFileName));
        Assert.Equal(["X.TSV"], Directory.GetFiles(PackFile(project, "db")).Select(Path.GetFileName));
        Assert.Equal([1, 2], File.ReadAllBytes(PackFile(project, "A.tsv")));
        Assert.Equal(2, top.Fingerprint!.Length);
    }

    [Fact] public void Rename_of_a_missing_source_is_SourceMissing()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Directory.CreateDirectory(PackFile(project, "folder"));
        var before = Snapshot(dir.Path);

        foreach (var from in new[] { "missing.tsv", @"nofolder\missing.tsv", "folder" })
        {
            var result = ProjectFileOps.Rename(project, from, "new.tsv");

            Assert.Equal(FileOpProblem.SourceMissing, result.Problem);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            Assert.Equal(before, Snapshot(dir.Path));
        }
    }

    [Fact] public void Rename_with_an_unsafe_path_is_refused_and_changes_nothing()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, "a.tsv", [1]);
        File.WriteAllBytes(Path.Combine(project, "x.tsv"), [2]);   // where "..\x.tsv" would point
        var before = Snapshot(dir.Path);

        Assert.Equal(FileOpProblem.UnsafePath, ProjectFileOps.Rename(project, "a.tsv", @"..\y.tsv").Problem);
        Assert.Equal(FileOpProblem.UnsafePath, ProjectFileOps.Rename(project, "a.tsv", "").Problem);
        Assert.Equal(FileOpProblem.UnsafePath, ProjectFileOps.Rename(project, @"..\x.tsv", "b.tsv").Problem);
        Assert.Equal(FileOpProblem.UnsafePath, ProjectFileOps.Rename(project, "a.tsv", "NUL.tsv").Problem);
        Assert.Equal(before, Snapshot(dir.Path));
    }

    [Fact] public void Rename_of_a_locked_file_is_Locked_and_changes_nothing()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var file = Put(project, "a.tsv", [1, 2, 3]);
        var before = Snapshot(dir.Path);

        FileOpResult result;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = ProjectFileOps.Rename(project, "a.tsv", @"new\sub\b.tsv");   // the folders it makes are taken back
        }

        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.Empty(result.CreatedFolders);
        Assert.False(result.LeftBehind);
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
    }

    [Fact] public void A_failed_call_that_could_not_take_back_a_folder_says_LeftBehind()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var file = Put(project, "a.tsv", [1, 2, 3]);

        // Another program puts a file into the folder this call made: the folder stays, so the call did not leave the disk as it was.
        FileOpResult locked;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            locked = ProjectFileOps.Rename(project, "a.tsv", @"new\b.tsv",
                new ProjectFileOps.OpsHooks { BeforeMove = () => File.WriteAllBytes(PackFile(project, @"new\other.txt"), [7]) });
        }

        Assert.Equal(FileOpProblem.Locked, locked.Problem);
        Assert.True(locked.LeftBehind);
        Assert.Contains("left behind", locked.Detail);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));

        var target = PackFile(project, @"made\sub\a.tsv");
        var raced = ProjectFileOps.Create(project, @"made\sub\a.tsv", new byte[] { 5 }, CancellationToken.None,
            new ProjectFileOps.OpsHooks { AfterFolders = () => File.WriteAllBytes(target, [9]) });
        Assert.True(raced.LeftBehind);

        // A failure that took everything back, and a success, leave nothing behind.
        Assert.False(ProjectFileOps.Rename(project, "missing.tsv", "b.tsv").LeftBehind);
        Assert.False(ProjectFileOps.Create(project, "fresh.tsv", new byte[] { 1 }, CancellationToken.None).LeftBehind);
    }

    [Fact] public void RemoveEmptyFolders_stops_at_the_first_folder_that_is_not_empty_and_never_removes_pack()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Directory.CreateDirectory(PackFile(project, @"a\b\c"));
        Put(project, @"a\b\keep.tsv", [1]);

        ProjectFileOps.RemoveEmptyFolders(project, ["a", @"a\b", @"a\b\c"]);

        Assert.False(Directory.Exists(PackFile(project, @"a\b\c")));   // empty: removed
        Assert.True(File.Exists(PackFile(project, @"a\b\keep.tsv")));   // "a\b" is not empty: kept, and "a" is not asked
        Assert.True(Directory.Exists(PackFile(project, "a")));

        File.Delete(PackFile(project, @"a\b\keep.tsv"));
        ProjectFileOps.RemoveEmptyFolders(project, ["a", @"a\b", @"a\b\gone"]);   // a folder that is already gone is skipped
        Assert.False(Directory.Exists(PackFile(project, "a")));

        // The pack folder itself, a path out of the pack folder and an unknown name are never removed; nothing throws.
        Directory.CreateDirectory(Path.Combine(project, "sibling"));
        ProjectFileOps.RemoveEmptyFolders(project, [""]);
        ProjectFileOps.RemoveEmptyFolders(project, ["."]);
        ProjectFileOps.RemoveEmptyFolders(project, [@"..\sibling"]);
        ProjectFileOps.RemoveEmptyFolders(project, ["x\\\\y"]);
        ProjectFileOps.RemoveEmptyFolders(project, []);
        Assert.True(Directory.Exists(PackFile(project, "")));
        Assert.True(Directory.Exists(Path.Combine(project, "sibling")));

        // A folder that is a link is not removed and its target is not touched.
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        var link = PackFile(project, "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            ProjectFileOps.RemoveEmptyFolders(project, ["linked"]);

            Assert.True(Directory.Exists(link));
            Assert.True(Directory.Exists(outside));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact] public void RemoveEmptyFolders_takes_back_what_create_reported()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var created = ProjectFileOps.Create(project, @"db\t\a.tsv", new byte[] { 1 }, CancellationToken.None);
        File.Delete(PackFile(project, @"db\t\a.tsv"));

        ProjectFileOps.RemoveEmptyFolders(project, created.CreatedFolders);

        Assert.Equal([Path.Combine(project, "pack")], Directory.GetDirectories(project));
        Assert.Empty(Directory.GetFileSystemEntries(PackFile(project, "")));
    }

    [Fact] public void Exists_ignores_case_and_sees_folders()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, @"db\A.tsv", [1]);

        Assert.True(ProjectFileOps.Exists(project, @"db\A.tsv"));
        Assert.True(ProjectFileOps.Exists(project, "DB/a.TSV"));
        Assert.True(ProjectFileOps.Exists(project, "db"));   // a folder
        Assert.False(ProjectFileOps.Exists(project, @"db\b.tsv"));
        Assert.False(ProjectFileOps.Exists(project, @"other\a.tsv"));
        Assert.False(ProjectFileOps.Exists(project, ""));   // unsafe
        Assert.False(ProjectFileOps.Exists(project, @"..\project\pack\db\A.tsv"));   // unsafe, although it names an existing file
        Assert.False(ProjectFileOps.Exists(project, "a.tsv:stream"));
    }

    [Fact] public void Exists_is_true_when_a_file_is_in_the_place_of_a_folder_on_the_way()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Put(project, "a.tsv", [1]);

        Assert.True(ProjectFileOps.Exists(project, @"a.tsv\b.tsv"));   // like Create: no folder can be made there
        Assert.True(ProjectFileOps.Exists(project, @"A.TSV\deeper\b.tsv"));
        Assert.False(ProjectFileOps.Exists(project, @"other\b.tsv"));
        Assert.Equal(FileOpProblem.TargetExists, ProjectFileOps.Create(project, @"a.tsv\b.tsv", new byte[] { 1 }, CancellationToken.None).Problem);
    }

    // ---- links: refused at the target name, as the source, and when they appear while the operation runs ----

    /// <summary>A folder in the temp dir that stands for "somewhere behind a link": one file whose content must never change.</summary>
    private static string NewOutside(TempDir dir)
    {
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "a.tsv"), [7, 7, 7]);
        return outside;
    }

    /// <summary>Removes a junction (the link itself, never what it points to) if it is there.</summary>
    private static void DropLink(string link)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link);
        }
    }

    private static void AssertOutsideUntouched(string outside, string[] before)
    {
        Assert.Equal(before, Snapshot(outside));
        Assert.Equal([7, 7, 7], File.ReadAllBytes(Path.Combine(outside, "a.tsv")));
    }

    [Fact] public void A_link_at_the_name_of_the_target_or_of_the_source_is_refused_and_not_moved()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        Put(project, "own.tsv", [4]);
        var link = PackFile(project, "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var before = Snapshot(outside);

            var create = ProjectFileOps.Create(project, "linked", new byte[] { 1 }, CancellationToken.None);
            var renameOnto = ProjectFileOps.Rename(project, "own.tsv", "linked");
            var renameLink = ProjectFileOps.Rename(project, "linked", "moved");

            Assert.Equal(FileOpProblem.Link, create.Problem);
            Assert.Equal(FileOpProblem.Link, renameOnto.Problem);
            Assert.Equal(FileOpProblem.Link, renameLink.Problem);
            Assert.True(Directory.Exists(link));   // still the link, not moved
            Assert.False(Directory.Exists(PackFile(project, "moved")));
            Assert.True(File.Exists(PackFile(project, "own.tsv")));
            AssertOutsideUntouched(outside, before);
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void A_link_that_appears_under_the_name_of_a_missing_folder_is_refused_by_Create()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        var before = Snapshot(outside);
        var link = PackFile(project, "newdir");
        var made = false;

        try
        {
            var result = ProjectFileOps.Create(project, @"newdir\sub\a.tsv", new byte[] { 1 }, CancellationToken.None,
                new ProjectFileOps.OpsHooks { BeforeFolders = () => made = JunctionHelper.TryCreate(link, outside) });

            if (!made)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Contains("newdir", result.Detail);
            Assert.Empty(result.CreatedFolders);
            Assert.True(Directory.Exists(link));   // the link is not ours and is not removed
            Assert.Equal([link], Directory.GetFileSystemEntries(PackFile(project, "")));   // nothing was made next to the link
            AssertOutsideUntouched(outside, before);
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void A_link_that_takes_the_place_of_a_created_folder_is_refused_by_Create()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        var before = Snapshot(outside);
        var folder = PackFile(project, "newdir");
        var swapBefore = false;
        var swapMove = false;

        try
        {
            // Before anything is written: the empty folder this call made is replaced by a link.
            var early = ProjectFileOps.Create(project, @"newdir\a.tsv", new byte[] { 1 }, CancellationToken.None, new ProjectFileOps.OpsHooks
            {
                AfterFolders = () =>
                {
                    Directory.Delete(folder);
                    swapBefore = JunctionHelper.TryCreate(folder, outside);
                },
            });

            if (!swapBefore)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, early.Problem);
            Assert.True(Directory.Exists(folder));   // the link is left alone
            Assert.Contains("left behind", early.Detail);
            AssertOutsideUntouched(outside, before);
            DropLink(folder);

            // After the temp file was written: the folder, with the temp file in it, is replaced by a link.
            var late = ProjectFileOps.Create(project, @"newdir\a.tsv", new byte[] { 1 }, CancellationToken.None, new ProjectFileOps.OpsHooks
            {
                BeforeMove = () =>
                {
                    Directory.Delete(folder, recursive: true);
                    swapMove = JunctionHelper.TryCreate(folder, outside);
                },
            });

            Assert.True(swapMove);
            Assert.Equal(FileOpProblem.Link, late.Problem);
            Assert.True(Directory.Exists(folder));
            AssertOutsideUntouched(outside, before);
        }
        finally
        {
            DropLink(folder);
        }
    }

    [Fact] public void A_link_that_appears_while_a_file_is_renamed_is_refused()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        var before = Snapshot(outside);
        Put(project, "own.tsv", [4]);
        var link = PackFile(project, "newdir");
        var made = false;

        try
        {
            // A link under the name of a missing target folder.
            var viaMissing = ProjectFileOps.Rename(project, "own.tsv", @"newdir\b.tsv",
                new ProjectFileOps.OpsHooks { BeforeFolders = () => made = JunctionHelper.TryCreate(link, outside) });

            if (!made)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, viaMissing.Problem);
            Assert.Contains("newdir", viaMissing.Detail);
            Assert.True(File.Exists(PackFile(project, "own.tsv")));
            AssertOutsideUntouched(outside, before);
            DropLink(link);

            // A link that takes the place of the target folder this call made.
            var swapped = false;
            var viaCreated = ProjectFileOps.Rename(project, "own.tsv", @"newdir\b.tsv", new ProjectFileOps.OpsHooks
            {
                AfterFolders = () =>
                {
                    Directory.Delete(link);
                    swapped = JunctionHelper.TryCreate(link, outside);
                },
            });

            Assert.True(swapped);
            Assert.Equal(FileOpProblem.Link, viaCreated.Problem);
            Assert.True(File.Exists(PackFile(project, "own.tsv")));
            Assert.True(Directory.Exists(link));
            AssertOutsideUntouched(outside, before);
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void A_source_folder_that_becomes_a_link_before_the_move_is_not_moved()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);   // holds an "a.tsv", like the folder "plain" does
        var before = Snapshot(outside);
        Put(project, @"plain\a.tsv", [4]);
        var folder = PackFile(project, "plain");
        var swapped = false;

        try
        {
            var result = ProjectFileOps.Rename(project, @"plain\a.tsv", "b.tsv", new ProjectFileOps.OpsHooks
            {
                BeforeMove = () =>
                {
                    Directory.Delete(folder, recursive: true);
                    swapped = JunctionHelper.TryCreate(folder, outside);
                },
            });

            if (!swapped)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Contains("source", result.Detail);
            Assert.False(File.Exists(PackFile(project, "b.tsv")));
            AssertOutsideUntouched(outside, before);
        }
        finally
        {
            DropLink(folder);
        }
    }

    // ---- the cleanup and the folder creation check the whole way again ----

    [Fact] public void The_cleanup_does_not_remove_a_folder_behind_a_link_that_replaced_a_folder_on_the_way()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var other = Path.Combine(dir.Path, "other");
        Directory.CreateDirectory(Path.Combine(other, "b"));   // an empty real folder "b" behind the future link
        File.WriteAllBytes(Path.Combine(other, "keep.tsv"), [7]);
        var before = Snapshot(other);
        var link = PackFile(project, "a");
        var made = false;

        try
        {
            var result = ProjectFileOps.Create(project, @"a\b\f.tsv", new byte[] { 1 }, CancellationToken.None, new ProjectFileOps.OpsHooks
            {
                AfterFolders = () =>
                {
                    // Someone moves "a" (with the empty "b" this call made) away and puts a link named "a" in its place.
                    Directory.Move(link, PackFile(project, "a_moved"));
                    made = JunctionHelper.TryCreate(link, other);
                },
            });

            if (!made)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Contains("left behind", result.Detail);
            Assert.True(Directory.Exists(Path.Combine(other, "b")));   // not deleted through the link
            Assert.Equal(before, Snapshot(other));
            Assert.True(Directory.Exists(PackFile(project, @"a_moved\b")));
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void The_cleanup_does_not_delete_a_file_behind_a_link_that_replaced_the_folder_of_the_temp_file()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var other = Path.Combine(dir.Path, "other");
        Directory.CreateDirectory(other);
        var link = PackFile(project, "a");
        string? strangerFile = null;
        var made = false;

        try
        {
            var result = ProjectFileOps.Create(project, @"a\f.tsv", new byte[] { 1 }, CancellationToken.None, new ProjectFileOps.OpsHooks
            {
                BeforeMove = () =>
                {
                    // The folder with the temp file is moved away; behind the new link lies a file that has the very name of the temp file.
                    var tempName = Path.GetFileName(Directory.GetFiles(link, "*.tmp").Single());
                    Directory.Move(link, PackFile(project, "a_moved"));
                    made = JunctionHelper.TryCreate(link, other);
                    strangerFile = Path.Combine(other, tempName);
                    if (made)
                    {
                        File.WriteAllBytes(strangerFile, [5]);
                    }
                },
            });

            if (!made)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Contains("temp file", result.Detail);   // named as left behind, not deleted
            Assert.Equal([5], File.ReadAllBytes(strangerFile!));
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void Rename_checks_the_pack_folder_for_a_link_before_it_creates_folders()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        var before = Snapshot(outside);
        Put(project, "own.tsv", [4]);
        var link = Path.Combine(project, "pack");
        var made = false;

        try
        {
            var result = ProjectFileOps.Rename(project, "own.tsv", @"newdir\b.tsv", new ProjectFileOps.OpsHooks
            {
                BeforeFolders = () =>
                {
                    Directory.Move(link, Path.Combine(project, "pack_real"));
                    made = JunctionHelper.TryCreate(link, outside);
                },
            });

            if (!made)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Contains("pack", result.Detail);
            Assert.True(File.Exists(Path.Combine(project, @"pack_real\own.tsv")));
            AssertOutsideUntouched(outside, before);   // no folder "newdir" was made behind the link
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void A_rename_that_only_changes_the_case_of_a_folder_is_TargetExists()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var file = Put(project, @"db\a.tsv", [1, 2]);

        var result = ProjectFileOps.Rename(project, @"db\a.tsv", @"DB\a.tsv");

        Assert.Equal(FileOpProblem.TargetExists, result.Problem);
        Assert.Equal([1, 2], File.ReadAllBytes(file));
        Assert.Equal(["db"], Directory.GetDirectories(PackFile(project, "")).Select(Path.GetFileName));   // the folder keeps its spelling
    }

    // ---- a sharing violation of the move is waited out, but a file is never overwritten ----

    [Fact] public async Task Create_waits_for_a_program_that_holds_the_temp_file_briefly()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        Task? release = null;

        var result = ProjectFileOps.Create(project, "a.tsv", new byte[] { 1, 2 }, CancellationToken.None, new ProjectFileOps.OpsHooks
        {
            BeforeMove = () =>
            {
                var holder = new FileStream(Directory.GetFiles(PackFile(project, ""), "*.tmp").Single(), FileMode.Open, FileAccess.Read, FileShare.None);
                release = Task.Run(async () =>
                {
                    await Task.Delay(150);
                    holder.Dispose();
                });
            },
        });

        await release!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal([1, 2],File.ReadAllBytes(PackFile(project, "a.tsv")));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public async Task Rename_waits_for_a_program_that_holds_the_file_briefly()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var file = Put(project, "a.tsv", [1, 2, 3]);

        var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            holder.Dispose();
        });
        var result = ProjectFileOps.Rename(project, "a.tsv", "b.tsv");

        await release.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.False(File.Exists(file));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(PackFile(project, "b.tsv")));
    }

    [Fact] public void Argument_errors_are_thrown()
    {
        using var dir = new TempDir();

        Assert.ThrowsAny<ArgumentException>(() => ProjectFileOps.Create(" ", "a.tsv", ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => ProjectFileOps.Create(dir.Path, null!, ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        Assert.ThrowsAny<ArgumentException>(() => ProjectFileOps.Rename("", "a", "b"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileOps.Rename(dir.Path, null!, "b"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileOps.Rename(dir.Path, "a", null!));
        Assert.ThrowsAny<ArgumentException>(() => ProjectFileOps.RemoveEmptyFolders(" ", []));
        Assert.Throws<ArgumentNullException>(() => ProjectFileOps.RemoveEmptyFolders(dir.Path, null!));
        Assert.ThrowsAny<ArgumentException>(() => ProjectFileOps.Exists(" ", "a"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileOps.Exists(dir.Path, null!));
    }
}
