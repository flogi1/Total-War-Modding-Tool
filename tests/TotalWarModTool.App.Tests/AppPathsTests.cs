namespace TotalWarModTool.App.Tests;

public class AppPathsTests
{
    [Fact] public void Default_paths_end_with_the_app_folder()
    {
        var p = AppPaths.Default();
        Assert.EndsWith(@"\TotalWarModTool", p.RoamingRoot); Assert.EndsWith(@"\TotalWarModTool", p.LocalRoot);
        Assert.Equal(Path.Combine(p.ProgramRoot, "locales"), p.LocalesDirectory);
    }
}
