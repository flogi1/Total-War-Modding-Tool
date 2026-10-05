using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// One entry of the context menu of a node. <paramref name="DisabledReason"/> says why the entry cannot be used right now (null when it can,
/// or when the only reason is that another operation runs); it is shown as the tool tip of the disabled item.
/// </summary>
public sealed record NodeAction(string Header, ICommand Command, string? DisabledReason)
{
    /// <summary>The line between two groups of entries of a menu. It has no header, cannot be run and is shown as a separator.</summary>
    public static NodeAction Separator { get; } = new(string.Empty, NoCommand.Instance, null) { IsSeparator = true };

    /// <summary>The entry is a <see cref="Separator"/>, not a command.</summary>
    public bool IsSeparator { get; init; }

    // The command of the shared separator: never enabled, and it keeps no subscriber, so the one instance holds no reference to any menu item.
    private sealed class NoCommand : ICommand
    {
        public static readonly NoCommand Instance = new();

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => false;

        public void Execute(object? parameter)
        {
        }
    }
}
