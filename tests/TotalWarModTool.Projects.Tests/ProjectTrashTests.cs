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
        }

        Assert.Equal([9, 9], File.ReadAllBytes(locked));
        Assert.Empty(Snapshot(trash));   // the entry folder and the project folder were taken back
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

        var result = ProjectTrash.Restore(file, project);

        Assert.Equal(FileOpProblem.None, result.Problem);
        Assert.Null(result.Detail);
        Assert.Empty(result.CreatedFolders);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(source));
        Assert.Equal(source, result.Fingerprint!.Path);
        Assert.False(Directory.Exists(EntryFolder(file)));
        Assert.False(Directory.Exists(GuidFolder(file)));   // empty now
        Assert.True(Directory.Exists(trash));
    }

    [Fact] public void Restore_with_the_trash_root_does_the_same_and_keeps_other_entries_of_the_project()
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
        var viaFile = ProjectTrash.Restore(file, project);
        File.Delete(taken);
        Directory.CreateDirectory(taken);
        var viaFolder = ProjectTrash.Restore(file, project);
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
        var missing = ProjectTrash.Restore(file, project);

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

        var elsewhere = ProjectTrash.Restore(new TrashedFile(Id, victim, "victim.tsv"), project);
        var wrongProject = ProjectTrash.Restore(real with { ProjectId = Guid.NewGuid() }, project);
        var wrongName = ProjectTrash.Restore(real with { RelativeFile = "b.tsv" }, project);
        var wrongEntry = ProjectTrash.Restore(new TrashedFile(Id, Path.Combine(GuidFolder(real), "not-an-entry", "a.tsv"), "a.tsv"), project);
        var relativePath = ProjectTrash.Restore(real with { TrashFile = "a.tsv" }, project);
        var other = ProjectTrash.Restore(inOtherTrash, project, trash);
        var otherViaTwo = ProjectTrash.Restore(inOtherTrash with { RelativeFile = @"..\a.tsv" }, project);

        Assert.All(new[] { elsewhere, wrongProject, wrongName, wrongEntry, relativePath, other, otherViaTwo },
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

        var result = ProjectTrash.Restore(file, project);

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
            var result = ProjectTrash.Restore(file, project);

            Assert.Equal(FileOpProblem.Locked, result.Problem);
            Assert.Empty(result.CreatedFolders);
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
            var viaGuidLink = ProjectTrash.Restore(forged, project);
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
            var viaTarget = ProjectTrash.Restore(file with { RelativeFile = @"linked\own.tsv" }, project);
            Assert.Equal(FileOpProblem.UnsafePath, ProjectTrash.Restore(file with { RelativeFile = @"linked\x.tsv" }, project).Problem);   // the name differs
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
            var result = ProjectTrash.MoveToTrash(trash, Id, project, "a.tsv", new FakeTimeProvider(Start), () =>
            {
                var entry = Directory.GetDirectories(Path.Combine(trash, Id.ToString("D"))).Single();
                Directory.Delete(entry);   // empty: the entry folder of this call
                link = entry;
                swapped = JunctionHelper.TryCreate(entry, outside);
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
            var result = ProjectTrash.Restore(file, project, trash, () =>
            {
                Directory.Delete(link);   // the folder this call made
                swapped = JunctionHelper.TryCreate(link, outside);
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
        File.SetAttributes(old.TrashFile, FileAttributes.ReadOnly);   // must not stop the clean-up
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

        var deleted = ProjectTrash.Cleanup(trash, TimeSpan.FromDays(30), time, CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(deletedA));
        Assert.False(File.Exists(deletedB));
        Assert.All(kept, p => Assert.True(File.Exists(p), p));
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
