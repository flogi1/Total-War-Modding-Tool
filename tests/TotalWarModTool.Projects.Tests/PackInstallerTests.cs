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

        File.WriteAllBytes(built, [4, 5, 6, 7]);
        var second = PackInstaller.Install(built, new InstallRequest(data, "MY_MOD.pack", first.Installed, Vanilla), CancellationToken.None);
        Assert.Equal((InstallProblem.None, 4L), (second.Problem, second.Installed!.Length));
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
        using (new FileStream(Path.Combine(data, "my_mod.pack"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var request = new InstallRequest(data, "my_mod.pack", first.Installed, Vanilla);
            Assert.Equal(InstallProblem.TargetUnwritable, PackInstaller.Install(built, request, CancellationToken.None).Problem);
            Assert.Equal(InstallProblem.TargetUnwritable, PackInstaller.Uninstall(request).Problem);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(data, "my_mod.pack")));
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
