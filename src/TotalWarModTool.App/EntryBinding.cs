using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// What a toolbar button or a tile of one catalog id runs and shows: the command, the label, and the reason why the command is disabled at
/// the moment (null when the entry has no reason of its own, or the reason is null or empty when it can run).
/// </summary>
public sealed record EntryBinding(ICommand Command, string Label, Func<string?>? Reason);
