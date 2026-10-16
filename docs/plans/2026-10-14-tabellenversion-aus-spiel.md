# Tabellenversion aus den Spieldateien – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die aktuelle Definition einer Tabelle ist die Version, die das Spiel in seinen Vanilla-Packs nutzt. Neue Tabellen, Objekt-Ansicht, Kopieren und Assistenten folgen ihr. Bauen bricht bei Tabellendateien in anderer Version ab. Übernehmen, „Ins Projekt übernehmen“ und Import lehnen veraltete Tabellen ab.

**Architecture:** Eine pure Regel in `Tables` (`TableVersionRule`) entscheidet aus Schema und `GameTableVersions`. `Workspace` liest die Versionen nach jedem Katalog-Scan aus den Kopf-Bytes der Vanilla-Einträge und cacht sie mit Fingerprint. Der `ReferenceGraph` trägt die Versionen. Damit folgen Objekt-Stack und `ProjectWritePlan` ohne neue Parameter. `Projects` bekommt die Versionen ausdrücklich für Bauen, neue Tabellen, Adopt und Import. Die App hält die Versionen im `WorkspaceController` und sperrt die abhängigen Funktionen mit Grund, solange sie fehlen.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2, ZstdSharp.Port 0.8.8 (vorhanden). Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-14-tabellenversion-aus-spiel.md` (ganz). Projektregeln: `CLAUDE.md`.

**Branch:** `fix/tabellenversion-aus-spiel` (existiert, enthält die Spec). Vor Task 1 die Baseline festhalten:
- `build.bat` ohne Warnung
- `dotnet test` für die Testprojekte `Tables`, `PackFiles`, `Workspace`, `Objects`, `Projects` und `Editing` grün
- die Zahlen in den Commit-Text von Task 1

**Testregel** (mit dem Nutzer vereinbart, Memory „App tests only with the user's OK“):
- Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt.
- Library-Tests sind frei (`dotnet test tests/<Projekt>.Tests`).
- Einzelne Testklassen der App sind erlaubt (`--filter "FullyQualifiedName~<Klasse>"`), immer nur ein Lauf zur Zeit.
- Echtdaten-Tests laufen mit `TWMT_WH3_DIR=D:\SteamLibrary\steamapps\common\Total War WARHAMMER III`, sonst werden sie übersprungen.
- Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers.

## Global Constraints

- **Sprachen:** Code, Bezeichner, Kommentare und Commit-Messages Englisch. Spec, Plan, Backlog und Changelog Deutsch.
- **Pakete und Build:** Kein neues NuGet-Paket. `TreatWarningsAsErrors`, Nullable und `ImplicitUsings` sind an.
- **Abhängigkeiten:** Die Regel liegt in `Tables`, das Lesen der Packs in `Workspace`. `Projects` und `Editing` referenzieren nie `Workspace` oder `Objects` neu.
- **Kein Rückfall:** Nirgends gibt es einen Rückfall auf „höchste Version“, wenn Spielversionen fehlen (Spec 5.5). Die höchste Version gilt nur nach Regel-Fall 3 (Spiel hat die Tabelle nicht).
- **Unvollständig = fehlend:** Ein unvollständiges Leseergebnis zählt wie „nicht gelesen“ und wird nicht gecacht (Spec 5.2).
- **Bau-Meldung**, englisches Detail, wörtlich, mit eingesetzten Zahlen: `the table file has version {file}, the game uses version {game}; the game crashes on this file. Delete the file or create the table again.`
- **Cache-Datei:** `%LocalAppData%\TotalWarModTool\cache\table_versions_<profileId>.json`, `schemaVersion` 1, über `JsonFileStore`.
- **Kopf-Bytes:** Für die Versionslesung reichen die ersten `TableHeaderBytes = 256` Bytes eines Eintrags (GUID max. 36 UTF-16-Zeichen plus Marken und Zeilenzahl).
- **UI-Texte:** Alle UI-Texte über `AppTexts`. Jeder neue Key steht in `locales/en.json` und `locales/de.json`, `AppTextsTests` bleibt grün.
- **Spielbegriffe in Meldungen** (Tabellen, Pack-Namen) laufen über `DisplayText.Safe` bzw. `MessageText.EscapeControlCharacters`.
- **Logging:** Libraries loggen nicht. Die App loggt das Ergebnis der Versionslesung (Anzahl Tabellen, Cache-Treffer oder neu gelesen, jedes Problem mit Pack und Grund), keine Zellwerte.
- **Test-first:** failing Test → RED → implementieren → GREEN → Commit. Dateisystem-Tests im `TempDir`.
- **Commits:** ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- **Abweichungen** von der Spec kommen im selben Commit in die Spec, als Unterabschnitt `#### Festlegungen aus dem Plan` unter 9 (Task 1 legt ihn an).

## Review Focus

1. **`db.pack` gesperrt oder ein Eintrag kaputt.** Erwartung: Das Ergebnis ist unvollständig, es wird kein Cache geschrieben, Bauen, Objekte und „Neue Tabelle“ sind gesperrt mit dem Grund „nicht vollständig gelesen“. Kein stiller Rückfall. → Task 3 (`An_unreadable_pack_makes_the_result_incomplete_and_nothing_is_cached`), Task 7 (`Build_is_disabled_while_table_versions_are_incomplete`).
2. **Spiel-Patch bei laufender App.** Erwartung: Der nächste Katalog-Scan sieht geänderte Fingerprints, liest neu, verwirft die Objekt-Stores. Gleiche Versionen nach einem Refresh verwerfen die Stores nicht. → Task 3 (`A_cache_with_another_fingerprint_is_read_again`), Task 7 (`Stores_are_dropped_only_when_the_versions_change`).
3. **Projektdatei einer Tabelle, die das Spiel nicht hat.** Dazu gehört jede Tabelle, die nur Mods kennen. Erwartung: kein Bauproblem, Übernehmen in diese Datei geht, neue Datei in höchster Schema-Version. → Task 5 (`A_table_the_game_lacks_is_not_checked`), Task 6 (`A_file_of_a_table_the_game_lacks_takes_changes`).
4. **Binäre Tabelle im Projektordner** (`pack\db\<t>\x` ohne `.tsv`). Erwartung: Der Bau-Check liest ihren Kopf und meldet die falsche Version wie bei einer TSV. → Task 5 (`A_binary_table_in_another_version_is_a_build_problem`).
5. **Groß-/Kleinschreibung des Tabellennamens** (`Building_Levels_Tables` im Projekt). Erwartung: Die Versionstabelle findet ihn trotzdem. → Task 1 (`Versions_ignore_the_case_of_the_table_name`).

---

### Task 1: Regel und Tabellenkopf in `Tables`

**Files:**
- Create: `src/TotalWarModTool.Tables/GameTableVersions.cs`
- Create: `src/TotalWarModTool.Tables/TableVersionRule.cs`
- Modify: `src/TotalWarModTool.Tables/DbTableDecoder.cs` (öffentliches `ReadHeader`, nutzt das private `TryReadHeader`)
- Test: `tests/TotalWarModTool.Tables.Tests/TableVersionRuleTests.cs`, `tests/TotalWarModTool.Tables.Tests/DbTableDecoderHeaderTests.cs`
- Modify: `docs/specs/2026-10-14-tabellenversion-aus-spiel.md` (Unterabschnitt `#### Festlegungen aus dem Plan` unter 9 anlegen)

**Interfaces:**
- Produces:
  - `public sealed class GameTableVersions`
    - `public GameTableVersions(IReadOnlyDictionary<string, int> versions)`: kopiert in ein Dictionary mit `StringComparer.OrdinalIgnoreCase`
    - `public static GameTableVersions Empty { get; }`
    - `public IReadOnlyDictionary<string, int> Versions { get; }`
    - `public int? VersionOf(string table)`
    - `public bool SameAs(GameTableVersions other)`: gleiche Schlüssel (ohne Groß-/Kleinschreibung) und gleiche Versionen
  - `public enum CurrentDefinitionKind { GameVersion, SchemaNewest, SchemaLacksGameVersion, NotInSchema }`
  - `public sealed record CurrentDefinition(TableDefinition? Definition, CurrentDefinitionKind Kind, int? GameVersion)`
  - `public static class TableVersionRule`
    - `public static CurrentDefinition Current(GameSchema schema, GameTableVersions versions, string table)`
    - `public static string? MismatchDetail(GameTableVersions versions, string table, int fileVersion)`: null, wenn das Spiel die Tabelle nicht hat oder die Version passt; sonst der Bau-Text aus den Global Constraints
  - `public sealed record TableHeader(int Version, string? Guid)`
  - `public static TableHeader? DbTableDecoder.ReadHeader(ReadOnlySpan<byte> data)`: null bei kaputtem oder abgeschnittenem Kopf (Versionsmarke oder Zeilenzahl fehlt)
  - `public const int DbTableDecoder.TableHeaderBytes = 256`

- [ ] **Step 1: Failing Tests schreiben** (`TableVersionRuleTests`)
  - `Game_version_wins_over_a_higher_schema_version`: Schema `a_tables` mit v16 und v3, Versionen `{a_tables: 3}`. Ergebnis `Kind == GameVersion`, `Definition.Version == 3`.
  - `Unknown_game_version_gives_no_definition`: Versionen `{a_tables: 9}`, Schema ohne v9. Ergebnis `Definition == null`, `Kind == SchemaLacksGameVersion`, `GameVersion == 9`.
  - `A_table_the_game_lacks_takes_the_highest_schema_version`: `Versions.Empty`, Schema v1, v3, v2. Ergebnis `Version 3`, `Kind == SchemaNewest`.
  - `Equal_highest_versions_take_the_first`: zwei Definitionen v2, die erste gewinnt (`ReferenceEquals`).
  - `A_table_not_in_the_schema_has_no_definition`: `Kind == NotInSchema`.
  - `Versions_ignore_the_case_of_the_table_name`: `VersionOf("A_TABLES") == 3`.
  - `Mismatch_detail_names_both_versions`: `MismatchDetail({a_tables:3}, "a_tables", 16)` ergibt exakt den Bau-Text mit 16 und 3.
  - `No_mismatch_for_the_game_version_or_a_table_the_game_lacks`: Beide Fälle ergeben null.
  - `SameAs_compares_content`: gleicher Inhalt in anderer Instanz gibt true, eine andere Version gibt false.
- [ ] **Step 2: Failing Tests schreiben** (`DbTableDecoderHeaderTests`)
  - Mit `DbTableEncoder.Encode` erzeugte Tabellen in v0 (ohne Versionsmarke), v3 ohne GUID und v3 mit GUID ergeben die richtige `Version` und `Guid`.
  - Die ersten 256 Bytes einer großen Tabelle reichen (`ReadHeader(bytes.AsSpan(0, 256))`).
  - Eine abgeschnittene Versionsmarke ergibt null.
- [ ] **Step 3:** `dotnet test tests/TotalWarModTool.Tables.Tests` laufen lassen. Erwartet: FAIL, die Typen fehlen.
- [ ] **Step 4: Umsetzen.**
  - `Current`: Fall 3 nimmt die höchste Version, bei Gleichstand die erste, wie `ReferenceGraph.PickLatest` heute.
  - `ReadHeader` nutzt `TryReadHeader`, `ByteCursor` arbeitet auf dem Span. Hat `ByteCursor` nur einen Array-Konstruktor, `data.ToArray()`; die 256 Bytes sind billig.
- [ ] **Step 5:** `dotnet test tests/TotalWarModTool.Tables.Tests`. Erwartet: PASS.
- [ ] **Step 6:** Spec: `#### Festlegungen aus dem Plan` unter 9 anlegen, mit Punkt 1: „Regel `TableVersionRule.Current`, Typen `GameTableVersions`, `CurrentDefinition`; Kopf über `DbTableDecoder.ReadHeader`, 256 Bytes.“
- [ ] **Step 7:** Commit mit `feat: add the game table version rule and a header-only table read`. Die Baseline-Zahlen kommen in den Text.

### Task 2: Kopf-Bytes eines Pack-Eintrags lesen (`PackFiles`)

**Files:**
- Modify: `src/TotalWarModTool.PackFiles/PackReader.cs`, `src/TotalWarModTool.PackFiles/EntryReader.cs`
- Test: `tests/TotalWarModTool.PackFiles.Tests/PackReaderEntryPrefixTests.cs`, Echtdaten-Fall in `tests/TotalWarModTool.PackFiles.Tests/PackReaderEntryRealDataTests.cs`

**Interfaces:**
- Produces: `public static EntryContent PackReader.ReadEntryPrefix(PackIndex index, PackEntry entry, int length, CancellationToken cancellationToken)`. `Bytes` hat höchstens `length` Bytes, bei kürzeren Einträgen alle. Probleme laufen wie bei `ReadEntry` über `EntryProblem`.

- [ ] **Step 1: Failing Tests schreiben**
  - `Prefix_of_an_uncompressed_entry`, `Prefix_of_a_zstd_entry` (über `PackBuilder.Stored(Zstd(data), data.Length), compressed: true`, Helfer wie in `PackReaderEntryTests.cs:6`), `Prefix_of_a_short_entry_is_the_whole_entry`: Die Bytes gleichen `data[..length]`.
  - `Prefix_of_a_corrupt_zstd_entry_is_Corrupt`.
- [ ] **Step 2:** `dotnet test tests/TotalWarModTool.PackFiles.Tests --filter "FullyQualifiedName~PackReaderEntryPrefixTests"`. Erwartet: FAIL.
- [ ] **Step 3: Umsetzen.**
  - Unkomprimiert: die ersten `length` Bytes lesen.
  - zstd: `ZstdSharp.DecompressionStream` über den gespeicherten Frame, bis `length` Bytes da sind.
  - LZ4, LZMA, Unknown: Rückfall auf `ReadEntry` und abschneiden; WH3 nutzt zstd (Spike).
  - Die Pack-Änderungsprüfung (`PackChanged`) und die Fehlerabbildung bleiben dieselben wie in `ReadEntry`.
- [ ] **Step 4:** Tests laufen lassen. Erwartet: PASS.
- [ ] **Step 5: Echtdaten-Messung** `[RealDataFact] Prefix_of_every_db_pack_table_reads_a_header`. Für jeden DB-Eintrag aus `db.pack`: `ReadEntryPrefix(..., DbTableDecoder.TableHeaderBytes)` und dann `ReadHeader` ist nicht null. Die gemessene Dauer kommt per `ITestOutputHelper` in die Ausgabe.
  - Lauf mit `TWMT_WH3_DIR` gesetzt. Erwartet: PASS.
  - Die Dauer (Spike voll entpackt: 1,5 s) kommt in die Spec, 10 Annahme 1, als „Ergebnis Plan Task 2“.
- [ ] **Step 6:** Commit mit `feat: read the first bytes of a pack entry without unpacking all of it`. Die Spec-Änderung wird mit gestaged.

### Task 3: Spielversionen lesen und cachen (`Workspace`)

**Files:**
- Create: `src/TotalWarModTool.Workspace/GameTableVersionsReader.cs`, `src/TotalWarModTool.Workspace/GameTableVersionsCache.cs`
- Test: `tests/TotalWarModTool.Workspace.Tests/GameTableVersionsReaderTests.cs`, `tests/TotalWarModTool.Workspace.Tests/GameTableVersionsCacheTests.cs`

**Interfaces:**
- Consumes: `PackSource`, `PackOrigin` (Workspace), `PackReader.ReadIndex`, `PackReader.ReadEntryPrefix` (Task 2), `DbTablePath.TryGetTableName`, `DbTableDecoder.ReadHeader` und `TableHeaderBytes` (Task 1), `JsonFileStore` (Storage, transitiv über Projects).
- Produces:
  - `public sealed record TableVersionsProblem(string PackPath, string? EntryPath, string Detail)`
  - `public sealed record GameTableVersionsResult(GameTableVersions? Versions, IReadOnlyList<FileFingerprint> Sources, IReadOnlyList<TableVersionsProblem> Problems, IReadOnlyList<string> Conflicts, bool FromCache, string? CacheWriteProblem = null)`
    - `public bool IsComplete => Versions is not null && Problems.Count == 0;`
    - `Versions` ist null genau dann, wenn kein Vanilla-Pack gelesen wurde.
  - `public static GameTableVersionsResult GameTableVersionsReader.Read(IReadOnlyList<PackSource> catalogPacks, CancellationToken cancellationToken)`: nimmt nur `Origin == Vanilla`, in Listenreihenfolge.
  - `public static GameTableVersionsResult? GameTableVersionsCache.TryLoad(string path, IReadOnlyList<PackSource> catalogPacks)`: null bei fehlender, kaputter (nach `.bad`) oder veralteter Datei. „Veraltet“ heißt: Die Fingerprint-Liste gleicht nicht den Vanilla-Packs des Katalogs in Reihenfolge (Pfad ohne Groß-/Kleinschreibung, Länge, `LastWriteTimeUtc`).
  - `public static void GameTableVersionsCache.Save(string path, GameTableVersionsResult result)`: wirft `ArgumentException`, wenn `!result.IsComplete`.
  - `public static GameTableVersionsResult GameTableVersionsCache.LoadOrRead(string path, IReadOnlyList<PackSource> catalogPacks, CancellationToken cancellationToken)`: Cache-Treffer oder `Read`. Ein vollständiges Ergebnis wird gespeichert. Ein Schreibfehler (`IOException`, `UnauthorizedAccessException`) landet als Text in `CacheWriteProblem`. Das Ergebnis bleibt vollständig und gilt für die Sitzung (Spec 7).

- [ ] **Step 1: Failing Tests schreiben** (`GameTableVersionsReaderTests`). Die Packs entstehen mit `PackBuilder` im `TempDir`, die `PackSource` mit `Origin` von Hand.
  - `Reads_the_version_of_every_db_table_of_vanilla_packs`: unkomprimiert v3 und zstd v0.
  - `Mod_and_workshop_packs_do_not_count`.
  - `The_later_vanilla_pack_wins_and_the_conflict_is_named`: `Conflicts` nennt Tabelle, beide Packs und beide Versionen.
  - `An_unreadable_pack_makes_the_result_incomplete_and_nothing_is_cached`: Die Datei ist gesperrt per `FileShare.None`, `IsComplete == false`. Danach gibt `LoadOrRead` keine Cache-Datei.
  - `A_broken_header_makes_the_result_incomplete`: Das Problem nennt Pack und Eintrag.
  - `No_vanilla_pack_gives_no_versions`: `Versions == null`.
- [ ] **Step 2: Failing Tests schreiben** (`GameTableVersionsCacheTests`)
  - `A_complete_result_is_saved_and_loaded`: Danach ist `FromCache == true`.
  - `A_cache_with_another_fingerprint_is_read_again`: `mtime` eines Packs ändern.
  - `A_broken_cache_is_moved_to_bad_and_read_again`.
  - `Saving_an_incomplete_result_throws`.
- [ ] **Step 3:** `dotnet test tests/TotalWarModTool.Workspace.Tests`. Erwartet: FAIL.
- [ ] **Step 4: Umsetzen.** Die Datei hat dieses Format: `{ schemaVersion: 1, sources: [{path, length, lastWriteTimeUtc}], versions: {table: version} }`.
- [ ] **Step 5:** Tests laufen lassen. Erwartet: PASS.
- [ ] **Step 6:** Commit mit `feat: read and cache the table versions of the game's vanilla packs`.

### Task 4: Referenzgraph folgt der Regel (`Objects`)

**Files:**
- Modify: `src/TotalWarModTool.Objects/ReferenceGraph.cs`. `PickLatest` entfällt, `Latest` wird zu `Current`.
- Modify: alle Aufrufer von `Latest(` in `src/TotalWarModTool.Objects` (`ObjectBuilder.cs`, `ObjectDiff.cs`, `ObjectTextParser.cs`, `TableStore.cs`, `WizardDefinitions.cs`), `src/TotalWarModTool.Editing/ProjectWritePlan.cs:218` und `tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs:349`. Dazu Doku-Kommentare, die „latest“ oder „highest version“ sagen.
- Modify: die 23 Aufrufe von `ReferenceGraph.Build(` in den Tests (Liste: App.Tests `ObjectsTestSchema.cs`, `ShippedCategoryTests.cs`; Editing.Tests `WriteFixture.cs`; Objects.Tests `BlockFixture.cs`, `BuildingWizardTests.cs`, `FileReplacementTests.cs`, `ObjectBuilderTests.cs`, `ObjectCopyTests.cs`, `ObjectDiffTests.cs`, `ObjectsRealDataTests.cs`, `ReferenceGraphTests.cs`, `TableStoreTests.cs`, `TraitWizardTests.cs`, `WizardDefinitionsTests.cs`). Sie bekommen `GameTableVersions.Empty` als zweites Argument; ausgenommen sind die neuen Tests unten und `ObjectsRealDataTests`.
- Modify: `src/TotalWarModTool.App/Objects/ObjectStores.cs` minimal, damit es kompiliert: `GameTableVersions.Empty`, mit `// Task 7 passes the real versions`. Task 7 ersetzt das.
- Test: `tests/TotalWarModTool.Objects.Tests/ReferenceGraphTests.cs`, `tests/TotalWarModTool.Objects.Tests/TableStoreTests.cs`, `tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs`

**Interfaces:**
- Consumes: `TableVersionRule.Current`, `GameTableVersions` (Task 1), `GameTableVersionsReader.Read` (Task 3, nur im Echtdaten-Test).
- Produces:
  - `public static ReferenceGraph Build(GameSchema schema, GameTableVersions gameVersions)`. Die Überladung mit nur dem Schema entfällt.
  - `public TableDefinition? Current(string table)`
  - `public GameTableVersions GameVersions { get; }`
  - Eine Tabelle mit `Kind` `SchemaLacksGameVersion` oder `NotInSchema` fehlt im Graphen: `Current` gibt null, die Tabelle hat keine eingehenden Referenzen.

- [ ] **Step 1: Failing Tests schreiben.**
  - In `ReferenceGraphTests` ersetzt `Current_is_the_game_version` (Schema v1 und v3, Spiel 1, ergibt v1) den Test `Latest_picks_the_highest_version`.
  - `Current_without_game_version_is_the_highest` (`Empty`, ergibt v3).
  - `A_table_whose_game_version_the_schema_lacks_is_left_out`: `Current` ist null, eine Referenz aus dieser Tabelle taucht in `Incoming` der Zieltabelle nicht auf.
  - `Incoming_uses_the_fields_of_the_game_version`: Ein Referenzfeld existiert nur in v1. Ist das Spiel auf v1, ist es in `Incoming`; mit `Empty` (dann v3) nicht.
  - In `TableStoreTests`: `A_vanilla_entry_in_the_game_version_is_not_older_layout`: Schema v1 und v2, Spiel 1, Vanilla-Eintrag v1, also kein `OlderLayout`. Ein Mod-Eintrag v2 ist `OlderLayout`.
- [ ] **Step 2:** `dotnet test tests/TotalWarModTool.Objects.Tests`. Erwartet: Compile-FAIL bzw. FAIL.
- [ ] **Step 3: Umsetzen.** Danach die Test-Aufrufe mechanisch anpassen, dann `build.bat`. Erwartet: ohne Warnung.
- [ ] **Step 4: Echtdaten-Test umstellen.** `ObjectsRealDataTests` liest die Versionen über `GameTableVersionsReader.Read(catalog.Packs, ct)` aus dem Katalog des Tests und baut den Graphen damit. `Every_table_of_the_load_order_merges_without_exception` prüft weiter `olderLayoutRows == 0` ohne Mods; mit den echten Versionen muss das jetzt halten (Spec 2.3).
- [ ] **Step 5:** `dotnet test tests/TotalWarModTool.Objects.Tests`, dann `dotnet test tests/TotalWarModTool.Editing.Tests`, beide mit `TWMT_WH3_DIR`. Erwartet: PASS, der Echtdaten-Test mit 0 „older layout“-Zeilen.
- [ ] **Step 6:** Commit mit `fix: take the current table definition from the game's version, not the highest in the schema`.

### Task 5: Neue Tabellen, Adopt, Bau-Check, Import (`Projects`)

**Files:**
- Modify: `src/TotalWarModTool.Projects/NewFiles.cs`, `src/TotalWarModTool.Projects/EntryConversion.cs`, `src/TotalWarModTool.Projects/ProjectBuilder.cs`, `src/TotalWarModTool.Projects/PackImporter.cs`, `src/TotalWarModTool.Projects/ImportModel.cs`
- Modify (nur Aufrufe, damit es kompiliert): `src/TotalWarModTool.Editing/ProjectWritePlan.cs` (`NewFiles.Table(_schema, _graph.GameVersions, ...)`), `src/TotalWarModTool.Editing/ProjectSnapshotReader.cs` (`Adopt(..., gameVersions: null, ...)` mit Kommentar „versions are checked by the write plan“), App-Aufrufer mit `GameTableVersions.Empty` und `// Task 7`
- Test: `tests/TotalWarModTool.Projects.Tests/NewFilesTests.cs`, `ProjectBuilderTests.cs`, `PackImporterTests.cs`

**Interfaces:**
- Consumes: Task 1 (`TableVersionRule`, `GameTableVersions`, `ReadHeader`, `TableHeaderBytes`).
- Produces:
  - `public static IReadOnlyList<string> NewFiles.CreatableTables(GameSchema schema, GameTableVersions gameVersions)`: nur Tabellen mit `Current(...).Definition` ohne `Unsupported`-Feld und mit mindestens einem Feld.
  - `public static NewFileResult NewFiles.Table(GameSchema schema, GameTableVersions gameVersions, string tableName, string name)`. Bei `SchemaLacksGameVersion` ist `Reason` = `the schema does not know version {v} of this table, which the game uses; choose a newer schema in the settings`.
  - `public static NewFileResult NewFiles.Adopt(string packPath, byte[] entryBytes, GameSchema? schema, GameTableVersions? gameVersions, string? newName)`. Ist `gameVersions` gesetzt und die Tabelle des Eintrags in anderer Version, ist `Reason` = `MismatchDetail`. `null` heißt keine Prüfung, nur für den Snapshot-Leser.
  - `PackEntryContent` bekommt `int? TableVersion` (gesetzt von `TableToPackEntry`).
  - `public static BuildPlan ProjectBuilder.ComputeBuild(string projectFolder, ModProject project, GameSchema? schema, GameTableVersions gameVersions, Func<string> newGuid, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)`
  - `ImportProblem.OutdatedTables` (neu, am Ende des Enums). `ImportReport.Detail` listet je Zeile `{packPath}: {MismatchDetail}`.
  - `public static ImportReport PackImporter.Import(ImportRequest request, GameSchema? schema, GameTableVersions gameVersions, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)`. Die interne Überladung mit `beforeMove` bekommt denselben Parameter.

- [ ] **Step 1: Failing Tests schreiben.**
  - `NewFilesTests`, mit Versionen `{newest_tables: 1}`:
    - `Table_writes_an_empty_table_in_the_game_version` (Erwartung `#newest_tables;1;`)
    - `Table_without_game_version_writes_the_highest` (der bisherige Test, mit `Empty`)
    - `Table_with_a_game_version_the_schema_lacks_is_refused` (`{newest_tables: 9}`, `Reason` wie oben)
    - `CreatableTables_leaves_out_tables_whose_game_version_the_schema_lacks`
    - `Adopt_refuses_an_entry_in_another_version_than_the_game` (`Reason` ist `MismatchDetail`)
    - `Adopt_without_versions_does_not_check`
  - `ProjectBuilderTests`, Spiel `{units_tables: 3}` (TestData-Schema hat v2):
    - `A_table_file_in_another_version_is_a_build_problem`: genau ein `BuildProblem` für die Datei, `Detail` ist der Bau-Text mit 2 und 3.
    - `A_binary_table_in_another_version_is_a_build_problem`: `TestData.Units(...)` als `pack\db\units_tables\my_mod` ohne Endung.
    - `A_table_file_in_the_game_version_builds` (Spiel `{units_tables: 2}`)
    - `A_table_the_game_lacks_is_not_checked` (`Empty`)
  - `PackImporterTests`:
    - `Import_with_an_outdated_table_writes_nothing`: `Problem == OutdatedTables`, der Zielordner bleibt leer, `Detail` nennt den Eintrag.
    - `Import_with_tables_in_the_game_version_works`.
- [ ] **Step 2:** `dotnet test tests/TotalWarModTool.Projects.Tests`. Erwartet: FAIL.
- [ ] **Step 3: Umsetzen.**
  - Bau: Für `ProjectFileKind.Table` prüft `ComputeBuild` die `TableVersion` aus `PackEntryContent`. Für `Raw` unter einem DB-Tabellenpfad (`DbTablePath.TryGetTableName`) liest es die ersten `TableHeaderBytes` der Datei und nutzt `ReadHeader`; ein Kopf, der null ergibt, ist kein Versionsproblem (die Datei bleibt wie heute roh).
  - Import: Vor dem ersten Schreiben läuft ein eigener Durchgang über die DB-Einträge des Packs (`ReadEntryPrefix` und `ReadHeader`), der alle veralteten Einträge sammelt.
- [ ] **Step 4:** `dotnet test tests/TotalWarModTool.Projects.Tests` und `dotnet test tests/TotalWarModTool.Editing.Tests`, dann `build.bat`. Erwartet: PASS, keine Warnung.
- [ ] **Step 5:** Spec, Festlegungen aus dem Plan, Punkt 2: „‚Ins Projekt übernehmen‘ nimmt in der App genau einen Eintrag (`AdoptAsync`); der Fall ‚mehrere Einträge‘ aus 5.7 kommt nicht vor. `Adopt` mit `gameVersions: null` prüft nicht; das nutzt nur der Snapshot-Leser, die Prüfung macht dort der Schreibplan.“
- [ ] **Step 6:** Commit mit `feat: check table versions against the game when creating, adopting, importing and building`.

### Task 6: Schreibplan lehnt Dateien in anderer Version ab (`Editing`)

**Files:**
- Modify: `src/TotalWarModTool.Editing/WriteModel.cs` (`WriteErrorKind.OutdatedTableVersion`, am Ende), `src/TotalWarModTool.Editing/ProjectWritePlan.cs` (`GetWork`, `CopyWork`)
- Test: `tests/TotalWarModTool.Editing.Tests/ProjectWritePlanTests.cs`, `tests/TotalWarModTool.Editing.Tests/WriteFixture.cs` (zweiter Graph `GraphWithGame = ReferenceGraph.Build(MiniSchema, new GameTableVersions(new Dictionary<string, int> { [X] = 2 }))`)

**Interfaces:**
- Consumes: `ReferenceGraph.GameVersions` (Task 4), `TableVersionRule.MismatchDetail` (Task 1).
- Produces: `WriteErrorKind.OutdatedTableVersion`.
  - `GetWork`: Weicht die Version der Projektdatei ab, folgt einmal pro Datei `Fail(OutdatedTableVersion, line, file.RelativeFile, MismatchDetail)`. Die Arbeit läuft weiter; der Plan schreibt wegen `HasErrors` nichts.
  - `CopyWork`: dasselbe mit `layer.EntryPath`. Das `Detail` beginnt mit `the mod entry is outdated: ` und endet mit `MismatchDetail`.
  - `GetWork` hat heute keinen `line`-Parameter. Er bekommt `int line`; alle Aufrufer geben die Zeile der Änderung.

- [ ] **Step 1: Failing Tests schreiben**, alle mit `GraphWithGame`.
  - `An_insert_into_a_project_file_in_another_version_is_an_error`: Projektdatei `x_tables` v1. Ergebnis: ein Fehler `OutdatedTableVersion`, `RelativeFile` ist die Datei, nichts geplant.
  - `A_delete_in_a_project_file_in_another_version_is_an_error`.
  - `Deleting_a_row_of_an_outdated_mod_entry_is_an_error`: Mod-Eintrag `x_tables` v1, Zeile löschen (Dateikopie). Ergebnis: Fehler mit `EntryPath`, `Detail` beginnt mit `the mod entry is outdated: `.
  - `A_new_file_is_created_in_the_game_version`: Erwartung `Version == 2`. Damit ist der bisherige `An_insert_without_a_project_file_creates_one_named_after_the_project` mit `Graph` (Empty, höchste Version 2) unverändert gültig.
  - `A_file_of_a_table_the_game_lacks_takes_changes`: `y_tables` hat keine Spielversion. Insert geht durch.
  - Die bestehenden Tests zu älteren Dateien (`An_override_row_is_mapped_onto_the_columns_of_an_older_file` u. a.) bleiben auf `Graph` (Empty). Dort hat das Spiel `x_tables` nicht, sie gelten also weiter (Spec 8).
- [ ] **Step 2:** `dotnet test tests/TotalWarModTool.Editing.Tests`. Erwartet: FAIL.
- [ ] **Step 3:** Umsetzen.
- [ ] **Step 4:** `dotnet test tests/TotalWarModTool.Editing.Tests`. Erwartet: PASS.
- [ ] **Step 5:** Commit mit `feat: refuse writing into tables of another version than the game's`.

### Task 7: App – Versionen im Workspace, Sperren, Weiterreichen

**Files:**
- Modify: `src/TotalWarModTool.App/AppPaths.cs` (`CacheDirectory => Path.Combine(LocalRoot, "cache")`)
- Modify: `src/TotalWarModTool.App/Workspace/WorkspaceController.cs`:
  - `Loaded` bekommt `GameTableVersionsResult? TableVersions`.
  - `Compute` ruft nach dem Katalog-Scan `GameTableVersionsCache.LoadOrRead(Path.Combine(_paths.CacheDirectory, $"table_versions_{profile.Id}.json"), catalog.Packs, ct)`.
  - `ApplyLoaded` setzt die Property und loggt (Global Constraints).
- Modify: `src/TotalWarModTool.App/Objects/ObjectStores.cs`, `src/TotalWarModTool.App/Objects/ObjectsController.cs` (`MissingReason`, `EnsureStores`, `BuildAsync`, `OnWorkspaceChanged`)
- Modify: `src/TotalWarModTool.App/Projects/ProjectsController.Operations.cs` (`BuildDisabledReason`, Build, Import), `src/TotalWarModTool.App/Projects/ProjectFilesController.cs` (`CreatableTables`-Cache, `CreateTableAsync`, `AdoptAsync`, `DisabledReason`), `src/TotalWarModTool.App/Projects/ProjectFilesViewModel.cs` falls nötig
- Modify: `src/TotalWarModTool.App/AppTexts.cs` (Gründe), `AppTexts.ObjectsApply.cs` (Text für `OutdatedTableVersion`), `AppTexts.Projects.cs` (Text für `ImportProblem.OutdatedTables`), `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/` – die bestehenden Testklassen von `WorkspaceController`, `ObjectsController`, `ProjectsController` und `ProjectFilesController` (Namen per `grep -l "class .*ControllerTests"`)

**Interfaces:**
- Consumes: Tasks 3–6.
- Produces:
  - `public GameTableVersionsResult? WorkspaceController.TableVersions { get; private set; }`
  - `public string? WorkspaceController.TableVersionsMissingReason`. Er liefert genau einen der drei Texte unten, oder null, wenn `TableVersions` vollständig ist:
    - `Status != Found`: kein Spiel
    - `TableVersions is null`: wird gelesen
    - `!IsComplete`: unvollständig
  - `ObjectStores(..., GameTableVersions GameVersions, ...)` und `ObjectStores.Create(ResolvedSchema schema, GameTableVersions gameVersions, string installPath)`
- Neue Keys, Englisch / Deutsch:
  - `tableVersions.reading`: "The game's table versions are still being read." / „Die Tabellenversionen des Spiels werden noch gelesen.“
  - `tableVersions.incomplete`: "The game's table versions could not be read completely. See the log." / „Die Tabellenversionen des Spiels konnten nicht vollständig gelesen werden. Siehe Log.“
  - `tableVersions.noGame`: "No game found. Building and new tables need the game's table versions." / „Kein Spiel gefunden. Bauen und neue Tabellen brauchen die Tabellenversionen des Spiels.“
  - `objects.writeError.outdatedTableVersion`: "The table file has another version than the game uses. Delete the file or create the table again." / „Die Tabellendatei hat eine andere Version, als das Spiel nutzt. Datei löschen oder die Tabelle neu anlegen.“
  - `import.problem.outdatedTables`: "The pack contains tables in another version than the game uses (outdated mod). Nothing was imported." / „Das Pack enthält Tabellen in einer anderen Version, als das Spiel nutzt (veraltete Mod). Es wurde nichts importiert.“

- [ ] **Step 1: Failing Tests schreiben**, mit den vorhandenen Fakes der jeweiligen Testklasse.
  - `Table_versions_are_read_after_the_catalog_scan_and_cached`: Die Cache-Datei liegt danach im `TempDir`-`LocalRoot`.
  - `Build_is_disabled_while_table_versions_are_incomplete`: `BuildDisabledReason` ist der Text von `tableVersions.incomplete`.
  - `Object_tabs_report_the_missing_versions`: `DisabledReason` ist `tableVersions.reading`, solange `TableVersions` null ist.
  - `Stores_are_dropped_only_when_the_versions_change`: Refresh mit gleichem Inhalt behält `Stores` (`ReferenceEquals`), geänderte Version verwirft sie.
  - `Create_table_offers_only_tables_with_a_current_definition`
  - `Adopt_of_an_outdated_entry_reports_the_reason`
  - `Import_of_a_pack_with_an_outdated_table_reports_it`
- [ ] **Step 2:** Je Testklasse einzeln laufen lassen (`--filter`). Erwartet: FAIL.
- [ ] **Step 3: Umsetzen.**
  - `OnWorkspaceChanged` verwirft die Stores zusätzlich, wenn `!stores.GameVersions.SameAs(neu)`.
  - `ProjectFilesController` cacht `CreatableTables` pro Schema-Instanz und `GameTableVersions`-Instanz.
  - Die Gründe stehen vor den bisherigen Gründen nur dort, wo die Funktion Versionen braucht. Der Tabelleneditor bekommt keinen neuen Grund.
- [ ] **Step 4:** Je Testklasse einzeln, dann `build.bat`, dann `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~AppTextsTests"`. Erwartet: PASS, keine Warnung.
- [ ] **Step 5:** Commit mit `feat: hold the game's table versions in the workspace and pass them to objects, build, import and new files`.

### Task 8: Echtdaten, Doku, Abschluss

**Files:**
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs` (neuer Test), `tests/TotalWarModTool.App.Tests/ShippedCategoryTests.cs` (Echtdaten-Fall)
- Modify: `CHANGELOG.md` (`### Behoben`), `docs/specs/2026-10-14-tabellenversion-aus-spiel.md` (Ergebnisse unter 10), die drei alten Specs an den Stellen aus dem Kopf der neuen Spec. Jede bekommt einen Satz: „Ersetzt durch `docs/specs/2026-10-14-tabellenversion-aus-spiel.md`: aktuelle Definition = Version im Spiel.“

- [ ] **Step 1: Failing Echtdaten-Test schreiben.** `[RealDataFact] Building_wizard_writes_building_levels_in_the_game_version`: Der Gebäude-Assistent komponiert eine Kopie von `wh_main_emp_barracks_1` als neue Kette. Die geplante Datei von `building_levels_tables` hat die Version aus `GameTableVersionsReader.Read` (heute 3). Die Kette läuft wie in den bestehenden Assistenten-Echtdaten-Tests. Es wird nichts geschrieben, nur `ProjectWritePlan.Compute` in einem leeren Projekt im `TempDir`.
- [ ] **Step 2: Echtdaten-Fall** `Shipped_categories_and_wizards_match_the_game_versions`: Jedes Feld der ausgelieferten Kategorie-Dateien und von `wizards.json` existiert in `Current` des Graphen mit echten Versionen (Spec 10 Annahme 2).
- [ ] **Step 3:** Beide mit `TWMT_WH3_DIR` laufen lassen. Erwartet: PASS. Scheitert Step 2 an einem Feld, wird das gemeldet und in die Spec geschrieben, nicht still entschieden.
- [ ] **Step 4: Changelog unter `### Behoben`**, aus Nutzersicht, in einem Satz pro Punkt:
  - Mods mit kopierten Gebäuden ließen das Spiel beim Start abstürzen, weil Tabellen in einer veralteten Version geschrieben wurden. Das Tool nutzt jetzt die Tabellenversionen des Spiels.
  - Bauen bricht mit Meldung ab, wenn eine Tabelle des Projekts eine andere Version hat als im Spiel.
  - Übernehmen, „Ins Projekt übernehmen“ und Import lehnen veraltete Tabellen ab.
- [ ] **Step 5: Spec 10** um „Ergebnisse des Echtdaten-Laufs“ ergänzen: Dauer aus Task 2, 0 „older layout“-Zeilen, Ergebnis von Step 2. Die alten Specs bekommen ihre Verweise.
- [ ] **Step 6:** Commit mit `docs: record the table version fix in the changelog and the specs`.
- [ ] **Step 7: Controller, nach Okay des Nutzers:** voller Testlauf `test.bat`. Danach Spieltest durch den Nutzer:
  - Testprojekte löschen.
  - Mit dem Gebäude-Assistenten eine Kaserne kopieren, Hellebardiere ergänzen.
  - Bauen, installieren, Spiel starten, Imperiums-Kampagne, Gebäude baubar.
  - Gegenprobe: eine v16-Datei von Hand ins Projekt legen. Bauen bricht mit Meldung ab.
  - Danach `test.pack` im data-Ordner durch den neuen Bau ersetzen. Heute liegt dort die Test-Variante V5.

## Manuelle Testcheckliste (UI)

1. **App-Start mit Spiel:** Kurz zeigt der Tooltip von „Bauen“ „werden noch gelesen“. Danach ist „Bauen“ frei, `%LocalAppData%\TotalWarModTool\cache\table_versions_warhammer_3.json` existiert.
2. **Zweiter Start:** Das Log sagt „from cache“.
3. **Gesperrtes `db.pack`:** `db.pack` in einem anderen Programm exklusiv öffnen, „Packs neu laden“. „Bauen“, Objekt-Tab und „Neue Tabelle“ sind gesperrt mit „nicht vollständig gelesen“, das Log nennt `db.pack`. Datei freigeben, neu laden: alles wieder frei.
4. **Objekt-Ansicht der Kaserne:** keine „older layout“-Marke an Vanilla-Zeilen. Felder wie `resource_cost` sind sichtbar, `military_prestige` nicht.
5. **Neue Tabelle `building_levels_tables`:** Die TSV-Metazeile trägt `;3;`.
6. **Pack einer veralteten Mod importieren:** Import abgelehnt, Meldung nennt die Einträge, kein Projektordner angelegt.
7. **Fremde Zeile aus veralteter Mod löschen:** Übernehmen abgelehnt mit Meldung, nichts geschrieben, kein Undo-Schritt.
8. **Ohne Spielpfad:** Bauen und „Neue Tabelle“ zeigen „Kein Spiel gefunden“. Der Tabelleneditor öffnet Projektdateien weiter.
9. **Patch simulieren:** In der Cache-Datei einen Fingerabdruck ändern (z. B. die Größe eines Packs), Packs neu laden. Die Versionen werden neu gelesen, die Objekt-Stores sind neu aufgebaut, offene Objekt-Tabs tragen „veraltet“.
10. **Bauen mit falscher Projekttabelle:** Eine Projekttabelle in einer anderen Version als im Spiel ablegen und bauen. Der Bau bricht ab, die Meldung nennt die Version der Datei und die des Spiels.
