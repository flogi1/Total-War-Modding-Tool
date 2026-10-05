using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// One entry of the context menu of a node. <paramref name="DisabledReason"/> says why the entry cannot be used right now (null when it can,
/// or when the only reason is that another operation runs); it is shown as the tool tip of the disabled item.
/// </summary>
public sealed record NodeAction(string Header, ICommand Command, string? DisabledReason)
{
    /// <summary>The line between two groups of entries of a menu. It has no header, cannot be run and is shown as a separator.</summary>
    public static NodeAction Separator { get; } = new(string.Empty, new RelayCommand(() => { }, () => false), null) { IsSeparator = true };

    /// <summary>The entry is a <see cref="Separator"/>, not a command.</summary>
    public bool IsSeparator { get; init; }
}
