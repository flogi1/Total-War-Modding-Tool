using TotalWarModTool.Storage;

namespace TotalWarModTool.Session;

/// <summary>One object in the history of an object tab: the table, the key values of the row and the pack the chain started from (null: none).</summary>
public sealed record SessionObjectRef(string Table, IReadOnlyList<string> KeyValues, string? RootPackPath);

/// <summary>The part of an object tab that is stored. History has at least one entry; Position indexes it.</summary>
public sealed record SessionObjectTab(IReadOnlyList<SessionObjectRef> History, int Position);

/// <summary>
/// One open tab: the entry <paramref name="EntryPath"/> inside the pack at <paramref name="PackPath"/>. An object tab has the pack path
/// "object:&lt;table&gt;", the id of its first entry as entry path and <paramref name="Object"/> set; it is null for a file tab.
/// </summary>
public sealed record SessionTab(string PackPath, string EntryPath, SessionObjectTab? Object = null);

/// <summary>What is restored after a restart: open tabs, the active tab (index, -1 without tabs) and the expanded packs.</summary>
public sealed record SessionState(IReadOnlyList<SessionTab> Tabs, int ActiveTab, IReadOnlyList<string> ExpandedPacks)
{
    public static SessionState Empty { get; } = new([], -1, []);
}

public sealed record SessionLoadResult(SessionState State, LoadStatus Status, string? BadCopyPath);
