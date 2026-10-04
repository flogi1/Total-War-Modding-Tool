namespace TotalWarModTool.Projects.Tests;

public class PackInstallerTests
{
    private static readonly IReadOnlySet<string> Vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data.pack" };

    private static (string Built, string Data) Setup(TempDir dir)
    {
        var data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        File.WriteAllBytes(dir.File("built.pack"), [1, 2, 3]);
        return (dir.File("built.pack"), data);
    }

    [Fact] public void Install_update_and_uninstall()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        Assert.Equal(InstallProblem.None, first.Problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(data, "my_mod.pack")));
        var info = new FileInfo(Path.Combine(data, "my_mod.pack"));
        Assert.Equal(new InstalledPack("my_mod.pack", 3, info.LastWriteTimeUtc), first.Installed);
        Assert.Equal(DateTimeKind.Utc, first.Installed!.LastWriteTimeUtc.Kind);

        File.WriteAllBytes(built, [4, 5, 6, 7]);
        var second = PackInstaller.Install(built, new InstallRequest(data, "MY_MOD.pack", first.Installed, Vanilla), CancellationToken.None);
        Assert.Equal((InstallProblem.None, 4L), (second.Problem, second.Installed!.Length));
        Assert.Equal("MY_MOD.pack", second.Installed.FileName);
        Assert.Equal(DateTimeKind.Utc, second.Installed.LastWriteTimeUtc.Kind);
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

        var removed = PackInstaller.Uninstall(new InstallRequest(data, "my_mod.pack", second.Installed, Vanilla));
        Assert.Equal((InstallProblem.None, null), (removed.Problem, removed.Installed));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
        Assert.Equal(InstallProblem.None, PackInstaller.Uninstall(new InstallRequest(data, "my_mod.pack", second.Installed, Vanilla)).Problem);
    }

    [Fact] public void A_foreign_or_changed_file_is_never_touched()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var target = Path.Combine(data, "my_mod.pack");
        File.WriteAllBytes(target, [9, 9]);
        var time = File.GetLastWriteTimeUtc(target);

        foreach (var recorded in new InstalledPack?[]
                 {
                     null,
                     new("other.pack", 2, time),
                     new("my_mod.pack", 3, time),
                     new("my_mod.pack", 2, time.AddTicks(1)),
                 })
        {
            var request = new InstallRequest(data, "my_mod.pack", recorded, Vanilla);
            Assert.Equal(InstallProblem.ForeignFile, PackInstaller.CheckOwnership(request));
            Assert.Equal(InstallProblem.ForeignFile, PackInstaller.Install(built, request, CancellationToken.None).Problem);
            Assert.Equal(InstallProblem.ForeignFile, PackInstaller.Uninstall(request).Problem);
        }

        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(target));
        Assert.Equal(time, File.GetLastWriteTimeUtc(target));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
        Assert.Equal(InstallProblem.None, PackInstaller.CheckOwnership(new InstallRequest(data, "my_mod.pack", new InstalledPack("MY_MOD.PACK", 2, time), Vanilla)));
    }

    [Fact] public void Problems_before_anything_is_written()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        Assert.Equal(InstallProblem.VanillaName, PackInstaller.Install(built, new InstallRequest(data, "DATA.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.VanillaName, PackInstaller.Uninstall(new InstallRequest(data, "data.pack", null, Vanilla)).Problem);
        Assert.Equal(InstallProblem.InvalidPackName, PackInstaller.Install(built, new InstallRequest(data, @"..\x.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.DataFolderMissing, PackInstaller.Install(built, new InstallRequest(Path.Combine(dir.Path, "nope"), "a.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.NoBuiltPack, PackInstaller.Install(dir.File("missing.pack"), new InstallRequest(data, "a.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }

    [Fact] public void A_locked_target_is_reported_and_stays()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        File.WriteAllBytes(built, [4, 5]);
        var oldTime = File.GetLastWriteTimeUtc(Path.Combine(data, "my_mod.pack"));
        using (new FileStream(Path.Combine(data, "my_mod.pack"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var request = new InstallRequest(data, "my_mod.pack", first.Installed, Vanilla);
            Assert.Equal(InstallProblem.TargetUnwritable, PackInstaller.Install(built, request, CancellationToken.None).Problem);
            Assert.Equal(InstallProblem.TargetUnwritable, PackInstaller.Uninstall(request).Problem);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(data, "my_mod.pack")));
        Assert.Equal(oldTime, File.GetLastWriteTimeUtc(Path.Combine(data, "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void The_vanilla_check_does_not_depend_on_the_comparer_of_the_set()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        IReadOnlySet<string> ordinal = new HashSet<string>(StringComparer.Ordinal) { "data.pack" };
        var request = new InstallRequest(data, "DATA.pack", null, ordinal);
        Assert.Equal(InstallProblem.VanillaName, PackInstaller.CheckOwnership(request));
        Assert.Equal(InstallProblem.VanillaName, PackInstaller.Install(built, request, CancellationToken.None).Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }

    [Fact] public void A_folder_named_like_the_pack_is_foreign_and_stays()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        Directory.CreateDirectory(Path.Combine(data, "my_mod.pack"));
        var request = new InstallRequest(data, "my_mod.pack", null, Vanilla);
        Assert.Equal(InstallProblem.ForeignFile, PackInstaller.CheckOwnership(request));
        Assert.Equal(InstallProblem.ForeignFile, PackInstaller.Install(built, request, CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.ForeignFile, PackInstaller.Uninstall(request).Problem);
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
        Assert.True(Directory.Exists(Path.Combine(data, "my_mod.pack")));
    }

    [Fact] public void A_relative_or_empty_data_folder_is_missing()
    {
        using var dir = new TempDir();
        var (built, _) = Setup(dir);
        foreach (var folder in new[] { ".", string.Empty })
        {
            var request = new InstallRequest(folder, "my_mod.pack", null, Vanilla);
            Assert.Equal(InstallProblem.DataFolderMissing, PackInstaller.CheckOwnership(request));
            Assert.Equal(InstallProblem.DataFolderMissing, PackInstaller.Install(built, request, CancellationToken.None).Problem);
            Assert.Equal(InstallProblem.DataFolderMissing, PackInstaller.Uninstall(request).Problem);
        }
    }

    [Fact] public void Uninstall_of_a_missing_file_does_nothing()
    {
        using var dir = new TempDir();
        var (_, data) = Setup(dir);
        File.WriteAllBytes(Path.Combine(data, "my_mod.pack.twmt-tmp"), [8]);
        File.WriteAllBytes(Path.Combine(data, "other.pack"), [9]);
        var result = PackInstaller.Uninstall(new InstallRequest(data, "my_mod.pack", null, Vanilla));
        Assert.Equal((InstallProblem.None, null), (result.Problem, result.Installed));
        Assert.Equal(["my_mod.pack.twmt-tmp", "other.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName).Order());
    }

    [Fact] public void A_stale_temp_file_is_replaced_by_the_new_pack()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        File.WriteAllBytes(Path.Combine(data, "my_mod.pack.twmt-tmp"), [7, 7, 7, 7, 7, 7, 7, 7]);
        var result = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        Assert.Equal(InstallProblem.None, result.Problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(data, "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void A_foreign_file_that_appears_before_the_first_install_is_never_overwritten()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var target = Path.Combine(data, "my_mod.pack");
        var result = PackInstaller.Install(
            built,
            new InstallRequest(data, "my_mod.pack", null, Vanilla),
            CancellationToken.None,
            () => File.WriteAllBytes(target, [9, 9]));
        Assert.Equal(InstallProblem.ForeignFile, result.Problem);
        Assert.Null(result.Installed);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(target));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void An_owned_file_that_changes_before_the_replace_is_never_overwritten()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var target = Path.Combine(data, "my_mod.pack");
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        var result = PackInstaller.Install(
            built,
            new InstallRequest(data, "my_mod.pack", first.Installed, Vanilla),
            CancellationToken.None,
            () => File.WriteAllBytes(target, [7, 7, 7, 7, 7]));
        Assert.Equal(InstallProblem.ForeignFile, result.Problem);
        Assert.Null(result.Installed);
        Assert.Equal(new byte[] { 7, 7, 7, 7, 7 }, File.ReadAllBytes(target));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void An_owned_file_that_vanishes_before_the_replace_is_installed_without_overwriting()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var target = Path.Combine(data, "my_mod.pack");
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        var result = PackInstaller.Install(
            built,
            new InstallRequest(data, "my_mod.pack", first.Installed, Vanilla),
            CancellationToken.None,
            () => File.Delete(target));
        Assert.Equal(InstallProblem.None, result.Problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void A_temp_file_that_cannot_be_removed_is_named_in_the_detail()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var temp = Path.Combine(data, "my_mod.pack.twmt-tmp");
        FileStream? holder = null;
        try
        {
            var result = PackInstaller.Install(
                built,
                new InstallRequest(data, "my_mod.pack", null, Vanilla),
                CancellationToken.None,
                () => holder = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None));
            Assert.Equal(InstallProblem.TargetUnwritable, result.Problem);
            Assert.Contains("my_mod.pack.twmt-tmp", result.Detail);
            Assert.True(File.Exists(temp));
            Assert.False(File.Exists(Path.Combine(data, "my_mod.pack")));
        }
        finally
        {
            holder?.Dispose();
        }
    }

    [Fact] public void A_built_pack_that_cannot_be_read_is_reported()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        using (new FileStream(built, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
            Assert.Equal(InstallProblem.NoBuiltPack, result.Problem);
            Assert.False(string.IsNullOrEmpty(result.Detail));
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }

    [Fact] public void The_built_pack_must_not_be_the_target_or_the_temp_file()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        var request = new InstallRequest(data, "my_mod.pack", first.Installed, Vanilla);
        var target = Path.Combine(data, "my_mod.pack");
        Assert.Throws<ArgumentException>(() => PackInstaller.Install(target, request, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PackInstaller.Install(target.ToUpperInvariant(), request, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PackInstaller.Install(target + ".twmt-tmp", request, CancellationToken.None));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void Cancel_leaves_no_temp_file()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), cts.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }
}
