using System.Windows;
using Microsoft.Win32;

namespace TotalWarModTool.App;

/// <summary>The WPF side of <see cref="IProjectDialogs"/>: system dialogs and the two project windows. Wiring only; the logic is in the view models.</summary>
public sealed class ProjectDialogs : IProjectDialogs
{
    private readonly AppTexts _texts;
    private readonly Func<Window?> _owner;

    /// <param name="owner">Returns the window that owns the dialogs (the main window); null when there is none.</param>
    public ProjectDialogs(AppTexts texts, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(owner);

        _texts = texts;
        _owner = owner;
    }

    public string? PickFolder(string title) => PickFolder(title, _owner());

    public string? PickPackFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = _texts.ImportPickTitle,
            Filter = _texts.DialogPackFilter.Replace('|', '/') + "|*.pack",          // a pipe in the translation would break the filter
            CheckFileExists = true,
            Multiselect = false,
        };
        var owner = _owner();
        var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return chosen == true ? dialog.FileName : null;
    }

    public ProjectInput? AskProject(string title, ProjectInput defaults, IReadOnlyList<string> info, Func<ProjectInput, string?> validate)
    {
        ProjectInputWindow? window = null;
        var viewModel = new ProjectInputViewModel(_texts, defaults, info, validate, () => PickFolder(_texts.DialogChooseFolder, window));
        window = new ProjectInputWindow(viewModel, title);
        Own(window);
        return window.ShowDialog() == true ? viewModel.Result : null;
    }

    public void ShowReport(Report report)
    {
        var window = new ReportWindow(report, _texts);
        Own(window);
        window.ShowDialog();
    }

    public void ShowMessage(string title, string message)
    {
        if (_owner() is { IsLoaded: true } owner)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? PickFolder(string title, Window? owner)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return chosen == true ? dialog.FolderName : null;
    }

    // A window can only be owned by one that has been shown; without one it stays on its own.
    private void Own(Window window)
    {
        if (_owner() is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }
    }
}
