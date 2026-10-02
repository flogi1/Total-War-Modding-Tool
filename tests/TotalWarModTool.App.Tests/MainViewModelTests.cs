using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class MainViewModelTests
{
    [Fact] public void OpenLogFolder_opens_the_log_directory()
    {
        var paths = new AppPaths(@"X:\roaming", @"X:\local", @"X:\program");
        string? opened = null;
        var vm = new MainViewModel(new AppTexts(new UiText(@"X:\none")), paths, p => opened = p);
        vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal(@"X:\local\logs", opened);
    }
}
