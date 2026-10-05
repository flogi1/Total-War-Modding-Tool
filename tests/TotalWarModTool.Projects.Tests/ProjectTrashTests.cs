namespace TotalWarModTool.Projects.Tests;

public class ProjectTrashTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static string PackFile(string project, string relative) => Path.Combine(project, "pack", relative);

    private static string Put(string project, string relative, byte[] bytes)
    {
        var file = PackFile(project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    /// <summary>A project folder (a sub folder of the temp dir) with an empty pack folder.</summary>
    private static string NewProject(TempDir dir)
    {
        var project = Path.Combine(dir.Path, "project");
        Directory.CreateDirectory(Path.Combine(project, "pack"));
        return project;
    }

    /// <summary>The trash root of a test: a folder in the temp dir that does not exist yet.</summary>
    private static string NewTrashRoot(TempDir dir) => Path.Combine(dir.Path, "trash");

    /// <summary>A folder in the temp dir that stands for "somewhere behind a link": one file whose content must never change.</summary>
    private static string NewOutside(TempDir dir, string name = "outside")
    {
        var outside = Path.Combine(dir.Path, name);
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "a.tsv"), [7, 7, 7]);
        return outside;
    }

    /// <summary>Every file and folder below <paramref name="folder"/> with its size; empty when the folder is not there.</summary>
    private static string[] Snapshot(string folder) =>
        !Directory.Exists(folder)
            ? []
            : Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                .Select(p => File.Exists(p) ? p + "|" + new FileInfo(p).Length : p)
                .Order(StringComparer.Ordinal)
                .ToArray();

    private static void DropLink(string link)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link);
        }
    }

    private static string EntryFolder(TrashedFile file) => Path.GetDirectoryName(file.TrashFile)!;

    private static string GuidFolder(TrashedFile file) => Path.GetDirectoryName(EntryFolder(file))!;

    private static TrashedFile Trash(string trash, string project, string relative, FakeTimeProvider time, Guid? id = null)
    {
        var result = ProjectTrash.MoveToTrash(trash, id ?? Id, project, relative, time);
        Assert.Equal(FileOpProblem.None, result.Problem);
        return result.File!;
    }

    private static string EntryName(DateTimeOffset at, string hex = "abcdef12") => at.UtcDateTime.ToString("yyyyMMdd-HHmmss") + "-" + hex;

    /// <summary>Puts a file into an entry folder by hand, as an old version or another program would have left it.</summary>
    private static string PutEntry(string trash, Guid id, string entry, string fileName = "a.tsv", byte[]? bytes = null)
    {
        var folder = Path.Combine(trash, id.ToString("D"), entry);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, fileName);
        File.WriteAllBytes(file, bytes ?? [1, 2, 3]);
        return file;
    }

    // ---- MoveToTrash ----

    [Fact] public void MoveToTrash_moves_the_file_under_the_project_id_with_a_dated_folder()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var source = Put(project, @"db\a.tsv", [1, 2, 3]);

        var result = ProjectTrash.MoveToTrash(trash, Id, project, @"db/a.tsv", new FakeTimeProvider(Start));

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Null(result.Detail);
        var file = result.File!;
        Assert.Equal(Id, file.ProjectId);
        Assert.Equal(@"db\a.tsv", file.RelativeFile);
        Assert.False(File.Exists(source));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file.TrashFile));
        Assert.Equal("a.tsv", Path.GetFileName(file.TrashFile));
        Assert.Equal(Id.ToString("D"), Path.GetFileName(GuidFolder(file)));
        Assert.Equal(Path.GetFullPath(trash), Path.GetDirectoryName(GuidFolder(file)));
        Assert.Matches(@"^20261005-120000-[0-9a-f]{8}\z", Path.GetFileName(EntryFolder(file)));
        Assert.Single(Snapshot(trash), p => p.EndsWith("a.tsv|3", StringComparison.Ordinal));
    }

    [Fact] public void Two_deletes_of_the_same_name_do_not_collide()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);

        Put(project, "a.tsv", [1]);
        var first = Trash(trash, project, "a.tsv", time);
        Put(project, "a.tsv", [2, 2]);
        var second = Trash(trash, project, "a.tsv", time);   // the same second

        Assert.NotEqual(first.TrashFile, second.TrashFile);
        Assert.Equal([1], File.ReadAllBytes(first.TrashFile));
        Assert.Equal([2, 2], File.ReadAllBytes(second.TrashFile));
    }

    [Fact] public void MoveToTrash_of_a_missing_file_a_locked_file_and_an_unsafe_path_changes_nothing()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var locked = Put(project, "locked.tsv", [9, 9]);
        Directory.CreateDirectory(PackFile(project, "folder.tsv"));
        File.WriteAllBytes(Path.Combine(dir.Path, "outside.tsv"), [5]);
        var before = Snapshot(dir.Path);

        var missing = ProjectTrash.MoveToTrash(trash, Id, project, "none.tsv", time);
        var isFolder = ProjectTrash.MoveToTrash(trash, Id, project, "folder.tsv", time);
        var up = ProjectTrash.MoveToTrash(trash, Id, project, @"..\..\outside.tsv", time);
        var rooted = ProjectTrash.MoveToTrash(trash, Id, project, Path.Combine(dir.Path, "outside.tsv"), time);
        var empty = ProjectTrash.MoveToTrash(trash, Id, project, "", time);
        var stream = ProjectTrash.MoveToTrash(trash, Id, project, "locked.tsv:stream", time);

        Assert.Equal(FileOpProblem.SourceMissing, missing.Problem);
        Assert.Equal(FileOpProblem.SourceMissing, isFolder.Problem);
        Assert.Equal(FileOpProblem.UnsafePath, up.Problem);
        Assert.Equal(FileOpProblem.UnsafePath, rooted.Problem);
        Assert.Equal(FileOpProblem.UnsafePath, empty.Problem);
        Assert.Equal(FileOpProblem.UnsafePath, stream.Problem);
        Assert.All(new[] { missing, isFolder, up, rooted, empty, stream }, r => Assert.Null(r.File));
        Assert.Equal(before, Snapshot(dir.Path));

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = ProjectTrash.MoveToTrash(trash, Id, project, "locked.tsv", time);

            Assert.Equal(FileOpProblem.Locked, result.Problem);
            Assert.Null(result.File);
            Assert.False(result.LeftBehind);
        }

        Assert.Equal([9, 9], File.ReadAllBytes(locked));
        Assert.Empty(Snapshot(trash));   // the entry folder and the project folder were taken back
    }

    [Fact] public void A_locked_MoveToTrash_that_cannot_take_back_its_entry_folder_says_LeftBehind()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var locked = Put(project, "locked.tsv", [9, 9]);

        TrashResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Another program puts a file into the entry folder this call made: the folder stays.
            result = ProjectTrash.MoveToTrash(trash, Id, project, "locked.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
            {
                BeforeMove = () => File.WriteAllBytes(Path.Combine(Directory.GetDirectories(Path.Combine(trash, Id.ToString("D"))).Single(), "other.txt"), [1]),
            });
        }

        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.True(result.LeftBehind);
        Assert.Contains("left behind", result.Detail);
        Assert.Equal([9, 9], File.ReadAllBytes(locked));
    }

    [Fact] public void A_failed_MoveToTrash_that_leaves_a_file_at_the_trash_target_names_it_in_LeftCopy()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var source = Put(project, "a.tsv", [9, 9]);
        FileStream? held = null;
        string? target = null;

        TrashResult result;
        try
        {
            // The move fails because a file is at the target, and that file cannot be deleted again (it is held open).
            result = ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
            {
                BeforeMove = () =>
                {
                    target = Path.Combine(Directory.GetDirectories(Path.Combine(trash, Id.ToString("D"))).Single(), "a.tsv");
                    held = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                },
            });
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.True(result.LeftBehind);
        Assert.Equal(target, result.LeftCopy);
        Assert.True(File.Exists(target));
        Assert.Equal([9, 9], File.ReadAllBytes(source));

        // A failure that leaves no file in the trash names none.
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start)).LeftCopy);
        }
    }

    [Fact] public void MoveToTrash_refuses_a_link_in_the_way_a_link_as_the_file_and_a_link_as_trash_folder()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var folderLink = PackFile(project, "linked");
        if (!JunctionHelper.TryCreate(folderLink, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var outsideBefore = Snapshot(outside);
            var viaFolder = ProjectTrash.MoveToTrash(trash, Id, project, @"linked\a.tsv", time);
            var asFile = ProjectTrash.MoveToTrash(trash, Id, project, "linked", time);
            Assert.Equal(FileOpProblem.Link, viaFolder.Problem);
            Assert.Equal(FileOpProblem.Link, asFile.Problem);
            Assert.Equal(outsideBefore, Snapshot(outside));
            Assert.True(Directory.Exists(folderLink));
            Assert.False(Directory.Exists(trash));

            // The trash root itself is a link.
            var ownFile = Put(project, "own.tsv", [4]);
            var trashAsLink = Path.Combine(dir.Path, "trashlink");
            if (!JunctionHelper.TryCreate(trashAsLink, outside))
            {
                return;
            }

            try
            {
                var rootLinked = ProjectTrash.MoveToTrash(trashAsLink, Id, project, "own.tsv", time);
                Assert.Equal(FileOpProblem.Link, rootLinked.Problem);
                Assert.Equal(outsideBefore, Snapshot(outside));
                Assert.True(File.Exists(ownFile));
            }
            finally
            {
                DropLink(trashAsLink);
            }

            // The folder of the project in the trash is a link.
            Directory.CreateDirectory(trash);
            var guidLink = Path.Combine(trash, Id.ToString("D"));
            if (!JunctionHelper.TryCreate(guidLink, outside))
            {
                return;
            }

            try
            {
                var guidLinked = ProjectTrash.MoveToTrash(trash, Id, project, "own.tsv", time);
                Assert.Equal(FileOpProblem.Link, guidLinked.Problem);
                Assert.Equal(outsideBefore, Snapshot(outside));
                Assert.True(File.Exists(ownFile));
            }
            finally
            {
                DropLink(guidLink);
            }
        }
        finally
        {
            DropLink(folderLink);
        }
    }

    // ---- Restore ----

    [Fact] public void Restore_brings_the_same_bytes_back_and_removes_the_empty_trash_folder()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var source = Put(project, @"db\a.tsv", [1, 2, 3, 4]);
        var file = Trash(trash, project, @"db\a.tsv", new FakeTimeProvider(Start));

        var result = ProjectTrash.Restore(file, project, trash);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Null(result.Detail);
        Assert.Empty(result.CreatedFolders);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(source));
        Assert.Equal(source, result.Fingerprint!.Path);
        Assert.False(Directory.Exists(EntryFolder(file)));
        Assert.False(Directory.Exists(GuidFolder(file)));   // empty now
        Assert.True(Directory.Exists(trash));
    }

    [Fact] public void Restore_keeps_the_other_entries_of_the_project()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        Put(project, "a.tsv", [1]);
        Put(project, "b.tsv", [2]);
        var a = Trash(trash, project, "a.tsv", time);
        var b = Trash(trash, project, "b.tsv", time);

        var result = ProjectTrash.Restore(a, project, trash);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal([1], File.ReadAllBytes(PackFile(project, "a.tsv")));
        Assert.False(Directory.Exists(EntryFolder(a)));
        Assert.True(File.Exists(b.TrashFile));
        Assert.True(Directory.Exists(GuidFolder(b)));
    }

    [Fact] public void Restore_refuses_a_taken_target_and_a_missing_trash_file()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        Put(project, "a.tsv", [1]);
        var file = Trash(trash, project, "a.tsv", time);

        // A file is at the old place.
        var taken = Put(project, "a.tsv", [8, 8]);
        var viaFile = ProjectTrash.Restore(file, project, trash);
        File.Delete(taken);
        Directory.CreateDirectory(taken);
        var viaFolder = ProjectTrash.Restore(file, project, trash);
        Directory.Delete(taken);
        Put(project, "a.tsv", [8, 8]);

        Assert.Equal(FileOpProblem.TargetExists, viaFile.Problem);
        Assert.Equal(FileOpProblem.TargetExists, viaFolder.Problem);
        Assert.Equal([8, 8], File.ReadAllBytes(taken));
        Assert.Equal([1], File.ReadAllBytes(file.TrashFile));
        Assert.Equal([taken], Directory.GetFiles(project, "*", SearchOption.AllDirectories));
        Assert.Empty(viaFile.CreatedFolders);

        // The trash file is gone.
        File.Delete(file.TrashFile);
        File.Delete(taken);
        var before = Snapshot(dir.Path);
        var missing = ProjectTrash.Restore(file, project, trash);

        Assert.Equal(FileOpProblem.SourceMissing, missing.Problem);
        Assert.Equal(before, Snapshot(dir.Path));
    }

    [Fact] public void Restore_refuses_a_trash_path_outside_the_project_s_trash_folder()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var otherTrash = Path.Combine(dir.Path, "other-trash");
        var time = new FakeTimeProvider(Start);
        var victim = Path.Combine(dir.Path, "elsewhere", "victim.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(victim)!);
        File.WriteAllBytes(victim, [6, 6]);
        Put(project, "a.tsv", [1]);
        var real = Trash(trash, project, "a.tsv", time);
        Put(project, "a.tsv", [2]);
        var inOtherTrash = Trash(otherTrash, project, "a.tsv", time);
        var before = Snapshot(dir.Path);

        var elsewhere = ProjectTrash.Restore(new TrashedFile(Id, victim, "victim.tsv"), project, trash);
        var wrongProject = ProjectTrash.Restore(real with { ProjectId = Guid.NewGuid() }, project, trash);
        var wrongName = ProjectTrash.Restore(real with { RelativeFile = "b.tsv" }, project, trash);
        var wrongEntry = ProjectTrash.Restore(new TrashedFile(Id, Path.Combine(GuidFolder(real), "not-an-entry", "a.tsv"), "a.tsv"), project, trash);
        var relativePath = ProjectTrash.Restore(real with { TrashFile = "a.tsv" }, project, trash);
        var other = ProjectTrash.Restore(inOtherTrash, project, trash);
        var upwards = ProjectTrash.Restore(real with { RelativeFile = @"..\a.tsv" }, project, trash);

        Assert.All(new[] { elsewhere, wrongProject, wrongName, wrongEntry, relativePath, other, upwards },
            r => Assert.Equal(FileOpProblem.UnsafePath, r.Problem));
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([6, 6], File.ReadAllBytes(victim));
    }

    [Fact] public void Restore_recreates_missing_folders()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, @"db\sub\a.tsv", [3]);
        var file = Trash(trash, project, @"db\sub\a.tsv", new FakeTimeProvider(Start));
        Directory.Delete(Path.Combine(project, "pack"), recursive: true);   // even the pack folder is gone

        var result = ProjectTrash.Restore(file, project, trash);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal(["db", @"db\sub"], result.CreatedFolders);
        Assert.Equal([3], File.ReadAllBytes(PackFile(project, @"db\sub\a.tsv")));
    }

    [Fact] public void Restore_of_a_locked_trash_file_changes_nothing_and_takes_back_its_folders()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, @"db\a.tsv", [3]);
        var file = Trash(trash, project, @"db\a.tsv", new FakeTimeProvider(Start));
        Directory.Delete(PackFile(project, "db"));
        var before = Snapshot(dir.Path);

        using (new FileStream(file.TrashFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = ProjectTrash.Restore(file, project, trash);

            Assert.Equal(FileOpProblem.Locked, result.Problem);
            Assert.Empty(result.CreatedFolders);
            Assert.False(result.LeftBehind);
        }

        Assert.Equal(before, Snapshot(dir.Path));   // no "db" folder left behind, the trash file is where it was
        Assert.Equal([3], File.ReadAllBytes(file.TrashFile));
    }

    [Fact] public void Restore_refuses_links_in_the_trash_and_in_the_target_and_touches_nothing_behind_them()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var outside = NewOutside(dir);
        var time = new FakeTimeProvider(Start);

        // The folder of the project in the trash is a link to a folder that has a well-formed entry.
        var behind = Path.Combine(outside, "entry-holder");
        var entryName = EntryName(Start);
        Directory.CreateDirectory(Path.Combine(behind, entryName));
        File.WriteAllBytes(Path.Combine(behind, entryName, "a.tsv"), [4, 4]);
        Directory.CreateDirectory(trash);
        var guidLink = Path.Combine(trash, Id.ToString("D"));
        if (!JunctionHelper.TryCreate(guidLink, behind))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var forged = new TrashedFile(Id, Path.Combine(guidLink, entryName, "a.tsv"), "a.tsv");
            var outsideBefore = Snapshot(outside);
            var viaGuidLink = ProjectTrash.Restore(forged, project, trash);
            Assert.Equal(FileOpProblem.UnsafePath, viaGuidLink.Problem);
            Assert.Equal(outsideBefore, Snapshot(outside));
            Assert.False(File.Exists(PackFile(project, "a.tsv")));
        }
        finally
        {
            DropLink(guidLink);
        }

        // The target folder is a link.
        Put(project, "own.tsv", [1]);
        var file = Trash(trash, project, "own.tsv", time);
        var folderLink = PackFile(project, "linked");
        if (!JunctionHelper.TryCreate(folderLink, outside))
        {
            return;
        }

        try
        {
            var outsideBefore = Snapshot(outside);
            var viaTarget = ProjectTrash.Restore(file with { RelativeFile = @"linked\own.tsv" }, project, trash);
            Assert.Equal(FileOpProblem.UnsafePath, ProjectTrash.Restore(file with { RelativeFile = @"linked\x.tsv" }, project, trash).Problem);   // the name differs
            Assert.Equal(FileOpProblem.Link, viaTarget.Problem);
            Assert.Equal(outsideBefore, Snapshot(outside));
            Assert.True(File.Exists(file.TrashFile));
        }
        finally
        {
            DropLink(folderLink);
        }
    }

    // ---- a link that appears while the operation runs ----

    [Fact] public void A_link_that_takes_the_place_of_the_entry_folder_is_refused_and_nothing_goes_through_it()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var outside = NewOutside(dir);
        var source = Put(project, "a.tsv", [1, 2]);
        var outsideBefore = Snapshot(outside);
        var link = string.Empty;
        var swapped = false;

        try
        {
            var result = ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
            {
                BeforeMove = () =>
                {
                    var entry = Directory.GetDirectories(Path.Combine(trash, Id.ToString("D"))).Single();
                    Directory.Delete(entry);   // empty: the entry folder of this call
                    link = entry;
                    swapped = JunctionHelper.TryCreate(entry, outside);
                },
            });

            if (!swapped)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Null(result.File);
            Assert.Equal([1, 2], File.ReadAllBytes(source));
            Assert.Equal(outsideBefore, Snapshot(outside));
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void A_link_that_takes_the_place_of_a_folder_made_for_the_restore_is_refused_and_the_trash_file_stays()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var outside = NewOutside(dir);
        Put(project, @"db\a.tsv", [1, 2]);
        var file = Trash(trash, project, @"db\a.tsv", new FakeTimeProvider(Start));
        Directory.Delete(PackFile(project, "db"));
        var outsideBefore = Snapshot(outside);
        var link = PackFile(project, "db");
        var swapped = false;

        try
        {
            var result = ProjectTrash.Restore(file, project, trash, new ProjectTrash.TrashHooks
            {
                BeforeMove = () =>
                {
                    Directory.Delete(link);   // the folder this call made
                    swapped = JunctionHelper.TryCreate(link, outside);
                },
            });

            if (!swapped)
            {
                return;
            }

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.Equal([1, 2], File.ReadAllBytes(file.TrashFile));
            Assert.Equal(outsideBefore, Snapshot(outside));
            Assert.Contains("db", result.Detail);   // the link stays and is named: it is never removed
        }
        finally
        {
            DropLink(link);
        }
    }

    // ---- a move that reports success while the source stays (a move across drives whose delete failed) ----

    [Fact] public void MoveToTrash_whose_source_stays_after_the_move_deletes_the_copy_and_is_Locked()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var source = Put(project, @"db\a.tsv", [1, 2, 3]);

        var result = ProjectTrash.MoveToTrash(trash, Id, project, @"db\a.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
        {
            // As if the delete of the source had failed after the copy: the file is back.
            AfterMove = () => File.Copy(Directory.GetFiles(trash, "a.tsv", SearchOption.AllDirectories).Single(), source),
        });

        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.Null(result.File);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(source));
        Assert.Empty(Snapshot(trash));   // the copy and the folders made for it are gone: exactly one copy survives
    }

    [Fact] public void Restore_whose_trash_file_stays_after_the_move_deletes_the_copy_and_is_Locked()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, @"db\a.tsv", [1, 2, 3]);
        var file = Trash(trash, project, @"db\a.tsv", new FakeTimeProvider(Start));
        Directory.Delete(PackFile(project, "db"));
        var target = PackFile(project, @"db\a.tsv");

        var result = ProjectTrash.Restore(file, project, trash, new ProjectTrash.TrashHooks
        {
            AfterMove = () => File.Copy(target, file.TrashFile),
        });

        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file.TrashFile));
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(PackFile(project, "db")));   // the folder made for the copy is gone, too
    }

    [Fact] public void MoveToTrash_keeps_the_trash_entry_when_another_file_is_at_the_source_after_the_move()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var source = Put(project, "a.tsv", [1, 2, 3]);

        var result = ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
        {
            // Another program creates a new file at the old place (different content, also a different length in the second round).
            AfterMove = () => File.WriteAllBytes(source, [9, 9, 9]),
        });

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(result.File!.TrashFile));   // the only copy of the deleted file stays
        Assert.Equal([9, 9, 9], File.ReadAllBytes(source));

        var longer = Put(project, "b.tsv", [1, 2, 3]);
        var second = ProjectTrash.MoveToTrash(trash, Id, project, "b.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
        {
            AfterMove = () => File.WriteAllBytes(longer, [1, 2, 3, 4]),
        });

        Assert.Equal(FileOpProblem.None, second.Problem);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(second.File!.TrashFile));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(longer));
    }

    [Fact] public void Restore_stands_when_the_trash_file_is_a_different_file_after_the_move()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, "a.tsv", [1, 2, 3]);
        var file = Trash(trash, project, "a.tsv", new FakeTimeProvider(Start));
        var target = PackFile(project, "a.tsv");

        var result = ProjectTrash.Restore(file, project, trash, new ProjectTrash.TrashHooks
        {
            AfterMove = () => File.WriteAllBytes(file.TrashFile, [9, 9, 9]),
        });

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
        Assert.Equal([9, 9, 9], File.ReadAllBytes(file.TrashFile));   // harmless leftover, Cleanup removes it later
    }

    [Fact] public void MoveToTrash_never_compares_the_copy_with_itself_when_the_folder_of_the_source_becomes_a_link_to_the_entry()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, @"db\a.tsv", [1, 2, 3]);
        var folder = PackFile(project, "db");
        var swapped = false;
        string? copy = null;

        try
        {
            var result = ProjectTrash.MoveToTrash(trash, Id, project, @"db\a.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
            {
                AfterMove = () =>
                {
                    // After the move: pack\db (empty now) becomes a junction to the entry folder, so that the "source" is the copy itself.
                    copy = Directory.GetFiles(trash, "a.tsv", SearchOption.AllDirectories).Single();
                    Directory.Delete(folder);
                    swapped = JunctionHelper.TryCreate(folder, Path.GetDirectoryName(copy)!);
                },
            });

            if (!swapped)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.None, result.Problem);   // the move stands
            Assert.Equal([1, 2, 3], File.ReadAllBytes(result.File!.TrashFile));   // the only copy was not deleted
        }
        finally
        {
            DropLink(folder);
        }
    }

    [Fact] public void MoveToTrash_with_a_hard_link_at_the_source_loses_no_data()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var source = Put(project, "a.tsv", [1, 2, 3]);
        var made = false;

        var result = ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start), new ProjectTrash.TrashHooks
        {
            AfterMove = () => made = TryHardLink(source, Directory.GetFiles(trash, "a.tsv", SearchOption.AllDirectories).Single()),
        });

        if (!made)
        {
            return;   // hard links cannot be made in this environment
        }

        // Two names of one file: deleting the name of the copy leaves the data under the other name, so it is a correct roll back, not a loss.
        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(source));
        Assert.Empty(Snapshot(trash));
    }

    [Fact] public void Restore_never_compares_the_copy_with_itself_when_the_trash_entry_becomes_a_link_to_the_target_folder()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, @"db\a.tsv", [1, 2, 3]);
        var file = Trash(trash, project, @"db\a.tsv", new FakeTimeProvider(Start));
        Directory.Delete(PackFile(project, "db"));
        var entry = EntryFolder(file);
        var swapped = false;

        try
        {
            var result = ProjectTrash.Restore(file, project, trash, new ProjectTrash.TrashHooks
            {
                AfterMove = () =>
                {
                    // The entry folder (empty now) becomes a junction to pack\db: the "trash file" is the restored file itself.
                    Directory.Delete(entry);
                    swapped = JunctionHelper.TryCreate(entry, PackFile(project, "db"));
                },
            });

            if (!swapped)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(FileOpProblem.None, result.Problem);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(PackFile(project, @"db\a.tsv")));   // the restored file was not deleted
        }
        finally
        {
            DropLink(entry);
        }
    }

    [Fact] public void Restore_with_a_hard_link_as_the_trash_file_loses_no_data()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        Put(project, "a.tsv", [1, 2, 3]);
        var file = Trash(trash, project, "a.tsv", new FakeTimeProvider(Start));
        var target = PackFile(project, "a.tsv");
        var made = false;

        var result = ProjectTrash.Restore(file, project, trash, new ProjectTrash.TrashHooks
        {
            AfterMove = () => made = TryHardLink(file.TrashFile, target),
        });

        if (!made)
        {
            return;   // hard links cannot be made in this environment
        }

        // Two names of one file: deleting the name of the copy leaves the data under the other name, so it is a correct roll back, not a loss.
        Assert.Equal(FileOpProblem.Locked, result.Problem);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file.TrashFile));
        Assert.False(File.Exists(target));
    }

    /// <summary>Makes <paramref name="link"/> a second name of the existing file <paramref name="existing"/> (NTFS); false when that is not possible here.</summary>
    private static bool TryHardLink(string link, string existing)
    {
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("mklink");
        info.ArgumentList.Add("/H");
        info.ArgumentList.Add(link);
        info.ArgumentList.Add(existing);
        try
        {
            using var process = System.Diagnostics.Process.Start(info)!;
            process.StandardInput.Close();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0 && File.Exists(link);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- the comparison and the path prefix ----

    /// <summary>A stream with a content of its own that reports a length of its own: it shrinks or grows "during" a comparison.</summary>
    private sealed class MisreportingStream(byte[] content, long reportedLength) : MemoryStream(content)
    {
        public override long Length => reportedLength;
    }

    [Fact] public void SameContent_is_true_only_for_streams_that_end_together_with_the_same_bytes()
    {
        Assert.True(ProjectTrash.SameContent(new MemoryStream([1, 2, 3]), new MemoryStream([1, 2, 3])));
        Assert.True(ProjectTrash.SameContent(new MemoryStream([]), new MemoryStream([])));
        Assert.False(ProjectTrash.SameContent(new MemoryStream([1, 2, 3]), new MemoryStream([1, 2, 4])));
        Assert.False(ProjectTrash.SameContent(new MemoryStream([1, 2, 3]), new MemoryStream([1, 2, 3, 4])));

        // The second stream reports the same length but ends early, or carries more than it reported.
        Assert.False(ProjectTrash.SameContent(new MemoryStream([1, 2, 3, 4]), new MisreportingStream([1, 2, 3], 4)));
        Assert.False(ProjectTrash.SameContent(new MemoryStream([1, 2, 3]), new MisreportingStream([1, 2, 3, 4], 3)));
        Assert.False(ProjectTrash.SameContent(new MisreportingStream([1, 2, 3], 4), new MemoryStream([1, 2, 3, 4])));

        // More than one block, with the difference in the last byte.
        var big = new byte[200_000];
        var other = (byte[])big.Clone();
        other[^1] = 1;
        Assert.True(ProjectTrash.SameContent(new MemoryStream(big), new MemoryStream((byte[])big.Clone())));
        Assert.False(ProjectTrash.SameContent(new MemoryStream(big), new MemoryStream(other)));
    }

    [Fact] public void IsBelow_ends_the_prefix_with_exactly_one_separator()
    {
        Assert.True(ProjectTrash.IsBelow(@"D:\", @"D:\x"));
        Assert.True(ProjectTrash.IsBelow(@"D:\", @"D:\x\y"));
        Assert.False(ProjectTrash.IsBelow(@"D:\", @"D:\"));
        Assert.False(ProjectTrash.IsBelow(@"D:\", @"E:\x"));
        Assert.True(ProjectTrash.IsBelow(@"D:\a", @"D:\a\b"));
        Assert.True(ProjectTrash.IsBelow(@"D:\a\", @"D:\a\b"));
        Assert.True(ProjectTrash.IsBelow(@"D:\A", @"d:\a\b"));   // ignoring case
        Assert.False(ProjectTrash.IsBelow(@"D:\a", @"D:\a"));
        Assert.False(ProjectTrash.IsBelow(@"D:\a", @"D:\ab\c"));   // on a folder boundary
    }

    // ---- the trash root ----

    [Fact] public void A_trash_root_that_is_not_a_full_path_a_drive_root_or_a_device_form_is_refused_and_nothing_is_touched()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var time = new FakeTimeProvider(Start);
        // The source does not exist: if a check were missing, nothing could be moved or created, and the result would differ.
        var driveRoot = Path.GetPathRoot(dir.Path)!;
        var roots = new[]
        {
            @".\no<>such",
            @"no<>such\trash",
            @"Z:no<>such",
            driveRoot,
            driveRoot.TrimEnd('\\'),
            @"\\?\" + Path.Combine(dir.Path, "trash"),
            @"\\.\" + Path.Combine(dir.Path, "trash"),
        };
        var before = Snapshot(dir.Path);
        var trashed = new TrashedFile(Id, Path.Combine(dir.Path, "trash", Id.ToString("D"), EntryName(Start), "a.tsv"), "a.tsv");

        // A regression of the root check must fail here, before anything outside the temp dir is changed or deleted (the drive root is real).
        var forbidden = new ProjectTrash.TrashHooks
        {
            BeforeMove = () => throw new InvalidOperationException("the trash root check let a path pass"),
            AfterMove = () => throw new InvalidOperationException("the trash root check let a path pass"),
            BeforeDeleteEntry = _ => throw new InvalidOperationException("the trash root check let a path pass"),
        };
        foreach (var root in roots)
        {
            Assert.Equal(FileOpProblem.UnsafePath, ProjectTrash.MoveToTrash(root, Id, project, "missing.tsv", time, forbidden).Problem);
            Assert.Equal(FileOpProblem.UnsafePath, ProjectTrash.Restore(trashed, project, root, forbidden).Problem);
            Assert.Equal(0, ProjectTrash.Cleanup(root, TimeSpan.Zero, time, CancellationToken.None, forbidden));
        }

        Assert.Equal(before, Snapshot(dir.Path));
    }

    [Fact] public void MoveToTrash_makes_the_missing_folders_of_the_trash_root_one_by_one_and_takes_them_back_on_failure()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = Path.Combine(dir.Path, "app", "data", "trash");
        var locked = Put(project, "locked.tsv", [9]);
        Put(project, "a.tsv", [1]);

        var ok = ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start));
        Assert.Equal(FileOpProblem.None, ok.Problem);
        Assert.True(File.Exists(ok.File!.TrashFile));

        Directory.Delete(Path.Combine(dir.Path, "app"), recursive: true);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = ProjectTrash.MoveToTrash(trash, Id, project, "locked.tsv", new FakeTimeProvider(Start));
            Assert.Equal(FileOpProblem.Locked, result.Problem);
        }

        Assert.False(Directory.Exists(Path.Combine(dir.Path, "app")));   // everything this call made is gone again
    }

    [Fact] public void MoveToTrash_refuses_a_link_above_a_missing_trash_root()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var outside = NewOutside(dir);
        var source = Put(project, "a.tsv", [1]);
        var link = Path.Combine(dir.Path, "linked-parent");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var outsideBefore = Snapshot(outside);

            var result = ProjectTrash.MoveToTrash(Path.Combine(link, "trash"), Id, project, "a.tsv", new FakeTimeProvider(Start));

            Assert.Equal(FileOpProblem.Link, result.Problem);
            Assert.True(File.Exists(source));
            Assert.Equal(outsideBefore, Snapshot(outside));
        }
        finally
        {
            DropLink(link);
        }
    }

    // ---- Cleanup ----

    [Fact] public void Cleanup_deletes_entries_older_than_the_limit_and_keeps_newer_ones()
    {
        using var dir = new TempDir();
        var project = NewProject(dir);
        var trash = NewTrashRoot(dir);
        var other = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var time = new FakeTimeProvider(Start);

        Put(project, "old.tsv", [1]);
        Put(project, "old2.tsv", [1]);
        Put(project, "new.tsv", [2]);
        Put(project, "other.tsv", [3]);
        time.Now = Start.AddDays(-40);
        var old = Trash(trash, project, "old.tsv", time);
        var oldOther = Trash(trash, project, "old2.tsv", time, other);   // the only entry of its project
        time.Now = Start.AddDays(-31).AddMinutes(-1);
        var justOld = Trash(trash, project, "other.tsv", time);
        time.Now = Start.AddDays(-10);
        var recent = Trash(trash, project, "new.tsv", time);
        time.Now = Start;

        var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.False(Directory.Exists(EntryFolder(old)));
        Assert.False(Directory.Exists(EntryFolder(justOld)));
        Assert.False(Directory.Exists(GuidFolder(oldOther)));   // empty project folder removed
        Assert.True(File.Exists(recent.TrashFile));
        Assert.True(Directory.Exists(trash));
        Assert.Equal(0, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None));   // nothing more to do
    }

    [Fact] public void Cleanup_keeps_folders_whose_name_carries_no_time_and_skips_links()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var old = EntryName(Start.AddDays(-100));
        var fullWidth = new string((char)0xFF12, 8) + "-000000-abcdef12";   // digits of another script are no digits here
        var kept = new List<string>
        {
            PutEntry(trash, Id, "not-a-time"),
            PutEntry(trash, Id, "20200101-000000-ABCDEF12"),   // hex must be lower case
            PutEntry(trash, Id, "20200101-000000-abcdef1"),
            PutEntry(trash, Id, "20201340-000000-abcdef12"),   // month 13
            PutEntry(trash, Id, "20200101-250000-abcdef12"),   // hour 25
            PutEntry(trash, Id, fullWidth),
            PutEntry(trash, Id, old + "-x"),
        };
        var notAProject = Path.Combine(trash, "misc", old);   // old name, but the folder above is no GUID
        Directory.CreateDirectory(notAProject);
        var inMisc = Path.Combine(notAProject, "a.tsv");
        File.WriteAllBytes(inMisc, [1]);
        kept.Add(inMisc);
        var looseFile = Path.Combine(trash, old);
        File.WriteAllBytes(looseFile, [1]);
        kept.Add(looseFile);
        var deletedA = PutEntry(trash, Id, old, "a.tsv");
        Directory.CreateDirectory(Path.Combine(trash, Id.ToString("D"), old, "sub"));
        var deletedB = PutEntry(trash, Guid.Empty, old, "b.tsv");   // the only entry of its project

        // Links at both levels: old names behind them, which must stay untouched.
        var otherProject = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var oldBehind = EntryName(Start.AddDays(-101));
        var behindProject = Path.Combine(dir.Path, "behind-project");
        var behindProjectFile = Path.Combine(behindProject, oldBehind, "x.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(behindProjectFile)!);
        File.WriteAllBytes(behindProjectFile, [1]);
        var behindEntry = Path.Combine(dir.Path, "behind-entry");
        Directory.CreateDirectory(behindEntry);
        File.WriteAllBytes(Path.Combine(behindEntry, "y.tsv"), [2]);
        var projectLink = Path.Combine(trash, otherProject.ToString("D"));
        var entryLink = Path.Combine(trash, Id.ToString("D"), oldBehind);
        var madeProjectLink = JunctionHelper.TryCreate(projectLink, behindProject);
        var madeEntryLink = JunctionHelper.TryCreate(entryLink, behindEntry);

        try
        {
            var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None);

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(deletedA));
            Assert.False(File.Exists(deletedB));
            Assert.All(kept, p => Assert.True(File.Exists(p), p));
            Assert.Equal([1], File.ReadAllBytes(behindProjectFile));
            Assert.Equal([2], File.ReadAllBytes(Path.Combine(behindEntry, "y.tsv")));
            Assert.Equal(madeProjectLink, Directory.Exists(projectLink));
            Assert.Equal(madeEntryLink, Directory.Exists(entryLink));
        }
        finally
        {
            DropLink(entryLink);
            DropLink(projectLink);
        }
    }

    [Fact] public void Cleanup_removes_a_link_inside_an_old_entry_as_a_link_and_never_enters_it()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var outside = NewOutside(dir);
        var entry = Path.GetDirectoryName(PutEntry(trash, Id, EntryName(Start.AddDays(-100)), "a.tsv"))!;
        Directory.CreateDirectory(Path.Combine(entry, "sub"));
        File.WriteAllBytes(Path.Combine(entry, "sub", "deep.tsv"), [1]);
        var link = Path.Combine(entry, "sub", "linked");
        if (!JunctionHelper.TryCreate(link, outside))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            var outsideBefore = Snapshot(outside);

            var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None);

            Assert.Equal(1, deleted);
            Assert.False(Directory.Exists(entry));   // the link and everything else of the entry is gone
            Assert.Equal(outsideBefore, Snapshot(outside));
            Assert.Equal([7, 7, 7], File.ReadAllBytes(Path.Combine(outside, "a.tsv")));
        }
        finally
        {
            DropLink(link);
        }
    }

    [Fact] public void Cleanup_checks_the_folder_of_the_project_again_right_before_it_deletes()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var old = EntryName(Start.AddDays(-100));
        PutEntry(trash, Id, old, "a.tsv");
        var behind = Path.Combine(dir.Path, "behind");
        var behindFile = Path.Combine(behind, old, "x.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(behindFile)!);
        File.WriteAllBytes(behindFile, [1]);
        var behindBefore = Snapshot(behind);
        var guidFolder = Path.Combine(trash, Id.ToString("D"));
        var swapped = false;

        try
        {
            var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None, new ProjectTrash.TrashHooks
            {
                BeforeDeleteEntry = _ =>
                {
                    Directory.Move(guidFolder, Path.Combine(dir.Path, "moved-away"));   // the folder of the project is replaced by a link
                    swapped = JunctionHelper.TryCreate(guidFolder, behind);
                },
            });

            if (!swapped)
            {
                return;   // junctions cannot be created in this environment
            }

            Assert.Equal(0, deleted);
            Assert.Equal(behindBefore, Snapshot(behind));
            Assert.Equal([1], File.ReadAllBytes(behindFile));
            Assert.True(Directory.Exists(guidFolder));   // the link stays: never removed
        }
        finally
        {
            DropLink(guidFolder);
        }
    }

    [Fact] public void Cleanup_keeps_an_entry_with_a_read_only_file_and_goes_on_with_the_next()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var readOnly = PutEntry(trash, Id, EntryName(Start.AddDays(-100), "00000001"), "a.tsv");
        var plain = PutEntry(trash, Id, EntryName(Start.AddDays(-100), "00000002"), "b.tsv");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        try
        {
            var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None);

            Assert.Equal(1, deleted);
            Assert.True(File.Exists(readOnly));   // the attribute is not changed; the entry stays
            Assert.True(File.GetAttributes(readOnly).HasFlag(FileAttributes.ReadOnly));
            Assert.False(File.Exists(plain));
            Assert.True(Directory.Exists(Path.Combine(trash, Id.ToString("D"))));   // not empty: stays
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }

    [Fact] public void CleanupDetailed_names_every_entry_it_could_not_delete_with_a_reason()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var readOnly = PutEntry(trash, Id, EntryName(Start.AddDays(-100), "00000001"), "a.tsv");
        var held = PutEntry(trash, Id, EntryName(Start.AddDays(-100), "00000002"), "b.tsv");
        var plain = PutEntry(trash, Id, EntryName(Start.AddDays(-100), "00000003"), "c.tsv");
        var young = PutEntry(trash, Id, EntryName(Start.AddDays(-1), "00000004"), "d.tsv");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        try
        {
            TrashCleanupResult result;
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = ProjectTrash.CleanupDetailed(trash, TimeSpan.FromDays(30), time, CancellationToken.None);
            }

            Assert.Equal(1, result.Deleted);
            Assert.False(File.Exists(plain));
            Assert.True(File.Exists(young));                                     // too young: no failure, just not due
            Assert.Equal(
                [Path.GetDirectoryName(readOnly)!, Path.GetDirectoryName(held)!],
                result.Failed.Select(failure => failure.Entry).Order(StringComparer.Ordinal));
            Assert.All(result.Failed, failure => Assert.False(string.IsNullOrWhiteSpace(failure.Reason)));

            // Cleanup keeps its answer: the number of deleted entries.
            File.SetAttributes(readOnly, FileAttributes.Normal);
            Assert.Equal(2, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None));
            Assert.Empty(ProjectTrash.CleanupDetailed(trash, TimeSpan.FromDays(30), time, CancellationToken.None).Failed);
        }
        finally
        {
            if (File.Exists(readOnly))
            {
                File.SetAttributes(readOnly, FileAttributes.Normal);
            }
        }
    }

    [Fact] public void Cleanup_removes_an_empty_project_folder_only_when_this_run_deleted_an_entry_from_it()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var emptyFolder = Path.Combine(trash, Id.ToString("D"));
        Directory.CreateDirectory(emptyFolder);

        Assert.Equal(0, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None));
        Assert.True(Directory.Exists(emptyFolder));

        var other = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        PutEntry(trash, other, EntryName(Start.AddDays(-100)));
        Assert.Equal(1, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(trash, other.ToString("D"))));
        Assert.True(Directory.Exists(emptyFolder));
    }

    [Fact] public void Cleanup_never_follows_a_link_at_either_level()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var old = EntryName(Start.AddDays(-100));
        var behindProject = Path.Combine(dir.Path, "behind-project");
        PutEntry(behindProject, Guid.Empty, "ignored");   // a folder with files; the link points at behindProject\<guid>
        var projectTarget = Path.Combine(behindProject, Guid.Empty.ToString("D"));
        Directory.CreateDirectory(Path.Combine(projectTarget, old));
        File.WriteAllBytes(Path.Combine(projectTarget, old, "x.tsv"), [1]);
        var behindEntry = Path.Combine(dir.Path, "behind-entry");
        Directory.CreateDirectory(behindEntry);
        File.WriteAllBytes(Path.Combine(behindEntry, "y.tsv"), [2]);
        var guidFolder = Path.Combine(trash, Id.ToString("D"));
        Directory.CreateDirectory(guidFolder);
        var guidLink = Path.Combine(trash, Guid.Empty.ToString("D"));
        var entryLink = Path.Combine(guidFolder, old);
        if (!JunctionHelper.TryCreate(guidLink, projectTarget))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            if (!JunctionHelper.TryCreate(entryLink, behindEntry))
            {
                return;
            }

            var behindBefore = Snapshot(dir.Path).Where(p => !p.StartsWith(trash, StringComparison.OrdinalIgnoreCase)).ToArray();

            var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None);

            Assert.Equal(0, deleted);
            Assert.True(Directory.Exists(guidLink));
            Assert.True(Directory.Exists(entryLink));
            Assert.Equal(behindBefore, Snapshot(dir.Path).Where(p => !p.StartsWith(trash, StringComparison.OrdinalIgnoreCase)).ToArray());
            Assert.Equal([2], File.ReadAllBytes(Path.Combine(behindEntry, "y.tsv")));
            Assert.Equal([1], File.ReadAllBytes(Path.Combine(projectTarget, old, "x.tsv")));
        }
        finally
        {
            DropLink(entryLink);
            DropLink(guidLink);
        }
    }

    [Fact] public void Cleanup_of_a_link_as_trash_root_deletes_nothing()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider(Start);
        var behind = Path.Combine(dir.Path, "behind");
        var file = PutEntry(behind, Id, EntryName(Start.AddDays(-100)));
        var trash = NewTrashRoot(dir);
        if (!JunctionHelper.TryCreate(trash, behind))
        {
            return;   // junctions cannot be created in this environment
        }

        try
        {
            Assert.Equal(0, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None));
            Assert.True(File.Exists(file));
        }
        finally
        {
            DropLink(trash);
        }
    }

    [Fact] public void Cleanup_never_calls_an_entry_from_the_future_older_and_a_cancelled_run_deletes_nothing()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);
        var time = new FakeTimeProvider(Start);
        var future = PutEntry(trash, Id, EntryName(Start.AddDays(5)));
        var old = PutEntry(trash, Id, EntryName(Start.AddDays(-100)), "b.tsv");

        Assert.Equal(0, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(-1000), time, CancellationToken.None));
        Assert.True(File.Exists(future));
        Assert.True(File.Exists(old));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(0, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, cancelled.Token));
        Assert.True(File.Exists(old));

        Assert.Equal(1, ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None));
        Assert.True(File.Exists(future));
        Assert.False(File.Exists(old));
    }

    [Fact] public void Cleanup_of_a_missing_trash_root_returns_zero()
    {
        using var dir = new TempDir();
        var trash = NewTrashRoot(dir);

        var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), new FakeTimeProvider(Start), CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.False(Directory.Exists(trash));
    }
}
