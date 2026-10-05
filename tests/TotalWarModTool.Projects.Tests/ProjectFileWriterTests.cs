using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects.Tests;

public class ProjectFileWriterTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

    [Fact] public void WriteText_replaces_the_file_and_returns_its_fingerprint()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, @"db\units_tables\a.tsv", [1, 2, 3]);
        var text = "first line\nGr\u00fc\u00dfe \u20ac\nlast\n";

        var result = ProjectFileWriter.WriteText(dir.Path, @"db\units_tables\a.tsv", text);

        Assert.Equal(FileWriteProblem.None, result.Problem);
        Assert.Null(result.Detail);
        var bytes = File.ReadAllBytes(file);
        Assert.Equal(Utf8NoBom.GetBytes(text), bytes);   // UTF-8 without BOM, exactly the text, line endings as given
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(FileFingerprint.TryRead(file), result.Fingerprint);
        Assert.Equal(bytes.Length, result.Fingerprint!.Length);
        Assert.Equal(result.Fingerprint, ProjectFileWriter.Fingerprint(dir.Path, @"db\units_tables\a.tsv"));
    }

    [Fact] public void WriteText_accepts_slashes_and_an_empty_text()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, @"text\a.loc.tsv", [9]);

        var result = ProjectFileWriter.WriteText(dir.Path, "text/a.loc.tsv", string.Empty);

        Assert.Equal(FileWriteProblem.None, result.Problem);
        Assert.Empty(File.ReadAllBytes(file));
        Assert.Equal(0, result.Fingerprint!.Length);
    }

    [Fact] public void The_fingerprint_is_the_same_for_every_spelling_of_the_path()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"a\x.tsv", [1]);

        var result = ProjectFileWriter.WriteText(dir.Path, @"a\x.tsv", "text");

        Assert.Equal(result.Fingerprint, ProjectFileWriter.Fingerprint(dir.Path, "a/x.tsv"));
        Assert.Equal(result.Fingerprint, ProjectFileWriter.Fingerprint(dir.Path + Path.DirectorySeparatorChar, @"a\x.tsv"));
    }

    [Fact] public void WriteText_creates_a_missing_file_in_an_existing_folder()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(PackFile(dir.Path, "text"));

        var result = ProjectFileWriter.WriteText(dir.Path, @"text\new.loc.tsv", "x\n");

        Assert.Equal(FileWriteProblem.None, result.Problem);
        Assert.Equal(Utf8NoBom.GetBytes("x\n"), File.ReadAllBytes(PackFile(dir.Path, @"text\new.loc.tsv")));
        Assert.NotNull(result.Fingerprint);
    }

    [Fact] public void A_missing_folder_is_Failed_and_nothing_is_created()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(PackFile(dir.Path, "other"));
        var before = Snapshot(dir.Path);

        var noSubFolder = ProjectFileWriter.WriteText(dir.Path, @"missing\deep\a.tsv", "x");
        Assert.Equal(FileWriteProblem.Failed, noSubFolder.Problem);
        Assert.False(string.IsNullOrWhiteSpace(noSubFolder.Detail));
        Assert.Null(noSubFolder.Fingerprint);
        Assert.Equal(before, Snapshot(dir.Path));

        using var other = new TempDir();
        var noPackFolder = ProjectFileWriter.WriteText(other.Path, "a.tsv", "x");
        Assert.Equal(FileWriteProblem.Failed, noPackFolder.Problem);
        Assert.Empty(Directory.GetFileSystemEntries(other.Path));
    }

    // "{tmp}" stands for the temp dir of the test, "{tmpNoDrive}" for the same path without its drive letter and colon (a rooted path
    // on the current drive). If the refusal were missing, Path.Combine(packFolder, relativeFile) would point as follows for each row:
    //   ""                          the pack folder itself (a folder, the write fails)
    //   "..\x.tsv", "a/../../x"     a file in the project folder or its parent-of-pack level, inside the temp dir
    //   {tmp}\..., \\?\{tmp}\...    the same files inside the temp dir (a rooted path replaces the pack folder)
    //   {tmpNoDrive}\...            the same, on the current drive: the temp dir again
    //   "a.tsv:stream"              a stream of pack\a.tsv, inside the temp dir
    //   "x.tsv.", "x.tsv "          pack\x.tsv (Windows trims the dot and the space), inside the temp dir
    //   "NUL.tsv"                   the NUL device, which discards everything; it names no location
    //   "a\\b", "a\\b.tsv"          pack\a\b (doubled separators collapse), inside the temp dir
    //   ".", "sub\."                the pack folder and pack\sub (folders, the write fails)
    // Forms that could name a place outside the temp dir (a drive-relative path such as "C:x", a UNC path) are not part of this
    // theory; PackFolderPathsTests covers them on ResolveForWrite, which writes nothing.
    [Theory]
    [InlineData("")]
    [InlineData("..\\x.tsv")]
    [InlineData("a/../../x")]
    [InlineData("{tmp}\\outside.tsv")]
    [InlineData("{tmp}\\pack\\x.tsv")]
    [InlineData("\\\\?\\{tmp}\\outside.tsv")]
    [InlineData("\\\\?\\{tmp}\\pack\\x.tsv")]
    [InlineData("{tmpNoDrive}\\outside.tsv")]
    [InlineData("\\\\?\\{tmpNoDrive}\\outside.tsv")]
    [InlineData("a.tsv:stream")]
    [InlineData("x.tsv.")]
    [InlineData("x.tsv ")]
    [InlineData("NUL.tsv")]
    [InlineData("a\\\\b")]
    [InlineData("a\\\\b.tsv")]
    [InlineData(".")]
    [InlineData("sub\\.")]
    public void Unsafe_paths_are_refused_without_touching_the_disk(string relativeFile)
    {
        using var dir = new TempDir();
        Put(dir.Path, @"a\keep.tsv", [1]);
        Directory.CreateDirectory(PackFile(dir.Path, "sub"));
        var outside = Path.Combine(dir.Path, "outside.tsv");
        File.WriteAllBytes(outside, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var noDrive = dir.Path[2..];
        Assert.Equal(':', dir.Path[1]);
        Assert.Equal('\\', noDrive[0]);
        relativeFile = relativeFile.Replace("{tmpNoDrive}", noDrive).Replace("{tmp}", dir.Path);
        var before = Snapshot(dir.Path);

        var result = ProjectFileWriter.WriteText(dir.Path, relativeFile, "changed");

        Assert.Equal(FileWriteProblem.UnsafePath, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], File.ReadAllBytes(outside));
        Assert.Null(ProjectFileWriter.Fingerprint(dir.Path, relativeFile));
    }

    [Fact] public void Argument_errors_are_thrown()
    {
        using var dir = new TempDir();

        Assert.ThrowsAny<ArgumentException>(() => ProjectFileWriter.WriteText(" ", "a.tsv", "x"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileWriter.WriteText(dir.Path, null!, "x"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileWriter.WriteText(dir.Path, "a.tsv", null!));
    }

    [Fact] public void Probe_of_an_existing_file_is_Present_with_the_fingerprint_of_Fingerprint()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"db\units_tables\a.tsv", [1, 2, 3]);

        var probe = ProjectFileWriter.Probe(dir.Path, @"db\units_tables\a.tsv");

        Assert.Equal(FileProbeState.Present, probe.State);
        Assert.Equal(ProjectFileWriter.Fingerprint(dir.Path, @"db\units_tables\a.tsv"), probe.Fingerprint);
        Assert.Equal(3, probe.Fingerprint!.Length);
        Assert.Null(probe.Detail);
    }

    [Fact] public void Probe_of_a_missing_file_a_missing_folder_and_a_missing_pack_folder_is_Missing()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"db\units_tables\a.tsv", [1]);
        using var empty = new TempDir();

        foreach (var (project, file) in new[] { (dir.Path, @"db\units_tables\b.tsv"), (dir.Path, @"other\deep\b.tsv"), (empty.Path, "a.tsv") })
        {
            var probe = ProjectFileWriter.Probe(project, file);

            Assert.Equal(FileProbeState.Missing, probe.State);
            Assert.Null(probe.Fingerprint);
            Assert.Null(probe.Detail);
        }

        File.Delete(PackFile(dir.Path, @"db\units_tables\a.tsv"));
        Assert.Equal(FileProbeState.Missing, ProjectFileWriter.Probe(dir.Path, @"db\units_tables\a.tsv").State);
    }

    [Theory]
    [InlineData(@"..\outside.tsv")]
    [InlineData("")]
    [InlineData("a.tsv:stream")]
    public void Probe_of_an_unsafe_path_is_Unknown_with_a_detail(string relativeFile)
    {
        using var dir = new TempDir();
        Put(dir.Path, "a.tsv", [1]);

        var probe = ProjectFileWriter.Probe(dir.Path, relativeFile);

        Assert.Equal(FileProbeState.Unknown, probe.State);
        Assert.Null(probe.Fingerprint);
        Assert.False(string.IsNullOrWhiteSpace(probe.Detail));
    }

    [Fact] public void Probe_of_a_folder_in_the_place_of_the_file_is_Unknown()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(PackFile(dir.Path, @"db\a.tsv"));

        var probe = ProjectFileWriter.Probe(dir.Path, @"db\a.tsv");

        Assert.Equal(FileProbeState.Unknown, probe.State);
        Assert.False(string.IsNullOrWhiteSpace(probe.Detail));
    }

    [Fact] public void Probe_throws_argument_errors()
    {
        using var dir = new TempDir();

        Assert.ThrowsAny<ArgumentException>(() => ProjectFileWriter.Probe(" ", "a.tsv"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileWriter.Probe(dir.Path, null!));
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
            var viaPack = ProjectFileWriter.WriteText(linkedProject, "x.tsv", "changed");
            Assert.Equal(FileWriteProblem.Link, viaPack.Problem);
            Assert.Contains("pack", viaPack.Detail);
            Assert.Null(viaPack.Fingerprint);
            Assert.Null(ProjectFileWriter.Fingerprint(linkedProject, "x.tsv"));
        }
        finally
        {
            Directory.Delete(packLink);   // the link itself, never the target behind it
        }

        // A linked folder below the pack folder.
        var project = Path.Combine(dir.Path, "project");
        Directory.CreateDirectory(PackFile(project, "plain"));
        var folderLink = PackFile(project, "linked");
        Assert.True(JunctionHelper.TryCreate(folderLink, outside));
        try
        {
            var viaFolder = ProjectFileWriter.WriteText(project, @"linked\x.tsv", "changed");
            Assert.Equal(FileWriteProblem.Link, viaFolder.Problem);
            Assert.Contains("linked", viaFolder.Detail);
            Assert.Null(viaFolder.Fingerprint);
            Assert.Null(ProjectFileWriter.Fingerprint(project, @"linked\x.tsv"));
        }
        finally
        {
            Directory.Delete(folderLink);
        }

        Assert.Equal([7, 7, 7], File.ReadAllBytes(outsideFile));   // the link target is byte-identical
        Assert.Equal(before, Snapshot(outside));
    }

    // Its own test on purpose: a symbolic link to a file needs administrator rights (or developer mode) on Windows. Where it cannot be
    // created this test returns early, and a green run of the junction test above still proves that one ran.
    [Fact] public void A_linked_file_is_refused()
    {
        using var dir = new TempDir();
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "x.tsv");
        File.WriteAllBytes(outsideFile, [7, 7, 7]);
        Directory.CreateDirectory(PackFile(dir.Path, "plain"));
        var fileLink = PackFile(dir.Path, @"plain\x.tsv");
        try
        {
            File.CreateSymbolicLink(fileLink, outsideFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;   // file links cannot be created without the privilege
        }

        try
        {
            var result = ProjectFileWriter.WriteText(dir.Path, @"plain\x.tsv", "changed");

            Assert.Equal(FileWriteProblem.Link, result.Problem);
            Assert.Null(result.Fingerprint);
            Assert.Null(ProjectFileWriter.Fingerprint(dir.Path, @"plain\x.tsv"));
        }
        finally
        {
            File.Delete(fileLink);   // the link itself
        }

        Assert.Equal([7, 7, 7], File.ReadAllBytes(outsideFile));
    }

    // A part of the way whose attributes cannot be read is refused for writes and is not taken for "no link". The attribute read is made
    // to fail with access rules that deny "read attributes" on the file and "list" on its folder (either right alone is enough for the
    // read to succeed); where the rules cannot be set the test returns early.
    [Fact, SupportedOSPlatform("windows")] public void A_path_part_that_cannot_be_checked_is_refused_for_writes_only()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, @"a\x.tsv", [1, 2, 3]);
        var fileInfo = new FileInfo(file);
        var folderInfo = new DirectoryInfo(Path.GetDirectoryName(file)!);
        var user = WindowsIdentity.GetCurrent().User!;
        var fileRule = new FileSystemAccessRule(user, FileSystemRights.ReadAttributes, AccessControlType.Deny);
        var folderRule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);

        // Every rule that was set is taken back before the test leaves, so that the temp dir cleanup can always delete the folder.
        var undo = new Stack<Action>();
        try
        {
            var fileSecurity = fileInfo.GetAccessControl();
            fileSecurity.AddAccessRule(fileRule);
            fileInfo.SetAccessControl(fileSecurity);
            undo.Push(() =>
            {
                fileSecurity.RemoveAccessRule(fileRule);
                fileInfo.SetAccessControl(fileSecurity);
            });

            var folderSecurity = folderInfo.GetAccessControl();
            folderSecurity.AddAccessRule(folderRule);
            folderInfo.SetAccessControl(folderSecurity);
            undo.Push(() =>
            {
                folderSecurity.RemoveAccessRule(folderRule);
                folderInfo.SetAccessControl(folderSecurity);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            RestoreRules(undo);   // the rule that was already added is removed again
            return;   // access rules cannot be set in this environment
        }

        try
        {
            if (!ReadAttributesFails(file))
            {
                return;   // the rules do not make the read fail on this system
            }

            var read = PackFolderPaths.Resolve(dir.Path, @"a\x.tsv");
            var write = PackFolderPaths.ResolveForWrite(dir.Path, @"a\x.tsv");
            var result = ProjectFileWriter.WriteText(dir.Path, @"a\x.tsv", "changed");

            Assert.Equal(PackPathProblem.None, read.Problem);   // the opener's rule is unchanged
            Assert.Equal(PackPathProblem.Link, write.Problem);
            Assert.Equal(@"pack\a\x.tsv", write.LinkPath);
            Assert.Contains("cannot check the folder for links", write.Detail);
            Assert.Equal(FileWriteProblem.Link, result.Problem);
            Assert.Contains("cannot check the folder for links", result.Detail);
            Assert.Null(ProjectFileWriter.Fingerprint(dir.Path, @"a\x.tsv"));
        }
        finally
        {
            RestoreRules(undo);
        }

        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    /// <summary>Runs every undo step, last set first, each one in its own try/finally so that one failing step never skips the others.</summary>
    private static void RestoreRules(Stack<Action> undo)
    {
        if (undo.Count == 0)
        {
            return;
        }

        try
        {
            undo.Pop()();
        }
        finally
        {
            RestoreRules(undo);
        }
    }

    private static bool ReadAttributesFails(string file)
    {
        try
        {
            File.GetAttributes(file);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    [Fact] public void A_locked_file_is_reported_and_left_unchanged()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, @"a\x.tsv", [1, 2, 3]);

        FileWriteResult result;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = ProjectFileWriter.WriteText(dir.Path, @"a\x.tsv", "new text");
        }

        Assert.Equal(FileWriteProblem.Locked, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public void A_read_only_file_is_Locked_and_stays_read_only()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, "x.tsv", [1, 2, 3]);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var result = ProjectFileWriter.WriteText(dir.Path, "x.tsv", "new text");

            Assert.Equal(FileWriteProblem.Locked, result.Problem);
            Assert.Null(result.Fingerprint);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
            Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
            Assert.Empty(TempFilesBelow(dir.Path));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);   // so that the temp folder can be deleted
        }
    }

    [Fact] public void A_folder_in_place_of_the_file_is_Failed_and_leaves_no_temp_file()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(PackFile(dir.Path, "x.tsv"));

        var result = ProjectFileWriter.WriteText(dir.Path, "x.tsv", "new text");

        Assert.Equal(FileWriteProblem.Failed, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.True(Directory.Exists(PackFile(dir.Path, "x.tsv")));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public void No_temp_file_is_left_after_a_successful_write()
    {
        using var dir = new TempDir();
        Put(dir.Path, @"a\x.tsv", [1]);

        var result = ProjectFileWriter.WriteText(dir.Path, @"a\x.tsv", "new text");

        Assert.Equal(FileWriteProblem.None, result.Problem);
        Assert.Equal(["x.tsv"], Directory.GetFiles(PackFile(dir.Path, "a")).Select(Path.GetFileName));
        Assert.Empty(TempFilesBelow(dir.Path));
    }

    [Fact] public void Fingerprint_is_null_for_a_missing_file()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(PackFile(dir.Path, "a"));

        Assert.Null(ProjectFileWriter.Fingerprint(dir.Path, @"a\missing.tsv"));
        Assert.Null(ProjectFileWriter.Fingerprint(dir.Path, @"nofolder\missing.tsv"));
    }

    [Fact] public void Fingerprint_follows_the_file()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, "x.tsv", [1, 2]);

        var fingerprint = ProjectFileWriter.Fingerprint(dir.Path, "x.tsv");

        Assert.Equal(FileFingerprint.TryRead(file), fingerprint);
        Assert.Equal(2, fingerprint!.Length);
    }

    [Fact] public void Files_that_already_have_a_temp_name_are_never_overwritten_or_deleted()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, "x.tsv", [1]);
        var takenFile = Put(dir.Path, "x.tsv.deadbeef.tmp", [9, 9]);
        var takenFolder = PackFile(dir.Path, "x.tsv.feedface.tmp");
        Directory.CreateDirectory(takenFolder);
        var names = new Queue<string>(["deadbeef", "feedface", "cafebabe"]);

        var result = ProjectFileWriter.WriteText(dir.Path, "x.tsv", "new", names.Dequeue);

        Assert.Equal(FileWriteProblem.None, result.Problem);
        Assert.Equal(Utf8NoBom.GetBytes("new"), File.ReadAllBytes(file));
        Assert.Equal([9, 9], File.ReadAllBytes(takenFile));
        Assert.True(Directory.Exists(takenFolder));
        Assert.Equal([takenFile], TempFilesBelow(dir.Path));   // the one of this write is gone, the strangers stay
    }

    [Fact] public void When_every_temp_name_is_taken_the_write_is_Failed_and_nothing_is_touched()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, "x.tsv", [1]);
        var taken = Put(dir.Path, "x.tsv.deadbeef.tmp", [9, 9]);

        var result = ProjectFileWriter.WriteText(dir.Path, "x.tsv", "new", () => "deadbeef");

        Assert.Equal(FileWriteProblem.Failed, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.Equal([1], File.ReadAllBytes(file));
        Assert.Equal([9, 9], File.ReadAllBytes(taken));
    }

    [Fact] public void Text_with_a_lone_surrogate_is_Failed_and_nothing_is_written()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, "x.tsv", [1, 2, 3]);
        var before = Snapshot(dir.Path);
        var lonelyHigh = "ab" + (char)0xD800 + "cd";
        var lonelyLow = "ab" + (char)0xDC00;

        foreach (var text in new[] { lonelyHigh, lonelyLow })
        {
            var result = ProjectFileWriter.WriteText(dir.Path, "x.tsv", text);

            Assert.Equal(FileWriteProblem.Failed, result.Problem);
            Assert.Contains("UTF-16", result.Detail);
            Assert.Null(result.Fingerprint);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
            Assert.Empty(TempFilesBelow(dir.Path));
            Assert.Equal(before, Snapshot(dir.Path));
        }
    }

    [Fact] public void A_well_formed_surrogate_pair_is_written_as_one_character()
    {
        using var dir = new TempDir();
        var file = Put(dir.Path, "x.tsv", [1]);
        var text = "a" + char.ConvertFromUtf32(0x1F600) + "b";

        var result = ProjectFileWriter.WriteText(dir.Path, "x.tsv", text);

        Assert.Equal(FileWriteProblem.None, result.Problem);
        Assert.Equal([(byte)'a', 0xF0, 0x9F, 0x98, 0x80, (byte)'b'], File.ReadAllBytes(file));
    }
}
