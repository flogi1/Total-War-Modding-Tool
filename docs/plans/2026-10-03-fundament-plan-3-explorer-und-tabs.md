# Fundament Plan 3: Explorer und Tabs – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die App findet das Spiel, zeigt alle Packs nach Herkunft im Explorer, öffnet Einträge als Tabs (Tabelle, Loc, Text, Hinweis), merkt sich die Sitzung und bietet Spielpfad und Schema-Ordner in den Einstellungen.

**Architecture:** Die Lese-Libraries aus Plan 2 bleiben die Fachlogik. `Workspace` bekommt die fehlenden Bausteine (Eintrag öffnen, Dateibaum, Wiederholversuch). In der App hält ein `WorkspaceController` den Zustand eines Spiels (Ort, Katalog, Schema, offene Indexe); Explorer, Tabs, Statuszeile und Einstellungen sind ViewModels darüber. Jede vom Nutzer gestartete Operation läuft über den `ProgressRunner`, der Scan beim Start und das Wiederherstellen der Sitzung laufen still.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-02-fundament.md`. Maßgeblich: 2.1 (Ziele), 2.3 (Erfolgskriterien), 3.5 (Raster), 4.6 (App), 4.7 (Datenfluss), 4.8 (Persistenz), 5 (Fehlerfälle), 8 (Annahmen 5, 10, 13, 14). Projektregeln: `CLAUDE.md`. Hinweise aus den Reviews: `Backlog.txt`, Abschnitt „Hinweise für Plan 2 und 3“.

**Branch:** `feature/fundament-explorer-und-tabs` (von `master`, Stand `445da6c` oder neuer). Baseline vor Task 1: `dotnet test TotalWarModTool.slnx` ergibt 419 bestandene Tests (mit gesetzten Variablen `TWMT_WH3_DIR` und `TWMT_RPFM_CLI`, sonst 8 übersprungen), Build ohne Warnung.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Plan, Backlog, Changelog Deutsch.
- Kein neues NuGet-Paket. Build-Einstellungen nur in `Directory.Build.props`, Paketversionen nur in `Directory.Packages.props`. `TreatWarningsAsErrors` ist an.
- **Schreibgrenze:** Die App schreibt nur unter `%APPDATA%\TotalWarModTool` und `%LocalAppData%\TotalWarModTool`. Nie in Spielordner, Workshop-Ordner, RPFM-Ordner oder Programmordner. Alle Schreibzugriffe laufen über `JsonFileStore` (atomar).
- **Dateien:** `settings.json` (global), `games\<profilId>\game-settings.json`, `games\<profilId>\session.json`. Jede mit `schemaVersion` 1. Kaputte Datei → `.bad`, Defaults, Log-Eintrag; nie ein blockierter Start. Für `NewerVersion` und `Unreadable` gilt dieselbe Regel wie bei `settings.json`: in dieser Sitzung nicht schreiben, eine Warnung im Log.
- **Fachlogik in Libraries.** ViewModels rufen Libraries auf und formen Ergebnisse für die Anzeige um. Code-behind enthält nur View-Verdrahtung (Spalten erzeugen, Doppelklick an ein Command reichen).
- **Operationen.** Jede vom Nutzer gestartete Operation (Pack-Liste aktualisieren, Pack aufklappen, Eintrag öffnen, Neuladen nach einer Einstellungsänderung) läuft über `IOperationRunner.RunAsync`. Der Scan beim Start und das Wiederherstellen der Sitzung laufen still mit `Task.Run`, ohne Fenster und ohne Eingabesperre. Kein Tab und kein Baum erscheint vor dem Ende seiner Operation. Abbruch hinterlässt keinen Teilzustand.
- **Ein zweiter Lauf wird nie gestartet.** Vor jedem `RunAsync` prüft der Aufrufer `IsRunning`; läuft schon etwas, passiert nichts (kein Fehler, keine Exception).
- **Diagnosen.** Libraries liefern Enum plus englisches `Detail`. Die App übersetzt den Enum-Wert in einen UI-Text und zeigt `Detail` nur als technischen Zusatz. Jede Diagnose, die der Nutzer sieht, steht auch im Log (einmal, nicht bei jedem Neuzeichnen).
- **Log:** keine personenbezogenen Daten. Pfade gehen wie bisher durch die Schwärzung von `FileLog`. Kein `catch (Exception)` ohne Log-Eintrag.
- **UI-Texte** nur über `AppTexts` (`T` / `F` mit englischem Fallback). Jeder neue Key steht in `locales/en.json` und `locales/de.json`. Keine Ellipsen in Labels. Tabellennamen, Spaltennamen, Pack-Namen und Dateipfade sind Daten, keine UI-Texte.
- **Farben** nur über Theme-Tokens und `DynamicResource`. Neue Tokens nur in `ThemePalette`.
- **Kein Spielwissen im Code.** Alles Spielspezifische kommt aus dem `GameProfile`. Die Tests der App benutzen ein synthetisches Profil (`testgame`, Datenordner `packs`).
- **Pfade von Packs** werden so benutzt, wie der Katalog sie liefert (`PackSource.Path`). Pfade aus gespeicherten Dateien werden ohne Beachtung der Groß-/Kleinschreibung gegen den Katalog abgeglichen; danach gilt die Schreibweise des Katalogs.
- Keine statischen Singletons. Services entstehen in `App.xaml.cs` und werden übergeben. Interfaces nur an der Außenwelt; die eine Ausnahme dieses Plans ist `IOperationRunner` (Begründung in Task 3).
- Test-first: failing Test → RED prüfen → implementieren → GREEN → Commit. Die ganze Suite bleibt grün. Keine Zeit-Assertions. WPF-Tests laufen über `Sta`.
- Tests der App bauen ihre Spieldaten in `TempDir` mit `PackBuilder` und einem synthetischen Profil. Tests auf echten Daten tragen `[RealDataFact]` und prüfen Invarianten.
- Bestehende Tests aus Plan 1 und 2 behalten ihre Erwartungen. Ändert sich ein Konstruktor, wird nur der Aufbau im Test angepasst.
- Ein Commit pro Task, nach GREEN. Vor jedem Commit `git add -A`. Die Attribution-Zeile der laufenden Sitzung gehört ans Ende der Commit-Message.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst, ohne die Erwartung zu ändern.
- YAGNI: keine Suche, kein Filter, kein Spielwähler, keine Spaltenbreiten-Persistenz, kein Kontextmenü. Was auffällt und fehlt, kommt in `Backlog.txt`.

## Review Focus

Diese Fälle nennt die Spec nicht einzeln, sie treffen Nutzer aber zuerst. Jeder hat einen Test in der genannten Task.

1. **Pack ändert sich oder verschwindet, während die App offen ist** (Steam-Update, Mod abbestellt, Mod in RPFM gespeichert). Erwartung: Der Index wird neu gelesen, offene Tabs des Packs tragen „veraltet“, ein verschwundenes Pack gibt eine Meldung statt eines Absturzes. → Task 4 (`OpenEntry_after_a_pack_change_rereads_the_index_and_reports_it`, `OpenEntry_of_a_deleted_pack_reports_the_problem`).
2. **Die gespeicherte Sitzung zeigt auf Dinge, die es nicht mehr gibt.** Erwartung: Diese Tabs fallen still weg (Log-Eintrag), alle anderen kommen wieder, der aktive Tab bleibt sinnvoll. → Task 1 (`Load_repairs_the_state`), Task 7 (`Restore_drops_tabs_of_missing_packs_and_entries`).
3. **Doppelklick auf etwas Großes oder Unbekanntes** (Video mit 200 MB, Modell, unbekannte Endung). Erwartung: Die Bytes werden nicht gelesen, der Tab nennt Typ, Größen und Kompression. → Task 2 (`Other_kind_reads_no_content`).
4. **Spielpfad zeigt ins Leere** (falscher Ordner gewählt, Laufwerk fehlt beim nächsten Start). Erwartung: Die Eingabe wird mit lesbarer Meldung abgelehnt und der alte Wert bleibt; ein gespeicherter, inzwischen ungültiger Pfad führt zu „Spiel nicht gefunden“ mit bedienbarer App. → Task 8 (`Invalid_game_path_is_rejected_and_keeps_the_old_value`), Task 4 (`Stored_install_path_without_data_folder_is_not_found`).
5. **Ungeduld:** derselbe Eintrag zweimal geöffnet, Aktion während des stillen Ladens, Aktualisieren während eine Operation läuft. Erwartung: ein Tab, keine Exception, kein zweiter Lauf. → Task 6 (`Opening_an_open_entry_activates_its_tab`), Task 4 (`A_request_while_the_runner_is_busy_does_nothing`), Task 7 (`Refresh_is_disabled_while_loading`).

---

### Task 1: Einstellungen pro Spiel und `Session`

**Files:**
- Modify: `src/TotalWarModTool.Settings/GlobalSettings.cs`, `GlobalSettingsStore.cs`
- Create: `src/TotalWarModTool.Settings/GameSettings.cs`, `GameSettingsStore.cs`
- Create: `src/TotalWarModTool.Session/TotalWarModTool.Session.csproj`, `SessionState.cs`, `SessionStore.cs`
- Test: `tests/TotalWarModTool.Settings.Tests/GlobalSettingsNewFieldsTests.cs`, `GameSettingsStoreTests.cs`, `SettingsFileMapTests.cs`
- Test: `tests/TotalWarModTool.Session.Tests/…csproj`, `SessionStoreTests.cs`

**Interfaces:**
- Consumes: `JsonFileStore.Load<T>(path, currentSchemaVersion, createDefault)` → `LoadResult<T>(Value, Status, BadCopyPath)`, `JsonFileStore.Save<T>(path, schemaVersion, value)`, `LoadStatus` (alle aus `Storage`). `TempDir`.
- Produces:

```csharp
namespace TotalWarModTool.Settings;

public sealed class GlobalSettings      // added members, same pattern as Language/Theme (Changed only on a real change)
{
    public string? SelectedGame { get; set; }   // profile id; trimmed; blank becomes null
    public string? SchemaFolder { get; set; }   // override folder for schema files; trimmed; blank becomes null
}

/// Settings of one game. Changed carries the property name, like GlobalSettings.
public sealed class GameSettings
{
    public event EventHandler<string>? Changed;
    public string? InstallPath { get; set; }    // manually chosen install folder; trimmed; blank becomes null (= detect through Steam)
}

public sealed record GameSettingsLoadResult(GameSettings Settings, LoadStatus Status, string? BadCopyPath);

public sealed class GameSettingsStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "game-settings.json";
    public GameSettingsStore(string roamingRoot, string profileId);   // FilePath = <roamingRoot>\games\<profileId>\game-settings.json
    public string FilePath { get; }
    public GameSettingsLoadResult Load();
    public void Save(GameSettings settings);                          // creates the folder when missing
}
```

```csharp
namespace TotalWarModTool.Session;   // references Storage only

public sealed record SessionTab(string PackPath, string EntryPath);

public sealed record SessionState(IReadOnlyList<SessionTab> Tabs, int ActiveTab, IReadOnlyList<string> ExpandedPacks)
{
    public static SessionState Empty { get; } = new([], -1, []);
}

public sealed record SessionLoadResult(SessionState State, LoadStatus Status, string? BadCopyPath);

public sealed class SessionStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "session.json";
    public const int MaxTabs = 100;
    public SessionStore(string roamingRoot, string profileId);        // FilePath = <roamingRoot>\games\<profileId>\session.json
    public string FilePath { get; }
    public SessionLoadResult Load();
    public void Save(SessionState state);                             // creates the folder when missing
}
```

Festlegungen:
- Dateiformat wie bei `GlobalSettingsStore`: eigene private Dateiklasse mit lauter nullbaren Mitgliedern, `schemaVersion` kommt von `JsonFileStore`.
- `SessionStore.Load` repariert still: Tabs mit leerem Pack- oder Eintragspfad fallen weg; doppelte Tabs (beide Pfade ohne Groß-/Kleinschreibung verglichen) fallen weg, der erste bleibt; höchstens `MaxTabs` Tabs; leere und doppelte Einträge in `ExpandedPacks` fallen weg; `ActiveTab` außerhalb `0 … Tabs.Count - 1` wird `0`, ohne Tabs `-1`. Fällt der aktive Tab weg, zeigt `ActiveTab` auf denselben Tab wie vorher, wenn er noch da ist, sonst auf `0`.
- `Save` schreibt den Zustand, wie er übergeben wird.
- „Gewähltes Spiel“ steht in `settings.json` (Spec 4.8), nicht in der Session.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class GlobalSettingsNewFieldsTests
{
    [Fact] public void New_fields_round_trip_through_the_store()
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(new GlobalSettings { SelectedGame = "testgame", SchemaFolder = @"D:\schemas" });
        var loaded = store.Load().Settings;
        Assert.Equal(("testgame", @"D:\schemas"), (loaded.SelectedGame, loaded.SchemaFolder));
    }

    [Fact] public void Old_file_without_the_new_fields_loads_with_nulls()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"language":"de","theme":"Dark"}""");
        var loaded = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal((LoadStatus.Loaded, "de", (string?)null, (string?)null),
            (loaded.Status, loaded.Settings.Language, loaded.Settings.SelectedGame, loaded.Settings.SchemaFolder));
    }

    [Theory]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData("  x  ", "x")]
    public void Blank_values_become_null_and_text_is_trimmed(string input, string? expected)
    {
        var s = new GlobalSettings { SelectedGame = input, SchemaFolder = input };
        Assert.Equal((expected, expected), (s.SelectedGame, s.SchemaFolder));
    }

    [Fact] public void Changed_is_raised_only_for_a_real_change()
    {
        var s = new GlobalSettings(); var changes = new List<string>();
        s.Changed += (_, name) => changes.Add(name);
        s.SchemaFolder = @"D:\a"; s.SchemaFolder = @" D:\a "; s.SelectedGame = "g"; s.SelectedGame = "g";
        Assert.Equal(["SchemaFolder", "SelectedGame"], changes);
    }
}

public class GameSettingsStoreTests
{
    [Fact] public void Round_trip_writes_below_the_game_folder()
    {
        using var dir = new TempDir();
        var store = new GameSettingsStore(dir.Path, "testgame");
        Assert.Equal(Path.Combine(dir.Path, "games", "testgame", "game-settings.json"), store.FilePath);
        store.Save(new GameSettings { InstallPath = @"D:\Games\Test" });
        var loaded = store.Load();
        Assert.Equal((LoadStatus.Loaded, @"D:\Games\Test"), (loaded.Status, loaded.Settings.InstallPath));
    }

    [Fact] public void Missing_file_gives_defaults()
    {
        using var dir = new TempDir();
        var loaded = new GameSettingsStore(dir.Path, "testgame").Load();
        Assert.Equal((LoadStatus.Missing, (string?)null), (loaded.Status, loaded.Settings.InstallPath));
    }

    [Fact] public void Corrupt_file_is_copied_to_bad_and_defaults_are_used()
    {
        using var dir = new TempDir();
        var store = new GameSettingsStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ broken");
        var loaded = store.Load();
        Assert.Equal(LoadStatus.Corrupt, loaded.Status);
        Assert.True(File.Exists(loaded.BadCopyPath));
        Assert.Null(loaded.Settings.InstallPath);
    }

    [Fact] public void InstallPath_raises_Changed_once_and_blank_becomes_null()
    {
        var s = new GameSettings(); var changes = new List<string>();
        s.Changed += (_, name) => changes.Add(name);
        s.InstallPath = @"D:\x"; s.InstallPath = @"D:\x "; s.InstallPath = "  ";
        Assert.Equal(["InstallPath", "InstallPath"], changes);
        Assert.Null(s.InstallPath);
    }
}

// Spec 4.8: every setting belongs to exactly one of the two settings files.
public class SettingsFileMapTests
{
    private static string[] Names(Type t) => t.GetProperties().Where(p => p.CanWrite).Select(p => p.Name).Order().ToArray();

    private static object Sample(Type t) =>
        t == typeof(string) ? "x"
        : t == typeof(ThemeChoice) ? ThemeChoice.Dark
        : t == typeof(WindowPlacement) ? new WindowPlacement(1, 2, 3, 4, true)
        : throw new InvalidOperationException($"No sample value for {t}: extend Sample when a setting has a new type.");

    [Fact] public void No_setting_exists_in_both_files() =>
        Assert.Empty(Names(typeof(GlobalSettings)).Intersect(Names(typeof(GameSettings))));

    [Fact] public void Every_global_setting_survives_a_round_trip()
    {
        using var dir = new TempDir();
        var settings = new GlobalSettings();
        foreach (var p in typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite))
            p.SetValue(settings, Sample(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(settings);
        var loaded = store.Load().Settings;
        Assert.All(typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite), p => Assert.Equal(p.GetValue(settings), p.GetValue(loaded)));
    }

    [Fact] public void Every_game_setting_survives_a_round_trip()
    {
        using var dir = new TempDir();
        var settings = new GameSettings();
        foreach (var p in typeof(GameSettings).GetProperties().Where(p => p.CanWrite))
            p.SetValue(settings, Sample(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
        var store = new GameSettingsStore(dir.Path, "testgame");
        store.Save(settings);
        var loaded = store.Load().Settings;
        Assert.All(typeof(GameSettings).GetProperties().Where(p => p.CanWrite), p => Assert.Equal(p.GetValue(settings), p.GetValue(loaded)));
    }
}

public class SessionStoreTests
{
    private static SessionState State(params (string Pack, string Entry)[] tabs) =>
        new(tabs.Select(t => new SessionTab(t.Pack, t.Entry)).ToList(), tabs.Length == 0 ? -1 : 0, []);

    [Fact] public void Round_trip_keeps_tabs_active_tab_and_expanded_packs()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Assert.Equal(Path.Combine(dir.Path, "games", "testgame", "session.json"), store.FilePath);
        var state = new SessionState([new(@"D:\a.pack", "db/x_tables/data__"), new(@"D:\b.pack", "script/main.lua")], 1, [@"D:\a.pack"]);
        store.Save(state);
        var loaded = store.Load();
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(state.Tabs, loaded.State.Tabs);
        Assert.Equal((1, state.ExpandedPacks), (loaded.State.ActiveTab, loaded.State.ExpandedPacks));
    }

    [Fact] public void Missing_file_gives_the_empty_state()
    {
        using var dir = new TempDir();
        var loaded = new SessionStore(dir.Path, "testgame").Load();
        Assert.Equal((LoadStatus.Missing, 0, -1), (loaded.Status, loaded.State.Tabs.Count, loaded.State.ActiveTab));
    }

    [Fact] public void Corrupt_file_is_copied_to_bad_and_the_empty_state_is_used()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "[1,2");
        var loaded = store.Load();
        Assert.Equal((LoadStatus.Corrupt, 0), (loaded.Status, loaded.State.Tabs.Count));
        Assert.True(File.Exists(loaded.BadCopyPath));
    }

    [Fact] public void Load_repairs_the_state()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeTab":3,"expandedPacks":["D:\\a.pack","","d:\\A.PACK",null],
             "tabs":[{"packPath":"","entryPath":"x"},{"packPath":"D:\\a.pack","entryPath":"db/x"},
                     {"packPath":"d:\\A.pack","entryPath":"DB/X"},{"packPath":"D:\\a.pack","entryPath":null},
                     {"packPath":"D:\\b.pack","entryPath":"y"},null]}
            """);
        var state = store.Load().State;
        Assert.Equal([new SessionTab(@"D:\a.pack", "db/x"), new SessionTab(@"D:\b.pack", "y")], state.Tabs);
        Assert.Equal(0, state.ActiveTab);                      // index 3 pointed at a tab that was dropped as invalid
        Assert.Equal([@"D:\a.pack"], state.ExpandedPacks);
    }

    [Fact] public void Active_tab_follows_its_tab_when_earlier_tabs_are_dropped()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeTab":2,"tabs":[{"packPath":"","entryPath":"x"},{"packPath":"D:\\a.pack","entryPath":"a"},{"packPath":"D:\\b.pack","entryPath":"b"}]}
            """);
        var state = store.Load().State;
        Assert.Equal((2, 1), (state.Tabs.Count, state.ActiveTab));
        Assert.Equal("b", state.Tabs[state.ActiveTab].EntryPath);
    }

    [Fact] public void More_tabs_than_the_limit_are_cut()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        store.Save(new SessionState(Enumerable.Range(0, SessionStore.MaxTabs + 5).Select(i => new SessionTab(@"D:\a.pack", $"e{i}")).ToList(), 0, []));
        Assert.Equal(SessionStore.MaxTabs, store.Load().State.Tabs.Count);
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Settings.Tests` und `dotnet test tests/TotalWarModTool.Session.Tests` – Expected: Build-Fehler, Typen fehlen.
- [ ] **Step 3: Implementieren.** Beide neuen Projekte der Solution hinzufügen. Spec 4.1: Zeile `Session` auf „Offene Tabs, aktiver Tab und aufgeklappte Packs speichern und laden“ ändern (gewähltes Spiel steht in `settings.json`).
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen.
- [ ] **Step 5: Commit** – `git commit -m "feat: add per-game settings and the session store"`

---

### Task 2: `Workspace` – Eintrag öffnen, Dateibaum, Wiederholversuch

**Files:**
- Modify: `src/TotalWarModTool.PackFiles/EntryContent.cs`, `EntryReader.cs`, `PackReader.cs` (neu: `ReadEntryInfo`)
- Modify: `src/TotalWarModTool.Workspace/TotalWarModTool.Workspace.csproj` (zusätzlich `Schema`, `Tables`), `PackSource.cs`, `PackCatalog.cs`
- Create: `src/TotalWarModTool.Workspace/PackAccess.cs`, `PackTree.cs`, `EntryText.cs`, `EntryOpener.cs`
- Test: `tests/TotalWarModTool.PackFiles.Tests/PackReaderEntryInfoTests.cs`
- Test: `tests/TotalWarModTool.Workspace.Tests/PackAccessTests.cs`, `PackTreeTests.cs`, `EntryTextTests.cs`, `EntryOpenerTests.cs`

**Interfaces:**
- Consumes: `PackReader.ReadIndex(path)` → `PackIndexResult(Index, Problem, Detail)`, `PackReader.ReadEntry(index, entry, ct, maxBytes)` → `EntryContent(Bytes, Format, UncompressedSize, Problem, Detail)`, `PackIndex` (`Entries`, `Find`, `Fingerprint`, `Header`), `PackEntry(Path, Offset, StoredSize, IsCompressed)`, `DbTablePath.TryGetTableName(entryPath, out tableName)`, `DbTableDecoder.Decode(data, tableName, schema)` → `DecodedTable`, `LocDecoder.Decode(data)` → `DecodedLoc`, `GameSchema`. Testhelfer `PackBuilder` (`AddEntry`, `Stored`, `Hex`, `WriteTo`), `TempDir`.
- Produces:

```csharp
namespace TotalWarModTool.PackFiles;

/// What the first bytes of an entry say, without reading its content.
public sealed record EntryInfo(CompressionFormat Format, long UncompressedSize, EntryProblem Problem, string? Detail);

public static class PackReader   // added member
{
    // Reads at most 8 bytes. Same order of checks as ReadEntry up to the size probe (token, fingerprint -> PackChanged, encrypted -> Encrypted).
    // Uncompressed entry: Format None, UncompressedSize = StoredSize. Compressed with StoredSize 0: None, 0. StoredSize 1..8: Corrupt.
    // LZMA and unknown formats are reported in Format with Problem None: the caller only wants to show them.
    public static EntryInfo ReadEntryInfo(PackIndex index, PackEntry entry, CancellationToken cancellationToken);
}
```

```csharp
namespace TotalWarModTool.Workspace;

public sealed record CatalogFolderProblem(string Path, string Detail);

// FolderProblems: one item per folder that exists but could not be listed (data folder, workshop folder, a workshop sub folder).
// A missing folder is not a problem.
public sealed record PackCatalogResult(IReadOnlyList<PackSource> Packs, bool UsedManifest, IReadOnlyList<CatalogFolderProblem> FolderProblems);

public static class PackAccess
{
    public const int DefaultAttempts = 3;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(250);
    // Reads the index. Only PackProblem.Unreadable (locked or access denied) is tried again, up to `attempts` times in total,
    // waiting `retryDelay` on the token between tries. Throws OperationCanceledException when cancelled.
    public static PackIndexResult ReadIndex(string path, CancellationToken cancellationToken, int attempts = DefaultAttempts, TimeSpan? retryDelay = null);
    internal static PackIndexResult ReadIndex(string path, CancellationToken cancellationToken, int attempts, TimeSpan retryDelay, Func<string, PackIndexResult> read);
}

/// A folder (Entry is null) or a file of a pack. Path is the full path inside the pack with '/' and is empty for the root.
public sealed class PackTreeNode
{
    public string Name { get; }
    public string Path { get; }
    public PackEntry? Entry { get; }
    public IReadOnlyList<PackTreeNode> Children { get; }   // folders first, then files, each sorted by name, ordinal ignoring case
}

public static class PackTree
{
    // Builds the folder tree of an index without recursion (paths can be thousands of segments deep).
    public static PackTreeNode Build(IReadOnlyList<PackEntry> entries, CancellationToken cancellationToken);
}

public sealed record TextContent(string Text, string EncodingName, bool Truncated, long TotalBytes);

public static class EntryText
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public static TextContent Decode(byte[] bytes, int maxBytes = MaxBytes);
}

public enum EntryKind { Table, Loc, Text, Other }

/// Result of opening one entry. Exactly one of Table, Loc, Text is set when the kind matches and the read worked.
/// ReadProblem is the problem of reading or decompressing; decode problems live inside Table / Loc.
/// SchemaMissing is true for a table when no schema was given: nothing was read then.
public sealed record OpenedEntry(string EntryPath, EntryKind Kind, long StoredSize, long UncompressedSize, CompressionFormat Format,
    EntryProblem ReadProblem, string? ReadDetail, bool SchemaMissing, DecodedTable? Table, DecodedLoc? Loc, TextContent? Text);

public static class EntryOpener
{
    public static EntryKind KindOf(string entryPath);
    public static OpenedEntry Open(PackIndex index, PackEntry entry, GameSchema? schema, CancellationToken cancellationToken);
}
```

Festlegungen:
- `KindOf`: `DbTablePath.TryGetTableName` trifft → `Table`. Endung `.loc` → `Loc`. Endung `.lua`, `.xml`, `.txt`, `.json`, `.csv`, `.tsv` → `Text`. Alles andere → `Other`. Endungen ohne Beachtung der Groß-/Kleinschreibung. Die Liste ist eine Konstante in `EntryOpener`.
- `Open`:
  - `Other`: nur `ReadEntryInfo`. Der Inhalt wird nicht gelesen.
  - `Table` ohne Schema: nur `ReadEntryInfo`, `SchemaMissing = true`.
  - Sonst `ReadEntry`. Problem → `ReadProblem` und `ReadDetail` gesetzt, kein Inhalt; `Format` und `UncompressedSize` aus dem Ergebnis.
  - `Table`: `DbTableDecoder.Decode(bytes, tableName, schema)`. `Loc`: `LocDecoder.Decode(bytes)`. `Text`: `EntryText.Decode(bytes)`.
  - `StoredSize` kommt immer aus dem Index-Eintrag. Das Token wird vor dem Lesen und vor dem Dekodieren geprüft.
- `EntryText.Decode`: BOM `EF BB BF` → UTF-8, `FF FE` → UTF-16 LE, `FE FF` → UTF-16 BE, sonst UTF-8. Die BOM gehört nicht zum Text. `EncodingName` ist `"UTF-8"`, `"UTF-16 LE"` oder `"UTF-16 BE"`. Länger als `maxBytes` (BOM mitgezählt): nur die ersten `maxBytes` Bytes, `Truncated = true`; der Schnitt liegt nie in einem Zeichen (UTF-8: bis zu 3 Bytes zurück auf eine Zeichengrenze; UTF-16: gerade Byte-Anzahl, kein einzelnes hohes Surrogat am Ende). Ungültige Bytes werden zum Ersatzzeichen, nie zu einer Exception. `TotalBytes` ist die volle Länge.
- `PackTree.Build`: Leere Segmente (`a//b`) und Einträge mit leerem Pfad werden übergangen. Ordner, die sich nur in Groß-/Kleinschreibung unterscheiden, verschmelzen (erste Schreibweise gilt). Doppelte Dateipfade: der erste Eintrag gilt. Gibt es `a` als Datei und `a/b`, entstehen ein Dateiknoten `a` und ein Ordnerknoten `a`. Das Token wird mindestens alle 1000 Einträge geprüft.
- `PackCatalog`: `FolderProblems` wie oben. Die bestehende Regel bleibt (ein Ordner, dessen Auflistung scheitert, liefert keine Packs).
- `Workspace` referenziert zusätzlich `Schema` und `Tables`. Spec 4.1 im selben Commit anpassen (Zeile `Workspace`: Aufgabe um „Eintrag öffnen, Dateibaum, Wiederholversuch“ ergänzen, Referenzen um `Schema`, `Tables`).
- `InternalsVisibleTo` für `TotalWarModTool.Workspace.Tests` (wegen der internen Überladung von `PackAccess.ReadIndex`).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackReaderEntryInfoTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private const string Lz4Simple = "04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00 | 00 00 00 00";

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, byte[] stored, bool compressed, uint flags = 0)
    {
        var path = new PackBuilder().WithFlags(flags).AddEntry("a/file.bin", stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    [Fact] public void Uncompressed_entry_reports_its_stored_size()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        Assert.Equal(new EntryInfo(CompressionFormat.None, 5, EntryProblem.None, null), PackReader.ReadEntryInfo(index, entry, CancellationToken.None));
    }

    [Theory]
    [InlineData(Lz4Simple, CompressionFormat.Lz4)]
    [InlineData("28 B5 2F FD 00 00 00 00 00", CompressionFormat.Zstd)]
    [InlineData("5D 00 00 80 00 FF FF FF FF", CompressionFormat.Lzma)]
    [InlineData("01 02 03 04 05 06 07 08 09", CompressionFormat.Unknown)]
    public void Compressed_entry_reports_format_and_claimed_size_without_a_problem(string frame, CompressionFormat format)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(frame), 1234), compressed: true);
        var info = PackReader.ReadEntryInfo(index, entry, CancellationToken.None);
        Assert.Equal((format, 1234L, EntryProblem.None), (info.Format, info.UncompressedSize, info.Problem));
    }

    [Theory]
    [InlineData(0, EntryProblem.None)]
    [InlineData(5, EntryProblem.Corrupt)]
    public void Tiny_compressed_entries(int storedLength, EntryProblem expected)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, new byte[storedLength], compressed: true);
        Assert.Equal(expected, PackReader.ReadEntryInfo(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Changed_pack_and_encrypted_data_are_reported()
    {
        using var dir = new TempDir();
        var (encIndex, encEntry) = One(dir, B("hello"), compressed: false, flags: 0x10);
        Assert.Equal(EntryProblem.Encrypted, PackReader.ReadEntryInfo(encIndex, encEntry, CancellationToken.None).Problem);
        using var dir2 = new TempDir();
        var (index, entry) = One(dir2, B("hello"), compressed: false);
        new PackBuilder().AddEntry("a/file.bin", B("hello!!")).WriteTo(dir2.File("a.pack"));
        Assert.Equal(EntryProblem.PackChanged, PackReader.ReadEntryInfo(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        Assert.Throws<OperationCanceledException>(() => PackReader.ReadEntryInfo(index, entry, new CancellationToken(true)));
    }
}

public class PackAccessTests
{
    private static readonly PackIndexResult Unreadable = new(null, PackProblem.Unreadable, "locked");
    private static readonly PackIndexResult Truncated = new(null, PackProblem.Truncated, "short");

    [Fact] public void Unreadable_is_tried_again_until_it_works()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WriteTo(dir.File("a.pack"));
        var calls = 0;
        var result = PackAccess.ReadIndex(path, CancellationToken.None, 3, TimeSpan.Zero, p => ++calls < 3 ? Unreadable : PackReader.ReadIndex(p));
        Assert.Equal((3, PackProblem.None), (calls, result.Problem));
    }

    [Fact] public void Gives_up_after_the_last_attempt()
    {
        var calls = 0;
        var result = PackAccess.ReadIndex("x", CancellationToken.None, 3, TimeSpan.Zero, _ => { calls++; return Unreadable; });
        Assert.Equal((3, PackProblem.Unreadable), (calls, result.Problem));
    }

    [Fact] public void Other_problems_are_not_tried_again()
    {
        var calls = 0;
        var result = PackAccess.ReadIndex("x", CancellationToken.None, 3, TimeSpan.Zero, _ => { calls++; return Truncated; });
        Assert.Equal((1, PackProblem.Truncated), (calls, result.Problem));
    }

    [Fact] public void Cancel_during_the_wait_throws()
    {
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() =>
            PackAccess.ReadIndex("x", cts.Token, 3, TimeSpan.FromMinutes(5), _ => { cts.Cancel(); return Unreadable; }));
    }

    [Fact] public void Public_overload_reads_a_real_pack()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("a", [1]).WriteTo(dir.File("a.pack"));
        Assert.Equal(1, PackAccess.ReadIndex(path, CancellationToken.None).Index!.Entries.Count);
    }
}

public class PackTreeTests
{
    private static PackEntry E(string path) => new(path, 0, 0, false);
    private static string Dump(PackTreeNode node, int depth = 0) =>
        string.Concat(node.Children.Select(c => new string(' ', depth) + (c.Entry is null ? c.Name + "/" : c.Name) + "\n" + Dump(c, depth + 1)));

    [Fact] public void Folders_come_first_and_names_are_sorted_ignoring_case()
    {
        var root = PackTree.Build([E("script/b.lua"), E("db/x_tables/data__"), E("Zeta.txt"), E("alpha.txt"), E("script/A.lua")], CancellationToken.None);
        Assert.Equal(("", ""), (root.Name, root.Path));
        Assert.Equal("db/\n x_tables/\n  data__\nscript/\n A.lua\n b.lua\nalpha.txt\nZeta.txt\n", Dump(root));
        Assert.Equal("db/x_tables", root.Children[0].Children[0].Path);
        Assert.Equal("db/x_tables/data__", root.Children[0].Children[0].Children[0].Entry!.Path);
    }

    [Fact] public void Odd_paths_do_not_break_the_tree()
    {
        var root = PackTree.Build([E("DB/a"), E("db/b"), E("x//y"), E(""), E("f"), E("f/inner"), E("dup"), E("DUP")], CancellationToken.None);
        Assert.Equal("DB/\n a\n b\nf/\n inner\nx/\n y\ndup\nf\n", Dump(root));
    }

    [Fact] public void Very_deep_path_needs_no_recursion()
    {
        var deep = string.Join('/', Enumerable.Repeat("d", 20000)) + "/file";
        var root = PackTree.Build([E(deep)], CancellationToken.None);
        var node = root; var depth = 0;
        while (node.Children.Count > 0) { node = node.Children[0]; depth++; }
        Assert.Equal((20001, "file"), (depth, node.Name));
    }

    [Fact] public void Cancelled_token_throws() =>
        Assert.Throws<OperationCanceledException>(() => PackTree.Build([E("a")], new CancellationToken(true)));
}

public class EntryTextTests
{
    [Theory]
    [InlineData("68 C3 A4", "hä", "UTF-8")]
    [InlineData("EF BB BF 68 C3 A4", "hä", "UTF-8")]
    [InlineData("FF FE 68 00 E4 00", "hä", "UTF-16 LE")]
    [InlineData("FE FF 00 68 00 E4", "hä", "UTF-16 BE")]
    [InlineData("", "", "UTF-8")]
    public void Decodes_by_bom(string hex, string expected, string encoding)
    {
        var bytes = PackBuilder.Hex(hex);
        Assert.Equal(new TextContent(expected, encoding, false, bytes.Length), EntryText.Decode(bytes));
    }

    [Fact] public void Invalid_bytes_become_the_replacement_character() =>
        Assert.Equal("a\uFFFDb", EntryText.Decode(PackBuilder.Hex("61 FF 62")).Text);

    [Theory]
    [InlineData("61 C3 A4 62", 2, "a")]          // cut would split the two-byte character
    [InlineData("61 C3 A4 62", 3, "aä")]
    [InlineData("F0 9F 98 80 61", 3, "")]        // cut inside a four-byte character
    [InlineData("FF FE 61 00 62 00", 5, "a")]    // odd byte count in UTF-16
    [InlineData("FF FE 3D D8 00 DE", 4, "")]     // cut after a high surrogate
    public void Truncation_never_splits_a_character(string hex, int maxBytes, string expected)
    {
        var bytes = PackBuilder.Hex(hex);
        var text = EntryText.Decode(bytes, maxBytes);
        Assert.Equal((expected, true, (long)bytes.Length), (text.Text, text.Truncated, text.TotalBytes));
    }
}

public class EntryOpenerTests
{
    // Build `schema` the way tests/TotalWarModTool.Tables.Tests does: table "x_tables", version 0, one I32 field "value".
    private static readonly byte[] TableBytes = PackBuilder.Hex("01 | 01 00 00 00 | 2A 00 00 00");                               // 1 row, value 42
    private static readonly byte[] LocBytes = PackBuilder.Hex("FF FE 4C 4F 43 00 | 01 00 00 00 | 01 00 00 00 | 01 00 6B 00 | 01 00 74 00 | 01");

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, string entryPath, byte[] stored, bool compressed = false)
    {
        var path = new PackBuilder().AddEntry(entryPath.Replace('/', '\\'), stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    [Theory]
    [InlineData("db/x_tables/data__", EntryKind.Table)]
    [InlineData("text/db/names.LOC", EntryKind.Loc)]
    [InlineData("script/campaign/main.lua", EntryKind.Text)]
    [InlineData("ui/layout.XML", EntryKind.Text)]
    [InlineData("a.txt", EntryKind.Text)] [InlineData("a.json", EntryKind.Text)] [InlineData("a.csv", EntryKind.Text)] [InlineData("a.tsv", EntryKind.Text)]
    [InlineData("movies/intro.ca_vp8", EntryKind.Other)]
    [InlineData("db/x_tables", EntryKind.Other)]                 // a file directly below db is not a table
    [InlineData("noextension", EntryKind.Other)]
    public void KindOf_decides_by_path(string path, EntryKind kind) => Assert.Equal(kind, EntryOpener.KindOf(path));

    [Fact] public void Table_is_decoded_with_the_schema()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "db/x_tables/data__", TableBytes);
        var opened = EntryOpener.Open(index, entry, schema, CancellationToken.None);
        Assert.Equal((EntryKind.Table, EntryProblem.None, false, 9L, 9L), (opened.Kind, opened.ReadProblem, opened.SchemaMissing, opened.StoredSize, opened.UncompressedSize));
        Assert.Equal((TableDecodeStatus.Ok, 42), (opened.Table!.Status, (int)opened.Table.Rows[0][0]!));
        Assert.Null(opened.Loc); Assert.Null(opened.Text);
    }

    [Fact] public void Table_that_does_not_decode_keeps_its_diagnosis()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "db/unknown_tables/data__", TableBytes);
        var opened = EntryOpener.Open(index, entry, schema, CancellationToken.None);
        Assert.Equal((EntryProblem.None, TableDecodeStatus.TableNotInSchema), (opened.ReadProblem, opened.Table!.Status));
    }

    [Fact] public void Table_without_a_schema_reads_no_content()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "db/x_tables/data__", TableBytes);
        var opened = EntryOpener.Open(index, entry, null, CancellationToken.None);
        Assert.Equal((true, (DecodedTable?)null, 9L), (opened.SchemaMissing, opened.Table, opened.UncompressedSize));
    }

    [Fact] public void Loc_and_text_are_decoded()
    {
        using var dir = new TempDir();
        var (locIndex, locEntry) = One(dir, "text/names.loc", LocBytes);
        var loc = EntryOpener.Open(locIndex, locEntry, null, CancellationToken.None);
        Assert.Equal(new LocRow("k", "t", true), Assert.Single(loc.Loc!.Rows));
        using var dir2 = new TempDir();
        var (textIndex, textEntry) = One(dir2, "script/main.lua", System.Text.Encoding.UTF8.GetBytes("print('hä')"));
        Assert.Equal("print('hä')", EntryOpener.Open(textIndex, textEntry, null, CancellationToken.None).Text!.Text);
    }

    // Review Focus 3: a big or unknown file is never read. The claimed size is far above what the pack holds; a real read would fail.
    [Fact] public void Other_kind_reads_no_content()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "movies/intro.ca_vp8", PackBuilder.Stored(PackBuilder.Hex("28 B5 2F FD 00 00 00 00 00"), 200_000_000), compressed: true);
        var opened = EntryOpener.Open(index, entry, null, CancellationToken.None);
        Assert.Equal((EntryKind.Other, EntryProblem.None, CompressionFormat.Zstd, 200_000_000L, 13L),
            (opened.Kind, opened.ReadProblem, opened.Format, opened.UncompressedSize, opened.StoredSize));
        Assert.Null(opened.Text);
    }

    [Fact] public void Read_problem_gives_no_content()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "script/main.lua", PackBuilder.Stored(PackBuilder.Hex("5D 00 00 80 00 FF FF FF FF"), 100), compressed: true);
        var opened = EntryOpener.Open(index, entry, null, CancellationToken.None);
        Assert.Equal((EntryProblem.UnsupportedCompression, CompressionFormat.Lzma, (TextContent?)null), (opened.ReadProblem, opened.Format, opened.Text));
        Assert.False(string.IsNullOrWhiteSpace(opened.ReadDetail));
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "a.txt", [1]);
        Assert.Throws<OperationCanceledException>(() => EntryOpener.Open(index, entry, null, new CancellationToken(true)));
    }
}

// added to the existing PackCatalogTests
[Fact] public void Missing_folders_are_no_folder_problems()
{
    using var dir = new TempDir();
    var result = PackCatalog.Scan(TestGame, dir.Path, Path.Combine(dir.Path, "no-workshop"), null, CancellationToken.None);
    Assert.Empty(result.FolderProblems);
}
```

Der Fall „Ordner existiert, lässt sich aber nicht auflisten“ ist nicht portabel auslösbar. Er wird per Review geprüft; das steht im Bericht.

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Workspace.Tests` und `dotnet test tests/TotalWarModTool.PackFiles.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` mit gesetzten Variablen. Expected: alles bestanden, 0 übersprungen.
- [ ] **Step 5: Commit** – `git commit -m "feat: open pack entries, build the file tree and retry locked packs"`

---

### Task 3: App-Infrastruktur

**Files:**
- Create: `src/TotalWarModTool.App/ProgressUi/IOperationRunner.cs`, `src/TotalWarModTool.App/RegistrySteamLocationSource.cs`, `src/TotalWarModTool.App/DebouncedSaver.cs`
- Modify: `src/TotalWarModTool.App/ProgressUi/ProgressRunner.cs`, `MainViewModel.cs` (Parametertyp), `AppPaths.cs`, `TotalWarModTool.App.csproj`, `Theme/ThemePalette.cs`, `Themes/Controls.xaml`
- Test: `tests/TotalWarModTool.App.Tests/FakeOperationRunner.cs`, `FakeOperationRunnerTests.cs`, `DebouncedSaverTests.cs`, `AppOutputTests.cs`, `ControlTemplatesTests.cs`, `WindowPlacementTrackerTests.cs`; ergänzt: `AppPathsTests.cs`, `ProgressRunnerTests.cs`, `ThemeTests.cs`

**Interfaces:**
- Consumes: `ProgressRunner.RunAsync<T>(title, work)` → `OperationOutcome<T>(Status, Value, Error)`, `OperationStatus { Completed, Cancelled, Failed }`, `ISteamLocationSource` (`GameProfiles`), `IAppLog`, `Sta`, `FakeAppLog`, `ThemeManager`, `ThemePalette.All`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// The way view models start a user operation. One implementation (ProgressRunner) and one test fake.
public interface IOperationRunner
{
    bool IsRunning { get; }
    Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work);
}

public sealed class ProgressRunner : IOperationRunner { public bool IsRunning { get; } /* true from the start of RunAsync until its lock is released */ }

/// Steam path from the registry: HKCU\Software\Valve\Steam, value SteamPath; else HKLM\SOFTWARE\WOW6432Node\Valve\Steam, value InstallPath.
/// Null when neither exists or the registry cannot be read (SecurityException, UnauthorizedAccessException, IOException).
public sealed class RegistrySteamLocationSource : ISteamLocationSource { public string? GetSteamPath(); }

public sealed record AppPaths   // added members
{
    public string ProfilesDirectory { get; }   // <ProgramRoot>\profiles
    public string SchemasDirectory { get; }    // <ProgramRoot>\schemas
}

/// Runs `save` 500 ms after the last MarkDirty (one DispatcherTimer). Must be created on the UI thread.
/// A save that throws IOException or UnauthorizedAccessException is logged as a warning ("<what> could not be saved: …") and stays pending.
public sealed class DebouncedSaver
{
    public DebouncedSaver(Action save, IAppLog log, string what, TimeSpan? delay = null);
    public void MarkDirty();
    public void Flush();     // saves now when dirty, cancels the pending timer; does nothing when clean
}
```

```csharp
// tests/TotalWarModTool.App.Tests/FakeOperationRunner.cs
public sealed class FakeOperationRunner : IOperationRunner
{
    public bool IsRunning { get; set; }                 // set by a test to simulate a run in progress; true while work runs
    public List<string> Titles { get; } = [];
    public bool CancelNext { get; set; }                // the next run gets a cancelled token; work that returns anyway yields Cancelled with its value
    // Runs work synchronously on the calling thread. Completed with the value; OperationCanceledException -> Cancelled;
    // any other exception -> Failed with Error (not rethrown). Progress reports are collected in Reports.
    public List<OperationProgress> Reports { get; } = [];
    public Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work);
}
```

Festlegungen:
- `IOperationRunner` ist die bewusste Ausnahme von „Interfaces nur an der Außenwelt“: Ohne sie bräuchte jeder ViewModel-Test einen STA-Thread und echte Fenster (Hinweis aus dem Review von Plan 1). Kommentar an der Schnittstelle nennt den Grund.
- `RegistrySteamLocationSource` liest nur und bekommt keinen Test (wie `RegistrySystemThemeSource`); es wird erst in Task 7 benutzt.
- `MainViewModel` nimmt `IOperationRunner` statt `ProgressRunner`. Die bestehenden Tests bleiben unverändert (ein `ProgressRunner` ist ein `IOperationRunner`).
- `TotalWarModTool.App.csproj`: Projektverweise auf `GameProfiles`, `PackFiles`, `Schema`, `Tables`, `Workspace`, `Session`. Content mit `CopyToOutputDirectory="PreserveNewest"`: `profiles\*.json` → `profiles\`, `schemas\*` → `schemas\` (Schema, `LICENSE`, `README.md`), `THIRD-PARTY-NOTICES.md` und `LICENSE` in den Ausgabeordner.
- `ThemePalette`: ein neues Token `Warning` (hell `#B45309`, dunkel `#E0A040`) für Markierungen an Packs und Hinweise.
- `Controls.xaml`: Stile für `DataGrid`, `DataGridColumnHeader`, `DataGridRow`, `DataGridCell`, `StatusBar`, `StatusBarItem`, `GridSplitter`. Nur Theme-Tokens. Raster: Hintergrund `PanelBackground`, Gitterlinien `Separator`, Kopf `ControlBackground`, Auswahl `Selection` / `SelectionText`, ohne Fokus `InactiveSelection`. Zeilenkopf ausgeblendet (`HeadersVisibility="Column"`).
- Die Backlog-Zeilen „Tests: WindowPlacementTracker; alle Vorlagen aus Controls.xaml einmal instanziieren“ und „Plan 3: entscheiden, ob ViewModels weiter den konkreten ProgressRunner bekommen“, „Plan 3: Controls.xaml hat keinen Stil für DataGrid“, „Plan 3: `profiles/` und `schemas/` … kopieren“ werden mit dieser Task aus `Backlog.txt` entfernt.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class FakeOperationRunnerTests
{
    [Fact] public async Task Runs_the_work_and_records_the_title()
    {
        var runner = new FakeOperationRunner();
        var outcome = await runner.RunAsync("Title", (progress, _) => { progress.Report(new OperationProgress(1, 2, "x")); return 7; });
        Assert.Equal((OperationStatus.Completed, 7), (outcome.Status, outcome.Value));
        Assert.Equal(["Title"], runner.Titles);
        Assert.Equal(new OperationProgress(1, 2, "x"), Assert.Single(runner.Reports));
        Assert.False(runner.IsRunning);
    }

    [Fact] public async Task Cancel_and_failure_become_outcomes()
    {
        var runner = new FakeOperationRunner { CancelNext = true };
        var cancelled = await runner.RunAsync<int>("a", (_, ct) => { ct.ThrowIfCancellationRequested(); return 1; });
        Assert.Equal(OperationStatus.Cancelled, cancelled.Status);
        var failed = await runner.RunAsync<int>("b", (_, _) => throw new IOException("boom"));
        Assert.Equal((OperationStatus.Failed, "boom"), (failed.Status, failed.Error!.Message));
    }
}

// added to ProgressRunnerTests, in the style of the tests there
[Fact] public void IsRunning_is_true_only_during_a_run() => Sta.RunAsync(async () =>
{
    var runner = /* runner as in the neighbouring tests, long show delay */;
    Assert.False(runner.IsRunning);
    var seen = false;
    var run = runner.RunAsync("t", (_, _) => { Thread.Sleep(50); return 1; });
    seen = runner.IsRunning;
    await run;
    Assert.True(seen); Assert.False(runner.IsRunning);
});

public class DebouncedSaverTests
{
    [Fact] public void Flush_saves_once_and_only_when_dirty() => Sta.Run(() =>
    {
        var saves = 0;
        var saver = new DebouncedSaver(() => saves++, new FakeAppLog(), "Session");
        saver.Flush(); Assert.Equal(0, saves);
        saver.MarkDirty(); saver.MarkDirty(); saver.Flush(); saver.Flush();
        Assert.Equal(1, saves);
    });

    [Fact] public void Failed_save_is_logged_and_stays_pending() => Sta.Run(() =>
    {
        var log = new FakeAppLog(); var fail = true; var saves = 0;
        var saver = new DebouncedSaver(() => { if (fail) throw new IOException("locked"); saves++; }, log, "Session");
        saver.MarkDirty(); saver.Flush();
        Assert.Contains(log.Warnings, w => w.Contains("Session") && w.Contains("locked"));   // use the member names FakeAppLog has
        fail = false; saver.Flush();
        Assert.Equal(1, saves);
    });

    [Fact] public void Saves_by_itself_after_the_delay() => Sta.RunAsync(async () =>
    {
        var saves = 0;
        var saver = new DebouncedSaver(() => saves++, new FakeAppLog(), "Session", TimeSpan.FromMilliseconds(20));
        saver.MarkDirty();
        var deadline = Environment.TickCount64 + 5000;
        while (saves == 0 && Environment.TickCount64 < deadline) await Task.Delay(10);
        Assert.Equal(1, saves);
    });
}

public class AppOutputTests
{
    // The content of the app project flows into the output folder of this test project.
    [Theory]
    [InlineData(@"profiles\warhammer_3.json")]
    [InlineData(@"schemas\schema_wh3.ron")]
    [InlineData(@"schemas\LICENSE")]
    [InlineData(@"schemas\README.md")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("LICENSE")]
    [InlineData(@"locales\en.json")]
    public void Shipped_file_is_in_the_output_folder(string relativePath) =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, relativePath)), relativePath);
}

// added to AppPathsTests
[Fact] public void Profiles_and_schemas_are_below_the_program_folder()
{
    var paths = new AppPaths(@"X:\roaming", @"X:\local", @"X:\program");
    Assert.Equal((@"X:\program\profiles", @"X:\program\schemas"), (paths.ProfilesDirectory, paths.SchemasDirectory));
}

public class ControlTemplatesTests
{
    // Every control type that Controls.xaml styles is created once per theme inside a window that carries the app resources
    // (build the resources the way ThemeTests does). ApplyTemplate and a layout pass must not throw, and no brush may stay unresolved.
    [Theory]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    public void Every_styled_control_builds_its_template(ThemeChoice theme) => Sta.Run(() =>
    {
        // controls: Button, ToggleButton, CheckBox, RadioButton, TextBox, ScrollBar, ComboBox (with one item, IsDropDownOpen not needed),
        // ListBox + ListBoxItem, ListView + GridView column, TreeView + TreeViewItem, Menu + MenuItem, ContextMenu, ToolBar, TabControl + TabItem,
        // Expander, ProgressBar, ToolTip, Separator, DataGrid (2 columns, 2 rows), StatusBar + StatusBarItem, GridSplitter.
        // For each: put it into a Grid of a Window with the resources, Measure/Arrange, ApplyTemplate.
        // Assert: no exception; for DataGrid the Background brush equals the Theme.PanelBackground resource of that theme.
    });
}

// added to ThemeTests, in the style of the tests there
[Fact] public void Warning_token_exists_in_both_themes() =>
    Assert.Contains(ThemePalette.All, t => t.Key == "Warning" && t.Light == "#B45309" && t.Dark == "#E0A040");

public class WindowPlacementTrackerTests
{
    // Read WindowPlacementTracker first and write these three with its real API (Sta.Run, a real Window that is shown and closed):
    [Fact] public void A_usable_saved_placement_is_applied_to_the_window() { /* settings.MainWindow inside the virtual screen -> window Left/Top/Width/Height equal it */ }
    [Fact] public void An_unusable_saved_placement_is_ignored() { /* placement far outside the virtual screen -> window keeps its default position */ }
    [Fact] public void Moving_the_window_writes_the_placement_into_the_settings() { /* change Left/Top -> settings.MainWindow has the new values */ }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.** Scheitert `AppOutputTests`, weil Content nicht in die Testausgabe fließt: nicht den Test abschwächen, sondern im Bericht melden.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen. Zusätzlich `build.bat` und prüfen, dass `profiles\`, `schemas\` und `THIRD-PARTY-NOTICES.md` im Ausgabeordner der App liegen.
- [ ] **Step 5: Commit** – `git commit -m "feat: add operation runner interface, Steam registry source and DataGrid styles"`

---

### Task 4: `WorkspaceController` und Diagnosetexte

**Files:**
- Create: `src/TotalWarModTool.App/Workspace/WorkspaceController.cs`, `src/TotalWarModTool.App/Workspace/PackContent.cs`, `src/TotalWarModTool.App/AppTexts.Workspace.cs`
- Modify: `src/TotalWarModTool.App/AppTexts.cs` (wird `partial`), `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/TestGame.cs` (Testhelfer), `WorkspaceControllerTests.cs`, `DiagnosisTextsTests.cs`

**Interfaces:**
- Consumes: `GameProfile`, `GameLocator.Find(profile, steam)` / `FromInstallPath(profile, path)` → `GameLocation(InstallPath, DataPath, WorkshopPath)?`, `ISteamLocationSource`; `PackCatalog.Scan(profile, installPath, workshopPath, progress, ct)` → `PackCatalogResult(Packs, UsedManifest, FolderProblems)`; `PackSource(Path, Name, Origin, WorkshopId, Fingerprint, Header, Problem, Detail)`; `PackAccess.ReadIndex(path, ct)`; `PackTree.Build(entries, ct)`; `EntryOpener.Open(index, entry, schema, ct)` → `OpenedEntry`; `SchemaSource.Resolve(shippedFolder, overrideFolder, fileName)` → `ResolvedSchema(Schema, Origin, Path, FileDateUtc, OverrideAttempt, ShippedAttempt)`; `GlobalSettings.SchemaFolder`, `GameSettings.InstallPath` (Task 1); `IOperationRunner`, `AppPaths.SchemasDirectory` (Task 3); `SessionState`, `SessionTab` (Task 1).
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed record PackContent(PackIndex Index, PackTreeNode Tree);

public enum GameStatus { NoProfile, NotFound, Found }

public sealed record RestoredPack(PackSource Pack, PackContent Content);
public sealed record RestoredTab(PackSource Pack, OpenedEntry Entry);
public sealed record RestoreResult(IReadOnlyList<RestoredPack> Packs, IReadOnlyList<RestoredTab> Tabs, int ActiveTab);

/// State of one game: where it is, its packs, its schema and the indexes of the packs that are open. Lives on the UI thread;
/// work runs on pool threads. No window, no view: usable in tests with FakeOperationRunner.
public sealed class WorkspaceController
{
    public WorkspaceController(GameProfile? profile, GameSettings gameSettings, GlobalSettings settings, AppPaths paths,
        ISteamLocationSource steam, IOperationRunner runner, IAppLog log, AppTexts texts);

    public GameProfile? Profile { get; }
    public GameStatus Status { get; }                 // NoProfile when Profile is null
    public GameLocation? Location { get; }
    public bool ManualPathInvalid { get; }            // a stored InstallPath that has no data folder
    public PackCatalogResult? Catalog { get; }        // null until the first load ended and whenever the game is not found
    public ResolvedSchema? Schema { get; }            // null until the first load ended
    public bool IsBusy { get; }                       // true during LoadAsync and RestoreAsync (the silent runs)
    public bool CanStartOperation { get; }            // !IsBusy && !runner.IsRunning

    public event EventHandler? Changed;               // Status, Location, Catalog, Schema or IsBusy changed
    public event EventHandler<PackSource>? PackChanged;   // a pack was found changed on disk; its cached index was replaced or dropped

    public Task LoadAsync();                          // silent start: locate, scan, resolve schema. No runner.
    public Task RefreshAsync();                       // user: locate and scan again, through the runner
    public Task ReloadSchemaAsync();                  // user: resolve the schema again, through the runner
    public Task<PackContent?> OpenPackAsync(PackSource pack);              // user: index + tree, through the runner; cached
    public void ClosePack(PackSource pack);                                // drops the cached index of a collapsed pack
    public PackIndexResult? LastIndexProblem(PackSource pack);             // the problem of the last failed OpenPackAsync, else null
    public Task<OpenedEntry?> OpenEntryAsync(PackSource pack, string entryPath);   // user: through the runner
    public Task<RestoreResult> RestoreAsync(SessionState state);           // silent: indexes of expanded packs and tabs of the session
}

public sealed partial class AppTexts   // AppTexts.Workspace.cs
{
    public string PackProblemText(PackProblem problem, string? expectedFormat);   // None -> ""
    public string EntryProblemText(EntryProblem problem);                         // None -> ""
    public string TableStatusText(TableDecodeStatus status);                      // Ok -> ""
    public string SchemaProblemText(SchemaProblem problem);                       // None -> ""
    public string OriginName(PackOrigin origin);
    public string OperationRefreshPacks { get; }                                  // "Refresh pack list"
    public string OperationLoadSchema { get; }                                    // "Load schema"
    public string OperationOpenPack(string packName);                             // "Open {0}"
    public string OperationOpenEntry(string entryName);                           // "Open {0}"
}
```

Texte (Key, Englisch, Deutsch):

| Key | en | de |
|---|---|---|
| `pack.problem.notFound` | The pack file no longer exists. Refresh the pack list. | Die Pack-Datei existiert nicht mehr. Aktualisiere die Pack-Liste. |
| `pack.problem.unreadable` | The pack file cannot be read. It may be locked by the game or another program. Close that program and try again. | Die Pack-Datei kann nicht gelesen werden. Vielleicht sperrt sie das Spiel oder ein anderes Programm. Schließe das Programm und versuche es erneut. |
| `pack.problem.truncated` | The pack file is incomplete. | Die Pack-Datei ist unvollständig. |
| `pack.problem.unknownFormat` | Unknown pack format. This game uses {0}. | Unbekanntes Pack-Format. Dieses Spiel nutzt {0}. |
| `pack.problem.encryptedIndex` | Encrypted pack, not supported. | Verschlüsseltes Pack, nicht unterstützt. |
| `pack.problem.inconsistentIndex` | The file list of the pack is damaged. | Die Dateiliste des Packs ist beschädigt. |
| `entry.problem.packChanged` | The pack was changed on disk. Open the entry again. | Das Pack wurde auf der Platte geändert. Öffne den Eintrag erneut. |
| `entry.problem.unreadable` | The entry cannot be read. The pack may be locked by the game or another program. | Der Eintrag kann nicht gelesen werden. Vielleicht sperrt das Spiel oder ein anderes Programm das Pack. |
| `entry.problem.encrypted` | Encrypted pack, not supported. | Verschlüsseltes Pack, nicht unterstützt. |
| `entry.problem.unsupportedCompression` | This compression format is not supported yet. | Dieses Kompressionsformat wird noch nicht unterstützt. |
| `entry.problem.corrupt` | The entry is damaged. | Der Eintrag ist beschädigt. |
| `entry.problem.tooLarge` | The entry is too large to open. | Der Eintrag ist zu groß zum Öffnen. |
| `table.status.tableNotInSchema` | The schema does not know this table. | Das Schema kennt diese Tabelle nicht. |
| `table.status.versionNotInSchema` | The schema does not know this version of the table. | Das Schema kennt diese Version der Tabelle nicht. |
| `table.status.unsupportedFieldType` | The table uses a field type that is not supported yet. | Die Tabelle nutzt einen Feldtyp, der noch nicht unterstützt wird. |
| `table.status.failed` | The table does not match the schema. It may come from another game version. | Die Tabelle passt nicht zum Schema. Vielleicht stammt sie aus einer anderen Spielversion. |
| `schema.problem.notFound` | file not found | Datei nicht gefunden |
| `schema.problem.unreadable` | file cannot be read | Datei nicht lesbar |
| `schema.problem.invalidSyntax` | file is not valid RON | Datei ist kein gültiges RON |
| `schema.problem.invalidStructure` | file has an unexpected structure | Datei hat eine unerwartete Struktur |
| `origin.vanilla` | Game | Spiel |
| `origin.dataFolderMod` | Mods in the data folder | Mods im Datenordner |
| `origin.workshop` | Workshop | Workshop |
| `operation.refreshPacks` | Refresh pack list | Pack-Liste aktualisieren |
| `operation.loadSchema` | Load schema | Schema laden |
| `operation.openPack` | Open {0} | {0} öffnen |
| `operation.openEntry` | Open {0} | {0} öffnen |

Festlegungen:
- **Ort bestimmen.** `gameSettings.InstallPath` gesetzt → `GameLocator.FromInstallPath`; ergibt das `null`: `Status = NotFound`, `ManualPathInvalid = true` (kein Rückfall auf Steam, damit die Unsicherheit sichtbar bleibt). Nicht gesetzt → `GameLocator.Find`. Ohne Ort: `Catalog = null`.
- **Der Konstruktor bestimmt den Ort sofort und synchron** (Spec 4.7: die Installation steht fest, bevor das Fenster erscheint). `Status`, `Location` und `ManualPathInvalid` sind danach gesetzt; `Catalog` und `Schema` sind bis zum Ende von `LoadAsync` `null`.
- **`LoadAsync`** läuft mit `Task.Run`, ohne Runner. Reihenfolge: `PackCatalog.Scan` am schon bestimmten Ort (ohne Fortschritt, Token nie abgebrochen), dann `SchemaSource.Resolve(paths.SchemasDirectory, settings.SchemaFolder, profile.SchemaFile)`. Das Schema wird auch ohne Ort geladen. `IsBusy` ist ab dem Aufruf (noch vor dem ersten `await`) bis zum Ende `true`; `Changed` kommt am Anfang und am Ende. Dasselbe gilt für `RestoreAsync`. Eine unerwartete Exception wird geloggt (`log.Error`) und nicht weitergeworfen; der Zustand ist danach „kein Katalog“.
- **`RefreshAsync`**, **`ReloadSchemaAsync`**, **`OpenPackAsync`**, **`OpenEntryAsync`**: tun nichts und liefern `null` bzw. kehren zurück, wenn `!CanStartOperation`. Sonst `runner.RunAsync` mit dem Titel aus `AppTexts`. Abbruch oder Fehlschlag: kein Zustand ändert sich, Ergebnis `null`.
- **`RefreshAsync`** bestimmt den Ort neu, scannt mit Fortschritt und lädt das Schema neu. Danach: gecachte Indexe von Packs, die es nicht mehr gibt oder deren Fingerprint sich geändert hat, fallen weg; für jedes solche Pack, das noch existiert, kommt `PackChanged`.
- **Reaktion auf Einstellungen.** `gameSettings.Changed` (`InstallPath`) → `RefreshAsync`. `settings.Changed` (`SchemaFolder`) → `ReloadSchemaAsync`. Kann gerade keine Operation starten (`!CanStartOperation`), merkt sich der Controller den Wunsch und holt ihn nach, sobald der laufende stille Lauf endet.
- **`OpenPackAsync`**: aus dem Cache, wenn vorhanden (kein Runner-Lauf). Sonst im Runner `PackAccess.ReadIndex` und `PackTree.Build`. Problem → `null`, das Ergebnis ist über `LastIndexProblem` abrufbar und steht im Log. Cache-Schlüssel ist `pack.Path`, Vergleich ohne Groß-/Kleinschreibung.
- **`OpenEntryAsync`**: Index aus dem Cache oder im selben Runner-Lauf gelesen (ohne ihn zu cachen, wenn das Pack nicht aufgeklappt ist). `index.Find(entryPath)` ohne Treffer → `OpenedEntry` mit `ReadProblem = Corrupt` und `ReadDetail = "The entry no longer exists in the pack."`. `EntryOpener.Open(…, Schema?.Schema, ct)`. Meldet das Ergebnis `PackChanged`: im selben Lauf den Index neu lesen (Cache ersetzen, bei einem Problem verwerfen), den Eintrag ein zweites Mal öffnen, nach dem Lauf `PackChanged` auslösen. Liefert auch der zweite Versuch ein Problem, kommt dieses Ergebnis zurück.
- **`RestoreAsync`** läuft still mit `Task.Run`, `IsBusy = true`. Für jedes Pack aus `ExpandedPacks` und jeden Tab: Pack im Katalog suchen (Pfad ohne Groß-/Kleinschreibung, danach gilt die Schreibweise des Katalogs). Fehlt es → fällt weg, ein `log.Info` pro Tab. Index mit `PackAccess.ReadIndex`; Problem → Pack und seine Tabs fallen weg (Log). Aufgeklappte Packs kommen mit Baum in den Cache und in `Packs`. Tab: Eintrag fehlt → fällt weg (Log); sonst `EntryOpener.Open`, das Ergebnis kommt auch mit Lese- oder Dekodierproblem in `Tabs` (der Tab zeigt dann den Hinweis). `ActiveTab`: Index des früher aktiven Tabs in der neuen Liste, sonst `0`, ohne Tabs `-1`.
- **Log.** Jede Diagnose einmal: Probleme beim Profil-Laden (macht `App.xaml.cs`), Ordnerprobleme und Anzahl der Packs mit Problem nach jedem Scan, Schema-Quelle und fehlgeschlagene Versuche nach jedem Laden, Indexprobleme, `PackChanged`.
- **`AppTexts`** wird `partial`. `LocaleCheck` muss jeden Key sehen: zuerst `LocaleCheck.FindProblems` und `TextsBase` lesen. Erreicht die Prüfung nur Eigenschaften und Formatmethoden, bekommt jeder Enum-Text eine eigene Eigenschaft, und die Methode mit Enum-Parameter wählt per `switch` daraus. Kein Text wird doppelt angelegt.
- `expectedFormat` in `PackProblemText` ist `profile.PackFormat` (damit wird das Feld benutzt). Die Backlog-Zeilen „Plan 3: `GameProfile.PackFormat` …“, „Plan 3: Der kurze Wiederholversuch …“, „Plan 3: Bei `PackChanged` den Index neu lesen …“, „Plan 3: `FileFingerprint` vergleicht den Pfad ordinal …“ und „Plan 3: ProgressRunner.RunAsync lehnt einen zweiten Lauf … ab“ werden entfernt.

Testhelfer:

```csharp
// tests/TotalWarModTool.App.Tests/TestGame.cs — a synthetic game in a TempDir
public sealed class TestGame : IDisposable
{
    public static readonly GameProfile Profile = new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["release"], null);
    public TempDir Root { get; }
    public string InstallPath { get; }                // <Root>\game, with the folder "packs"
    public string DataPath { get; }
    public AppPaths Paths { get; }                    // RoamingRoot <Root>\roaming, LocalRoot <Root>\local, ProgramRoot <Root>\program
    public GameSettings GameSettings { get; }         // InstallPath preset to InstallPath
    public GlobalSettings Settings { get; }
    public FakeOperationRunner Runner { get; }
    public FakeAppLog Log { get; }
    public AppTexts Texts { get; }
    public TestGame(bool withSchema = true);          // writes <ProgramRoot>\schemas\schema_test.ron: table "x_tables", version 0, one I32 field "value"
    public string AddPack(string name, params (string Path, byte[] Bytes)[] entries);   // PFH5 mod pack (type 3) in the data folder, returns its path
    public WorkspaceController NewController(ISteamLocationSource? steam = null);        // default: a source that returns null
    public static readonly byte[] TableBytes;         // "01 | 01 00 00 00 | 2A 00 00 00": one row, value 42
}
```

Die RON-Datei für `schema_test.ron` hat die Form der kleinen Beispiele in `tests/TotalWarModTool.Schema.Tests/SchemaLoaderTests.cs`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class WorkspaceControllerTests
{
    private static PackSource Pack(WorkspaceController c, string name) => c.Catalog!.Packs.Single(p => p.Name == name);

    [Fact] public async Task Load_finds_the_game_scans_the_packs_and_resolves_the_schema()
    {
        using var game = new TestGame();
        game.AddPack("b.pack"); game.AddPack("a.pack");
        var c = game.NewController(); var changes = 0; c.Changed += (_, _) => changes++;
        await c.LoadAsync();
        Assert.Equal((GameStatus.Found, false, false), (c.Status, c.IsBusy, c.ManualPathInvalid));
        Assert.Equal(game.InstallPath, c.Location!.InstallPath);
        Assert.Equal(["a.pack", "b.pack"], c.Catalog!.Packs.Select(p => p.Name));
        Assert.Equal(SchemaOrigin.Shipped, c.Schema!.Origin);
        Assert.True(changes >= 2);
        Assert.Empty(game.Runner.Titles);                                   // silent: never through the runner
    }

    [Fact] public async Task Without_a_profile_nothing_is_loaded()
    {
        using var game = new TestGame();
        var c = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        await c.LoadAsync();
        Assert.Equal((GameStatus.NoProfile, (PackCatalogResult?)null), (c.Status, c.Catalog));
    }

    // Review Focus 4
    [Fact] public async Task Stored_install_path_without_data_folder_is_not_found()
    {
        using var game = new TestGame();
        game.GameSettings.InstallPath = game.Root.File("elsewhere");
        var c = game.NewController();
        await c.LoadAsync();
        Assert.Equal((GameStatus.NotFound, true, (PackCatalogResult?)null), (c.Status, c.ManualPathInvalid, c.Catalog));
        Assert.NotNull(c.Schema);                                           // the schema does not depend on the game folder
    }

    [Fact] public async Task Without_a_manual_path_the_game_is_found_through_steam()
    {
        using var game = new TestGame();
        game.GameSettings.InstallPath = null;
        // steam library: <Root>\steam\steamapps\common\Test Game\packs
        var steamRoot = game.Root.File("steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps", "common", "Test Game", "packs"));
        var c = game.NewController(new FixedSteam(steamRoot));
        await c.LoadAsync();
        Assert.Equal(Path.Combine(steamRoot, "steamapps", "common", "Test Game"), c.Location!.InstallPath);
    }

    [Fact] public async Task Override_schema_wins_and_a_broken_override_falls_back()
    {
        using var game = new TestGame();
        var overrideFolder = game.Root.File("override"); Directory.CreateDirectory(overrideFolder);
        File.Copy(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"), Path.Combine(overrideFolder, "schema_test.ron"));
        game.Settings.SchemaFolder = overrideFolder;
        var c = game.NewController();
        await c.LoadAsync();
        Assert.Equal(SchemaOrigin.Override, c.Schema!.Origin);

        File.WriteAllText(Path.Combine(overrideFolder, "schema_test.ron"), "( broken");
        await c.ReloadSchemaAsync();
        Assert.Equal((SchemaOrigin.Shipped, SchemaProblem.InvalidSyntax), (c.Schema!.Origin, c.Schema.OverrideAttempt!.Problem));
        Assert.Equal(["Load schema"], game.Runner.Titles);
    }

    [Fact] public async Task Refresh_runs_through_the_runner_and_a_cancel_keeps_the_old_catalog()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var c = game.NewController();
        await c.LoadAsync();
        game.AddPack("b.pack");
        game.Runner.CancelNext = true;
        await c.RefreshAsync();
        Assert.Single(c.Catalog!.Packs);
        await c.RefreshAsync();
        Assert.Equal(2, c.Catalog!.Packs.Count);
        Assert.Equal(["Refresh pack list", "Refresh pack list"], game.Runner.Titles);
        Assert.Contains(game.Runner.Reports, r => r.Total == 2);
    }

    [Fact] public async Task OpenPack_returns_index_and_tree_and_caches_them()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("script/main.lua", [1]), ("db/x_tables/data__", TestGame.TableBytes));
        var c = game.NewController();
        await c.LoadAsync();
        var content = await c.OpenPackAsync(Pack(c, "a.pack"));
        Assert.Equal(2, content!.Index.Entries.Count);
        Assert.Equal(["db", "script"], content.Tree.Children.Select(n => n.Name));
        Assert.Same(content, await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Equal(["Open a.pack"], game.Runner.Titles);                  // the second call came from the cache
        c.ClosePack(Pack(c, "a.pack"));
        Assert.NotSame(content, await c.OpenPackAsync(Pack(c, "a.pack")));
    }

    [Fact] public async Task OpenPack_with_a_broken_index_returns_null_and_keeps_the_problem()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("x", [1, 2, 3]));
        var c = game.NewController();
        await c.LoadAsync();
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);            // data no longer ends at the file end
        Assert.Null(await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Equal(PackProblem.InconsistentIndex, c.LastIndexProblem(Pack(c, "a.pack"))!.Problem);
        Assert.NotEmpty(game.Log.Warnings);                                 // use the member names FakeAppLog has
    }

    [Fact] public async Task OpenEntry_decodes_a_table_with_the_schema()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes));
        var c = game.NewController();
        await c.LoadAsync();
        var opened = await c.OpenEntryAsync(Pack(c, "a.pack"), "db/x_tables/data__");
        Assert.Equal((EntryKind.Table, TableDecodeStatus.Ok), (opened!.Kind, opened.Table!.Status));
        Assert.Equal(["Open data__"], game.Runner.Titles);
    }

    // Review Focus 1
    [Fact] public async Task OpenEntry_after_a_pack_change_rereads_the_index_and_reports_it()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", "old"u8.ToArray()));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        var before = await c.OpenPackAsync(pack);
        var changed = new List<PackSource>(); c.PackChanged += (_, p) => changed.Add(p);
        new PackBuilder().AddEntry("a.txt", "newer text"u8.ToArray()).WriteTo(path);
        var opened = await c.OpenEntryAsync(pack, "a.txt");
        Assert.Equal((EntryProblem.None, "newer text"), (opened!.ReadProblem, opened.Text!.Text));
        Assert.Equal(pack, Assert.Single(changed));
        Assert.NotSame(before, await c.OpenPackAsync(pack));                // the cached index was replaced
    }

    // Review Focus 1
    [Fact] public async Task OpenEntry_of_a_deleted_pack_reports_the_problem()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        await c.OpenPackAsync(pack);
        File.Delete(path);
        var opened = await c.OpenEntryAsync(pack, "a.txt");
        Assert.Equal(EntryProblem.PackChanged, opened!.ReadProblem);
        Assert.Null(opened.Text);
    }

    [Fact] public async Task OpenEntry_of_a_missing_entry_is_a_problem_not_an_exception()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var opened = await c.OpenEntryAsync(Pack(c, "a.pack"), "gone.txt");
        Assert.Equal(EntryProblem.Corrupt, opened!.ReadProblem);
    }

    // Review Focus 5
    [Fact] public async Task A_request_while_the_runner_is_busy_does_nothing()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        game.Runner.IsRunning = true;
        Assert.False(c.CanStartOperation);
        Assert.Null(await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Null(await c.OpenEntryAsync(Pack(c, "a.pack"), "a.txt"));
        await c.RefreshAsync(); await c.ReloadSchemaAsync();
        Assert.Empty(game.Runner.Titles);
    }

    [Fact] public async Task Changing_the_install_path_or_the_schema_folder_reloads()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var c = game.NewController();
        await c.LoadAsync();
        game.GameSettings.InstallPath = game.Root.File("nowhere");
        Assert.Equal(GameStatus.NotFound, c.Status);                        // FakeOperationRunner runs synchronously
        game.GameSettings.InstallPath = game.InstallPath;
        Assert.Equal(GameStatus.Found, c.Status);
        game.Settings.SchemaFolder = game.Root.File("no-schemas");
        Assert.Equal(["Refresh pack list", "Refresh pack list", "Load schema"], game.Runner.Titles);
        Assert.Equal(SchemaProblem.NotFound, c.Schema!.OverrideAttempt!.Problem);
    }

    [Fact] public async Task Refresh_drops_cached_indexes_of_changed_packs()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]));
        game.AddPack("b.pack", ("b.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var a = await c.OpenPackAsync(Pack(c, "a.pack")); var b = await c.OpenPackAsync(Pack(c, "b.pack"));
        var changed = new List<string>(); c.PackChanged += (_, p) => changed.Add(p.Name);
        new PackBuilder().AddEntry("a.txt", [1, 2, 3]).WriteTo(path);
        await c.RefreshAsync();
        Assert.Equal(["a.pack"], changed);
        Assert.Same(b, await c.OpenPackAsync(Pack(c, "b.pack")));
        Assert.NotSame(a, await c.OpenPackAsync(Pack(c, "a.pack")));
    }
}

public class DiagnosisTextsTests
{
    private static AppTexts Texts() => new(new UiText(@"X:\none"));

    [Fact] public void Every_problem_value_has_a_text()
    {
        var t = Texts();
        Assert.All(Enum.GetValues<PackProblem>().Where(p => p != PackProblem.None), p => Assert.NotEqual("", t.PackProblemText(p, "PFH5")));
        Assert.All(Enum.GetValues<EntryProblem>().Where(p => p != EntryProblem.None), p => Assert.NotEqual("", t.EntryProblemText(p)));
        Assert.All(Enum.GetValues<TableDecodeStatus>().Where(s => s != TableDecodeStatus.Ok), s => Assert.NotEqual("", t.TableStatusText(s)));
        Assert.All(Enum.GetValues<SchemaProblem>().Where(p => p != SchemaProblem.None), p => Assert.NotEqual("", t.SchemaProblemText(p)));
        Assert.All(Enum.GetValues<PackOrigin>(), o => Assert.NotEqual("", t.OriginName(o)));
        Assert.Equal(("", "", ""), (t.PackProblemText(PackProblem.None, null), t.EntryProblemText(EntryProblem.None), t.TableStatusText(TableDecodeStatus.Ok)));
    }

    [Fact] public void Unknown_format_names_the_format_of_the_game() =>
        Assert.Equal("Unknown pack format. This game uses PFH5.", Texts().PackProblemText(PackProblem.UnknownFormat, "PFH5"));

    [Fact] public void Operation_titles_carry_the_name() =>
        Assert.Equal(("Open a.pack", "Open data__"), (Texts().OperationOpenPack("a.pack"), Texts().OperationOpenEntry("data__")));
}
```

`NoSteam` und `FixedSteam(path)` sind zwei kleine `ISteamLocationSource`-Fakes in `TestGame.cs`. Der bestehende Test `Locale_files_are_complete` deckt die neuen Keys mit ab.

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen.
- [ ] **Step 5: Commit** – `git commit -m "feat: add workspace controller and diagnosis texts"`

---

### Task 5: Explorer

**Files:**
- Create: `src/TotalWarModTool.App/Explorer/ExplorerNodes.cs`, `ExplorerViewModel.cs`, `ExplorerView.xaml`, `ExplorerView.xaml.cs`
- Modify: `src/TotalWarModTool.App/AppTexts.Workspace.cs`, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/ExplorerViewModelTests.cs`, `ExplorerViewTests.cs`

**Interfaces:**
- Consumes: `WorkspaceController` (`Status`, `ManualPathInvalid`, `Catalog`, `IsBusy`, `Changed`, `PackChanged`, `OpenPackAsync`, `ClosePack`, `LastIndexProblem`), `PackContent(Index, Tree)`, `RestoredPack(Pack, Content)`, `PackTreeNode`, `PackSource`, `AppTexts.PackProblemText` / `OriginName` (Task 4), `ObservableObject`, `RelayCommand`, `TestGame` (Task 4).
- Produces:

```csharp
namespace TotalWarModTool.App;

public abstract class ExplorerNode : ObservableObject
{
    public string Name { get; }
    public string? ToolTip { get; protected set; }          // raises PropertyChanged
    public bool HasProblem { get; protected set; }          // raises PropertyChanged
    public ObservableCollection<ExplorerNode> Children { get; }
    public bool IsExpanded { get; set; }                    // bound two-way by the tree; raises PropertyChanged
}

public sealed class GroupNode : ExplorerNode { public PackOrigin Origin { get; } }       // Name: "<origin name> (<count>)"
public sealed class PackNode : ExplorerNode { public PackSource Pack { get; } public bool CanExpand { get; } }
public sealed class FolderNode : ExplorerNode { }
public sealed class EntryNode : ExplorerNode { public PackSource Pack { get; } public string EntryPath { get; } public ICommand OpenCommand { get; } }
public sealed class PlaceholderNode : ExplorerNode { }     // the one child that makes a node expandable before its children exist; Name ""

public sealed class ExplorerViewModel : ObservableObject
{
    public ExplorerViewModel(WorkspaceController workspace, AppTexts texts, Func<PackSource, string, Task> openEntry, Action openSettings);
    public AppTexts Texts { get; }
    public ObservableCollection<ExplorerNode> Groups { get; }
    public string? Message { get; }                         // shown instead of the tree; null when the tree is shown
    public bool ShowOpenSettings { get; }                   // true with the "not found" messages
    public ICommand OpenSettingsCommand { get; }
    public IReadOnlyList<string> ExpandedPackPaths { get; } // catalog order
    public event EventHandler? ExpandedPacksChanged;
    public void ApplyRestored(IReadOnlyList<RestoredPack> packs);   // expands these packs from the given content, without a runner run
}
```

Neue Texte:

| Key | en | de |
|---|---|---|
| `explorer.loading` | Loading the pack list | Die Pack-Liste wird geladen |
| `explorer.noProfile` | No game profile was found. The program folder is incomplete. | Kein Spielprofil gefunden. Der Programmordner ist unvollständig. |
| `explorer.gameNotFound` | The game was not found. Choose the game folder in the settings. | Das Spiel wurde nicht gefunden. Wähle den Spielordner in den Einstellungen. |
| `explorer.manualPathInvalid` | The game folder from the settings has no data folder. Choose the game folder in the settings. | Der Spielordner aus den Einstellungen hat keinen Datenordner. Wähle den Spielordner in den Einstellungen. |
| `explorer.noPacks` | No packs were found in the game folder. | Im Spielordner wurden keine Packs gefunden. |
| `explorer.openSettings` | Open settings | Einstellungen öffnen |
| `explorer.group` | {0} ({1}) | {0} ({1}) |
| `explorer.encrypted` | Encrypted pack, not supported. | Verschlüsseltes Pack, nicht unterstützt. |

Festlegungen:
- **Meldung statt Baum:** Spiel gefunden und noch kein Katalog (vor und während des ersten Ladens) → `explorer.loading`. `NoProfile` → `explorer.noProfile`. `NotFound` → `explorer.gameNotFound` bzw. `explorer.manualPathInvalid`, mit `ShowOpenSettings`. Katalog ohne Packs → `explorer.noPacks`. Sonst `Message = null`.
- **Gruppen** in der Reihenfolge `Vanilla`, `DataFolderMod`, `Workshop`; leere Gruppen fehlen. Die Gruppe `DataFolderMod` ist anfangs aufgeklappt, die anderen zugeklappt.
- **Pack-Knoten.** Name: `pack.Name`, bei Workshop `"<Name> (<WorkshopId>)"`. `ToolTip`: der Pfad. Pack mit `Problem != None`: `HasProblem`, `ToolTip` = Problemtext, Leerzeile, `Detail`; `CanExpand = false`. `Header.HasEncryptedIndex`: `HasProblem`, `ToolTip` = `explorer.encrypted`, `CanExpand = false`. `Header.HasEncryptedData`: `HasProblem` und derselbe Tooltip, aber aufklappbar. Aufklappbare Packs tragen einen `PlaceholderNode`.
- **Aufklappen eines Packs** (`IsExpanded` wird `true`, Kinder sind noch der Platzhalter): `workspace.OpenPackAsync`. Ergebnis da → Kinder aus dem Baum. `null` → `IsExpanded` zurück auf `false`; liefert `LastIndexProblem` etwas, wird der Knoten `HasProblem` mit dem Problemtext als Tooltip. **Zuklappen:** `workspace.ClosePack`, Kinder zurück auf den Platzhalter. Beides löst `ExpandedPacksChanged` aus.
- **Ordner** erzeugen ihre Kinder erst beim ersten Aufklappen (vorher Platzhalter). Ein Ordner mit 50.000 Kindern erzeugt 50.000 Knoten erst dann.
- **Eintrag öffnen:** `OpenCommand` ruft `openEntry(pack, entryPath)`. Die View löst es bei Doppelklick und bei Enter aus.
- **`workspace.Changed`:** Meldung und Gruppen neu aufbauen. Packs, die vorher aufgeklappt waren und deren Index noch im Cache liegt (`OpenPackAsync` liefert dann ohne Runner-Lauf), bleiben aufgeklappt; alle anderen sind zu. Gruppen behalten ihren Aufklapp-Zustand.
- **`workspace.PackChanged`:** der Knoten dieses Packs wird zugeklappt (Kinder zurück auf den Platzhalter).
- **View.** `UserControl` mit `TreeView`, `VirtualizingStackPanel.IsVirtualizing="True"`, `VirtualizationMode="Recycling"`. `IsExpanded` zweiseitig gebunden. Ein Datentemplate für alle Knoten: optionale Markierung (kleiner Kreis in `Theme.Warning`, sichtbar bei `HasProblem`) und der Name; `ToolTip` gebunden. `PlaceholderNode` wird ohne Inhalt gezeigt. Die Meldung steht in einem `TextBlock` mit Umbruch, darunter der Knopf. Code-behind: nur Doppelklick und Enter an `OpenCommand` reichen.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class ExplorerViewModelTests
{
    private static async Task<(TestGame Game, WorkspaceController C, ExplorerViewModel Vm, List<(string Pack, string Entry)> Opened)> Create(Action<TestGame>? arrange = null, bool load = true)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var c = game.NewController(); var opened = new List<(string, string)>();
        var vm = new ExplorerViewModel(c, game.Texts, (p, e) => { opened.Add((p.Name, e)); return Task.CompletedTask; }, () => opened.Add(("settings", "")));
        if (load) await c.LoadAsync();
        return (game, c, vm, opened);
    }
    private static PackNode PackNodeOf(ExplorerViewModel vm, string name) => vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single(p => p.Pack.Name == name);

    [Fact] public async Task Messages_follow_the_state()
    {
        var (game, c, vm, opened) = await Create(load: false);
        using (game)
        {
            Assert.Equal("No packs were found in the game folder.", await Loaded());
            game.GameSettings.InstallPath = game.Root.File("nowhere");
            Assert.Equal(("The game folder from the settings has no data folder. Choose the game folder in the settings.", true), (vm.Message, vm.ShowOpenSettings));
            vm.OpenSettingsCommand.Execute(null);
            Assert.Equal(("settings", ""), Assert.Single(opened));
            async Task<string?> Loaded() { await c.LoadAsync(); return vm.Message; }
        }
    }

    [Fact] public async Task Groups_list_the_packs_by_origin_with_counts()
    {
        var (game, _, vm, _) = await Create(g => { g.AddPack("b.pack"); g.AddPack("a.pack"); });
        using (game)
        {
            Assert.Null(vm.Message);
            var group = Assert.IsType<GroupNode>(Assert.Single(vm.Groups));
            Assert.Equal(("Mods in the data folder (2)", PackOrigin.DataFolderMod, true), (group.Name, group.Origin, group.IsExpanded));
            Assert.Equal(["a.pack", "b.pack"], group.Children.Select(n => n.Name));
            Assert.All(group.Children, n => Assert.IsType<PlaceholderNode>(Assert.Single(n.Children)));
        }
    }

    [Fact] public async Task Expanding_a_pack_shows_its_tree_and_folders_fill_on_demand()
    {
        var (game, _, vm, opened) = await Create(g => g.AddPack("a.pack", ("script/main.lua", [1]), ("readme.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack"); var raised = 0; vm.ExpandedPacksChanged += (_, _) => raised++;
            pack.IsExpanded = true;
            Assert.Equal(["script", "readme.txt"], pack.Children.Select(n => n.Name));
            var folder = Assert.IsType<FolderNode>(pack.Children[0]);
            Assert.IsType<PlaceholderNode>(Assert.Single(folder.Children));
            folder.IsExpanded = true;
            var entry = Assert.IsType<EntryNode>(Assert.Single(folder.Children));
            entry.OpenCommand.Execute(null);
            Assert.Equal(("a.pack", "script/main.lua"), Assert.Single(opened));
            Assert.Equal(([pack.Pack.Path], 1), (vm.ExpandedPackPaths, raised));
        }
    }

    [Fact] public async Task Collapsing_a_pack_releases_its_index()
    {
        var (game, c, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            pack.IsExpanded = true; pack.IsExpanded = false;
            Assert.IsType<PlaceholderNode>(Assert.Single(pack.Children));
            Assert.Empty(vm.ExpandedPackPaths);
            pack.IsExpanded = true;
            Assert.Equal(["Open a.pack", "Open a.pack"], game.Runner.Titles);      // read again: the index was not kept
        }
    }

    [Fact] public async Task A_pack_that_fails_to_open_collapses_and_is_marked()
    {
        var (game, _, vm, _) = await Create(g => g.AddPack("a.pack", ("x", [1, 2, 3])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            File.WriteAllBytes(pack.Pack.Path, File.ReadAllBytes(pack.Pack.Path)[..^1]);
            pack.IsExpanded = true;
            Assert.Equal((false, true), (pack.IsExpanded, pack.HasProblem));
            Assert.StartsWith("The file list of the pack is damaged.", pack.ToolTip);
        }
    }

    [Fact] public async Task A_cancelled_open_collapses_without_a_mark()
    {
        var (game, _, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            game.Runner.CancelNext = true;
            pack.IsExpanded = true;
            Assert.Equal((false, false), (pack.IsExpanded, pack.HasProblem));
        }
    }

    [Fact] public async Task Broken_and_encrypted_packs_are_marked()
    {
        var (game, _, vm, _) = await Create(g =>
        {
            File.WriteAllBytes(Path.Combine(g.DataPath, "broken.pack"), new byte[10]);
            new PackBuilder().WithFlags(0x80).WriteTo(Path.Combine(g.DataPath, "encindex.pack"));
            new PackBuilder().WithFlags(0x10).AddEntry("a", [1]).WriteTo(Path.Combine(g.DataPath, "encdata.pack"));
        });
        using (game)
        {
            var broken = PackNodeOf(vm, "broken.pack");
            Assert.Equal((true, false), (broken.HasProblem, broken.CanExpand));
            Assert.StartsWith("The pack file is incomplete.", broken.ToolTip);
            Assert.Empty(broken.Children);
            var encIndex = PackNodeOf(vm, "encindex.pack");
            Assert.Equal((true, false, "Encrypted pack, not supported."), (encIndex.HasProblem, encIndex.CanExpand, encIndex.ToolTip));
            var encData = PackNodeOf(vm, "encdata.pack");
            Assert.Equal((true, true), (encData.HasProblem, encData.CanExpand));
        }
    }

    [Fact] public async Task Refresh_keeps_unchanged_packs_expanded_and_collapses_changed_ones()
    {
        var (game, c, vm, _) = await Create(g => { g.AddPack("a.pack", ("a.txt", [1])); g.AddPack("b.pack", ("b.txt", [1])); });
        using (game)
        {
            PackNodeOf(vm, "a.pack").IsExpanded = true; PackNodeOf(vm, "b.pack").IsExpanded = true;
            new PackBuilder().AddEntry("a.txt", [1, 2, 3]).WriteTo(PackNodeOf(vm, "a.pack").Pack.Path);
            await c.RefreshAsync();
            Assert.Equal((false, true), (PackNodeOf(vm, "a.pack").IsExpanded, PackNodeOf(vm, "b.pack").IsExpanded));
            Assert.Equal(["b.txt"], PackNodeOf(vm, "b.pack").Children.Select(n => n.Name));
        }
    }

    [Fact] public async Task ApplyRestored_expands_without_a_runner_run()
    {
        var (game, c, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack").Pack;
            var restored = await c.RestoreAsync(new SessionState([], -1, [pack.Path.ToUpperInvariant()]));
            vm.ApplyRestored(restored.Packs);
            Assert.True(PackNodeOf(vm, "a.pack").IsExpanded);
            Assert.Equal(["a.txt"], PackNodeOf(vm, "a.pack").Children.Select(n => n.Name));
            Assert.Empty(game.Runner.Titles);
        }
    }

    [Fact] public async Task Workshop_packs_show_their_id()
    {
        // TestGame variant: install below <Root>\steam\steamapps\common\Test Game and a workshop folder <Root>\steam\steamapps\workshop\content\42\777\mod.pack,
        // found through FixedSteam. Assert: group "Workshop (1)", pack node name "mod.pack (777)".
    }
}

public class ExplorerViewTests
{
    // Sta.Run: an ExplorerView with a view model of a loaded TestGame inside a shown Window.
    [Fact] public void Tree_shows_groups_and_message_is_hidden() { /* the TreeView has one item per group; the message TextBlock is collapsed */ }
    [Fact] public void Message_replaces_the_tree_when_the_game_is_not_found() { /* TreeView collapsed, message text visible, the settings button visible */ }
    [Fact] public void Enter_on_an_entry_opens_it() { /* select the EntryNode's container, raise KeyDown Enter on the tree, the openEntry delegate was called once */ }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen.
- [ ] **Step 5: Commit** – `git commit -m "feat: add the pack explorer"`

---

### Task 6: Tabs

**Files:**
- Create: `src/TotalWarModTool.App/Tabs/TabViewModels.cs`, `TabFactory.cs`, `TabsViewModel.cs`, `TabsView.xaml`, `TabsView.xaml.cs`, `TableGrid.cs` (abgeleitetes `DataGrid`, das seine Spalten aus dem ViewModel erzeugt)
- Modify: `src/TotalWarModTool.App/AppTexts.Workspace.cs`, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/TabFactoryTests.cs`, `TabsViewModelTests.cs`, `TabsViewTests.cs`

**Interfaces:**
- Consumes: `OpenedEntry` (`EntryPath`, `Kind`, `StoredSize`, `UncompressedSize`, `Format`, `ReadProblem`, `ReadDetail`, `SchemaMissing`, `Table`, `Loc`, `Text`), `DecodedTable(Status, TableName, Version, HasGuid, Definition, Rows, Detail)`, `DecodedLoc(Status, Rows, Detail)`, `LocRow(Key, Text, Tooltip)`, `TextContent(Text, EncodingName, Truncated, TotalBytes)`, `ResolvedSchema`, `PackSource`, `AppTexts` (Task 4).
- Produces:

```csharp
namespace TotalWarModTool.App;

public abstract class TabViewModel : ObservableObject
{
    public string PackPath { get; }            // PackSource.Path
    public string EntryPath { get; }
    public string Title { get; }               // last segment of EntryPath
    public string ToolTip { get; }             // "<pack name>: <entry path>"
    public bool IsStale { get; }               // raises PropertyChanged
    public ICommand CloseCommand { get; }
}

public sealed class TableTabViewModel : TabViewModel
{
    public string Header { get; }                        // "x_tables, version 0, 1 row(s)" via AppTexts
    public IReadOnlyList<string> Columns { get; }        // field names in schema order
    public IReadOnlyList<object?[]> Rows { get; }
}
public sealed class LocTabViewModel : TabViewModel { public string Header { get; } public IReadOnlyList<LocRow> Rows { get; } }
public sealed class TextTabViewModel : TabViewModel { public string Text { get; } public string Header { get; } public string? Notice { get; } }
public sealed record InfoLine(string Label, string Value);
public sealed class InfoTabViewModel : TabViewModel { public string Heading { get; } public string Reason { get; } public IReadOnlyList<InfoLine> Lines { get; } }

public static class TabFactory
{
    public static TabViewModel Create(PackSource pack, OpenedEntry entry, ResolvedSchema? schema, AppTexts texts, Action<TabViewModel> close);
    public static string FormatSize(long bytes);         // "512 B", "1.5 KB", "3.2 MB", "1.1 GB"; invariant culture, one decimal from KB on
}

public sealed class TabsViewModel : ObservableObject
{
    public TabsViewModel(AppTexts texts);
    public AppTexts Texts { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }
    public TabViewModel? SelectedTab { get; set; }
    public ICommand CloseSelectedCommand { get; }
    public event EventHandler? Changed;                               // tabs added, removed or the selection changed
    public bool TryActivate(string packPath, string entryPath);       // true when such a tab exists; it is selected then
    public void Add(PackSource pack, OpenedEntry entry, ResolvedSchema? schema, bool select = true);
    public void Close(TabViewModel tab);
    public void MarkStale(string packPath);
}
```

Neue Texte:

| Key | en | de |
|---|---|---|
| `tab.close` | Close | Schließen |
| `tab.stale` | The pack was changed on disk. This tab shows the old content. | Das Pack wurde auf der Platte geändert. Dieser Tab zeigt den alten Inhalt. |
| `tab.table.header` | {0}, version {1}, {2} row(s) | {0}, Version {1}, {2} Zeile(n) |
| `tab.loc.header` | {0} row(s) | {0} Zeile(n) |
| `tab.loc.key` | Key | Schlüssel |
| `tab.loc.text` | Text | Text |
| `tab.loc.tooltip` | Tooltip | Tooltip |
| `tab.text.header` | {0}, {1} | {0}, {1} |
| `tab.text.truncated` | Only the first {0} of {1} are shown. | Es werden nur die ersten {0} von {1} gezeigt. |
| `tab.info.notDecodable` | This table cannot be decoded | Diese Tabelle ist nicht dekodierbar |
| `tab.info.notSupported` | This file type is not supported | Dieser Dateityp wird nicht unterstützt |
| `tab.info.notReadable` | This entry cannot be opened | Dieser Eintrag lässt sich nicht öffnen |
| `tab.info.noSchema` | No schema is loaded, so tables cannot be decoded. Check the schema folder in the settings. | Es ist kein Schema geladen, deshalb sind Tabellen nicht dekodierbar. Prüfe den Schema-Ordner in den Einstellungen. |
| `tab.info.type` | Type | Typ |
| `tab.info.storedSize` | Size in the pack | Größe im Pack |
| `tab.info.size` | Size | Größe |
| `tab.info.compression` | Compression | Kompression |
| `tab.info.compression.none` | none | keine |
| `tab.info.compression.unknown` | unknown | unbekannt |
| `tab.info.table` | Table | Tabelle |
| `tab.info.version` | Version | Version |
| `tab.info.schema` | Schema | Schema |
| `tab.info.detail` | Technical detail | Technisches Detail |
| `tab.info.type.none` | (no extension) | (keine Endung) |
| `schema.source.shipped` | shipped with the app, {0} | mit der App geliefert, {0} |
| `schema.source.override` | from the schema folder, {0} | aus dem Schema-Ordner, {0} |
| `schema.source.none` | none | keins |

Festlegungen:
- **`TabFactory.Create`** wählt in dieser Reihenfolge:
  1. `ReadProblem != None` → `InfoTabViewModel`, Überschrift `tab.info.notReadable`, Grund `EntryProblemText`.
  2. `Kind == Table`, `SchemaMissing` → Info, `tab.info.notDecodable`, Grund `tab.info.noSchema`.
  3. `Kind == Table`, `Table.Status != Ok` → Info, `tab.info.notDecodable`, Grund `TableStatusText`.
  4. `Kind == Table` → `TableTabViewModel`.
  5. `Kind == Loc`, `Loc.Status != Ok` → Info, `tab.info.notDecodable`, Grund `table.status.failed`.
  6. `Kind == Loc` → `LocTabViewModel`. `Kind == Text` → `TextTabViewModel`. `Kind == Other` → Info, `tab.info.notSupported`, Grund leer.
- **Zeilen des Info-Tabs**, in dieser Reihenfolge, nur wenn der Wert vorliegt: Typ (Endung mit Punkt, klein; ohne Endung `tab.info.type.none`; bei Tabellen entfällt die Zeile), Tabelle (Name) und Version (nur Tabellen, Version nur wenn der Kopf lesbar war: `Table != null`), Größe im Pack, Größe (unkomprimiert; entfällt, wenn sie 0 ist und ein Leseproblem vorliegt), Kompression (`none`, `zstd`, `LZ4`, `LZMA`, `unknown`; Formatnamen sind keine UI-Texte), Schema (nur Tabellen: Quelle mit Datum), Technisches Detail (`ReadDetail` bzw. `Table.Detail` bzw. `Loc.Detail`).
- **Schema-Quelle als Text:** `schema.source.shipped` / `schema.source.override` mit dem Dateidatum als `yyyy-MM-dd` (lokale Zeit), ohne Schema `schema.source.none`. Dieselbe Formatierung nutzt Task 7 für die Statuszeile; sie liegt als Methode `AppTexts.SchemaSourceText(ResolvedSchema?)` in `AppTexts.Workspace.cs`.
- **Text-Tab:** `Header` = `tab.text.header` mit Kodierung und `FormatSize(TotalBytes)`. `Notice` nur bei `Truncated`: `tab.text.truncated` mit `FormatSize(EntryText.MaxBytes)` und `FormatSize(TotalBytes)`.
- **`TabsViewModel`:** `TryActivate` und alle Vergleiche von Pack- und Eintragspfad ohne Groß-/Kleinschreibung. `Add` hängt hinten an. `Close` wählt den rechten Nachbarn, sonst den linken, sonst `null`. `MarkStale` setzt `IsStale` bei allen Tabs des Packs. `Changed` kommt bei jeder Änderung der Liste oder der Auswahl.
- **View.** `TabControl` an `Tabs` / `SelectedTab`. Reiter: Titel, bei `IsStale` die Markierung in `Theme.Warning` mit Tooltip `tab.stale`, Schließen-Knopf (Symbol als `Path`, Tooltip `tab.close`). Mittelklick auf den Reiter schließt (Code-behind reicht an `CloseCommand`). `Strg+W` ist an `CloseSelectedCommand` gebunden. Inhalt über Datentemplates je ViewModel-Typ.
- **Tabellen-Raster (`TableGrid`).** Nur lesen, `AutoGenerateColumns="False"`, Zeilen- und Spaltenvirtualisierung an (`VirtualizationMode="Recycling"`), `CanUserAddRows`/`CanUserDeleteRows` aus, `SelectionUnit="CellOrRowHeader"` nicht nötig (Standard bleibt). `TableGrid` hat eine Abhängigkeitseigenschaft `ColumnNames`; bei Änderung baut es je Name eine `DataGridTextColumn` mit `Binding("[i]")` (`OneTime`) und einem `TextBlock` als Kopf, damit Unterstriche sichtbar bleiben. Das ist View-Verdrahtung. Sortieren bleibt beim Standard des Rasters.
- **Loc-Raster:** drei feste Spalten (`tab.loc.key`, `tab.loc.text`, `tab.loc.tooltip`).
- **Text:** `TextBox`, nur lesen, kein Umbruch, beide Bildlaufleisten, Schrift `Consolas`. `Notice` steht darüber in `Theme.Warning`.
- **Info:** Überschrift, Grund mit Umbruch, darunter die Zeilen als zweispaltige Liste. Werte sind markierbar (nur lesende `TextBox` ohne Rahmen), damit man sie kopieren kann.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class TabFactoryTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource Pack = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
    private static OpenedEntry Entry(string path, EntryKind kind, EntryProblem problem = EntryProblem.None, string? detail = null, bool schemaMissing = false,
        DecodedTable? table = null, DecodedLoc? loc = null, TextContent? text = null, CompressionFormat format = CompressionFormat.None, long stored = 10, long size = 20) =>
        new(path, kind, stored, size, format, problem, detail, schemaMissing, table, loc, text);
    private static TabViewModel Create(OpenedEntry e, ResolvedSchema? schema = null) => TabFactory.Create(Pack, e, schema, Texts, _ => { });
    private static readonly TableDefinition Def = new(3, [new("unit_key", FieldType.StringU8, "StringU8", true, null), new("cost", FieldType.I32, "I32", false, null)]);

    [Theory]
    [InlineData(0, "0 B")] [InlineData(1023, "1023 B")] [InlineData(1024, "1.0 KB")] [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")] [InlineData(1181116006, "1.1 GB")]
    public void FormatSize_uses_binary_units(long bytes, string expected) => Assert.Equal(expected, TabFactory.FormatSize(bytes));

    [Fact] public void Table_tab_has_columns_rows_and_header()
    {
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 3, false, Def, [["a", 1], ["b", 2]], null);
        var tab = Assert.IsType<TableTabViewModel>(Create(Entry("db/units_tables/data__", EntryKind.Table, table: table)));
        Assert.Equal(("data__", "a.pack: db/units_tables/data__", "units_tables, version 3, 2 row(s)", Pack.Path), (tab.Title, tab.ToolTip, tab.Header, tab.PackPath));
        Assert.Equal(["unit_key", "cost"], tab.Columns);
        Assert.Equal(2, tab.Rows.Count);
    }

    [Fact] public void Table_that_does_not_decode_becomes_an_info_tab_with_table_version_and_schema()
    {
        var table = new DecodedTable(TableDecodeStatus.VersionNotInSchema, "units_tables", 9, false, null, [], "Version 9 is not in the schema.");
        var schema = new ResolvedSchema(null, SchemaOrigin.Shipped, @"X:\s.ron", new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(), null, null);
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("db/units_tables/data__", EntryKind.Table, table: table, format: CompressionFormat.Zstd), schema));
        Assert.Equal(("This table cannot be decoded", "The schema does not know this version of the table."), (tab.Heading, tab.Reason));
        Assert.Equal([new("Table", "units_tables"), new("Version", "9"), new("Size in the pack", "10 B"), new("Size", "20 B"), new("Compression", "zstd"),
            new("Schema", "shipped with the app, 2026-09-24"), new InfoLine("Technical detail", "Version 9 is not in the schema.")], tab.Lines);
    }

    [Fact] public void Table_without_schema_names_the_missing_schema()
    {
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("db/units_tables/data__", EntryKind.Table, schemaMissing: true)));
        Assert.StartsWith("No schema is loaded", tab.Reason);
        Assert.Contains(new InfoLine("Schema", "none"), tab.Lines);
        Assert.DoesNotContain(tab.Lines, l => l.Label == "Version");
    }

    [Fact] public void Unsupported_type_lists_type_sizes_and_compression()
    {
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("movies/Intro.CA_VP8", EntryKind.Other, format: CompressionFormat.Lz4, stored: 2048, size: 4096)));
        Assert.Equal(("This file type is not supported", ""), (tab.Heading, tab.Reason));
        Assert.Equal([new("Type", ".ca_vp8"), new("Size in the pack", "2.0 KB"), new("Size", "4.0 KB"), new InfoLine("Compression", "LZ4")], tab.Lines);
    }

    [Fact] public void Read_problem_wins_over_the_kind()
    {
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("script/main.lua", EntryKind.Text, EntryProblem.UnsupportedCompression, "LZMA is not supported.", format: CompressionFormat.Lzma)));
        Assert.Equal(("This entry cannot be opened", "This compression format is not supported yet."), (tab.Heading, tab.Reason));
        Assert.Contains(new InfoLine("Compression", "LZMA"), tab.Lines);
        Assert.Contains(new InfoLine("Technical detail", "LZMA is not supported."), tab.Lines);
    }

    [Fact] public void Loc_and_text_tabs()
    {
        var loc = Assert.IsType<LocTabViewModel>(Create(Entry("text/names.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Ok, [new("k", "t", false)], null))));
        Assert.Equal(("1 row(s)", 1), (loc.Header, loc.Rows.Count));
        var failedLoc = Assert.IsType<InfoTabViewModel>(Create(Entry("text/names.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Failed, [], "short"))));
        Assert.Equal("This table cannot be decoded", failedLoc.Heading);
        var text = Assert.IsType<TextTabViewModel>(Create(Entry("a.txt", EntryKind.Text, text: new TextContent("hi", "UTF-8", false, 2))));
        Assert.Equal(("hi", "UTF-8, 2 B", (string?)null), (text.Text, text.Header, text.Notice));
        var big = Assert.IsType<TextTabViewModel>(Create(Entry("a.txt", EntryKind.Text, text: new TextContent("hi", "UTF-8", true, 8 * 1024 * 1024))));
        Assert.Equal("Only the first 5.0 MB of 8.0 MB are shown.", big.Notice);
    }

    [Fact] public void File_without_extension_says_so() =>
        Assert.Contains(new InfoLine("Type", "(no extension)"), Assert.IsType<InfoTabViewModel>(Create(Entry("misc/blob", EntryKind.Other))).Lines);
}

public class TabsViewModelTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource A = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
    private static readonly PackSource B = A with { Path = @"D:\data\b.pack", Name = "b.pack" };
    private static OpenedEntry Text(string path) => new(path, EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, new TextContent("x", "UTF-8", false, 1));

    // Review Focus 5
    [Fact] public void Opening_an_open_entry_activates_its_tab()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(A, Text("b.txt"), null);
        Assert.Equal("b.txt", tabs.SelectedTab!.Title);
        Assert.True(tabs.TryActivate(@"d:\DATA\a.pack", "A.TXT"));
        Assert.Equal(("a.txt", 2), (tabs.SelectedTab!.Title, tabs.Tabs.Count));
        Assert.False(tabs.TryActivate(B.Path, "a.txt"));
    }

    [Fact] public void Add_without_select_keeps_the_selection()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(A, Text("b.txt"), null, select: false);
        Assert.Equal("a.txt", tabs.SelectedTab!.Title);
    }

    [Fact] public void Close_selects_the_right_neighbour_then_the_left_one()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("1"), null); tabs.Add(A, Text("2"), null); tabs.Add(A, Text("3"), null);
        tabs.SelectedTab = tabs.Tabs[1];
        tabs.SelectedTab.CloseCommand.Execute(null);
        Assert.Equal("3", tabs.SelectedTab!.Title);
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Equal("1", tabs.SelectedTab!.Title);
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Null(tabs.SelectedTab); Assert.Empty(tabs.Tabs);
        tabs.CloseSelectedCommand.Execute(null);                       // nothing selected: nothing happens
    }

    [Fact] public void MarkStale_marks_only_the_tabs_of_that_pack()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(B, Text("b.txt"), null);
        tabs.MarkStale(@"D:\DATA\A.PACK");
        Assert.Equal([true, false], tabs.Tabs.Select(t => t.IsStale));
    }

    [Fact] public void Changed_is_raised_for_add_close_and_selection()
    {
        var tabs = new TabsViewModel(Texts); var changes = 0; tabs.Changed += (_, _) => changes++;
        tabs.Add(A, Text("a.txt"), null); var afterAdd = changes;
        tabs.Add(A, Text("b.txt"), null); tabs.SelectedTab = tabs.Tabs[0]; var afterSelect = changes;
        tabs.Close(tabs.Tabs[0]);
        Assert.True(afterAdd >= 1); Assert.True(afterSelect > afterAdd); Assert.True(changes > afterSelect);
    }
}

public class TabsViewTests
{
    // Sta.Run: a TabsView with a TabsViewModel inside a shown Window that carries the app resources.
    [Fact] public void Table_grid_gets_one_column_per_field_and_keeps_underscores()
    { /* table tab with columns "unit_key", "cost" and 2 rows -> the TableGrid has 2 columns; each header is a TextBlock whose Text is the full name;
         after a layout pass the first cell of the first row shows "a" */ }
    [Fact] public void Switching_to_another_table_tab_rebuilds_the_columns() { /* second tab with 3 columns -> 3 columns, no leftovers */ }
    [Fact] public void Middle_click_on_a_tab_header_closes_the_tab() { /* raise MouseDown with MouseButton.Middle on the header element -> Tabs.Count drops by one */ }
    [Fact] public void Info_tab_shows_heading_reason_and_lines() { /* the visual tree contains the heading text and one row per InfoLine */ }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen.
- [ ] **Step 5: Commit** – `git commit -m "feat: add tabs for tables, loc files, text and hints"`

---

### Task 7: Hauptfenster, Statuszeile, Session, Zusammenbau

**Files:**
- Create: `src/TotalWarModTool.App/SessionCoordinator.cs`
- Modify: `src/TotalWarModTool.App/MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`, `AppTexts.Workspace.cs`, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/SessionCoordinatorTests.cs`, `MainWindowTests.cs`; ergänzt und im Aufbau angepasst: `MainViewModelTests.cs`

**Interfaces:**
- Consumes: `WorkspaceController` (Task 4), `ExplorerViewModel` (`ExpandedPackPaths`, `ExpandedPacksChanged`, `ApplyRestored`; Task 5), `TabsViewModel` (`Tabs`, `SelectedTab`, `Changed`, `TryActivate`, `Add`, `MarkStale`; Task 6), `AppTexts.SchemaSourceText` (Task 6), `SessionStore`, `SessionState`, `SessionLoadResult` (Task 1), `GameSettingsStore` (Task 1), `DebouncedSaver`, `RegistrySteamLocationSource`, `AppPaths.ProfilesDirectory` (Task 3), `GameProfileLoader.LoadAll`.
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed class MainViewModel : ObservableObject   // constructor and added members
{
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder, IOperationRunner progress, Action openSettings, WorkspaceController workspace);
    public ExplorerViewModel Explorer { get; }          // created here, with OpenEntryAsync and openSettings
    public TabsViewModel Tabs { get; }                  // created here
    public ICommand RefreshCommand { get; }             // enabled when workspace.CanStartOperation and a profile exists
    public string StatusGame { get; }
    public string? StatusGameToolTip { get; }           // the install path, null when not found
    public string StatusSchema { get; }
    public string? StatusHint { get; }
    public Task OpenEntryAsync(PackSource pack, string entryPath);
}

/// Connects tabs and explorer with session.json.
public sealed class SessionCoordinator
{
    public SessionCoordinator(SessionStore store, SessionLoadResult loaded, WorkspaceController workspace, ExplorerViewModel explorer, TabsViewModel tabs, IAppLog log);
    public event EventHandler? Dirty;      // tabs, selection or expanded packs changed after the restore ended
    public SessionState Capture();         // tabs in order, index of the selected tab (-1 without tabs), expanded packs
    public Task RestoreAsync();            // call once, after workspace.LoadAsync
    public void Save();                    // writes Capture(); see the rules below
}
```

Neue Texte:

| Key | en | de |
|---|---|---|
| `status.game` | Game: {0} | Spiel: {0} |
| `status.game.notFound` | Game: {0} (not found) | Spiel: {0} (nicht gefunden) |
| `status.game.none` | Game: none | Spiel: keins |
| `status.loading` | Loading | Wird geladen |
| `status.schema` | Schema: {0} | Schema: {0} |
| `status.hint.noSchema` | No schema could be loaded ({0}). Tables cannot be decoded. Check the schema folder in the settings. | Es konnte kein Schema geladen werden ({0}). Tabellen sind nicht dekodierbar. Prüfe den Schema-Ordner in den Einstellungen. |
| `status.hint.schemaOverrideFailed` | The schema folder has no usable {0} ({1}). The shipped schema is used. | Der Schema-Ordner enthält keine brauchbare {0} ({1}). Das mitgelieferte Schema wird benutzt. |
| `status.hint.folderProblem` | A pack folder could not be read: {0} | Ein Pack-Ordner konnte nicht gelesen werden: {0} |

Der Menüeintrag „Pack-Liste aktualisieren“ nutzt `Texts.OperationRefreshPacks`.

Festlegungen:
- **`OpenEntryAsync`:** `Tabs.TryActivate` trifft → fertig, kein Lesen. Sonst `workspace.OpenEntryAsync`; `null` → nichts. Sonst `Tabs.Add(pack, opened, workspace.Schema)`.
- **`workspace.PackChanged`** → `Tabs.MarkStale(pack.Path)`.
- **Statuszeile.** `StatusGame`: `status.game` mit dem Anzeigenamen des Profils; `status.game.notFound` bei `NotFound`; `status.game.none` ohne Profil. Während `IsBusy` und ohne Katalog hängt `" – " + status.loading` an. `StatusSchema`: `status.schema` mit `SchemaSourceText`. `StatusHint`, erste zutreffende Regel: kein Schema → `status.hint.noSchema` mit `SchemaProblemText` des ausgelieferten Versuchs; Override gesetzt und gescheitert → `status.hint.schemaOverrideFailed` mit Dateiname und `SchemaProblemText`; Ordnerprobleme → `status.hint.folderProblem` mit dem ersten Pfad; sonst `null`. Alle vier Eigenschaften melden `PropertyChanged` bei `workspace.Changed` und bei einem Sprachwechsel.
- **`RefreshCommand`:** `workspace.RefreshAsync`. `CanExecuteChanged` bei `workspace.Changed`.
- **`SessionCoordinator`:**
  - `RestoreAsync`: Ist `workspace.Catalog` `null`, passiert nichts. Sonst `workspace.RestoreAsync(loaded.State)`, dann `explorer.ApplyRestored`, dann die Tabs in der gelieferten Reihenfolge mit `select: false`, dann die Auswahl nach `ActiveTab`. Tabs, die inzwischen schon offen sind, werden nicht doppelt angelegt.
  - `Save` schreibt nicht, solange das Wiederherstellen nicht beendet ist, nicht wenn `workspace.Catalog` `null` ist (sonst würde eine Sitzung ohne gefundenes Spiel die gespeicherten Tabs löschen) und nie bei `loaded.Status` `NewerVersion` oder `Unreadable` (eine Warnung im Log, einmal im Konstruktor).
  - `Dirty` kommt bei `tabs.Changed` und `explorer.ExpandedPacksChanged`, aber erst nach dem Ende von `RestoreAsync`. Am Ende von `RestoreAsync` kommt `Dirty` einmal, wenn dabei etwas weggefallen ist.
  - Die Problem-Meldung zum Laden (`Corrupt` mit `.bad`-Pfad usw.) loggt `App.xaml.cs` wie bei den Einstellungen.
- **`MainWindow.xaml`:** Menü oben (Werkzeuge: Pack-Liste aktualisieren, Einstellungen, Log-Ordner öffnen; Debug-Menü bleibt). Statuszeile unten: `StatusGame` (mit Tooltip), Trenner, `StatusSchema`, dann `StatusHint` in `Theme.Warning` (ausgeblendet, wenn `null`). Dazwischen ein `Grid` mit drei Spalten: `ExplorerView` (Startbreite 320, Mindestbreite 150), `GridSplitter` (Breite 5), `TabsView`. `F5` ist an `RefreshCommand` gebunden.
- **`App.xaml.cs`, Reihenfolge in `Compose`** (nach Theme und `SettingsApplier`, vor dem Fenster):
  1. `GameProfileLoader.LoadAll(paths.ProfilesDirectory)`; jedes `ProfileProblem` als Warnung loggen.
  2. Profil wählen: das mit der Id `settings.SelectedGame` (ohne Groß-/Kleinschreibung); sonst das erste der Liste. Weicht die Wahl vom gespeicherten Wert ab, `settings.SelectedGame` setzen. Ohne Profile: `null`.
  3. Mit Profil: `GameSettingsStore` und `SessionStore` für `profile.Id` laden, Ladeprobleme loggen (Meldung wie `SettingsLoadMessage.Build`). Ohne Profil: `new GameSettings()`, keine Session.
  4. `DebouncedSaver` für die Spieleinstellungen (an `gameSettings.Changed`; nicht bei `NewerVersion` / `Unreadable`).
  5. `WorkspaceController`, `MainViewModel`, `SessionCoordinator`, `DebouncedSaver` für die Session (an `Dirty`).
  6. Fenster zeigen, `ShutdownMode` zurücksetzen. Danach, ohne zu warten: `LoadAsync`, dann `RestoreAsync`. Eine Exception aus dieser Kette geht an `crashHandler.Handle` (nicht fatal).
  - `OnExit`: alle drei Saver `Flush`.
  - Das Einstellungsfenster und sein ViewModel bleiben in dieser Task unverändert (Task 8).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// MainViewModelTests: the existing tests keep their expectations; only the construction gets the extra `workspace` argument
// (a controller of a TestGame). New tests:
public class MainViewModelWorkspaceTests
{
    private static (TestGame Game, WorkspaceController C, MainViewModel Vm) Create(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var c = game.NewController();
        return (game, c, new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => { }, c));
    }
    private static PackSource Pack(WorkspaceController c, string name) => c.Catalog!.Packs.Single(p => p.Name == name);

    [Fact] public async Task Opening_an_entry_adds_a_tab_and_a_second_request_only_activates_it()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray()), ("b.txt", "two"u8.ToArray())));
        using (game)
        {
            await c.LoadAsync();
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "a.txt"); await vm.OpenEntryAsync(Pack(c, "a.pack"), "b.txt");
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "A.TXT");
            Assert.Equal((2, "a.txt"), (vm.Tabs.Tabs.Count, vm.Tabs.SelectedTab!.Title));
            Assert.Equal(2, game.Runner.Titles.Count);                    // the third request read nothing
        }
    }

    [Fact] public async Task A_cancelled_open_adds_no_tab()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            await c.LoadAsync();
            game.Runner.CancelNext = true;
            await vm.OpenEntryAsync(Pack(c, "a.pack"), "a.txt");
            Assert.Empty(vm.Tabs.Tabs);
        }
    }

    // Review Focus 5
    [Fact] public async Task Refresh_is_disabled_while_loading()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack"));
        using (game)
        {
            var load = c.LoadAsync();
            Assert.False(vm.RefreshCommand.CanExecute(null));
            await load;
            Assert.True(vm.RefreshCommand.CanExecute(null));
            vm.RefreshCommand.Execute(null);
            Assert.Equal(["Refresh pack list"], game.Runner.Titles);
        }
    }

    [Fact] public async Task Status_line_names_game_and_schema()
    {
        var (game, c, vm) = Create();
        using (game)
        {
            Assert.Equal("Game: Test Game", vm.StatusGame.Split(" – ")[0]);
            var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            await c.LoadAsync();
            Assert.Equal(("Game: Test Game", game.InstallPath, (string?)null), (vm.StatusGame, vm.StatusGameToolTip, vm.StatusHint));
            Assert.StartsWith("Schema: shipped with the app, ", vm.StatusSchema);
            Assert.Contains(nameof(MainViewModel.StatusGame), changed); Assert.Contains(nameof(MainViewModel.StatusSchema), changed);
            game.GameSettings.InstallPath = game.Root.File("nowhere");
            Assert.Equal(("Game: Test Game (not found)", (string?)null), (vm.StatusGame, vm.StatusGameToolTip));
        }
    }

    [Fact] public async Task Status_hint_names_a_broken_schema_override_and_a_missing_schema()
    {
        var (game, c, vm) = Create();
        using (game)
        {
            await c.LoadAsync();
            game.Settings.SchemaFolder = game.Root.File("no-schemas");
            Assert.Equal("The schema folder has no usable schema_test.ron (file not found). The shipped schema is used.", vm.StatusHint);
            File.Delete(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"));
            await c.ReloadSchemaAsync();
            Assert.StartsWith("No schema could be loaded (file not found).", vm.StatusHint);
            Assert.Equal("Schema: none", vm.StatusSchema);
        }
    }

    [Fact] public async Task A_pack_change_marks_the_open_tabs_of_that_pack_as_stale()
    {
        var (game, c, vm) = Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray()), ("b.txt", "two"u8.ToArray())));
        using (game)
        {
            await c.LoadAsync();
            var pack = Pack(c, "a.pack");
            await vm.OpenEntryAsync(pack, "a.txt");
            new PackBuilder().AddEntry("a.txt", "changed"u8.ToArray()).AddEntry("b.txt", "two!"u8.ToArray()).WriteTo(pack.Path);
            await vm.OpenEntryAsync(pack, "b.txt");
            Assert.Equal([true, false], vm.Tabs.Tabs.Select(t => t.IsStale));
            Assert.Equal("two!", Assert.IsType<TextTabViewModel>(vm.Tabs.Tabs[1]).Text);
        }
    }
}

public class SessionCoordinatorTests
{
    private static async Task<(TestGame Game, WorkspaceController C, MainViewModel Vm, SessionStore Store)> Create(Action<TestGame> arrange)
    {
        var game = new TestGame(); arrange(game);
        var c = game.NewController();
        await c.LoadAsync();
        return (game, c, new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => { }, c), new SessionStore(game.Paths.RoamingRoot, "testgame"));
    }
    private static SessionCoordinator Coordinator(TestGame game, WorkspaceController c, MainViewModel vm, SessionStore store) =>
        new(store, store.Load(), c, vm.Explorer, vm.Tabs, game.Log);

    [Fact] public async Task Restore_reopens_tabs_selection_and_expanded_packs()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray()), ("db/x_tables/data__", TestGame.TableBytes)));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(path.ToUpperInvariant(), "A.TXT"), new(path, "db/x_tables/data__")], 1, [path]));
            var session = Coordinator(game, c, vm, store);
            await session.RestoreAsync();
            Assert.Equal(["a.txt", "data__"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal(path, vm.Tabs.Tabs[0].PackPath);                              // the spelling of the catalog
            Assert.IsType<TableTabViewModel>(vm.Tabs.SelectedTab);
            Assert.Equal([path], vm.Explorer.ExpandedPackPaths);
            Assert.Empty(game.Runner.Titles);                                          // silent
        }
    }

    // Review Focus 2
    [Fact] public async Task Restore_drops_tabs_of_missing_packs_and_entries()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(@"D:\gone.pack", "x.txt"), new(path, "gone.txt"), new(path, "b.txt"), new(path, "a.txt")], 2, [@"D:\gone.pack"]));
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            Assert.Equal(["b.txt", "a.txt"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal("b.txt", vm.Tabs.SelectedTab!.Title);                         // the tab that was active before
            Assert.Empty(vm.Explorer.ExpandedPackPaths);
            Assert.Equal(1, dirty);
            Assert.True(game.Log.Infos.Count >= 2);                                    // one entry per dropped tab; use the member names FakeAppLog has
        }
    }

    [Fact] public async Task Save_writes_tabs_selection_and_expanded_packs()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            var pack = c.Catalog!.Packs[0];
            await vm.OpenEntryAsync(pack, "a.txt"); await vm.OpenEntryAsync(pack, "b.txt");
            vm.Tabs.SelectedTab = vm.Tabs.Tabs[0];
            vm.Explorer.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single().IsExpanded = true;
            Assert.True(dirty >= 3);
            session.Save();
            var saved = store.Load().State;
            Assert.Equal([new SessionTab(pack.Path, "a.txt"), new SessionTab(pack.Path, "b.txt")], saved.Tabs);
            Assert.Equal((0, pack.Path), (saved.ActiveTab, Assert.Single(saved.ExpandedPacks)));
        }
    }

    [Fact] public async Task Nothing_is_saved_before_the_restore_ended_or_without_a_catalog()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(path, "a.txt")], 0, []));
            var session = Coordinator(game, c, vm, store);
            session.Save();                                                            // before RestoreAsync
            Assert.Single(store.Load().State.Tabs);
            game.GameSettings.InstallPath = game.Root.File("nowhere");                 // catalog gone
            await session.RestoreAsync();
            session.Save();
            Assert.Single(store.Load().State.Tabs);
        }
    }

    [Fact] public async Task A_session_file_of_a_newer_version_is_never_overwritten()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
            File.WriteAllText(store.FilePath, """{"schemaVersion":99,"tabs":[]}""");
            var before = File.ReadAllText(store.FilePath);
            var session = Coordinator(game, c, vm, store);
            await session.RestoreAsync();
            await vm.OpenEntryAsync(c.Catalog!.Packs[0], "a.txt");
            session.Save();
            Assert.Equal(before, File.ReadAllText(store.FilePath));
            Assert.NotEmpty(game.Log.Warnings);
        }
    }
}

public class MainWindowTests
{
    // Sta.Run: a MainWindow with a MainViewModel of a loaded TestGame, shown and closed.
    [Fact] public void Window_shows_explorer_tabs_and_status_line() { /* the visual tree contains one ExplorerView, one TabsView and a StatusBar whose first item shows vm.StatusGame */ }
    [Fact] public void Tools_menu_offers_refresh_settings_and_log_folder() { /* three items in this order, headers from AppTexts, the first bound to RefreshCommand */ }
    [Fact] public void Status_hint_is_hidden_without_a_hint() { /* the hint element is collapsed when StatusHint is null and visible after the schema override failed */ }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen. Danach `run.bat`: Die App startet, der Explorer füllt sich, ein Doppelklick auf eine Tabelle öffnet einen Tab, nach einem Neustart ist der Tab wieder da. Ergebnis im Bericht nennen.
- [ ] **Step 5: Commit** – `git commit -m "feat: wire explorer, tabs, status line and session into the main window"`

---

### Task 8: Einstellungen für Spielpfad und Schema-Ordner, Dokumente, Abnahme

**Files:**
- Modify: `src/TotalWarModTool.App/SettingsUi/SettingsViewModel.cs`, `SettingsWindow.xaml`, `App.xaml.cs`, `AppTexts.cs`, `locales/en.json`, `locales/de.json`
- Modify: `CHANGELOG.md`, `README.md`, `Backlog.txt`, `docs/specs/2026-10-02-fundament.md`
- Test: ergänzt und im Aufbau angepasst: `tests/TotalWarModTool.App.Tests/SettingsViewModelTests.cs`, `SettingsWindowTests.cs`

**Interfaces:**
- Consumes: `GameSettings.InstallPath`, `GlobalSettings.SchemaFolder` (Task 1), `WorkspaceController` (`Profile`, `Status`, `Location`, `Schema`, `Changed`; Task 4), `GameLocator.FromInstallPath`, `AppTexts.SchemaSourceText`, `SchemaProblemText`.
- Produces:

```csharp
public sealed class SettingsViewModel : ObservableObject   // constructor and added members
{
    /// <param name="pickFolder">Shows a folder dialog starting at the given folder and returns the choice, or null when cancelled.</param>
    public SettingsViewModel(GlobalSettings settings, AppTexts texts, IReadOnlyList<UiLanguageOption> languages,
        GameSettings gameSettings, WorkspaceController workspace, Func<string?, string?> pickFolder);

    public int SelectedCategory { get; set; }          // 0 General, 1 Game
    public bool HasGame { get; }                       // false without a profile: the game controls are disabled
    public string GameName { get; }                    // display name of the profile, "" without one
    public string GamePath { get; set; }               // the text of the box; setting it commits (see below)
    public string? GamePathError { get; }
    public string GamePathInfo { get; }                // what the automatic detection found; "" while a manual path is set
    public ICommand BrowseGamePathCommand { get; }
    public ICommand ClearGamePathCommand { get; }
    public string SchemaFolder { get; set; }           // the text of the box; setting it commits
    public string? SchemaFolderError { get; }
    public string SchemaStatus { get; }                // active source with date, plus the reason when the override is not used
    public ICommand BrowseSchemaFolderCommand { get; }
    public ICommand ClearSchemaFolderCommand { get; }
}
```

Neue Texte:

| Key | en | de |
|---|---|---|
| `settings.category.game` | Game | Spiel |
| `settings.gamePath` | Game folder | Spielordner |
| `settings.gamePath.browse` | Choose folder | Ordner wählen |
| `settings.gamePath.clear` | Detect automatically | Automatisch erkennen |
| `settings.gamePath.invalid` | This folder has no "{0}" folder. Choose the folder that contains it. | Dieser Ordner hat keinen Ordner „{0}“. Wähle den Ordner, der ihn enthält. |
| `settings.gamePath.detected` | Detected automatically: {0} | Automatisch erkannt: {0} |
| `settings.gamePath.notDetected` | The game was not found automatically. Choose the game folder. | Das Spiel wurde nicht automatisch gefunden. Wähle den Spielordner. |
| `settings.noGame` | No game profile was found, so there is nothing to set here. | Es wurde kein Spielprofil gefunden, deshalb gibt es hier nichts einzustellen. |
| `settings.schemaFolder` | Schema folder | Schema-Ordner |
| `settings.schemaFolder.browse` | Choose folder | Ordner wählen |
| `settings.schemaFolder.clear` | Use the shipped schema | Mitgeliefertes Schema nutzen |
| `settings.schemaFolder.missing` | This folder does not exist. | Dieser Ordner existiert nicht. |
| `settings.schemaFolder.help` | A folder with RPFM schema files, for example the schema folder of RPFM. A usable file from this folder is used instead of the shipped one. | Ein Ordner mit RPFM-Schema-Dateien, zum Beispiel der Schema-Ordner von RPFM. Eine brauchbare Datei aus diesem Ordner wird statt der mitgelieferten benutzt. |
| `settings.schemaStatus` | In use: {0} | In Benutzung: {0} |
| `settings.schemaStatus.overrideFailed` | In use: {0}. The schema folder has no usable {1} ({2}). | In Benutzung: {0}. Der Schema-Ordner enthält keine brauchbare {1} ({2}). |

Festlegungen:
- **Spielpfad festlegen** (`GamePath` gesetzt, durch Eingabe oder Dialog): Text getrimmt. Leer → `gameSettings.InstallPath = null`, kein Fehler. Sonst `GameLocator.FromInstallPath(profile, text)`: `null` → `GamePathError` = `settings.gamePath.invalid` mit `profile.DataFolder`; die Einstellung bleibt, wie sie war; die Box behält die Eingabe. Gültig → `gameSettings.InstallPath = text`, Fehler weg. Der Controller lädt daraufhin selbst neu (Task 4).
- `GamePath` liest die laufende Eingabe; nach einem erfolgreichen Festlegen, nach `Clear` und bei einer Änderung der Einstellung von außen zeigt die Box den Wert der Einstellung (leer ohne manuellen Pfad).
- `GamePathInfo`: ohne manuellen Pfad `settings.gamePath.detected` mit `workspace.Location.InstallPath` bzw. `settings.gamePath.notDetected`; mit manuellem Pfad `""`. Aktualisiert bei `workspace.Changed`.
- `BrowseGamePathCommand`: `pickFolder(aktueller Pfad oder erkannter Pfad)`; `null` → nichts; sonst wie eine Eingabe. `ClearGamePathCommand`: setzt `InstallPath = null`.
- **Schema-Ordner festlegen:** getrimmt. Leer → `settings.SchemaFolder = null`. Ordner existiert nicht → `SchemaFolderError` = `settings.schemaFolder.missing`, Einstellung bleibt. Sonst übernehmen. Ob der Ordner eine brauchbare Datei enthält, zeigt `SchemaStatus` nach dem Neuladen.
- `SchemaStatus`: `settings.schemaStatus` mit `SchemaSourceText`; ist ein Override gesetzt und gescheitert, `settings.schemaStatus.overrideFailed` mit Quelle, Dateiname und `SchemaProblemText`. Aktualisiert bei `workspace.Changed`.
- **View:** zweite Kategorie „Spiel“ (`SelectedCategory` zweiseitig an die Liste gebunden; je Kategorie ein Bereich, sichtbar nach `SelectedCategory`). Bereich Spiel: Name des Spiels als Überschrift; Spielordner mit Textfeld (übernimmt bei Enter und beim Verlassen), „Ordner wählen“, „Automatisch erkennen“, darunter Fehler (in `Theme.Warning`) und Info; Schema-Ordner genauso mit Hilfetext und Status. Ohne Profil sind die Controls deaktiviert, und `settings.noGame` steht darüber. Jedes Textfeld hat sein Label über `AutomationProperties.LabeledBy`.
- **`App.xaml.cs`:** `pickFolder` nutzt `Microsoft.Win32.OpenFolderDialog` (in .NET 8 enthalten) mit dem Einstellungsfenster als Besitzer.
- **Dokumente:**
  - `CHANGELOG.md`, „Unreleased / Neu“, aus Nutzersicht: Spiel wird über Steam gefunden oder in den Einstellungen gewählt; Explorer mit Packs nach Herkunft; Tabellen, Loc-Dateien und Textdateien öffnen sich in Tabs; nicht lesbare Einträge nennen den Grund; offene Tabs sind nach einem Neustart wieder da; Schema-Ordner wählbar, Statuszeile zeigt die Schema-Quelle.
  - `README.md`: ein kurzer englischer Abschnitt, was die App jetzt kann und dass sie nichts am Spiel ändert.
  - `Backlog.txt`: Abschnitt „Hinweise für Plan 2 und 3“ auflösen (erledigte Zeilen löschen, offene nach „Aus dem Fundament zurückgestellt“). Neu dort: Spielwähler, sobald ein zweites Profil ausgeliefert wird; Session wiederherstellen, wenn das Spiel erst nach dem Start gefunden wird; Breite des Explorers und Spaltenbreiten merken; Kontextmenü im Explorer (Pfad kopieren, im Dateimanager zeigen); Abbruch und Fortschritt innerhalb von `ReadIndex`, `SchemaLoader.Load` und den Dekodern; Ordnerprobleme des Katalogs ausführlich anzeigen (bisher nur der erste Pfad in der Statuszeile); r/g/b-Dreiergruppen als eine Farbspalte.
  - Spec: 4.2 um `ReadEntryInfo`, `PackAccess`, `PackTree`, `EntryOpener` und `FolderProblems` ergänzen; 4.6 um die Festlegungen dieses Plans (stille Läufe, `IOperationRunner`, Meldungen im Explorer, Statuszeile, kein Spielwähler); 4.7 Punkt 1 präzisieren (Wiederherstellen läuft still nach Scan und Schema); 4.8 unverändert prüfen; Abschnitt 8: neue Annahme 16 „Ein Text-Tab mit 5 MB bleibt bedienbar“ (Stand: Abnahme Plan 3), Annahmen 5, 10, 13, 14 auf „Abnahme Plan 3 (manuelle Checkliste)“ setzen; Kopf der Spec um den Verweis auf diesen Plan ergänzen.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// SettingsViewModelTests: the existing tests keep their expectations; only the construction gets the new arguments
// (GameSettings and controller of a TestGame, pickFolder returning null). New tests:
public class SettingsViewModelGameTests
{
    private static async Task<(TestGame Game, WorkspaceController C, SettingsViewModel Vm, List<string?> Picked)> Create(string? pick = null)
    {
        var game = new TestGame(); game.AddPack("a.pack");
        var c = game.NewController(); await c.LoadAsync();
        var picked = new List<string?>();
        var vm = new SettingsViewModel(game.Settings, game.Texts, [new("en", "English")], game.GameSettings, c, start => { picked.Add(start); return pick; });
        return (game, c, vm, picked);
    }

    [Fact] public async Task Shows_the_game_and_the_manual_path()
    {
        var (game, _, vm, _) = await Create();
        using (game) Assert.Equal((true, "Test Game", game.InstallPath, (string?)null, ""), (vm.HasGame, vm.GameName, vm.GamePath, vm.GamePathError, vm.GamePathInfo));
    }

    // Review Focus 4
    [Fact] public async Task Invalid_game_path_is_rejected_and_keeps_the_old_value()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            var wrong = game.Root.File("wrong"); Directory.CreateDirectory(wrong);
            vm.GamePath = wrong;
            Assert.Equal("This folder has no \"packs\" folder. Choose the folder that contains it.", vm.GamePathError);
            Assert.Equal((game.InstallPath, wrong, GameStatus.Found), (game.GameSettings.InstallPath, vm.GamePath, c.Status));
            Assert.Empty(game.Runner.Titles);
            vm.GamePath = "  " + game.InstallPath + "  ";
            Assert.Equal(((string?)null, game.InstallPath), (vm.GamePathError, vm.GamePath));
        }
    }

    [Fact] public async Task Clearing_the_path_switches_to_automatic_detection()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.ClearGamePathCommand.Execute(null);
            Assert.Equal(((string?)null, "", GameStatus.NotFound), (game.GameSettings.InstallPath, vm.GamePath, c.Status));
            Assert.Equal("The game was not found automatically. Choose the game folder.", vm.GamePathInfo);
            Assert.Contains(nameof(SettingsViewModel.GamePathInfo), changed);
        }
    }

    [Fact] public async Task Browse_takes_the_chosen_folder_and_a_cancel_changes_nothing()
    {
        var (game, _, vm, picked) = await Create();
        using (game)
        {
            vm.BrowseGamePathCommand.Execute(null);                       // pickFolder returns null
            Assert.Equal((game.InstallPath, game.InstallPath), (game.GameSettings.InstallPath, Assert.Single(picked)));
        }
        var other = await Create(pick: null);
        using (other.Game)
        {
            // a second install folder with the data folder
            var second = other.Game.Root.File("second"); Directory.CreateDirectory(Path.Combine(second, "packs"));
            var vm2 = new SettingsViewModel(other.Game.Settings, other.Game.Texts, [new("en", "English")], other.Game.GameSettings, other.C, _ => second);
            vm2.BrowseGamePathCommand.Execute(null);
            Assert.Equal(second, other.Game.GameSettings.InstallPath);
        }
    }

    [Fact] public async Task Schema_folder_is_checked_and_its_status_is_shown()
    {
        var (game, c, vm, _) = await Create();
        using (game)
        {
            Assert.StartsWith("In use: shipped with the app, ", vm.SchemaStatus);
            vm.SchemaFolder = game.Root.File("missing");
            Assert.Equal(("This folder does not exist.", (string?)null), (vm.SchemaFolderError, game.Settings.SchemaFolder));
            var empty = game.Root.File("empty"); Directory.CreateDirectory(empty);
            vm.SchemaFolder = empty;
            Assert.Equal(((string?)null, empty), (vm.SchemaFolderError, game.Settings.SchemaFolder));
            Assert.Contains("The schema folder has no usable schema_test.ron (file not found).", vm.SchemaStatus);
            File.Copy(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"), Path.Combine(empty, "schema_test.ron"));
            await c.ReloadSchemaAsync();
            Assert.StartsWith("In use: from the schema folder, ", vm.SchemaStatus);
            vm.ClearSchemaFolderCommand.Execute(null);
            Assert.Equal(((string?)null, ""), (game.Settings.SchemaFolder, vm.SchemaFolder));
        }
    }

    [Fact] public void Without_a_profile_the_game_controls_are_off()
    {
        using var game = new TestGame();
        var c = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        var vm = new SettingsViewModel(game.Settings, game.Texts, [new("en", "English")], game.GameSettings, c, _ => null);
        Assert.Equal((false, ""), (vm.HasGame, vm.GameName));
        var before = game.GameSettings.InstallPath;
        vm.GamePath = @"D:\x";
        Assert.Equal(before, game.GameSettings.InstallPath);              // without a profile nothing is committed
    }
}

// added to SettingsWindowTests, in the style of the tests there (Sta.Run, real window)
[Fact] public void Game_category_shows_the_game_controls() { /* select category 1: the game path box, both buttons and the schema folder box are visible; the general controls are collapsed */ }
[Fact] public void Game_path_box_commits_on_enter_and_shows_the_error() { /* type a folder without data folder, press Enter: the error text is visible, the setting unchanged */ }
[Fact] public void Text_boxes_are_labeled_for_screen_readers() { /* AutomationProperties.LabeledBy of both boxes points at their labels */ }
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren**, danach die Dokumente.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` mit gesetzten Variablen – Expected: alles bestanden, 0 übersprungen, 0 Warnungen. Danach `run.bat` und die Punkte 1 bis 8 der Abnahmeliste unten selbst durchgehen; Ergebnis im Bericht.
- [ ] **Step 5: Commit** – `git commit -m "feat: add game folder and schema folder settings"`

---

## Manuelle Abnahme durch den Nutzer

Start mit `run.bat` (Debug-Build). Jeder Punkt: erwartet / tatsächlich.

**Start und Explorer**
1. Start: Fenster erscheint sofort. Der Explorer zeigt kurz „Die Pack-Liste wird geladen“, danach drei Gruppen: Spiel, Mods im Datenordner, Workshop, je mit Anzahl. Statuszeile: „Spiel: Total War: WARHAMMER III“ und „Schema: mit der App geliefert, Datum“.
2. Gruppe „Mods im Datenordner“ ist aufgeklappt und zeigt die sechs `flogi*.pack`. Workshop-Packs tragen die Workshop-ID in Klammern.
3. Ein `flogi*.pack` aufklappen: Dateibaum erscheint, Ordner zuerst. Jedes der sechs öffnet ohne Fehler (Erfolgskriterium der Spec).
3a. In einem aufgeklappten großen Pack (z. B. `data.pack`) mehrere Ordner aufklappen und weit scrollen: aufgeklappte Ordner bleiben aufgeklappt, keine fremden Ordner klappen auf. Die Zeile unter der Maus und die ausgewählte Zeile sind in beiden Themes lesbar. Doppelklick und Enter öffnen einen Eintrag.
4. `data.pack` oder `db.pack` aufklappen: Baum erscheint; dauert es länger als einen Moment, erscheint das Fortschrittsfenster und verschwindet von selbst.
5. `audio_base_m.pack`: trägt eine Markierung, Tooltip „Verschlüsseltes Pack, nicht unterstützt“.

**Tabs**
6. In `db.pack` eine Tabelle doppelklicken (z. B. `db/land_units_tables/data__`): Tab mit Raster, Kopfzeile mit Tabellenname, Version, Zeilenanzahl. Spaltenköpfe zeigen Unterstriche. Klick auf einen Spaltenkopf sortiert. Der Pfeil im Spaltenkopf zeigt bei aufsteigender Sortierung nach oben.
7. Breite Tabelle (z. B. `main_units_tables`): Zeit bis zur Anzeige notieren (Annahme 14, erwartet rund 1,2 s). Scrollen ist flüssig.
8. Loc-Datei aus `local_en.pack` (die größte): Zeit bis zur Anzeige notieren (Annahme 5). Drei Spalten.
9. Eine `.lua`-Datei: Text-Tab, nicht editierbar, Kodierung und Größe in der Kopfzeile. Eine Textdatei über 5 MB, falls vorhanden: Hinweis auf die Kürzung, Fenster bleibt bedienbar (Annahme 16).
10. Eine Datei mit anderer Endung (z. B. `.rigid_model_v2`, `.ca_vp8`, `.png`): Hinweis-Tab mit Typ, Größen, Kompression. Öffnet sofort, auch bei großen Dateien.
11. Eine der drei Tabellen, die das Schema nicht kennt, oder eine alte Mod-Tabelle: Hinweis-Tab „nicht dekodierbar“ mit Grund, Tabellenname, Version, Schema-Quelle.
12. Denselben Eintrag zweimal öffnen: kein zweiter Tab, der vorhandene wird aktiv. Tab schließen mit dem Knopf, mit Mittelklick und mit Strg+W.

**Session**
13. Drei Tabs offen, ein Pack aufgeklappt, App schließen und neu starten: dieselben Tabs, derselbe aktive Tab, dasselbe Pack aufgeklappt. Beim Start erscheint kein Fortschrittsfenster. Während des Wiederherstellens zeigt die Statuszeile „Wird geladen“; Aufklappen und Doppelklick wirken erst danach.
14. App schließen, ein eigenes Test-Pack mit offenem Tab umbenennen, App starten: Der Tab fehlt, die anderen sind da, kein Fehlerdialog. Pack zurückbenennen.
14a. App schließen, in `%APPDATA%\TotalWarModTool\games\warhammer_3\game-settings.json` einen Ordner ohne `data` eintragen, starten: Der Explorer zeigt die Meldung mit dem Knopf „Einstellungen öffnen“, es gibt keine Tabs, `session.json` bleibt unverändert. Der Knopf öffnet die Seite „Spiel“ mit der Fehlermeldung am Spielordner. „Automatisch erkennen“ wählen: Die Liste lädt, die Tabs und aufgeklappten Packs der letzten Sitzung erscheinen ohne Neustart.

**Änderungen von außen**
15. Ein eigenes Pack aufklappen, einen Eintrag öffnen, das Pack in RPFM ändern und speichern, in TWMT einen anderen Eintrag desselben Packs öffnen: Der neue Tab zeigt den neuen Stand, der alte Tab trägt die Markierung „veraltet“. Gibt es den Eintrag im geänderten Pack nicht mehr, erscheint ein Hinweis-Tab statt eines Fehlerdialogs.
15a. Ein Pack mit offenem Tab ändern, ohne dass es aufgeklappt ist, dann F5: Der Tab trägt die Markierung „veraltet“.
16. Werkzeuge → Pack-Liste aktualisieren (oder F5): Fortschritt mit Zählstand bei kaltem Cache, die Liste stimmt danach. Abbrechen lässt die alte Liste stehen.
17. Spiel starten, währenddessen in TWMT ein Vanilla-Pack aufklappen und eine Tabelle öffnen (Annahme 10): funktioniert, oder es erscheint eine verständliche Meldung, dass das Pack gesperrt ist.

**Einstellungen**
18. Einstellungen → Spiel: Spielordner leer, Info „Automatisch erkannt: …“. Einen falschen Ordner wählen: Meldung, dass der Ordner `data` fehlt; die Pack-Liste bleibt. Den richtigen Ordner wählen: Liste lädt neu. „Automatisch erkennen“: Feld leer, Liste wie vorher.
19. Schema-Ordner auf den RPFM-Schema-Ordner (`%APPDATA%\FrodoWazEre\rpfm\config\schemas`) setzen: Status und Statuszeile zeigen „aus dem Schema-Ordner, Datum“. Auf einen leeren Ordner setzen: Hinweis in Statuszeile und Einstellungen, Tabellen öffnen weiter mit dem mitgelieferten Schema. „Mitgeliefertes Schema nutzen“ stellt zurück. Bereits offene Tabellen-Tabs tragen danach die Markierung „veraltet“ mit dem Hinweis auf das geänderte Schema; Tab schließen und neu öffnen zeigt das Ergebnis mit dem neuen Schema.
20. Sprache auf Deutsch und zurück: Explorer-Gruppen, Explorer-Meldung, Statuszeile, Tab-Kopfzeilen, Hinweis-Tabs und die Meldungen der Seite „Spiel“ wechseln sofort. Die Tooltips der Pack-Knoten wechseln erst nach F5 (Backlog). Theme Hell / Dunkel: Raster, Explorer, Statuszeile und Tabs sind in beiden lesbar.

**Schreibgrenze**
21. Nach der Sitzung: Im Spielordner, im Workshop-Ordner und im RPFM-Ordner hat sich keine Datei geändert (Änderungsdatum der Ordner prüfen). Unter `%APPDATA%\TotalWarModTool\games\warhammer_3\` liegen `session.json` und, nach Punkt 18, `game-settings.json`.
22. Log (`Werkzeuge → Log-Ordner öffnen`): enthält Schema-Quelle und Anzahl der Packs, keine Benutzernamen in Pfaden.

Die Ergebnisse der Punkte 7, 8, 9 und 17 trägt die nächste Sitzung in Spec Abschnitt 8 ein (Annahmen 5, 10, 14, 16).

