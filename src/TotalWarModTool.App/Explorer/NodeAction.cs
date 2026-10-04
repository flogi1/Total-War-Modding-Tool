using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// One entry of the context menu of a node. <paramref name="DisabledReason"/> says why the entry cannot be used right now (null when it can,
/// or when the only reason is that another operation runs); it is shown as the tool tip of the disabled item.
/// </summary>
public sealed record NodeAction(string Header, ICommand Command, string? DisabledReason);
