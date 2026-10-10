using TotalWarModTool.Storage;

namespace TotalWarModTool.Session;

/// <summary>
/// Loads and saves the <see cref="SessionState"/> of one game as session.json in the folder of that game below the roaming root.
/// A broken file never blocks the start: it is copied to a .bad file and the empty state is used. Unusable values in a readable
/// file are repaired silently on load.
/// </summary>
public sealed class SessionStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "session.json";
    public const int MaxTabs = 100;

    /// <summary>The longest history of an object tab that is kept (the limit of the tab itself).</summary>
    public const int MaxObjectHistory = 50;

    public SessionStore(string roamingRoot, string profileId)
    {
        FilePath = Path.Combine(roamingRoot, "games", profileId, FileName);
    }

    public string FilePath { get; }

    public SessionLoadResult Load()
    {
        var result = JsonFileStore.Load(FilePath, SchemaVersion, () => new SessionFile());
        return new SessionLoadResult(Repair(result.Value), result.Status, result.BadCopyPath);
    }

    /// <summary>Writes the state as given (repairs happen on load). Creates the game folder when it is missing.</summary>
    public void Save(SessionState state)
    {
        var file = new SessionFile
        {
            Tabs = state.Tabs.Select(t => (TabFile?)ToFile(t)).ToList(),
            ActiveTab = state.ActiveTab,
            ExpandedPacks = state.ExpandedPacks.Select(p => (string?)p).ToList(),
        };
        JsonFileStore.Save(FilePath, SchemaVersion, file);
    }

    // Every member of the file is nullable: System.Text.Json delivers JSON null and missing properties as null, whatever the annotations say.
    private static SessionState Repair(SessionFile file)
    {
        var tabs = new List<SessionTab>();
        var seen = new HashSet<(string, string)>();
        var storedActive = file.ActiveTab ?? -1;
        var activeIndex = -1;   // index in the repaired list of the tab the file marked as active, -1 if that tab did not survive

        var rawTabs = file.Tabs ?? [];
        for (var rawIndex = 0; rawIndex < rawTabs.Count && tabs.Count < MaxTabs; rawIndex++)
        {
            if (rawTabs[rawIndex] is not { PackPath: { Length: > 0 } packPath, EntryPath: { Length: > 0 } entryPath } rawTab)
            {
                continue;
            }

            // Object tabs are not unique by (pack, entry): two tabs may show the same object, each with a history of its own.
            SessionObjectTab? objectTab = null;
            if (rawTab.Object is not null)
            {
                objectTab = RepairObject(rawTab.Object);
                if (objectTab is null)
                {
                    continue;
                }
            }
            else if (!seen.Add((packPath.ToUpperInvariant(), entryPath.ToUpperInvariant())))
            {
                continue;
            }

            if (rawIndex == storedActive)
            {
                activeIndex = tabs.Count;
            }

            tabs.Add(new SessionTab(packPath, entryPath, objectTab));
        }

        var expanded = new List<string>();
        var seenPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in file.ExpandedPacks ?? [])
        {
            if (!string.IsNullOrEmpty(pack) && seenPacks.Add(pack))
            {
                expanded.Add(pack);
            }
        }

        var active = tabs.Count == 0 ? -1 : Math.Max(activeIndex, 0);
        return new SessionState(tabs, active, expanded);
    }

    private static TabFile ToFile(SessionTab tab) => new()
    {
        PackPath = tab.PackPath,
        EntryPath = tab.EntryPath,
        Object = tab.Object is null
            ? null
            : new ObjectFile
            {
                History = tab.Object.History.Select(entry => (RefFile?)new RefFile
                {
                    Table = entry.Table,
                    KeyValues = entry.KeyValues.Select(value => (string?)value).ToList(),
                    RootPackPath = entry.RootPackPath,
                }).ToList(),
                Position = tab.Object.Position,
            },
    };

    // Null when the tab cannot be used: no history, or an entry without a table or without key values (a null key value included).
    // Only the last MaxObjectHistory entries are kept (the same limit as the tab itself); the position follows its entry and is clamped.
    private static SessionObjectTab? RepairObject(ObjectFile file)
    {
        if (file.History is not { Count: > 0 } rawHistory)
        {
            return null;
        }

        var history = new List<SessionObjectRef>();
        foreach (var raw in rawHistory)
        {
            if (raw is not { Table: { Length: > 0 } table, KeyValues: { Count: > 0 } rawKey } || rawKey.Any(value => value is null))
            {
                return null;
            }

            history.Add(new SessionObjectRef(table, rawKey.Select(value => value!).ToList(), raw.RootPackPath));
        }

        var position = file.Position ?? 0;
        if (history.Count > MaxObjectHistory)
        {
            var cut = history.Count - MaxObjectHistory;
            history.RemoveRange(0, cut);
            position -= cut;
        }

        return new SessionObjectTab(history, Math.Clamp(position, 0, history.Count - 1));
    }

    // The file format. Separate from the model so that the file can be read leniently. schemaVersion is added by JsonFileStore.
    private sealed class SessionFile
    {
        public List<TabFile?>? Tabs { get; set; }
        public int? ActiveTab { get; set; }
        public List<string?>? ExpandedPacks { get; set; }
    }

    private sealed class TabFile
    {
        public string? PackPath { get; set; }
        public string? EntryPath { get; set; }
        public ObjectFile? Object { get; set; }
    }

    private sealed class ObjectFile
    {
        public List<RefFile?>? History { get; set; }
        public int? Position { get; set; }
    }

    private sealed class RefFile
    {
        public string? Table { get; set; }
        public List<string?>? KeyValues { get; set; }
        public string? RootPackPath { get; set; }
    }
}
