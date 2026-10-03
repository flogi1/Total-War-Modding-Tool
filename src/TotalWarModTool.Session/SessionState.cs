using TotalWarModTool.Storage;

namespace TotalWarModTool.Session;

/// <summary>One open tab: the entry <paramref name="EntryPath"/> inside the pack at <paramref name="PackPath"/>.</summary>
public sealed record SessionTab(string PackPath, string EntryPath);

/// <summary>What is restored after a restart: open tabs, the active tab (index, -1 without tabs) and the expanded packs.</summary>
public sealed record SessionState(IReadOnlyList<SessionTab> Tabs, int ActiveTab, IReadOnlyList<string> ExpandedPacks)
{
    public static SessionState Empty { get; } = new([], -1, []);
}

public sealed record SessionLoadResult(SessionState State, LoadStatus Status, string? BadCopyPath);
