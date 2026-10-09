# Teil 4.1 Plan 1: Library `Objects` (Referenzgraph, Ladereihenfolge, Tabellen-Store, Objektmodell, Rendering) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eine UI-freie Library, die aus einer Tabellenzeile (Tabelle plus Schlüssel) ein vollständiges, unveränderliches Objektmodell über Vanilla, aktive Mods und Projekt baut und daraus die Zeilen des Codeblocks mit Spans rendert. Keine App-Änderung; die App kommt in Plan 2.

**Architecture:** `TotalWarModTool.Objects` (net8.0) mit sechs Bausteinen: `ReferenceGraph` (pur, aus dem Schema), `LoadOrderResolver` (Vanilla, `used_mods.txt`, Projekt, Loc-Pack), `TableStore` und `LocStore` (Dekodieren bei Bedarf, Zusammenführung über Feldnamen, Cache mit Fingerprint und Obergrenze), `ObjectBuilder` (Modell `ObjectView`), `CategoryDefinitions` (JSON im Spielprofil) und `ObjectText` (deterministische Zeilen aus Spans). `GameProfile` bekommt die Loc-Pack-Zuordnung.

**Tech Stack:** C# 12, .NET 8, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-09-teil-4-1-objekt-ansicht.md`: 5.1 (Module), 5.2 (Referenzgraph), 5.3 (Ladereihenfolge), 5.4 (Tabellen-Store), 5.5 (Objektmodell), 5.6 (Rendering), 5.7 (Kategorie-Dateien), 3 (Messwerte), 7 (Fehlerfälle), 8 (Tests), 10 (Annahmen). Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-4-1-objects` (von `master`, Stand `cb35c27` oder neuer). Baseline: 1239 Library-Tests und 869 App-Tests grün, 0 übersprungen, Build ohne Warnung.

**Testregel** (mit dem Nutzer vereinbart): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei (`dotnet test tests/<Projekt>.Tests`). Kein `python`, `perl` oder anderes Skript-Laufzeitsystem. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers, und nie, während eine andere Sitzung baut oder testet. Echtdaten-Tests (`RealDataFact`) laufen nur mit `TWMT_WH3_DIR`; der Controller setzt die Variable beim Echtdaten-Lauf, Subagenten setzen sie nicht.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors` ist an. Nullable an, `ImplicitUsings` an (aus `Directory.Build.props`).
- Projektreferenzen von `Objects`: genau `Schema`, `Tables`, `PackFiles`, `Workspace`, `Projects`, `GameProfiles`, `Operations`. Nie `App`, `Editing`, `UndoRedo`, `Session`, `Settings`, `Logging`, `Localization`.
- Die Library loggt nicht und liefert keine UI-Texte: Probleme sind ein Enum plus englisches `Detail`. Abschnittstitel verlassen die Library als Loc-Schlüssel, nie als Text.
- Alles nur Lesen. Die Library schreibt keine Datei, legt keinen Ordner an und hält keinen Cache auf Platte. Pfade kommen als Parameter (Spielordner, Projektordner, Profil-Ordner).
- Bestehende öffentliche Signaturen bleiben nutzbar (`GameProfile`: neue Parameter mit Standardwert am Ende).
- Nur `OperationCanceledException` verlässt die Builder- und Store-Aufrufe; Lese- und Dekodierfehler werden als `SourceProblem` gesammelt. Kein `catch (Exception)` ohne Typfilter.
- Tabellennamen werden ohne Rücksicht auf Groß-/Kleinschreibung verglichen (`StringComparer.OrdinalIgnoreCase`, wie `GameSchema.Tables`); Schlüsselwerte und Loc-Schlüssel ordinal.
- Referenzziel-Name: `reference.Table + "_tables"`, wenn das Schema diese Tabelle kennt, sonst `reference.Table` unverändert.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in `TempDir`. Testpacks entstehen mit `PackBuilder` (tests/Shared) und `DbTableEncoder`/`LocEncoder`; Projektdateien mit `TableTsv.Write`/`WriteLoc` und `File.WriteAllText` im Temp-Ordner. Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`, nichts schreibt in Spiel-, Workshop- oder RPFM-Ordner.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Widerspricht eine Erwartung einer Regel des Plans, wird das gemeldet.

## Review Focus

1. **Mod-Eintrag mit anderem Layout als Vanilla** (weniger Felder, Felder in anderer Reihenfolge, ein Feld mit anderem Namen). Erwartung: Zeile wird über Feldnamen abgebildet, fehlende Felder bekommen den Standardwert des Typs, die Zeile trägt `OlderLayout`; nichts stürzt ab, keine Zelle landet in der falschen Spalte. → Task 4 (`An_entry_with_an_older_layout_is_merged_by_field_name`).
2. **Eine Quelle der Ladereihenfolge ist kaputt oder gesperrt** (Pack mit falschem Header, Eintrag ohne Schema, Projekt-TSV mit Syntaxfehler, Datei exklusiv geöffnet). Erwartung: `SourceProblem` nennt Quelle und Eintrag, alle anderen Quellen fließen ein, `Build` liefert ein Modell mit Warnung. → Task 4 (`A_broken_source_is_reported_and_the_others_are_merged`), Task 5 (`Source_problems_become_warnings`).
3. **Wurzel mit zusammengesetztem Schlüssel und Referenzen auf Nicht-Schlüsselspalten.** Erwartung: Abschnitte entstehen nur für Felder, die auf ein Schlüsselfeld der Wurzel zeigen, und matchen den Schlüsselwert dieser Spalte; `ObjectId` trägt alle Schlüsselwerte in Schemareihenfolge. → Task 5 (`A_composite_key_root_matches_each_key_column_separately`, `A_reference_to_a_non_key_column_gives_no_section`).
4. **Cache unter Druck** (Tabelle größer als die Obergrenze, Fingerprint eines einzigen Packs ändert sich, dieselbe Tabelle mit zwei verschiedenen Ladereihenfolgen). Erwartung: zu große Tabelle wird geliefert, aber nicht gehalten; nur Tabellen mit Einträgen aus dem geänderten Pack werden neu gelesen; zwei Ladereihenfolgen teilen keinen Eintrag. → Task 4 (`A_table_above_the_limit_is_returned_but_not_cached`, `A_changed_pack_invalidates_only_its_tables`, `Two_load_orders_do_not_share_merged_tables`).
5. **Junction, deren beide Referenzen auf die Wurzeltabelle zeigen** (`building_upgrades_junction.from` und `.to`), und Schlüsselwerte mit `_` im Loc-Schlüssel. Erwartung: zwei Abschnitte, `OtherEnd` ist jeweils das andere Feld; Loc-Schlüssel werden exakt zusammengesetzt, nie über Suffix-Suche. → Task 5 (`A_self_referencing_junction_gives_one_section_per_field`, `Loc_keys_are_built_from_table_column_and_key`).

---

### Task 1: Projekt `Objects` und `ReferenceGraph`

**Files:**
- Create: `src/TotalWarModTool.Objects/TotalWarModTool.Objects.csproj`
- Create: `src/TotalWarModTool.Objects/ReferenceGraph.cs`
- Create: `tests/TotalWarModTool.Objects.Tests/TotalWarModTool.Objects.Tests.csproj`
- Create: `tests/TotalWarModTool.Objects.Tests/TestSchema.cs` (Hilfsklasse für alle Tests des Projekts)
- Test: `tests/TotalWarModTool.Objects.Tests/ReferenceGraphTests.cs`
- Modify: `TotalWarModTool.slnx` (beide Projekte in die Ordner `/src/` und `/tests/`, alphabetisch)

**Interfaces:**
- Consumes: `GameSchema`, `TableDefinition`, `FieldDefinition`, `TableReference` aus `Schema`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

/// <summary>A field of <see cref="SourceTable"/> that references a column of another table. FieldIndex is the index in the latest definition of SourceTable.</summary>
public sealed record IncomingReference(string SourceTable, string FieldName, int FieldIndex, string TargetColumn);

public sealed class ReferenceGraph
{
    public static ReferenceGraph Build(GameSchema schema);
    /// <summary>The definition with the highest version, null when the schema does not know the table.</summary>
    public TableDefinition? Latest(string table);
    /// <summary>Every field of every table that references <paramref name="table"/>; empty when none. Sorted by SourceTable, then FieldName (ordinal).</summary>
    public IReadOnlyList<IncomingReference> Incoming(string table);
    /// <summary>Exactly two reference fields and at most four fields in the latest definition.</summary>
    public bool IsJunction(string table);
    /// <summary>Indexes of the fields with is_key in the latest definition; empty when unknown or without key.</summary>
    public IReadOnlyList<int> KeyFields(string table);
    public bool HasObjects(string table) => KeyFields(table).Count > 0;
    /// <summary>"building_levels" gives "building_levels_tables" when the schema has it, otherwise the name unchanged.</summary>
    public string ResolveTableName(string referenceTable);
}
```

`TestSchema` (Tests): `public static GameSchema Build(params (string Table, int Version, FieldDefinition[] Fields)[] defs)` und `public static FieldDefinition Field(string name, FieldType type = FieldType.StringU8, bool key = false, (string Table, string Column)? reference = null)` mit `RawType = type.ToString()`.

- [ ] **Step 1: Projekte anlegen.** `src/TotalWarModTool.Objects/TotalWarModTool.Objects.csproj` nach dem Muster von `src/TotalWarModTool.Workspace/TotalWarModTool.Workspace.csproj` (net8.0, Projektreferenzen `Schema`, `Tables`, `PackFiles`, `Workspace`, `Projects`, `GameProfiles`, `Operations`). Testprojekt nach dem Muster von `tests/TotalWarModTool.Workspace.Tests` mit Referenz auf `Objects` und dieselben Libraries. Beide in `TotalWarModTool.slnx` eintragen.

- [ ] **Step 2: Failing Tests schreiben** in `ReferenceGraphTests.cs`:

```csharp
[Fact] public void Incoming_lists_every_field_that_references_the_table()
// Schema: "a_tables" v1 [key "key" is_key], "b_tables" v1 ["owner" ref (a,key), "x"], "c_tables" v1 ["left" ref (a,key), "right" ref (a,key)].
// Incoming("a_tables") hat drei Einträge in dieser Reihenfolge: (b_tables, owner, 0, key), (c_tables, left, 0, key), (c_tables, right, 1, key).
// Incoming("b_tables") ist leer. Incoming("unknown_tables") ist leer.
[Fact] public void Latest_picks_the_highest_version()
// "a_tables" v1 mit 1 Feld und v3 mit 2 Feldern (v3 steht zuerst in der Liste): Latest gibt die Definition mit 2 Feldern. Latest("nope") ist null.
[Fact] public void IsJunction_needs_exactly_two_references_and_at_most_four_fields()
// 2 refs + 2 fields: true. 2 refs + 2 plain fields (4 total): true. 2 refs + 3 plain (5 total): false. 3 refs: false. 1 ref: false. unknown table: false.
[Fact] public void KeyFields_follow_the_schema_order()
// Felder [a (key), b, c (key)]: KeyFields == [0, 2]; HasObjects true. Tabelle ohne Schlüssel: leer, HasObjects false.
[Fact] public void ResolveTableName_appends_the_suffix_only_when_the_schema_knows_it()
// Schema kennt "a_tables": ResolveTableName("a") == "a_tables". Unbekannt: ResolveTableName("zzz") == "zzz". Groß-/Kleinschreibung: "A" gibt "a_tables".
[Fact] public void Incoming_resolves_references_without_the_suffix()
// Referenz (a, key) in b_tables wird unter "a_tables" gefunden; Incoming("a") (ohne Suffix) gibt dasselbe.
```

- [ ] **Step 3: RED prüfen.** `dotnet test tests/TotalWarModTool.Objects.Tests` schlägt mit Compile-Fehlern fehl.

- [ ] **Step 4: `ReferenceGraph` implementieren.** Beim `Build` einmal über alle Tabellen; `Incoming` als `Dictionary<string, List<IncomingReference>>` (OrdinalIgnoreCase); Lookup-Argument geht durch `ResolveTableName`.

- [ ] **Step 5: GREEN.** `dotnet test tests/TotalWarModTool.Objects.Tests` grün. `dotnet build TotalWarModTool.slnx` ohne Warnung.

- [ ] **Step 6: Commit.**

```bash
git add TotalWarModTool.slnx src/TotalWarModTool.Objects tests/TotalWarModTool.Objects.Tests
git commit -m "feat: add the Objects library with the reference graph"
```

---

### Task 2: `GameProfile` mit Loc-Pack-Zuordnung

**Files:**
- Modify: `src/TotalWarModTool.GameProfiles/GameProfile.cs`
- Modify: `src/TotalWarModTool.GameProfiles/GameProfileLoader.cs`
- Modify: `profiles/warhammer_3.json`
- Test: `tests/TotalWarModTool.GameProfiles.Tests/GameProfileLoaderTests.cs`

**Interfaces:**
- Produces: `GameProfile` bekommt zwei Parameter mit Standardwert am Ende:

```csharp
public sealed record GameProfile(
    string Id, string DisplayName, int SteamAppId, string InstallFolderName, string DataFolder, string PackFormat,
    string SchemaFile, IReadOnlyList<string> VanillaPackTypes, string? VanillaManifestFile,
    /// <summary>Language code of data\language.txt (upper case) to loc pack file name, for example "DE" to "local_ge.pack".</summary>
    IReadOnlyDictionary<string, string>? LocPacks = null,
    /// <summary>Loc pack when the language is unknown or language.txt is missing; null means no texts.</summary>
    string? DefaultLocPack = null);
```

JSON: optionales Objekt `"locPacks": { "DE": "local_ge.pack", … }` und optionaler String `"defaultLocPack"`. Fehlen sie, bleibt `LocPacks` ein leeres Dictionary (nie null nach dem Laden) und `DefaultLocPack` null. Ein `locPacks`, das kein Objekt ist, oder ein Wert, der kein String ist, macht die Datei ungültig (wie heute bei `vanillaPackTypes`).

`profiles/warhammer_3.json` bekommt (gemessen am 2026-10-09 im Datenordner): `"locPacks": { "BR": "local_br.pack", "CN": "local_cn.pack", "CZ": "local_cz.pack", "DE": "local_ge.pack", "EN": "local_en.pack", "FR": "local_fr.pack", "IT": "local_it.pack", "KR": "local_kr.pack", "PL": "local_pl.pack", "RU": "local_ru.pack", "SP": "local_sp.pack", "TR": "local_tr.pack", "ZH": "local_zh.pack" }` und `"defaultLocPack": "local_en.pack"`.

- [ ] **Step 1: Failing Tests** in `GameProfileLoaderTests.cs` (bestehende Muster übernehmen):

```csharp
[Fact] public void Loc_packs_are_read_with_upper_case_codes()
// JSON mit "locPacks": {"de": "local_ge.pack", "EN": "local_en.pack"}, "defaultLocPack": "local_en.pack":
// profile.LocPacks["DE"] == "local_ge.pack" (Schlüssel werden in Großbuchstaben normalisiert, Lookup OrdinalIgnoreCase), Count 2, DefaultLocPack "local_en.pack".
[Fact] public void Missing_loc_packs_give_an_empty_map()
// Datei ohne beide Felder: LocPacks leer (nicht null), DefaultLocPack null.
[Fact] public void A_non_object_loc_packs_value_is_a_problem()
// "locPacks": ["x"] und "locPacks": {"DE": 1}: Datei wird übersprungen, Problem.Detail enthält "locPacks".
[Fact] public void The_shipped_profile_maps_the_measured_languages()
// profiles/warhammer_3.json über RepoRoot.Find(): LocPacks hat 13 Einträge, ["DE"] == "local_ge.pack", DefaultLocPack == "local_en.pack".
```

- [ ] **Step 2: RED prüfen.** `dotnet test tests/TotalWarModTool.GameProfiles.Tests`.

- [ ] **Step 3: Implementieren** (Record erweitern, Loader-DTO und Validierung, Profil-JSON).

- [ ] **Step 4: GREEN**, dazu `dotnet build TotalWarModTool.slnx` ohne Warnung (der einzige bestehende `new GameProfile(`-Aufruf kompiliert dank Standardwerten weiter).

- [ ] **Step 5: Commit.**

```bash
git add src/TotalWarModTool.GameProfiles profiles/warhammer_3.json tests/TotalWarModTool.GameProfiles.Tests
git commit -m "feat: map game language codes to loc packs in the profile"
```

---

### Task 3: Ladereihenfolge (`used_mods.txt`, Loc-Pack, Projekt)

**Files:**
- Create: `src/TotalWarModTool.Objects/LoadOrder.cs` (Modell)
- Create: `src/TotalWarModTool.Objects/UsedModsFile.cs`
- Create: `src/TotalWarModTool.Objects/LoadOrderResolver.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/UsedModsFileTests.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/LoadOrderResolverTests.cs`

**Interfaces:**
- Consumes: `PackCatalogResult`, `PackSource`, `PackOrigin` aus `Workspace`; `GameProfile`; `FileFingerprint`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum SourceKind { Vanilla, Mod, Project }

/// <summary>One source of rows. Path is the pack file for Vanilla and Mod, the project folder for Project. Name is the pack file name or the project name.</summary>
public sealed record TableSource(SourceKind Kind, string Name, string Path);

public enum LoadOrderProblem { UsedModsMissing, UsedModsUnreadable, ModPackMissing, LanguageFileUnreadable, LocPackMissing, RootPackAdded }
public sealed record LoadOrderWarning(LoadOrderProblem Problem, string? Name, string Detail);

/// <summary>Sources in load order (later wins). LocPack is the vanilla loc pack of the game language, null when none exists.</summary>
public sealed record LoadOrder(IReadOnlyList<TableSource> Sources, TableSource? LocPack, IReadOnlyList<LoadOrderWarning> Warnings);

public sealed record UsedMods(IReadOnlyList<string> WorkingDirectories, IReadOnlyList<string> ModNames);
public static class UsedModsFile
{
    /// <summary>Parses lines like add_working_directory "path"; and mod "name.pack"; in file order. Unknown lines are ignored.</summary>
    public static UsedMods Parse(string text);
}

public sealed record LoadOrderRequest(
    GameProfile Profile,
    string InstallPath,
    PackCatalogResult Catalog,
    string? ProjectFolder,
    string? ProjectName,
    /// <summary>Pack of the root row when it might not be active; null when the root comes from vanilla or the project.</summary>
    string? RootPackPath);

public static class LoadOrderResolver
{
    public const string UsedModsFileName = "used_mods.txt";
    public const string LanguageFileName = "language.txt";
    public static LoadOrder Resolve(LoadOrderRequest request);
}
```

Regeln für `Resolve`:
1. Vanilla: alle `PackSource` mit `Origin == Vanilla` in Katalogreihenfolge, auch solche mit `Problem != None` (der Store meldet sie später). Kind `Vanilla`, Name = `PackSource.Name`.
2. `used_mods.txt` liegt unter `InstallPath`. Fehlt sie: Warnung `UsedModsMissing`, keine Mods. Unlesbar (IOException, UnauthorizedAccessException): `UsedModsUnreadable`. Jeder `mod "<name>"` wird in den `add_working_directory`-Ordnern (Dateireihenfolge), dann im Datenordner (`InstallPath\<Profile.DataFolder>`) gesucht; erstes Vorkommen zählt (`File.Exists`). Nicht gefunden: `ModPackMissing` mit dem Namen. Kind `Mod`, Name = Dateiname. Ein Mod, der schon als Vanilla gelistet ist (gleicher Pfad, OrdinalIgnoreCase), wird nicht doppelt aufgenommen.
3. `RootPackPath`: wenn nicht null und nicht schon unter 1 oder 2 (Pfadvergleich OrdinalIgnoreCase): hinter den Mods einfügen, Kind `Mod`, Warnung `RootPackAdded`.
4. Projekt: wenn `ProjectFolder` nicht null: zuletzt, Kind `Project`, Name = `ProjectName ?? Path.GetFileName(ProjectFolder)`, Path = `ProjectFolder`.
5. Loc-Pack: `data\language.txt` lesen, Inhalt trimmen, Großbuchstaben; `Profile.LocPacks[code]` liefert den Dateinamen, sonst `Profile.DefaultLocPack`. Datei fehlt: `DefaultLocPack` ohne Warnung. Datei unlesbar: `LanguageFileUnreadable` plus `DefaultLocPack`. Das Pack wird im Datenordner gesucht; fehlt es (oder ist kein Name bestimmt): `LocPack` null, Warnung `LocPackMissing`. Das Loc-Pack erscheint nicht zusätzlich in `Sources`, wenn es dort schon als Vanilla steht; `LocPack` zeigt auf dieselbe Instanz, sonst auf eine neue Vanilla-Quelle.

- [ ] **Step 1: Failing Tests** `UsedModsFileTests.cs`:

```csharp
[Fact] public void Parse_reads_directories_and_mods_in_file_order()
// Text aus Spec 3.6 (zwei add_working_directory, zwei mod): WorkingDirectories == [beide Pfade ohne Anführungszeichen], ModNames == ["end_times_mp_narrative_fix.pack", "QueBatAutResv1.pack"].
[Fact] public void Parse_ignores_unknown_lines_blank_lines_and_missing_semicolons()
// Zeilen "", "# x", "mod \"a.pack\"" (ohne ;), "mod a.pack" (ohne Anführungszeichen) -> nur "a.pack" aus der Zeile mit Anführungszeichen; CRLF und LF gleich.
```

`LoadOrderResolverTests.cs` (jeder Test baut in `TempDir` einen Spielordner `<tmp>\game\data` mit leeren Dateien als Packs; Katalog wird direkt als `PackCatalogResult` mit `PackSource`-Instanzen gebaut, Header null, Problem None; Profil per `new GameProfile(...)` mit `DataFolder = "data"`, `LocPacks = {"DE": "local_ge.pack"}`, `DefaultLocPack = "local_en.pack"`):

```csharp
[Fact] public void Vanilla_then_used_mods_then_project()
// Vanilla [data.pack, local_en.pack]; used_mods.txt: add_working_directory "<tmp>\ws\1"; mod "m1.pack"; mod "m2.pack"; m1.pack liegt in ws\1, m2.pack im Datenordner; ProjectFolder "<tmp>\proj", ProjectName "My Mod".
// Sources: [Vanilla data.pack, Vanilla local_en.pack, Mod m1.pack (Pfad ws\1\m1.pack), Mod m2.pack (Datenordner), Project "My Mod" (Pfad proj)]. Warnings leer. LocPack: language.txt fehlt -> local_en.pack, dieselbe Instanz wie in Sources.
[Fact] public void A_missing_used_mods_file_gives_a_warning_and_no_mods()
// Warnings == [UsedModsMissing]; Sources nur Vanilla (und Projekt, wenn gegeben).
[Fact] public void A_missing_mod_pack_is_skipped_with_its_name()
// mod "gone.pack" nirgends: Warnung ModPackMissing mit Name "gone.pack"; die anderen Mods bleiben in Reihenfolge.
[Fact] public void The_root_pack_is_added_behind_the_mods_when_it_is_not_active()
// RootPackPath "<tmp>\other\x.pack": Sources enden mit [.., Mod x.pack, Project]; Warnung RootPackAdded. Ist RootPackPath gleich einem Mod-Pfad (andere Groß-/Kleinschreibung): keine Ergänzung, keine Warnung.
[Fact] public void The_loc_pack_follows_language_txt_and_the_profile()
// language.txt "de\r\n" -> LocPack local_ge.pack (Datei vorhanden). "xx" -> local_en.pack. language.txt vorhanden, aber local_ge.pack fehlt -> LocPack null, Warnung LocPackMissing "local_ge.pack".
[Fact] public void A_used_mods_file_that_cannot_be_read_is_a_warning()
// used_mods.txt mit FileShare.None exklusiv geöffnet: Warnung UsedModsUnreadable, keine Mods, keine Exception.
```

- [ ] **Step 2: RED prüfen.**

- [ ] **Step 3: Implementieren.** `UsedModsFile.Parse`: zeilenweise, Trim, Muster `add_working_directory "<…>"` und `mod "<…>"`, optionales `;`. `LoadOrderResolver.Resolve` nach den Regeln oben; Dateien lesen mit `FileShare.ReadWrite | FileShare.Delete` wie `GameProfileLoader`.

- [ ] **Step 4: GREEN.**

- [ ] **Step 5: Commit.**

```bash
git add src/TotalWarModTool.Objects tests/TotalWarModTool.Objects.Tests
git commit -m "feat: resolve the load order from the catalog, used_mods.txt and the project"
```

---

### Task 4: `TableStore` und `LocStore`

**Files:**
- Create: `src/TotalWarModTool.Objects/MergedTable.cs` (Modell, `RowProvenance`, `MergedRow`, `SourceProblem`)
- Create: `src/TotalWarModTool.Objects/SourceReader.cs` (Einträge einer Quelle für eine Tabelle oder Loc lesen)
- Create: `src/TotalWarModTool.Objects/TableStore.cs`
- Create: `src/TotalWarModTool.Objects/LocStore.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/TestSources.cs` (Hilfsklasse: Testpack mit Tabellen und Loc schreiben, Projektordner mit TSV anlegen)
- Test: `tests/TotalWarModTool.Objects.Tests/TableStoreTests.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/LocStoreTests.cs`

**Interfaces:**
- Consumes: `ReferenceGraph` (Task 1), `LoadOrder`, `TableSource` (Task 3), `PackAccess.ReadIndex`, `PackReader.ReadEntry`, `DbTablePath.TryGetTableName`, `DbTableDecoder.Decode`, `LocDecoder.Decode`, `TableTsv.Read`, `TableTsv.ReadLoc`, `PackFolderPaths.Resolve`, `ProjectFile.PackFolder`, `CellText.DefaultValue`, `FileFingerprint.TryRead`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum SourceProblemKind { Unreadable, NotInSchema, VersionNotInSchema, DecodeFailed, TsvInvalid }
/// <summary>EntryPath is the pack entry path or the project file relative to the project folder.</summary>
public sealed record SourceProblem(TableSource Source, string EntryPath, SourceProblemKind Kind, string Detail);

/// <summary>Overridden lists the sources whose row with the same key this row replaced, earliest first.</summary>
public sealed record RowProvenance(TableSource Source, IReadOnlyList<TableSource> Overridden, bool OlderLayout, bool Duplicate);
public sealed record MergedRow(object?[] Cells, RowProvenance Provenance);

public sealed class MergedTable
{
    public string TableName { get; }
    /// <summary>Latest schema definition; null when the schema does not know the table (then Rows is empty).</summary>
    public TableDefinition? Definition { get; }
    public IReadOnlyList<int> KeyFields { get; }
    public IReadOnlyList<MergedRow> Rows { get; }
    public IReadOnlyList<SourceProblem> Problems { get; }
    public long EstimatedBytes { get; }
    /// <summary>Rows whose key fields equal the values (ordinal, in KeyFields order). Empty when the table has no key.</summary>
    public IReadOnlyList<MergedRow> FindByKey(IReadOnlyList<string> keyValues);
    /// <summary>Rows whose cell in the field (by name, OrdinalIgnoreCase) formats to the value. Empty for an unknown field.</summary>
    public IReadOnlyList<MergedRow> FindByField(string fieldName, string value);
    public int FieldIndex(string fieldName); // -1 when unknown
}

public sealed class TableStore
{
    public const long DefaultMaxCacheBytes = 200L * 1024 * 1024;
    public TableStore(GameSchema schema, ReferenceGraph graph, long maxCacheBytes = DefaultMaxCacheBytes);
    public MergedTable Get(string tableName, LoadOrder order, CancellationToken cancellationToken);
    public long CachedBytes { get; }
    public int CachedTables { get; }
    public void Clear();
}

public sealed record LocText(string Text, TableSource Source);
public sealed class LocTable
{
    public int Count { get; }
    public IReadOnlyList<SourceProblem> Problems { get; }
    public bool TryGet(string key, out LocText text);
}
public sealed class LocStore
{
    public LocTable Get(LoadOrder order, CancellationToken cancellationToken);
    public void Clear();
}
```

Regeln `TableStore.Get`:
- Einträge einer Quelle für `tableName`: Pack: alle Einträge, bei denen `DbTablePath.TryGetTableName` den Namen liefert (OrdinalIgnoreCase), in Indexreihenfolge; Projekt: alle `*.tsv` direkt unter `<ProjectFolder>\pack\db\<tableName>\` in ordinaler Namensreihenfolge, jeder Pfad über `PackFolderPaths.Resolve` (Problem ≠ None → `SourceProblem Unreadable`).
- Pack-Index: `PackAccess.ReadIndex`; Problem → ein `SourceProblem Unreadable` pro Quelle mit `EntryPath = ""`. Indexe werden im Store pro Pack-Fingerprint gehalten.
- Dekodieren: `DbTableDecoder.Decode`; Status `TableNotInSchema` → `NotInSchema`, `VersionNotInSchema` → `VersionNotInSchema`, `Failed`/`UnsupportedFieldType` → `DecodeFailed`; Lesefehler des Eintrags (`EntryContent.Bytes == null`) → `Unreadable`. Projekt-TSV: `TableTsv.Read`; `Problem` → `TsvInvalid` mit `Detail` der Diagnose.
- Zusammenführung: `Definition = graph.Latest(tableName)`; ist sie null, bleibt `Rows` leer und jeder Eintrag wird `NotInSchema`. Für jeden dekodierten Eintrag: ist seine Definition feldgleich (gleiche Namen in gleicher Reihenfolge), Zellen 1:1; sonst pro Zielfeld das Quellfeld gleichen Namens (OrdinalIgnoreCase), fehlend → `CellText.DefaultValue(type)`, Zeile `OlderLayout = true`.
- Schlüssel: Zellen der `KeyFields` als `CellText.Format` verkettet mit `\u0001`. Zeile mit schon bekanntem Schlüssel aus einer **späteren** Quelle ersetzt alle Zeilen dieses Schlüssels an der Position der ersten; `Overridden` = bisherige Quellen in Reihenfolge (ohne Duplikate). Gleicher Schlüssel innerhalb **derselben** Quelle: beide bleiben, beide `Duplicate = true`. Ohne Schlüsselfelder: nur anhängen.
- `EstimatedBytes` = Summe über Zeilen von `32 + 24 * Zellen + 2 * (Summe der Längen aller String-Zellen)`.
- Cache-Schlüssel: `tableName` (lower) plus für jede Quelle in Reihenfolge `Kind|Path|Length|LastWriteTimeUtc.Ticks` (Pack: `FileFingerprint.TryRead(Path)`, fehlend → `missing`; Projekt: Fingerprint jeder Datei unter `pack\db\<tableName>\`, Ordner fehlt → `none`). Gleicher Schlüssel → gecachte Instanz. Vor dem Einfügen werden die am längsten nicht genutzten Tabellen entfernt, bis `CachedBytes + EstimatedBytes <= maxCacheBytes`; ist `EstimatedBytes > maxCacheBytes`, wird die Tabelle geliefert, aber nicht gehalten.
- Abbruch: `cancellationToken.ThrowIfCancellationRequested()` vor jeder Quelle; nichts Halbes landet im Cache.

Regeln `LocStore.Get`: Quellen in dieser Reihenfolge: `order.LocPack` (alle Einträge mit Endung `.loc`), dann jede Quelle mit Kind `Mod` (`.loc`-Einträge), dann Projekt (alle `*.loc.tsv` unter `pack\`, rekursiv, ordinal nach relativem Pfad, über `PackFolderPaths.Resolve`). Spätere Quelle überschreibt gleichen Schlüssel. Problemfälle wie oben (`LocDecodeStatus.Failed` → `DecodeFailed`, TSV → `TsvInvalid`). Cache: ein `LocTable` pro Fingerprint-Schlüssel aller beteiligten Quellen; nur der letzte wird gehalten.

`TestSources` (Tests): `public static string WritePack(string path, GameSchema schema, params (string Table, int Version, object?[][] Rows)[] tables)` (ein Eintrag `db/<table>/data__` pro Tabelle, Bytes aus `DbTableEncoder.Encode` mit der Definition dieser Version; optional `(string Path, LocRow[] Rows)[] locs` als `text/<Path>.loc` über `LocEncoder.Encode`), `public static string WriteProjectTable(string projectFolder, GameSchema schema, string table, int version, string fileName, object?[][] rows)` (über `TableTsv.Write` nach `pack\db\<table>\<fileName>.tsv`), `WriteProjectLoc(projectFolder, relativePath, LocRow[] rows)`, und `public static LoadOrder Order(params TableSource[] sources)` plus `OrderWithLoc(TableSource locPack, params TableSource[] sources)`.

- [ ] **Step 1: Failing Tests** `TableStoreTests.cs` (Schema: `units_tables` v2 [key "key" is_key, "name", "cost" I32], `units_tables` v1 [key, cost] für ältere Layouts, `pairs_tables` v1 [a is_key, b is_key, "n" I32], `plain_tables` v1 ["x"] ohne Schlüssel):

```csharp
[Fact] public void Rows_of_later_sources_override_earlier_ones_and_keep_the_position()
// vanilla.pack units [(u1,"Alpha",10),(u2,"Beta",20)], mod.pack units [(u2,"Beta mod",25),(u3,"Gamma",30)]:
// Rows: u1 (vanilla), u2 (mod, Overridden [vanilla]), u3 (mod). FindByKey(["u2"])[0].Cells[1] == "Beta mod".
[Fact] public void An_entry_with_an_older_layout_is_merged_by_field_name()
// mod.pack units v1 (key, cost) [(u9, 5)]: Zeile hat 3 Zellen, name == "" (DefaultValue StringU8), cost == 5, OlderLayout true. Vanilla-Zeilen OlderLayout false.
[Fact] public void A_duplicate_key_in_one_source_keeps_both_rows_marked()
// vanilla units [(u1,..),(u1,..)]: 2 Rows, beide Duplicate true. mod units [(u1,...)]: danach genau 1 Row u1 aus mod, Overridden [vanilla], Duplicate false.
[Fact] public void A_table_without_key_is_appended()
// plain_tables aus zwei Quellen: alle Zeilen in Quellreihenfolge, KeyFields leer, FindByKey gibt leer.
[Fact] public void Project_tsv_files_are_the_last_source()
// Projekt pack\db\units_tables\a.tsv mit u1: Row u1 aus Project, Overridden [vanilla]. Provenance.Source.Kind == Project.
[Fact] public void A_broken_source_is_reported_and_the_others_are_merged()
// Quellen: vanilla.pack ok, broken.pack (Datei mit 10 Null-Bytes), schemaless.pack (Tabelle "ghost_tables"), Projekt-TSV mit "kaputt" als Inhalt:
// Get("units_tables"): Rows aus vanilla; Problems: broken.pack Unreadable (EntryPath ""), Projekt-TSV TsvInvalid (EntryPath "pack\db\units_tables\a.tsv" oder mit '/', Vergleich normalisiert). Get("ghost_tables"): Definition null, Problem NotInSchema.
[Fact] public void A_locked_pack_is_a_problem_not_an_exception()
// vanilla.pack exklusiv geöffnet (FileShare.None): Problem Unreadable, Rows leer, keine Exception.
[Fact] public void The_same_order_returns_the_cached_instance()
// zweimal Get mit gleicher Order: ReferenceEquals true, CachedTables 1.
[Fact] public void A_changed_pack_invalidates_only_its_tables()
// units in vanilla+mod, pairs nur in vanilla. Beide holen. mod.pack neu schreiben (andere Länge oder mtime +2 s): Get(units) neue Instanz, Get(pairs) dieselbe Instanz.
[Fact] public void Two_load_orders_do_not_share_merged_tables()
// Order A [vanilla], Order B [vanilla, mod]: Get(units, A) und Get(units, B) sind verschiedene Instanzen mit 2 bzw. 3 Zeilen; CachedTables 2.
[Fact] public void A_table_above_the_limit_is_returned_but_not_cached()
// maxCacheBytes 100: Get liefert Rows, CachedTables 0, CachedBytes 0.
[Fact] public void The_least_recently_used_table_is_evicted()
// maxCacheBytes so, dass zwei kleine Tabellen passen, drei nicht: units, pairs holen, units nochmal (frisch), dann plain: CachedTables 2, pairs ist raus (Get(pairs) gibt neue Instanz), units bleibt (ReferenceEquals).
[Fact] public void Cancellation_throws_and_caches_nothing()
// CancellationToken schon abgebrochen: Assert.Throws<OperationCanceledException>; CachedTables 0.
[Fact] public void A_composite_key_is_found_by_all_values()
// pairs [(a1,b1,1),(a1,b2,2)]: FindByKey(["a1","b2"]) genau die Zeile mit n == 2; FindByField("a", "a1") 2 Zeilen; FindByField("zzz", "a1") leer, FieldIndex("zzz") == -1.
```

`LocStoreTests.cs`:

```csharp
[Fact] public void Loc_pack_then_mods_then_project_override_each_other()
// LocPack local_en.pack text/db/a.loc [k1:"one", k2:"two"]; mod.pack text/m.loc [k2:"two mod"]; Projekt pack\text\p.loc.tsv [k3:"three"]:
// TryGet(k1) "one" aus local_en; k2 "two mod" aus mod; k3 "three" aus Project; Count 3; k9 false.
[Fact] public void Vanilla_packs_other_than_the_loc_pack_are_not_read_for_loc()
// data.pack (Vanilla) mit text/x.loc [k5]: TryGet(k5) false.
[Fact] public void A_missing_loc_pack_gives_an_empty_table_without_problems()
// Order mit LocPack null und ohne Mods: Count 0, Problems leer.
[Fact] public void A_broken_loc_entry_is_a_problem()
// mod.pack text/bad.loc mit Bytes "xx": Problem DecodeFailed mit EntryPath "text/bad.loc"; andere Schlüssel bleiben.
[Fact] public void The_loc_table_is_cached_until_a_source_changes()
// zweimal Get: ReferenceEquals; mod.pack neu schreiben: neue Instanz.
```

- [ ] **Step 2: RED prüfen.**

- [ ] **Step 3: Implementieren.** `SourceReader` kapselt „Einträge einer Quelle für eine Tabelle“ und „Loc-Einträge einer Quelle“ (Pack und Projekt) und liefert `(DecodedTable? Table, TsvTable? Tsv, SourceProblem? Problem)` beziehungsweise `(IReadOnlyList<LocRow>? Rows, SourceProblem? Problem)`. `TableStore` führt zusammen und cached (`LinkedList` plus `Dictionary` für LRU). Fingerprint-Schlüssel wie oben.

- [ ] **Step 4: GREEN.**

- [ ] **Step 5: Commit.**

```bash
git add src/TotalWarModTool.Objects tests/TotalWarModTool.Objects.Tests
git commit -m "feat: merge tables and loc over the load order with a bounded cache"
```

---

### Task 5: `ObjectBuilder` und `ObjectView`

**Files:**
- Create: `src/TotalWarModTool.Objects/ObjectView.cs` (Modell)
- Create: `src/TotalWarModTool.Objects/ObjectBuilder.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectBuilderTests.cs`

**Interfaces:**
- Consumes: `ReferenceGraph`, `TableStore`, `LocStore`, `MergedTable`, `LoadOrder`, `OperationProgress`, `CellText.Format`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

/// <summary>Table (with suffix) and the key values in KeyFields order. Equality is ordinal on the values and ignores case on the table.</summary>
public sealed class ObjectId : IEquatable<ObjectId>
{
    public ObjectId(string table, IReadOnlyList<string> keyValues);
    public string Table { get; }
    public IReadOnlyList<string> KeyValues { get; }
    /// <summary>The values joined with "" — the form loc keys use.</summary>
    public string JoinedKey { get; }
    // Equals, GetHashCode, ToString ("table:key1|key2")
}

public enum ReferenceResolution { None, Found, NotFound, LocOnly, Unchecked }

/// <summary>Target is set for Found and for Unchecked when the target table has exactly one key field. LocText is the loc text for LocOnly and the display name for Found when one exists.</summary>
public sealed record FieldValue(string Name, FieldType Type, string Text, TableReference? Reference, ReferenceResolution Resolution, ObjectId? Target, string? LocText);

/// <summary>RowId is null for tables without key. OtherEnd is set for rows of junction sections.</summary>
public sealed record ObjectRow(IReadOnlyList<FieldValue> Fields, RowProvenance Provenance, ObjectId? RowId, FieldValue? OtherEnd);

/// <summary>FieldName is the field of SourceTable that points at the root.</summary>
public sealed record ObjectSection(string SourceTable, string FieldName, bool IsJunction, IReadOnlyList<ObjectRow> Rows);

/// <summary>Table without the _tables suffix, as the loc key spells it.</summary>
public sealed record LocEntry(string Table, string Column, string Key, string Text, TableSource Source);

public enum ObjectWarningKind { RootNotFound, SourceProblem, TableNotInSchema, LoadOrder }
public sealed record ObjectWarning(ObjectWarningKind Kind, string? Name, string Detail);

public sealed record ObjectView(ObjectId Id, ObjectRow? Root, IReadOnlyList<LocEntry> Texts, IReadOnlyList<ObjectSection> Sections,
    IReadOnlyList<ObjectWarning> Warnings, LoadOrder Order);

public sealed class ObjectBuilder
{
    /// <summary>Columns tried, in this order, for the display name of an object: onscreen_name, name, screen_name, localised_title, title, localised_name, local_name.</summary>
    public static IReadOnlyList<string> DisplayNameColumns { get; }
    public ObjectBuilder(ReferenceGraph graph, TableStore tables, LocStore loc);
    /// <summary>Throws OperationCanceledException; every other problem is a warning. Progress counts tables: Done, Total, Item = table name.</summary>
    public ObjectView Build(ObjectId id, LoadOrder order, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
    /// <summary>Display name of an object from loc (first DisplayNameColumns hit), null when none. Uses only the LocStore.</summary>
    public string? TryDisplayName(ObjectId id, LoadOrder order, CancellationToken cancellationToken);
}
```

Regeln für `Build`:
1. `root = tables.Get(id.Table)`; `Definition` null → Warnung `TableNotInSchema`, `Root` null, keine Abschnitte. `FindByKey(id.KeyValues)` leer → Warnung `RootNotFound`, `Root` null, Abschnitte werden trotzdem gebaut (Zeilen, die auf einen nicht vorhandenen Schlüssel zeigen, sind sichtbar). Mehrere Treffer (Duplicate): erster wird `Root`.
2. Wurzelfelder: `Text = CellText.Format`. Feld mit `Reference` und leerem Text → `None`. Sonst `target = graph.ResolveTableName(Reference.Table)`; `merged = tables.Get(target)`; Treffer = `merged.FindByField(Reference.Column, Text)`. Treffer → `Found`, `Target = ObjectId(target, Schlüsselwerte der ersten Trefferzeile)` (Tabelle ohne Schlüssel: `Target` null), `LocText = TryDisplayName(Target)`. Kein Treffer: Loc-Schlüssel `<Reference.Table>_<Reference.Column>_<Text>` vorhanden → `LocOnly` mit dem Text; sonst `NotFound`.
3. `Texts`: für (a) die Wurzeltabelle mit `id.JoinedKey`, (b) jede `Found`-Referenz der Wurzel mit `Target.JoinedKey` und der Zieltabelle, (c) jede Abschnittstabelle mit dem Schlüsselwert der Wurzelspalte, auf die ihr Feld zeigt: für jede Spalte der jeweiligen Definition der Schlüssel `<tabelle ohne _tables>_<spalte>_<schlüssel>` im `LocTable`; Treffer in dieser Reihenfolge, ohne Doppelte (gleicher Loc-Schlüssel).
4. Abschnitte: für jede `IncomingReference r` aus `graph.Incoming(id.Table)`, deren `TargetColumn` ein Schlüsselfeld der Wurzel ist (Name, OrdinalIgnoreCase): `merged = tables.Get(r.SourceTable)`; `Definition` null → Warnung `TableNotInSchema` und weiter; `rows = merged.FindByField(r.FieldName, Schlüsselwert dieser Spalte)`; leer → kein Abschnitt. Reihenfolge wie `Incoming`. Zeilenfelder: alle Felder; Referenzfelder bekommen `Unchecked` mit `Target`, wenn die Zieltabelle genau ein Schlüsselfeld hat; nur bei Junction-Abschnitten wird das andere Referenzfeld (das nicht `r.FieldName` ist) wie ein Wurzelfeld aufgelöst (Regel 2) und als `OtherEnd` gesetzt. `RowId` aus den Schlüsselfeldern der Zeile.
5. Warnungen: `order.Warnings` → `LoadOrder` (Detail = `Problem` plus Name); `Problems` aller geholten Tabellen und des `LocTable` → `SourceProblem` (Name = Quellname, Detail = `EntryPath: Detail`), ohne Doppelte.
6. Fortschritt: `Total` = 1 + Zahl der Abschnittskandidaten + 1 (Loc); ein Report pro geholter Tabelle.
7. `cancellationToken.ThrowIfCancellationRequested()` vor jeder Tabelle.

- [ ] **Step 1: Failing Tests** `ObjectBuilderTests.cs`. Gemeinsames Schema (Mini-Abbild des Spiels):
  - `building_levels_tables` v1 [`level_name` key, `chain` ref (building_chains, key), `create_cost` I32, `commodity` ref (commodities, key)]
  - `building_chains_tables` v1 [`key` key, `name`]
  - `building_effects_junction_tables` v1 [`building` ref (building_levels, level_name), `effect` ref (effects, effect), `effect_scope`, `value` F32] (kein Schlüssel)
  - `building_upgrades_junction_tables` v1 [`from` key ref (building_levels, level_name), `to` key ref (building_levels, level_name)] (Junction)
  - `building_level_required_technology_junctions_tables` v1 [`building_level_key` key ref (building_levels, level_name), `technology_key` key ref (technologies, key)] (Junction)
  - `technologies_tables` v1 [`key` key, `building_level` ref (building_levels, level_name)]
  - `effects_tables` v1 [`effect` key, `icon`]
  - `building_culture_variants_tables` v1 [`building` key ref (building_levels, level_name), `culture` key, `name`, `short_description` ref (building_short_description_texts, key)]
  - `notes_tables` v1 [`building` ref (building_levels, create_cost)] (Referenz auf Nicht-Schlüssel)
  - `pairs_tables` v1 [`a` key, `b` key]; `pair_uses_tables` v1 [`pa` ref (pairs, a), `pb` ref (pairs, b)]
  - `commodities_tables` existiert nicht im Schema.

```csharp
[Fact] public void Root_fields_resolve_references_against_the_store()
// vanilla: building_levels [(b1, chain1, 100, "gold")], building_chains [(chain1, "Chain One")], loc building_chains_name_chain1 = "Chain One Loc".
// Root.Fields: chain -> Found, Target == ObjectId(building_chains_tables, [chain1]), LocText "Chain One Loc"; commodity -> NotFound (Tabelle unbekannt, kein Loc); create_cost -> Resolution None, Text "100".
[Fact] public void A_reference_to_a_loc_only_table_shows_the_text()
// Root-Feld commodity = "gold" (Referenz auf commodities, das weder Schema noch Packs kennen), loc commodities_key_gold = "Gold":
// Resolution LocOnly, LocText "Gold", Target null. Ohne den Loc-Eintrag: NotFound.
// Abschnittszeilen lösen nicht auf: im Abschnitt building_culture_variants bleibt short_description Unchecked, auch wenn ein Loc existiert.
[Fact] public void Sections_come_from_every_field_that_points_at_the_key()
// building_effects_junction [(b1, eff1, "province", 5), (b1, eff2, "province", -2), (b2, eff1, ...)]; technologies [(t1, b1)]:
// Sections enthält (building_effects_junction_tables, building, IsJunction false, 2 Rows) und (technologies_tables, building_level, false, 1 Row); keine Section für notes_tables (Referenz auf create_cost) — Review Focus 3.
[Fact] public void A_reference_to_a_non_key_column_gives_no_section()
// notes_tables [(100)]: trotz passendem Wert keine Section; keine Warnung.
[Fact] public void A_self_referencing_junction_gives_one_section_per_field()
// building_upgrades_junction [(b0, b1), (b1, b2)]: Section (building_upgrades_junction_tables, from): 1 Row mit OtherEnd.Text "b2", Target ObjectId(building_levels_tables,[b2]), Found. Section (..., to): 1 Row OtherEnd "b0". Beide IsJunction true.
[Fact] public void A_junction_other_end_that_does_not_exist_is_not_found()
// required_technology [(b1, t_missing)]: OtherEnd Resolution NotFound, Target == null, kein Fehler.
[Fact] public void Loc_keys_are_built_from_table_column_and_key()
// Zeilen: building_levels (b1, chain1, 100, ""), building_chains (chain1, "Chain One"), building_culture_variants (b1, cult1, "n", "sd1"), technologies (t1, b1).
// loc: building_levels_level_name_b1 = "B1 Name" (a), building_chains_name_chain1 = "Chain Loc" (b, über die Found-Referenz chain), building_culture_variants_name_b1 = "Barracks" (c, Abschnittstabelle mit Wurzelschlüssel), technologies_key_b1 = "T" (c), building_culture_variants_name_b1x = "Wrong" und building_effects_junction_effect_b1 = "Wrong" (dürfen nicht erscheinen: falscher Schlüssel beziehungsweise Tabelle ohne Abschnitt).
// Texts.Select(t => t.Key) == [building_levels_level_name_b1, building_chains_name_chain1, building_culture_variants_name_b1, technologies_key_b1] (Reihenfolge: (a), (b), dann (c) in Incoming-Reihenfolge der Abschnittstabellen).
[Fact] public void A_composite_key_root_matches_each_key_column_separately()
// pairs [(x, y)], pair_uses [(x, q), (z, y)]: Build(ObjectId(pairs_tables, [x, y])): Section pa hat 1 Row (x,q), Section pb hat 1 Row (z,y). Id.JoinedKey == "xy".
[Fact] public void Provenance_and_overrides_reach_the_rows()
// vanilla building_effects_junction [(b1, eff1, p, 5)], mod [(b1, eff3, p, 1)]; building_levels b1 in vanilla und mod (mod überschreibt create_cost):
// Root.Provenance.Source.Name == "mod.pack", Overridden == [vanilla.pack]; Section-Rows: eine aus vanilla, eine aus mod (Tabelle ohne Schlüssel, beide bleiben).
[Fact] public void A_missing_root_still_builds_the_sections_with_a_warning()
// building_levels ohne b7, effects_junction mit (b7, ...): Root null, Warnings enthält RootNotFound, Section building_effects_junction mit 1 Row.
[Fact] public void An_unknown_root_table_gives_only_a_warning()
// Build(ObjectId("ghost_tables", [x])): Root null, Sections leer, Warnings == [TableNotInSchema ghost_tables].
[Fact] public void Source_problems_become_warnings()
// Order mit broken.pack (Null-Bytes) und ModPackMissing-Warnung: Warnings enthält SourceProblem mit Name "broken.pack" und LoadOrder mit "ModPackMissing"; nur einmal, auch wenn drei Tabellen dieselbe Quelle melden.
[Fact] public void Progress_counts_tables_and_cancellation_stops()
// Progress-Fake sammelt Reports: letzter Report Done == Total; abgebrochenes Token vor Build: Assert.Throws<OperationCanceledException>.
[Fact] public void TryDisplayName_uses_the_column_order()
// loc technologies_onscreen_name_t1 = "Tech" und technologies_name_t1 = "Other": TryDisplayName(t1) == "Tech". Ohne Loc: null.
```

- [ ] **Step 2: RED prüfen.**

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN.**

- [ ] **Step 5: Commit.**

```bash
git add src/TotalWarModTool.Objects tests/TotalWarModTool.Objects.Tests
git commit -m "feat: build the object view of a row over the load order"
```

---

### Task 6: Kategorie-Dateien und `ObjectText`

**Files:**
- Create: `src/TotalWarModTool.Objects/CategoryDefinition.cs` (Modell und Loader)
- Create: `src/TotalWarModTool.Objects/ObjectText.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/CategoryDefinitionsTests.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectTextTests.cs`

**Interfaces:**
- Consumes: `ObjectView` und Teile (Task 5), `System.Text.Json`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum SectionMode { List, Sentence }
/// <summary>FieldName null means every section of the table. Pattern holds {fieldName} placeholders for Sentence.</summary>
public sealed record SectionRule(string Table, string? FieldName, string? TitleKey, SectionMode Mode, string? Pattern, bool Hidden);
public sealed record CategoryDefinition(string RootTable, string TitleKey, IReadOnlyList<SectionRule> Sections);
public sealed record CategoryProblem(string FileName, string Detail);
public sealed record CategoryLoadResult(IReadOnlyDictionary<string, CategoryDefinition> ByRootTable, IReadOnlyList<CategoryProblem> Problems);

public static class CategoryDefinitions
{
    public const int SchemaVersion = 1;
    /// <summary>Loads every top-level *.json of the folder; a missing folder gives an empty result. Never throws.</summary>
    public static CategoryLoadResult LoadAll(string folder);
}

public enum SpanKind { Normal, TableName, Key, FieldName, Value, Link, LocText, Title, ProvenanceVanilla, ProvenanceMod, ProvenanceProject, Overridden, Warning, Collapsed }
/// <summary>Target for Link; Section (index into ObjectView.Sections) for Collapsed and Title.</summary>
public sealed record TextSpan(string Text, SpanKind Kind, ObjectId? Target = null, int? Section = null);
public sealed record TextLine(IReadOnlyList<TextSpan> Spans, int Indent);
public sealed record RenderOptions(IReadOnlySet<int> ExpandedSections, int CollapseThreshold = ObjectText.DefaultCollapseThreshold, int CollapsedPreview = ObjectText.DefaultCollapsedPreview);

public static class ObjectText
{
    public const int DefaultCollapseThreshold = 20;
    public const int DefaultCollapsedPreview = 5;
    public static IReadOnlyList<TextLine> Render(ObjectView view, CategoryDefinition? category, RenderOptions options);
    /// <summary>"building_levels_tables" gives "building_levels".</summary>
    public static string ShortTableName(string table);
}
```

JSON der Kategorie-Datei (Spec 5.7): Pflicht `schemaVersion` (== 1), `rootTable` (String), `titleKey` (String); `sections` optional, jedes Element: `table` (Pflicht), `field` (optional), `titleKey` (optional), `mode` (`"list"` Standard oder `"sentence"`), `pattern` (Pflicht bei `sentence`), `hidden` (Bool, Standard false). Jeder Verstoß macht die Datei zu einem `CategoryProblem` mit Dateiname und Detail; zwei Dateien mit derselben `rootTable` (OrdinalIgnoreCase): die zweite (ordinale Dateinamensreihenfolge) ist ein Problem.

Rendering (Spec 5.6), Einrückung in `Indent` (0 Kopf, 1 Felder und Abschnittszeilen):
1. Kopf: `[TableName short] [Normal "  "] [Key JoinedKey] [Normal "  "] [Provenance… "[name]"]`, danach je überschriebener Quelle `[Normal " "] [Overridden "[name]"]`. Kein UI-Text dazwischen (die App setzt Tooltips). `Root` null: Kopf nur `[TableName short] [Normal "  "] [Key JoinedKey]`; die Warnung `RootNotFound` folgt in Regel 2.
2. Warnungen: eine Zeile pro `ObjectWarning`: `[Warning "<Kind>: <Name>: <Detail>"]` (Name weggelassen, wenn null).
3. Wurzelfelder (Indent 1): `[FieldName name] [Normal " = "] [Value|Link text]`; `Found`/`Unchecked` mit Target → `Link` mit Target; `LocOnly` → `[Value text] [Normal "  "] [LocText "\"<loc>\""]`; `NotFound` → `[Warning text]`; `Found` mit `LocText` → `[Link] [Normal "  "] [LocText "\"…\""]`.
4. Loc-Block (Indent 1): pro `LocEntry`: `[FieldName column] [Normal "  "] [LocText "\"text\""] [Normal "  "] [Provenance "[source]"]`.
5. Leerzeile (Spans leer) vor jedem Abschnitt. Abschnittstitel (Indent 0): `[Title key, Section i]` wenn die Regel einen `TitleKey` hat, sonst `[TableName short, Section i]`; dann `[Normal "  "] [Normal "(N)"]` mit Zeilenzahl, dann je beteiligter Quelle `[Provenance "[name]"]` (distinct, Reihenfolge des ersten Auftretens).
6. Zeilen eines Abschnitts (Indent 1), Modus `List`: Junction → `[Link|Value OtherEnd.Text]` (+ `[LocText]` wenn vorhanden); sonst Felder außer Schlüsselfeldern und außer `FieldName` des Abschnitts als `[FieldName] [Normal "="] [Value|Link]` getrennt durch `[Normal "  "]`. Am Ende jeder Zeile `[Normal "  "] [Provenance "[name]"]` und bei `Overridden` zusätzlich `[Overridden "[name]"]` je Quelle, bei `Duplicate` `[Warning "duplicate"]`, bei `OlderLayout` `[Warning "older layout"]`.
7. Modus `Sentence`: `Pattern` wird in Literale und `{feld}` zerlegt; Literale → `Normal`, Platzhalter → Feld wie in 6 (`Link` bei Target, sonst `Value`); unbekanntes Feld → `[Warning "{feld}"]`. Herkunft wie in 6.
8. Zuklappen: Abschnitt mit mehr als `CollapseThreshold` Zeilen und Index nicht in `ExpandedSections`: nur die ersten `CollapsedPreview` Zeilen, dann `[Collapsed "+N", Section i]` mit N = Rest.
9. Reihenfolge: mit Kategorie erst die Abschnitte in Regelreihenfolge (Regel mit `FieldName` null matcht alle Abschnitte der Tabelle, in `Incoming`-Reihenfolge), dann die übrigen alphabetisch nach `SourceTable`, dann `FieldName` (ordinal). `Hidden` lässt den Abschnitt weg. Ohne Kategorie alphabetisch.

- [ ] **Step 1: Failing Tests** `CategoryDefinitionsTests.cs`:

```csharp
[Fact] public void A_valid_file_is_loaded_with_defaults()
// Datei aus Spec 5.7: ByRootTable["building_levels_tables"].Sections hat 3 Regeln; erste Mode List, zweite Sentence mit Pattern, dritte Hidden true; FieldName null überall; Problems leer.
[Fact] public void Missing_fields_wrong_types_and_schema_versions_are_problems()
// ohne rootTable; schemaVersion 2; sections kein Array; mode "foo"; sentence ohne pattern; Text "{": jeweils ein Problem mit dem Dateinamen, ByRootTable leer.
[Fact] public void A_second_file_for_the_same_root_table_is_a_problem()
// a.json und b.json mit gleicher rootTable: ByRootTable hat 1 Eintrag (aus a.json), Problems enthält "b.json".
[Fact] public void A_missing_folder_gives_an_empty_result()
```

`ObjectTextTests.cs` (eine Hilfsfunktion `Text(TextLine)` = alle Span-Texte verkettet; ein `ObjectView` wird direkt aus den Records gebaut, ohne Store):

```csharp
[Fact] public void The_head_shows_table_key_and_provenance()
// Root aus mod.pack, Overridden [db.pack]: Zeile 0 Text == "building_levels  b1  [mod.pack] [db.pack]", Kinds [TableName, Normal, Key, Normal, ProvenanceMod, Normal, Overridden].
[Fact] public void Root_fields_render_values_links_and_loc_texts()
// chain Found mit Target und LocText: "chain = chain1  \"Chain One\"" mit Kinds [FieldName, Normal, Link, Normal, LocText], Link.Target == Target; commodity LocOnly: "commodity = gold  \"Gold\""; cost: "create_cost = 100" Kinds [FieldName, Normal, Value]; NotFound: Kind Warning.
[Fact] public void Sections_render_list_rows_without_key_and_back_reference_fields()
// Section building_effects_junction mit Row Fields [building, effect, effect_scope, value]: Zeile "effect=eff1  effect_scope=province  value=5  [db.pack]"; effect ist Link (Unchecked mit Target).
[Fact] public void Junction_rows_render_only_the_other_end()
// Row mit OtherEnd Found + LocText: "b2  \"Barracks 2\"  [db.pack]".
[Fact] public void A_long_section_is_collapsed_unless_expanded()
// 25 Rows, Threshold 20, Preview 5: 5 Zeilen plus Collapsed "+20" mit Section == i; mit ExpandedSections {i}: 25 Zeilen, kein Collapsed.
[Fact] public void A_category_orders_titles_and_hides_sections()
// Sections [cai_x, building_effects_junction, technologies] alphabetisch; Kategorie: effects zuerst mit TitleKey "objects.section.effects", cai_x hidden: Titelzeilen in Reihenfolge [Title "objects.section.effects", TableName "technologies"]; cai_x fehlt.
[Fact] public void A_sentence_rule_renders_the_pattern()
// Regel sentence "requires technology {technology_key}" auf einer Junction-Section mit OtherEnd t1 (Found): Zeile "requires technology t1  [db.pack]", Spans [Normal, Link, Normal, ProvenanceVanilla]; unbekanntes Feld {zzz} -> Warning "{zzz}".
[Fact] public void Warnings_and_a_missing_root_are_rendered()
// Root null, Warnings [RootNotFound]: Zeile 0 "building_levels  b9", Zeile 1 Kind Warning.
[Fact] public void Rendering_is_deterministic()
// zweimal Render derselben View: Zeilen und Kinds identisch (SequenceEqual über Text(line) und Kinds).
```

- [ ] **Step 2: RED prüfen.**

- [ ] **Step 3: Implementieren.** Loader nach dem Muster von `GameProfileLoader` (DTO, `JsonSerializerOptions` mit `PropertyNameCaseInsensitive`, Datei mit `FileShare.ReadWrite | FileShare.Delete`). Renderer als reine Funktion über `ObjectView`.

- [ ] **Step 4: GREEN.**

- [ ] **Step 5: Commit.**

```bash
git add src/TotalWarModTool.Objects tests/TotalWarModTool.Objects.Tests
git commit -m "feat: render the object view as spans with category definitions"
```

---

### Task 7: Echtdaten-Tests und Changelog

**Files:**
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs`
- Modify: `CHANGELOG.md` (Abschnitt „Unreleased“, Neu: noch kein Nutzer-Feature; Eintrag erst mit Plan 2. In diesem Task nur prüfen, dass nichts einzutragen ist, und nichts ändern.)
- Modify: `docs/specs/2026-10-09-teil-4-1-objekt-ansicht.md` (Abschnitt 10: Annahmen 4 und 6 mit dem Ergebnis der Echtdaten-Tests eintragen; Abschnitt 5.4 „Festlegungen aus Plan 1“ mit den Signaturen aus den Tasks 1 bis 6, sofern sie von der Spec abweichen)

**Interfaces:**
- Consumes: `RealData.Wh3Dir`, `RealData.Wh3Workshop`, `RealDataFact`, `RepoRoot.Find`, `SchemaLoader`, `GameProfileLoader`, `PackCatalog.Scan`, alle Bausteine aus Tasks 1 bis 6.

Alle Tests sind `[RealDataFact]` und bauen die Ladereihenfolge über `LoadOrderResolver.Resolve` mit `InstallPath = RealData.Wh3Dir`, `ProjectFolder` null. Die Tests lesen nur; sie schreiben nichts in den Spielordner.

- [ ] **Step 1: Tests schreiben**

```csharp
[RealDataFact] public void The_barracks_have_effects_units_and_upgrades()
// Build(building_levels_tables, [wh_main_emp_barracks_2]): Root nicht null; Sections enthalten SourceTable building_effects_junction_tables, building_units_allowed_tables, building_upgrades_junction_tables (from und to); Texts enthält einen Key, der mit "building_culture_variants_name_" beginnt; Warnings ohne RootNotFound und ohne TableNotInSchema.
[RealDataFact] public void The_empire_builds_in_under_two_seconds()
// Stopwatch um Build(factions_tables, [wh_main_emp_empire]) mit frischem Store: < 2000 ms; Sections.Count >= 20; Render liefert mehr als 100 Zeilen; mindestens ein Collapsed-Span.
[RealDataFact] public void A_trait_has_a_display_name_somewhere()
// Build(character_traits_tables, [wh_main_trait_all_personality_all_hates_dwarfs]): entweder Texts nicht leer oder eine Abschnittszeile (character_trait_levels) hat RowId mit TryDisplayName != null. Das Ergebnis (welcher Weg) wird in die Spec Abschnitt 10, Annahme 4, geschrieben.
[RealDataFact] public void Every_table_of_the_load_order_merges_without_exception()
// Für jede Tabelle des Schemas, die in mindestens einem Pack der Ladereihenfolge vorkommt: tables.Get; keine Exception; Summe der OlderLayout-Zeilen > 0 nur, wenn Mods in der Ladereihenfolge sind; Problems je Kind zählen und als Test-Output (ITestOutputHelper) ausgeben. Cache-Obergrenze 600 MB für diesen Test, damit nichts verdrängt wird; Laufzeit < 30 s.
[RealDataFact] public void The_second_build_of_a_building_does_not_decode_again()
// Zwei verschiedene Gebäude nacheinander mit demselben Store: zweiter Build unter 300 ms (Spike: 88 ms mit linearer Suche).
```

- [ ] **Step 2: Laufen lassen** mit `TWMT_WH3_DIR` gesetzt: `dotnet test tests/TotalWarModTool.Objects.Tests` (der Controller setzt die Variable; ohne sie werden die Tests übersprungen und der Task gilt als nicht geprüft).

- [ ] **Step 3: Spec nachführen** (Abschnitt 10, Annahmen 4 und 6; „Festlegungen aus Plan 1“ unter 5.4 nur für Abweichungen).

- [ ] **Step 4: Commit.**

```bash
git add tests/TotalWarModTool.Objects.Tests docs/specs/2026-10-09-teil-4-1-objekt-ansicht.md
git commit -m "test: check the object view against the real game data"
```

---

## Abnahme durch den Nutzer (nach dem Merge von Plan 1)

Plan 1 hat keine sichtbare Änderung in der App. Prüfbar ist nur der volle Testlauf: Build ohne Warnung, alle Library-Tests grün, Echtdaten-Tests mit `TWMT_WH3_DIR` grün (5 Tests), App-Tests unverändert grün.
