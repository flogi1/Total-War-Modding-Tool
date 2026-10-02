using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>View model of the main window.</summary>
public sealed class MainViewModel : ObservableObject
{
    /// <param name="openFolder">Opens a folder in the file explorer. Passed in so the view model stays free of processes and tests need no explorer.</param>
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder)
    {
        Texts = texts;
        OpenLogFolderCommand = new RelayCommand(() => openFolder(paths.LogDirectory));
    }

    public AppTexts Texts { get; }

    public ICommand OpenLogFolderCommand { get; }
}
