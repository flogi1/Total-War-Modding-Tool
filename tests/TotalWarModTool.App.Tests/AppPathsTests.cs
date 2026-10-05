namespace TotalWarModTool.App.Tests;

public class AppPathsTests
{
    [Fact] public void Default_paths_end_with_the_app_folder()
    {
        var p = AppPaths.Default();
        Assert.EndsWith(@"\TotalWarModTool", p.RoamingRoot); Assert.EndsWith(@"\TotalWarModTool", p.LocalRoot);
        Assert.Equal(Path.Combine(p.ProgramRoot, "locales"), p.LocalesDirectory);
    }

    [Fact] public void The_trash_is_below_the_local_folder_next_to_the_logs()
    {
        var paths = new AppPaths(@"X:\roaming", @"X:\local", @"X:\program");
        Assert.Equal(@"X:\local\trash", paths.TrashDirectory);
        Assert.Equal(@"X:\local\logs", paths.LogDirectory);
    }

    [Fact] public void Profiles_and_schemas_are_below_the_program_folder()
    {
        var paths = new AppPaths(@"X:\roaming", @"X:\local", @"X:\program");
        Assert.Equal((@"X:\program\profiles", @"X:\program\schemas"), (paths.ProfilesDirectory, paths.SchemasDirectory));
    }
}
