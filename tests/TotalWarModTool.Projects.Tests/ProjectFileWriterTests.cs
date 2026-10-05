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

    [Theory]
    [InlineData("")]
    [InlineData("..\\x.tsv")]
    [InlineData("a/../../x")]
    [InlineData("C:\\x.tsv")]
    [InlineData("\\x")]
    [InlineData("a:stream")]
    [InlineData("a\\\\b.tsv")]
    public void Unsafe_paths_are_refused_without_touching_the_disk(string relativeFile)
    {
        using var dir = new TempDir();
        Put(dir.Path, @"a\keep.tsv", [1]);
        File.WriteAllBytes(Path.Combine(dir.Path, "x.tsv"), [2]);
        File.WriteAllBytes(Path.Combine(dir.Path, "x"), [3]);
        var before = Snapshot(dir.Path);

        var result = ProjectFileWriter.WriteText(dir.Path, relativeFile, "changed");

        Assert.Equal(FileWriteProblem.UnsafePath, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Fingerprint);
        Assert.Equal(before, Snapshot(dir.Path));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(dir.Path, "x.tsv")));
        Assert.Null(ProjectFileWriter.Fingerprint(dir.Path, relativeFile));
    }

    [Fact] public void Argument_errors_are_thrown()
    {
        using var dir = new TempDir();

        Assert.ThrowsAny<ArgumentException>(() => ProjectFileWriter.WriteText(" ", "a.tsv", "x"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileWriter.WriteText(dir.Path, null!, "x"));
        Assert.Throws<ArgumentNullException>(() => ProjectFileWriter.WriteText(dir.Path, "a.tsv", null!));
    }

    [Fact] public void A_linked_pack_folder_a_linked_sub_folder_and_a_linked_file_are_refused()
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

        // A linked file: a symbolic link needs a privilege that is not always there, then this part is skipped.
        var fileLink = PackFile(project, @"plain\x.tsv");
        var fileLinkCreated = false;
        try
        {
            File.CreateSymbolicLink(fileLink, outsideFile);
            fileLinkCreated = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not possible in this environment
        }

        if (fileLinkCreated)
        {
            try
            {
                var viaFile = ProjectFileWriter.WriteText(project, @"plain\x.tsv", "changed");
                Assert.Equal(FileWriteProblem.Link, viaFile.Problem);
                Assert.Null(viaFile.Fingerprint);
                Assert.Null(ProjectFileWriter.Fingerprint(project, @"plain\x.tsv"));
            }
            finally
            {
                File.Delete(fileLink);
            }
        }

        Assert.Equal([7, 7, 7], File.ReadAllBytes(outsideFile));   // the link target is byte-identical
        Assert.Equal(before, Snapshot(outside));
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
}
