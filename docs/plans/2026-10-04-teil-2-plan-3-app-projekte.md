# Teil 2 Plan 3: Projekte in der App – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mod-Projekte werden in der App sichtbar und benutzbar: Gruppe „Projekte“ im Explorer, Projekt anlegen und öffnen, Pack importieren, bauen, installieren, deinstallieren, Projektdateien in Tabs ansehen, Textansicht für Tabellen, Wiederherstellen nach Neustart.

**Architecture:** Ein `ProjectsController` in der App hält die Projektliste eines Spiels und führt die Abläufe der Library `Projects` über die Fortschritts-Komponente aus; er liefert fertige Meldungstexte. Ein `ProjectsViewModel` trägt die Befehle und spricht mit dem Nutzer über das Interface `IProjectDialogs`. Der Explorer zeigt Projekte als eigene Knotenart. Tabs bekommen statt eines Packs eine allgemeine Quelle (`TabSource`), damit Projektdateien dieselben Tabs nutzen. Regeln, die zwei Seiten gleich verstehen müssen (welche Dateien zu einem Projekt gehören, wie eine Projektdatei gelesen wird), liegen in den Libraries.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`: 5.9 (App), 5.5, 5.7, 7, „Festlegungen aus Plan 2“ (besonders Punkt 22). Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-2-app-projekte` (von `master`, Stand `0644652` oder neuer). Baseline: 665 Library-Tests und 292 App-Tests grün, 0 übersprungen, Build ohne Warnung.

**Testregel.** Am 2026-10-04 mit dem Nutzer vereinbart: Subagenten starten keine App-Tests; den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers, und nie parallel zu einer anderen Sitzung, die baut oder testet. Dieser Plan ändert die App und braucht dafür eine Lockerung, die der Nutzer vor der Umsetzung bestätigen muss: Subagenten dürfen gezielt einzelne Testklassen der App starten (`dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~<Klasse>"`), immer nur ein Lauf zur Zeit, nie die ganze App-Suite und nie `dotnet test TotalWarModTool.slnx`. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors` ist an.
- Neue Referenzen: `Workspace` → `Projects`; `App` → `Projects`. `Projects` referenziert weiter weder `Workspace` noch `GameProfiles`.
- Bestehende öffentliche Signaturen bleiben nutzbar. Wo dieser Plan eine Signatur erweitert, bleibt der alte Aufruf gültig (Überladung, optionaler Parameter oder implizite Umwandlung).
- Fachlogik in Libraries, die App ist Hülle: keine Logik im Code-behind außer View-Verdrahtung. Keine statischen Singletons; alles entsteht im Composition Root (`App.xaml.cs`).
- Alle UI-Texte über `AppTexts` (`T`/`F`), jeder neue Key in `locales/en.json` und `locales/de.json`; der Inline-Fallback ist Englisch und gleicht `en`. Der Locale-Test bleibt grün. Keine Ellipsen in Labels. Enum-zu-Text-Methoden wählen unter einer Property pro Wert.
- Meldungen aus den Libraries (`Detail`, Gründe ausgelassener Einträge, Bau-Probleme) sind englischer Klartext und werden als Daten angezeigt. Pfade aus fremden Packs werden vor der Anzeige maskiert (Steuerzeichen als `\uXXXX`).
- Farben nur über Theme-Tokens und `DynamicResource`. Neue Fenster setzen Hintergrund und Vordergrund wie `CrashDialog`.
- Jede vom Nutzer gestartete Operation läuft über `IOperationRunner`. Automatische Läufe (Neu-Auflisten nach F5 oder nach einem Bau) bleiben still und laufen auf einem Pool-Thread.
- Schreibgrenze wie in Spec 4. Die App schreibt Projektordner und `data` nur über die Library `Projects`; die Projektliste nur über `ProjectListStore`.
- Eine Projektliste mit Status `Unreadable` oder `NewerVersion` wird nie gespeichert (nur Log-Warnung, Arbeit im Speicher).
- Kein Spielwissen im Code: Datenordner und Vanilla-Packnamen kommen aus `WorkspaceController` (`Location.DataPath`, Katalog-Packs mit `PackOrigin.Vanilla`).
- Kein `catch (Exception)` ohne Typfilter. View-Models bekommen kein Log; geloggt wird im Controller.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in `TempDir`; der „Spielordner“ der Tests ist `TestGame`. Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`, nichts schreibt in Spiel-, Workshop- oder RPFM-Ordner.
- View-Tests laufen über `Sta.RunAsync` und laden `Themes/Controls.xaml` wie die vorhandenen View-Tests.
- Quelltext: Escapes wie `\uFEFF` bleiben Escapes; kein `python` oder anderes Skript-Laufzeitsystem zum Editieren.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst. Widerspricht eine Erwartung einer Regel des Plans, wird das gemeldet.

## Review Focus

1. **Bauen, während außen ein Editor die Datei offen hat** (Sperrdatei `.~lock.x.tsv#`, `~$x.tsv`, versteckte Dateien wie `desktop.ini`). Erwartung: Solche Dateien landen nicht im Pack; die Meldung nach dem Bau nennt sie. → Task 1 (`Lock_hidden_and_system_files_are_ignored_and_listed`).
2. **Projektordner ist weg, verschoben oder kaputt** (USB-Laufwerk abgezogen, `twmt-project.json` von Hand zerstört, Projekt eines anderen Spiels). Erwartung: Das Projekt bleibt in der Liste mit Problemtext, seine Befehle sind deaktiviert, die übrigen Projekte funktionieren, der Start wird nicht blockiert. → Task 4 (`A_missing_or_broken_project_stays_in_the_list_with_its_problem`), Task 7 (`A_broken_project_node_shows_its_problem_and_disables_its_actions`).
3. **Bedienung ohne gefundenes Spiel oder ohne Schema.** Erwartung: Projekte sind sichtbar, Bauen geht (Tabellen melden „kein Schema“), Installieren ist deaktiviert und nennt den Grund. → Task 5 (`Install_without_a_game_folder_is_refused_with_a_reason`), Task 7 (`Projects_are_shown_while_the_game_is_not_found`).
4. **Abbruch oder Fehler mitten im Import oder Bau über die Oberfläche.** Erwartung: Projektliste unverändert, Meldung nennt einen liegengebliebenen Staging-Ordner, kein hängendes Fortschrittsfenster. → Task 2 (`Work_is_cancelled_when_the_window_cannot_be_shown`), Task 5 (`A_cancelled_import_changes_nothing_and_names_a_leftover_staging_folder`).
5. **Neustart mit offenen Projekt-Tabs, deren Datei oder Projekt inzwischen fehlt.** Erwartung: Übrige Tabs und ihre Reihenfolge bleiben, der fehlende Tab entfällt still, die Session wird nicht mit einem Fehler verworfen. → Task 8 (`Project_tabs_are_restored_in_their_old_order_and_missing_ones_are_dropped`).

---

### Task 1: Gemeinsame Dateiregeln (`ProjectFiles`, `ProjectFileOpener`)

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectFiles.cs`; `src/TotalWarModTool.Workspace/ProjectFileOpener.cs`
- Modify: `src/TotalWarModTool.Projects/ProjectBuilder.cs`, `BuildModel.cs`; `src/TotalWarModTool.Workspace/TotalWarModTool.Workspace.csproj` (Referenz auf `Projects`)
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectFilesTests.cs`, `ProjectBuilderTests.cs` (ergänzen); `tests/TotalWarModTool.Workspace.Tests/ProjectFileOpenerTests.cs`

**Interfaces:**
- Consumes: `ProjectLayout.FromRelativeFile`, `EntryConversion`, `TableTsv.Read/ReadLoc`, `DbTableDecoder`, `LocDecoder`, `EntryText.Decode`, `EntryOpener.KindOf`, `OpenedEntry`.
- Produces:

```csharp
// Projects
public sealed record ProjectFileItem(string RelativeFile, string PackPath, ProjectFileKind Kind);   // RelativeFile with '\', relative to pack\
public sealed record ProjectFileProblem(string RelativePath, string Detail);                          // a folder that is a link or cannot be listed
public sealed record ProjectFileList(bool PackFolderExists, IReadOnlyList<ProjectFileItem> Files,
    IReadOnlyList<string> Ignored, IReadOnlyList<ProjectFileProblem> Problems);

public static class ProjectFiles
{
    /// Lists pack\ of a project: files sorted ordinally by relative file. Never throws for the folder content.
    public static ProjectFileList List(string projectFolder, CancellationToken cancellationToken);
    /// True for files that never go into a pack: hidden or system attribute, or an editor lock or backup name.
    public static bool IsIgnored(string fileName, FileAttributes attributes);
}

public sealed record BuildPlan(IReadOnlyList<PackWriteEntry> Entries, IReadOnlyList<string> Dependencies,
    IReadOnlyList<BuildProblem> Problems, IReadOnlyList<string> Ignored);   // Ignored is new, last

// Workspace
public static class ProjectFileOpener
{
    public const long MaxFileBytes = 268_435_456;
    /// Opens one file below pack\ of a project for display. Only OperationCanceledException and argument errors are thrown.
    public static OpenedEntry Open(string projectFolder, string relativeFile, GameSchema? schema, CancellationToken cancellationToken);
}
```

Festlegungen:
- `ProjectFiles.List` übernimmt das Ordner-für-Ordner-Auflisten aus `ProjectBuilder` (Verknüpfungen nicht verfolgen, nicht lesbarer Ordner als Problem, Token pro Ordner und Datei). `ProjectBuilder.ComputeBuild` nutzt `ProjectFiles.List`; sein Verhalten bleibt gleich bis auf die ignorierten Dateien.
- `IsIgnored`: Attribut `Hidden` oder `System`; Name beginnt mit `.~lock.` oder `~$`; Name endet auf `.bak`, `.tmp` oder `~` (alles ohne Rücksicht auf Groß-/Kleinschreibung). Ignorierte Dateien stehen in `Ignored` (relativer Pfad), nicht in `Files`.
- `ProjectFileOpener.Open`: `EntryPath` des Ergebnisses ist `relativeFile` mit `/`. Datei über `MaxFileBytes` → `ReadProblem = TooLarge`; nicht lesbar → `Unreadable` mit Meldung der Ausnahme. `Kind` aus `ProjectLayout.FromRelativeFile`:
  - `Table`: `EntryKind.Table`; ohne Schema `SchemaMissing = true`, nichts wird gelesen. Sonst strenges UTF-8 und `TableTsv.Read`; Erfolg → `DecodedTable(Ok, Name, Version, HasGuid: false, Definition, Rows, null)`; Problem → `DecodedTable(Failed, <Tabellenname aus dem Pfad>, 0, false, null, [], "line N: <Detail>")` (bei ungültigem UTF-8 `the file is not valid UTF-8`).
  - `Loc`: `EntryKind.Loc`, entsprechend mit `TableTsv.ReadLoc` und `DecodedLoc`.
  - `Raw`: `EntryOpener.KindOf(packPath)` entscheidet: binäre Tabelle (`DbTableDecoder`), binäre Loc-Datei, Text (`EntryText.Decode`) oder anderes (nur Größe).
  - `StoredSize` und `UncompressedSize` sind die Dateigröße, `Format` ist `None`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// ProjectFilesTests
[Fact] public void Lists_files_sorted_with_kind_and_pack_path()
// pack\db\units_tables\a.tsv, pack\text\x.loc.tsv, pack\script\b.lua, empty folder pack\e
// -> Files == [(@"db\units_tables\a.tsv","db/units_tables/a",Table), (@"script\b.lua","script/b.lua",Raw), (@"text\x.loc.tsv","text/x.loc",Loc)]; PackFolderExists; Ignored and Problems empty

[Fact] public void Lock_hidden_and_system_files_are_ignored_and_listed()
// files: ".~lock.a.tsv#", "~$a.tsv", "a.tsv.bak", "notes.TMP", "x~", "desktop.ini" (Hidden|System), "keep.lua"
// -> Files only keep.lua; Ignored has the six others (ordinal order)

[Fact] public void A_missing_pack_folder_is_reported()            // PackFolderExists false, all lists empty
[Fact] public void A_junction_is_a_problem_and_not_followed()     // like the existing builder test; return early with a comment when no junction can be created

// ProjectBuilderTests (add)
[Fact] public void Ignored_files_are_not_packed_and_are_named_in_the_plan()
// pack\db\units_tables\my_mod.tsv (valid) + pack\db\units_tables\.~lock.my_mod.tsv# -> plan.Problems empty, plan.Entries.Count == 1,
// plan.Ignored == [@"db\units_tables\.~lock.my_mod.tsv#"]; the built pack has one entry

// ProjectFileOpenerTests (TestData-like helpers: a schema with units_tables v2: key StringU8, cost I32)
[Fact] public void Opens_a_table_tsv_as_a_decoded_table()         // Kind Table, Table.Status Ok, rows equal, EntryPath "db/units_tables/a.tsv"
[Fact] public void A_broken_tsv_gives_a_failed_table_with_the_line()  // value "many" in row 1 -> Table.Status Failed, Detail starts with "line 3:"
[Fact] public void A_table_without_schema_is_not_read()           // SchemaMissing true, Table null
[Fact] public void Opens_loc_tsv_binary_tables_text_and_other_files()
// x.loc.tsv -> Loc Ok; db\units_tables\bin (binary table bytes) -> Table Ok; script\a.lua -> Text; model.bin -> Other with sizes
[Fact] public void A_missing_file_is_unreadable()                 // ReadProblem Unreadable, ReadDetail not empty
```

- [ ] **Step 2: RED prüfen** — `dotnet test tests/TotalWarModTool.Projects.Tests` und `tests/TotalWarModTool.Workspace.Tests`: Build schlägt fehl.
- [ ] **Step 3: Implementieren.** Alle Aufrufe von `new BuildPlan(` anpassen.
- [ ] **Step 4: GREEN prüfen** — Library-Suite (Skript des Controllers): alle Projekte bestanden, 0 übersprungen, 0 Warnungen.
- [ ] **Step 5: Commit** — `feat: share the project file rules between build and explorer`

---

### Task 2: `ProgressRunner` bricht ab, wenn das Fenster nicht aufgeht

**Files:**
- Modify: `src/TotalWarModTool.App/ProgressUi/ProgressRunner.cs`
- Test: `tests/TotalWarModTool.App.Tests/ProgressRunnerTests.cs` (ergänzen)

**Interfaces:**
- Produces: unverändertes `IOperationRunner`. Neu nur für Tests: `internal Func<ProgressWindow, bool?>? ShowDialogOverride { get; set; }` (Standard `null` → `window.ShowDialog()`).

Festlegungen:
- Wirft das Anzeigen des Fortschrittsfensters eine Ausnahme (`InvalidOperationException`, `Win32Exception`, `ArgumentException`), fordert der Runner den Abbruch der Arbeit an, wartet auf ihr Ende, gibt die Eingabesperre frei und liefert `OperationStatus.Failed` mit dieser Ausnahme. Sie wird einmal geloggt und einmal über `reportError` gemeldet.
- Ignoriert die Arbeit den Abbruch und läuft normal zu Ende, bleibt das Ergebnis `Failed` (der Nutzer hat keinen Fortschritt gesehen und konnte nicht abbrechen).
- `InternalsVisibleTo` für `TotalWarModTool.App.Tests` anlegen, falls es fehlt.

- [ ] **Step 1: Failing Test schreiben**

```csharp
[Fact] public void Work_is_cancelled_when_the_window_cannot_be_shown() => Sta.RunAsync(async () =>
{
    var reported = new List<(string, Exception)>();
    var runner = NewRunner(reported, showDelay: TimeSpan.FromMilliseconds(10));   // helper of the existing tests
    runner.ShowDialogOverride = _ => throw new InvalidOperationException("no window");
    var sawCancel = false;
    var outcome = await runner.RunAsync("op", (_, ct) => { ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); sawCancel = ct.IsCancellationRequested; return 1; });
    Assert.Equal(OperationStatus.Failed, outcome.Status);
    Assert.IsType<InvalidOperationException>(outcome.Error);
    Assert.True(sawCancel);
    Assert.False(runner.IsRunning);
    Assert.Single(reported);
});
```

- [ ] **Step 2: RED prüfen** — `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~ProgressRunnerTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN prüfen** — dieselbe Testklasse ganz, dazu `FakeOperationRunnerTests`.
- [ ] **Step 5: Commit** — `fix: cancel the work when the progress window cannot be shown`

---

### Task 3: `TabSource` und Textansicht

**Files:**
- Create: `src/TotalWarModTool.App/Tabs/TabSource.cs`
- Modify: `Tabs/TabViewModels.cs`, `Tabs/TabFactory.cs`, `Tabs/TabsViewModel.cs`, `Tabs/TabsView.xaml`, `AppTexts.Workspace.cs`, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/TabsViewModelTests.cs`, `TabFactoryTests.cs`, `TabsViewTests.cs` (ergänzen)

**Interfaces:**
- Consumes: `TableTsv.Write`, `TableTsv.WriteLoc`.
- Produces:

```csharp
/// Where a tab comes from. Key identifies the source (a pack path, or "project:<guid D>"); Name is shown in the tool tip.
public sealed record TabSource(string Key, string Name)
{
    public const string ProjectPrefix = "project:";
    public static TabSource ForProject(Guid id, string name) => new(ProjectPrefix + id.ToString("D"), name);
    public static bool TryGetProjectId(string key, out Guid id);
    public static implicit operator TabSource(PackSource pack) => new(pack.Path, pack.Name);
}
```

- `TabViewModel` und alle Tab-Klassen, `TabFactory.Create`, `TabsViewModel.Add` nehmen `TabSource` statt `PackSource`; vorhandene Aufrufe mit `PackSource` kompilieren durch die implizite Umwandlung weiter. `TabViewModel.PackPath` bleibt und liefert `source.Key`.
- `TableTabViewModel` und `LocTabViewModel` bekommen:

```csharp
public bool ShowText { get; set; }        // false = grid. Raises PropertyChanged for ShowText, ShowGrid, Text, TextNotice.
public bool ShowGrid { get; }
public string? Text { get; }              // TSV form; built on the first switch to the text view, null before
public string? TextNotice { get; }        // why there is no text, or that it is cut
public const int MaxTextChars = 5_000_000;
```

Festlegungen:
- Der Text entsteht beim ersten Umschalten mit `TableTsv.Write(TableName, Version, EntryPath, Definition, Rows)` bzw. `WriteLoc(EntryPath, Rows)`. Verweigert `Write` (`Text` ist `null`), bleibt `Text` leer und `TextNotice` ist `TabTextViewUnavailable(detail)`. Ist der Text länger als `MaxTextChars`, wird am letzten Zeilenende davor gekürzt und `TextNotice` ist `TabTextViewTruncated`.
- Neue Texte: `tab.view.grid` „Grid“ / „Raster“, `tab.view.text` „Text“ / „Text“, `tab.view.unavailable` „This table cannot be shown as text: {0}“ / „Diese Tabelle lässt sich nicht als Text zeigen: {0}“, `tab.view.truncated` „Only the first part of the text is shown.“ / „Nur der Anfang des Textes wird gezeigt.“
- `TabsView.xaml`, Vorlagen für Tabelle und Loc: neben der Kopfzeile zwei `RadioButton` im Stil eines `ToggleButton` (`Style="{StaticResource {x:Type ToggleButton}}"`), gebunden an `ShowGrid` (OneWay, Klick setzt `ShowText = false`) und `ShowText`. Das Raster ist sichtbar bei `ShowGrid`, sonst ein schreibgeschütztes `TextBox` wie im Text-Tab (Consolas, kein Umbruch, beide Bildlaufleisten) mit `Text`; darüber `TextNotice` in `Theme.Warning`, zugeklappt wenn `null`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// TabsViewModelTests
[Fact] public void A_project_source_is_a_tab_source_of_its_own()
// TabSource.ForProject(id,"My Mod").Key == "project:" + id; TryGetProjectId round trips; TryGetProjectId(@"D:\a.pack") is false;
// Add(projectSource, entry, schema) then TryActivate("project:<id>", entry.EntryPath) is true; tab.ToolTip == "My Mod: <entryPath>"

[Fact] public void The_text_view_of_a_table_is_built_on_first_use()
// table with fields key(StringU8), cost(I32), rows [["a",1]]: Text is null before; ShowText = true ->
// Text == "key\tcost\n#units_tables;2;db/units_tables/x\t\na\t1\n", ShowGrid false, TextNotice null; PropertyChanged raised for the four names

[Fact] public void A_table_the_dialect_cannot_carry_has_a_notice_instead_of_text()
// fields r,g,b I32 with a channel 300 -> Text null, TextNotice contains the detail of TableTsv.Write

[Fact] public void A_long_text_is_cut_at_a_line_end()
// internal test seam: a constructor parameter or internal property MaxChars = 40 -> Text ends with '\n', length <= 40, TextNotice == texts.TabTextViewTruncated

[Fact] public void The_text_view_of_a_loc_tab_uses_the_loc_form()
// Text starts with "key\ttext\ttooltip\n#Loc;1;"

// TabsViewTests
[Fact] public void The_table_template_switches_between_grid_and_text()
// show a table tab; find TableGrid visible and the TextBox collapsed; set ShowText; pump; grid collapsed, TextBox visible with the text
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~TabsViewModelTests|FullyQualifiedName~TabFactoryTests|FullyQualifiedName~TabsViewTests|FullyQualifiedName~AppTextsTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben Klassen, dazu `DiagnosisTextsTests`, `SessionCoordinatorTests`, `MainViewModelTests`.
- [ ] **Step 5: Commit** — `feat: add a text view to table tabs and a general tab source`

---

### Task 4: `ProjectsController`, Teil 1 (Liste)

**Files:**
- Create: `src/TotalWarModTool.App/Projects/ProjectsController.cs`, `Projects/ProjectState.cs`, `AppTexts.Projects.cs`
- Modify: `TotalWarModTool.App.csproj` (Referenz `Projects`), `locales/*.json`; `tests/TotalWarModTool.App.Tests/TestGame.cs` (Helfer)
- Test: `tests/TotalWarModTool.App.Tests/ProjectsControllerTests.cs`

**Interfaces:**
- Consumes: `ProjectListStore`, `ProjectListEdits`, `ProjectFile`, `PackNameRules.Check(name, vanillaNames)`, `WorkspaceController` (`Profile`, `Location`, `Catalog`, `Schema`, `Changed`), `IOperationRunner`, `IAppLog`, `AppTexts`, `TimeProvider`.
- Produces:

```csharp
/// One project of the list as the App sees it. Project is null when the project file could not be loaded; Problem and Detail say why.
public sealed record ProjectState(Guid Id, string Folder, ModProject? Project, ProjectProblem Problem, string? Detail, InstalledPack? Installed)
{
    public string DisplayName { get; }          // Project.Name, else the folder name
    public bool IsUsable => Project is not null;
}

/// Ok false: Message is a ready UI text (problem text plus the technical detail on a second line). Ok true: Message is null or a note.
public sealed record ProjectActionResult(bool Ok, string? Message);

public sealed class ProjectsController
{
    public ProjectsController(ProjectListStore? store, ProjectListLoadResult loaded, WorkspaceController workspace,
        IOperationRunner runner, IAppLog log, AppTexts texts, TimeProvider time);

    public bool IsAvailable { get; }                         // a game profile exists (store is not null)
    public IReadOnlyList<ProjectState> Projects { get; }     // list order
    public Guid? ActiveId { get; }
    public ProjectState? Active { get; }
    public IReadOnlySet<string> VanillaPackNames { get; }    // names of the catalog packs with origin Vanilla, ignoring case; empty without a catalog
    public event EventHandler? Changed;

    public ProjectActionResult Create(string folder, string name, string packName);
    public ProjectActionResult AddExisting(string folder);
    public ProjectActionResult Remove(Guid id);
    public ProjectActionResult SetActive(Guid id);
    /// Reads every project file again (F5).
    public void Reload();
    /// Null when the pack name may be used for a project of this game; else a UI text.
    public string? CheckPackName(string packName);
}
```

Festlegungen:
- Konstruktor: lädt für jeden Listeneintrag `ProjectFile.Load`. Ein Projekt, dessen `GameId` nicht `workspace.Profile.Id` gleicht (ohne Rücksicht auf Groß-/Kleinschreibung), gilt als nicht nutzbar (`Problem = Invalid`, Detail `the project belongs to the game '<id>'`). Lädt nichts von einem Pool-Thread; die Dateien sind klein.
- Speichern: nach jeder Änderung `store.Save`. Bei `loaded.Status` `Unreadable` oder `NewerVersion` wird nie gespeichert (eine Log-Warnung beim Start). `IOException`/`UnauthorizedAccessException` beim Speichern: Log-Warnung, die Änderung bleibt im Speicher, das Ergebnis trägt `Ok = true` und als `Message` den Hinweis `ProjectListNotSaved`.
- `Create`: `CheckPackName` → Meldung; Ordner relativ → Meldung; `ProjectFile.CreateNew(folder, name, profile.Id, packName, [])`; Problem → Meldung aus `ProjectProblemText` plus Detail; `ProjectListEdits.Add`; speichern; `Changed`.
- `AddExisting`: `ProjectFile.Load`; Problem → Meldung; anderes Spiel → Meldung `ProjectOtherGame`; `Add` mit `DuplicateId` → `ProjectAlreadyInList` (nennt den Ordner des vorhandenen Eintrags), `DuplicatePath` ebenso.
- `Remove` streicht nur den Eintrag; der Ordner bleibt. `SetActive`, `Remove` mit unbekannter GUID: `Ok = false`.
- `CheckPackName`: `PackNameRules.Check(packName, VanillaPackNames)`; ein Grund wird als `ProjectPackNameInvalid(reason)` gemeldet.
- `workspace.Changed` → `Changed` weiterreichen, wenn sich `VanillaPackNames` geändert hat.
- Neue Texte (`AppTexts.Projects.cs`, Keys unter `project.*`): je eine Property pro Wert von `ProjectProblem` (außer `None`) über `ProjectProblemText(ProjectProblem)`; `ProjectOtherGame`, `ProjectAlreadyInList(folder)`, `ProjectPackNameInvalid(reason)`, `ProjectFolderNotAbsolute`, `ProjectListNotSaved`, `ProjectUnknown`. Deutsch und Englisch; `DiagnosisTextsTests` um `ProjectProblem` ergänzen.
- `TestGame` bekommt `ProjectsController NewProjects(WorkspaceController workspace, IOperationRunner? runner = null)` (Store unter `Paths.RoamingRoot`, Profil `testgame`) und `string NewFolder(string name)` (leerer Unterordner im TempDir).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Create_adds_an_active_project_and_saves_the_list()
// Create(folder,"My Mod","my_mod.pack") Ok; Projects.Count 1; Active.DisplayName "My Mod"; Changed raised once;
// a second controller over the same store shows the same project

[Fact] public void Create_refuses_a_vanilla_or_invalid_pack_name_and_a_relative_folder()
// the test game holds a vanilla pack "data.pack" (pack type release, which the test profile counts as vanilla; extend TestGame.AddPack or add a helper
// if it cannot write that type yet); Create(.., "DATA.pack") -> Ok false, message == texts.ProjectPackNameInvalid(reason); nothing created;
// "nope" -> Ok false; folder "relative\x" -> Ok false

[Fact] public void AddExisting_rejects_broken_foreign_and_duplicate_projects()
// broken json -> Ok false, message contains texts.ProjectProblemText(Corrupt); project with game "other" -> texts.ProjectOtherGame;
// same folder twice -> texts.ProjectAlreadyInList(folder)

[Fact] public void A_missing_or_broken_project_stays_in_the_list_with_its_problem()
// list file with two entries: one folder deleted, one with a broken project file; a third valid.
// controller: 3 Projects; [0].Problem NotFound, IsUsable false, DisplayName == folder name; [1].Problem Corrupt; [2].IsUsable; no exception; nothing written to the broken file

[Fact] public void Remove_keeps_the_folder_and_SetActive_moves_the_mark()
[Fact] public void A_list_that_must_not_be_saved_is_never_written()
// loaded.Status NewerVersion -> Create is Ok, file on disk unchanged, one warning in the log
[Fact] public void Reload_picks_up_a_changed_project_file()   // name changed on disk -> DisplayName changes, Changed raised
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~ProjectsControllerTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — `ProjectsControllerTests`, `AppTextsTests`, `DiagnosisTextsTests`.
- [ ] **Step 5: Commit** — `feat: keep the project list of a game in the app`

---

### Task 5: `ProjectsController`, Teil 2 (Abläufe)

**Files:**
- Modify: `Projects/ProjectsController.cs`, `Projects/ProjectState.cs`, `AppTexts.Projects.cs`, `locales/*.json`
- Test: `tests/TotalWarModTool.App.Tests/ProjectsControllerOperationsTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ProjectFiles.List`, `ProjectFileOpener.Open`), Task 4; `PackReader.ReadIndex`, `PackImporter.Preview/Import`, `ProjectBuilder.ComputeBuild/WriteBuild`, `PackInstaller.Install/Uninstall`.
- Produces (alle `Task`-Methoden: `null`, wenn keine Operation gestartet werden kann oder der Nutzer abbricht):

```csharp
public sealed record ImportPreviewResult(ImportPreview? Preview, string? Message);            // Message set when the pack cannot be read
public sealed record ImportOutcome(bool Ok, string? Message, ImportReport? Report, Guid? ProjectId);
public sealed record BuildReport(bool Built, bool Installed, string? PackPath, IReadOnlyList<BuildProblem> Problems,
    IReadOnlyList<string> Ignored, string? Message);

public bool CanStartOperation { get; }                         // workspace.CanStartOperation
public string? InstallDisabledReason { get; }                  // UI text; null when installing is possible
public Task<ImportPreviewResult?> PreviewImportAsync(string sourcePack);
public Task<ImportOutcome?> ImportAsync(string sourcePack, string targetFolder, string name, string packName);
public Task<BuildReport?> BuildAsync(Guid id, bool install);
public Task<ProjectActionResult?> UninstallAsync(Guid id);
public Task<ProjectFileList?> ListFilesAsync(Guid id);         // through the runner (user expands a project)
public Task<ProjectFileList?> ListFilesSilentlyAsync(Guid id); // pool thread, no runner (automatic refresh)
public Task<OpenedEntry?> OpenFileAsync(Guid id, string relativeFile);
```

Festlegungen:
- Titel der Läufe (neue Texte `operation.*`): `OperationReadPack(name)`, `OperationImportPack(name)`, `OperationBuild(projectName)`, `OperationBuildAndInstall(projectName)`, `OperationUninstall(projectName)`, `OperationOpenProject(projectName)`, `OperationOpenFile(fileName)`.
- `ImportAsync`: `ImportRequest` mit `workspace.Profile.Id` und `VanillaPackNames`, Schema aus `workspace.Schema?.Schema`. Erfolg: `ProjectListEdits.Add`, speichern, `Changed`; `Message` fasst zusammen (`ImportSummary(tables, locFiles, otherFiles)`), und nennt, falls vorhanden, die Zahlen binär gebliebener, ausgelassener und verworfener Einträge. Problem: `Ok = false`, Meldung aus `ImportProblemText` plus Detail. Abbruch: Ergebnis mit `Ok = false`; existiert danach ein Ordner `<Ziel>.import-????????` im Elternordner, nennt `Message` ihn (`ImportLeftover(path)`), sonst `ImportCancelled`. Die Projektliste bleibt bei Problem und Abbruch unverändert.
- `BuildAsync`: Projekt nicht nutzbar → `Message` mit seinem Problem. Ein Lauf: `ComputeBuild` (GUID aus `Guid.NewGuid().ToString()`), bei Problemen Ende mit `Built = false` und der Problemliste; `WriteBuild` mit `time.GetUtcNow()`; `WriteFailed` → `Message` mit Detail. Mit `install`:
  - `InstallDisabledReason` nicht `null` → nichts wird gebaut, `Message` ist der Grund.
  - Trägt die Liste ein installiertes Pack mit anderem Dateinamen als `Project.PackName`, wird zuerst unter dem alten Namen deinstalliert; scheitert das, Ende mit Meldung (das neue Pack ist gebaut, nicht installiert).
  - `PackInstaller.Install`; Erfolg → `SetInstalled`, speichern; Problem → Meldung aus `InstallProblemText` plus Detail, `Installed = false`.
  - `Message` bei Erfolg: `BuildDone(packName, entries)` bzw. `BuildInstalled(packName)`; mit ignorierten Dateien zusätzlich `BuildIgnored(count)`.
- `InstallDisabledReason`: kein Spielordner (`workspace.Location` ist `null`) → `InstallNoGameFolder`.
- `UninstallAsync`: `PackInstaller.Uninstall` mit dem gemerkten Namen; Erfolg → `SetInstalled(null)`, speichern.
- `ListFilesAsync`/`ListFilesSilentlyAsync`: `ProjectFiles.List`. `OpenFileAsync`: `ProjectFileOpener.Open` mit dem aktuellen Schema.
- Ausnahmen der Library (`ArgumentException` bei Programmierfehlern) gehen an den Runner und werden dort gemeldet.
- Texte: `ImportProblemText(ImportProblem)`, `InstallProblemText(InstallProblem)` mit je einer Property pro Wert; `DiagnosisTextsTests` ergänzen.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Import_creates_a_project_adds_it_to_the_list_and_reports_counts()
// source pack (PackBuilder in TempDir) with a table, a loc, a lua, an unsafe path; PreviewImportAsync -> counts and 1 skipped;
// ImportAsync -> Ok, Report.Tables 1, ProjectId in Projects, Message contains texts.ImportSummary(1,1,1); runner title == texts.OperationImportPack(...)

[Fact] public void Import_problems_leave_the_list_unchanged()
// vanilla pack name -> Ok false with texts.ImportProblemText(InvalidPackName); non-empty target -> TargetNotEmpty; Projects.Count 0

[Fact] public void A_cancelled_import_changes_nothing_and_names_a_leftover_staging_folder()
// runner.CancelNext = true -> Ok false, Message == texts.ImportCancelled, Projects empty, no project folder;
// second case: create "<target>.import-0123abcd" by hand before the cancelled run -> Message == texts.ImportLeftover(thatFolder)

[Fact] public void Build_writes_the_pack_and_reports_ignored_files()
// project with a valid tsv and a lock file -> Built, PackPath exists, Ignored.Count 1, Message contains texts.BuildDone("my_mod.pack", 1)

[Fact] public void Build_problems_are_returned_and_nothing_is_written()   // tsv with a bad value -> Built false, Problems[0].Line == 3, no build folder

[Fact] public void Build_and_install_copies_the_pack_and_remembers_the_fingerprint()
// game found (TestGame): BuildAsync(id, install: true) -> Installed; file in game.DataPath; Projects[0].Installed not null; a new controller over the same store still owns it
// second BuildAsync(install) replaces it (ownership holds)

[Fact] public void A_foreign_pack_in_the_data_folder_is_never_replaced()
// write "my_mod.pack" into DataPath by hand -> Installed false, Message contains texts.InstallProblemText(ForeignFile), bytes untouched

[Fact] public void Install_without_a_game_folder_is_refused_with_a_reason()
// controller over a workspace without a game: InstallDisabledReason == texts.InstallNoGameFolder; BuildAsync(id, true) -> Built false, Message == that reason

[Fact] public void A_changed_pack_name_uninstalls_the_old_pack_first()
// install as a.pack; change PackName to b.pack on disk; Reload; BuildAsync(install) -> a.pack gone, b.pack present, Installed.FileName "b.pack"

[Fact] public void Uninstall_removes_the_pack_and_the_record()
[Fact] public void ListFiles_and_OpenFile_serve_the_explorer()
// ListFilesAsync -> Files as on disk; OpenFileAsync(id, @"db\units_tables\a.tsv") -> Kind Table, Status Ok
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~ProjectsControllerOperationsTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — beide Controller-Testklassen, `AppTextsTests`, `DiagnosisTextsTests`.
- [ ] **Step 5: Commit** — `feat: run import, build and install from the app`

---

### Task 6: Dialoge und `ProjectsViewModel`

**Files:**
- Create: `Projects/IProjectDialogs.cs`, `Projects/ProjectsViewModel.cs`, `Projects/ProjectInputViewModel.cs`, `Projects/ProjectInputWindow.xaml(.cs)`, `Projects/ReportWindow.xaml(.cs)`, `Projects/ProjectDialogs.cs`, `Projects/DisplayText.cs`
- Modify: `AppTexts.Projects.cs`, `locales/*.json`
- Test: `tests/TotalWarModTool.App.Tests/ProjectsViewModelTests.cs`, `ProjectInputViewModelTests.cs`, `ProjectWindowsTests.cs`, `FakeProjectDialogs.cs`

**Interfaces:**
- Consumes: Task 4, Task 5.
- Produces:

```csharp
public sealed record ProjectInput(string Name, string Folder, string PackName);
public sealed record ReportSection(string Heading, IReadOnlyList<string> Lines);
public sealed record Report(string Title, string Summary, IReadOnlyList<ReportSection> Sections);

/// Everything the project commands ask the user. The fake in the tests records the calls and returns prepared answers.
public interface IProjectDialogs
{
    string? PickFolder(string title);
    string? PickPackFile();
    /// info: lines shown above the fields (the import preview), empty for a new project. Null when the user cancels.
    ProjectInput? AskProject(string title, ProjectInput defaults, IReadOnlyList<string> info, Func<ProjectInput, string?> validate);
    void ShowReport(Report report);
    void ShowMessage(string title, string message);
}

public sealed class ProjectsViewModel : ObservableObject
{
    public ProjectsViewModel(ProjectsController projects, IProjectDialogs dialogs, AppTexts texts);
    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand ImportPackCommand { get; }                 // asks for a pack file
    public ICommand BuildActiveCommand { get; }
    public ICommand BuildAndInstallActiveCommand { get; }
    public string? StatusMessage { get; }                      // result of the last command, shown in the status bar
    public Task ImportAsync(string sourcePack, string sourceName);   // also used by the pack node
    public Task BuildAsync(Guid id, bool install);
    public Task UninstallAsync(Guid id);
    public void SetActive(Guid id);
    public void Remove(Guid id);
    public string? BuildDisabledReason(Guid? id);              // null when the command may run
    public string? InstallDisabledReason(Guid? id);
    public string? UninstallDisabledReason(Guid id);
}

public static class DisplayText
{
    /// Control characters and unpaired surrogates as \uXXXX, at most maxChars characters (cut marked with " (shortened)").
    public static string Safe(string text, int maxChars = 300);
}
```

Festlegungen:
- `NewProjectCommand`: `AskProject` mit Titel `ProjectNewTitle`, Vorgaben (leerer Name, leerer Ordner, leerer Pack-Name), `validate` prüft: Name nicht leer, Ordner absolut, `CheckPackName`. Antwort → `Create`; `Ok = false` → `ShowMessage`. Erfolg → `StatusMessage = ProjectCreated(name)`.
- `OpenProjectCommand`: `PickFolder` → `AddExisting`; Problem → `ShowMessage`.
- `ImportAsync`: `PreviewImportAsync`; Meldung → `ShowMessage`. Sonst `AskProject` mit Titel `ImportTitle(sourceName)`, Vorgaben: Name = Dateiname ohne Endung, Pack-Name = Dateiname der Quelle, Ordner leer; `info` = Zeilen `ImportPreviewLine*` (Tabellen, Loc, andere, Größe, Zahl ausgelassener). Antwort → `projects.ImportAsync`. Problem → `ShowMessage`. Erfolg: `StatusMessage`; gibt es binär gebliebene, ausgelassene oder verworfene Einträge, zusätzlich `ShowReport` mit je einem Abschnitt (Pfad über `DisplayText.Safe`, dann `: ` und der Grund).
- `BuildAsync`: `BuildReport`; Probleme → `ShowReport` (Abschnitt `BuildProblemsHeading`, Zeile `<Datei>(<Zeile>): <Detail>`, ohne Zeile bei 0; Abschnitt `BuildIgnoredHeading` mit den ignorierten Dateien, wenn vorhanden); sonst `StatusMessage = report.Message`; `Message` bei `Built = false` ohne Probleme → `ShowMessage`.
- `BuildActiveCommand`/`BuildAndInstallActiveCommand`: `CanExecute` nur, wenn der passende `…DisabledReason(ActiveId)` `null` ist. Gründe: kein aktives Projekt (`ProjectNoneActive`), Projekt nicht nutzbar (sein Problemtext), laufende Operation (kein Grund-Text, nur deaktiviert), Installieren: `projects.InstallDisabledReason`. `UninstallDisabledReason`: nichts installiert → `UninstallNothing`.
- `Remove`: ohne Nachfrage (reversibel: der Ordner bleibt; „Projekt öffnen“ holt es zurück).
- `ProjectInputViewModel`: `Name`, `Folder`, `PackName` (sofortige Prüfung über `validate`), `Error` (Text oder `null`), `CanAccept`, `BrowseFolderCommand` (Delegate `Func<string?>`), `Info`-Zeilen. Solange der Nutzer `PackName` nicht selbst geändert hat, folgt er dem Namen: Kleinbuchstaben, Leerzeichen zu `_`, nur `a-z 0-9 _ -`, plus `.pack`.
- `ProjectInputWindow`: modal, `Owner` = Hauptfenster, `CenterOwner`, `ShowInTaskbar=False`; Felder mit Labels, Fehlerzeile in `Theme.Warning`, Knöpfe OK (`IsDefault`, gebunden an `CanAccept`) und Abbrechen (`IsCancel`). `ReportWindow`: modal, Titel, Zusammenfassung, scrollbare Abschnitte mit markierbarem Text (schreibgeschütztes `TextBox`), Knopf Schließen. Knopfmaße wie `CrashDialog`.
- `ProjectDialogs` (WPF): `OpenFolderDialog`, `OpenFileDialog` (Filter `*.pack`), die zwei Fenster, `MessageBox` für `ShowMessage`. Nur Verdrahtung, ohne Unit-Test.
- Texte unter `project.*`, `import.*`, `build.*`, `install.*`, `dialog.*`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// ProjectsViewModelTests (FakeProjectDialogs: queues for answers, lists of calls)
[Fact] public void New_project_asks_validates_and_creates()
// dialogs.ProjectAnswer = ("My Mod", folder, "my_mod.pack") -> project in the list, StatusMessage == texts.ProjectCreated("My Mod");
// the validate function given to the dialog returns a text for ("", folder, "x.pack"), for a relative folder and for a vanilla name, and null for the answer

[Fact] public void A_cancelled_dialog_changes_nothing()
[Fact] public void Open_project_shows_a_message_for_a_broken_folder()
[Fact] public void Import_shows_the_preview_and_reports_entries_that_stayed_binary_or_were_skipped()
// AskProject info contains texts.ImportPreviewLineTables(1); defaults: name "source", pack name "source.pack";
// after the import ShowReport was called once with a section whose line starts with the escaped unsafe path

[Fact] public void A_hostile_path_is_shown_escaped()               // DisplayText.Safe("a\u0007b\uD800") == @"a\u0007b\uD800"; long text is cut with " (shortened)"
[Fact] public void Build_problems_open_a_report_with_file_and_line()   // line == @"db\units_tables\my_mod.tsv(3): ..."
[Fact] public void A_successful_build_only_sets_the_status_message()
[Fact] public void Commands_for_the_active_project_are_disabled_with_a_reason()
// no project: BuildDisabledReason(null) == texts.ProjectNoneActive, BuildActiveCommand.CanExecute false;
// broken active project: reason is its problem text; no game: InstallDisabledReason == texts.InstallNoGameFolder; nothing installed: UninstallDisabledReason == texts.UninstallNothing

// ProjectInputViewModelTests
[Fact] public void The_pack_name_follows_the_name_until_the_user_edits_it()   // "My Mod 2" -> "my_mod_2.pack"; after PackName = "x.pack", a name change keeps "x.pack"
[Fact] public void The_error_text_comes_from_the_validation_and_blocks_accept()

// ProjectWindowsTests (Sta)
[Fact] public void The_input_window_binds_fields_error_and_buttons()
[Fact] public void The_report_window_shows_summary_and_sections()
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~ProjectsViewModelTests|FullyQualifiedName~ProjectInputViewModelTests|FullyQualifiedName~ProjectWindowsTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben Klassen, `AppTextsTests`, `ControlTemplatesTests`, `ThemeTests`.
- [ ] **Step 5: Commit** — `feat: add project commands and dialogs`

---

### Task 7: Explorer, Menü, Verdrahtung

**Files:**
- Create: `Explorer/ProjectNodes.cs`, `Explorer/NodeAction.cs`
- Modify: `Explorer/ExplorerNodes.cs`, `Explorer/ExplorerViewModel.cs`, `Explorer/ExplorerView.xaml(.cs)`, `MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`, `AppTexts.Projects.cs`, `locales/*.json`, `tests/TotalWarModTool.App.Tests/TestGame.cs`
- Test: `ExplorerViewModelTests.cs`, `ExplorerViewTests.cs`, `MainViewModelTests.cs`, `MainWindowTests.cs` (ergänzen und anpassen)

**Interfaces:**
- Consumes: Task 3 (`TabSource`), Task 4 bis 6.
- Produces:

```csharp
/// One entry of a node's context menu. DisabledReason is shown as the tool tip of the disabled item.
public sealed record NodeAction(string Header, ICommand Command, string? DisabledReason);

public abstract class ExplorerNode   // additions
{
    public IReadOnlyList<NodeAction> Actions { get; protected set; }   // empty by default
    public bool HasActions { get; }
    public bool IsEmphasized { get; protected set; }                   // the active project: shown bold
}

public sealed class ProjectsGroupNode : ExplorerNode { }               // "Projects (n)"
public sealed class ProjectNode : ExplorerNode { public Guid ProjectId { get; } }
public sealed class ProjectFolderNode : ExplorerNode { }
public sealed class ProjectFileNode : ExplorerNode { public Guid ProjectId { get; } public string RelativeFile { get; } public ICommand OpenCommand { get; } }

// ExplorerViewModel: new optional constructor parameters, the old call stays valid
public ExplorerViewModel(WorkspaceController workspace, AppTexts texts, Func<PackSource, string, Task> openEntry, Action openSettings,
    ProjectsController? projects = null, ProjectsViewModel? projectCommands = null, Func<Guid, string, Task>? openProjectFile = null);
public IReadOnlyList<Guid> ExpandedProjectIds { get; }
public void ApplyRestoredProjects(IReadOnlyList<(Guid Id, ProjectFileList Files)> projects);

// MainViewModel: new optional constructor parameters after openGameSettings
ProjectsController? projects = null, ProjectsViewModel? projectCommands = null
public ProjectsViewModel? Projects { get; }
public Task OpenProjectFileAsync(Guid projectId, string relativeFile);
```

Festlegungen:
- **Sichtbarkeit.** Die Gruppe „Projekte“ steht als erstes Element in `Groups`, sobald `projects.IsAvailable`, auch mit 0 Projekten und auch dann, wenn das Spiel nicht gefunden ist. `ShowTree` ist wahr, wenn `Groups` nicht leer ist. Die Meldung (`Message`) erscheint weiter, jetzt über dem Baum statt an seiner Stelle (`ExplorerView.xaml`: `DockPanel`, Meldung und Knopf oben, Baum füllt den Rest). Ohne Profil bleibt alles wie bisher. Die betroffenen vorhandenen Tests werden auf diese Regel umgestellt; welche es waren, steht im Bericht.
- **Projektknoten.** Name = `DisplayName`; aktives Projekt `IsEmphasized`; ToolTip = Ordner; nicht nutzbar → `HasProblem`, ToolTip = Problemtext plus Detail, nicht aufklappbar. Aufklappen lädt über `projects.ListFilesAsync` und baut Ordner- und Dateiknoten aus `Files` (Ordner zuerst, dann Dateien, je ordinal ohne Rücksicht auf Groß-/Kleinschreibung); Listen-Probleme hängen als Knoten mit `HasProblem` am Projekt (Name = Pfad, ToolTip = Detail). Doppelklick oder Enter auf einer Datei ruft `openProjectFile(id, relativeFile)`.
- **Abgleich statt Neuaufbau.** Bei `projects.Changed` werden Projektknoten nach GUID abgeglichen: vorhandene behalten ihren aufgeklappten Zustand und ihre Kinder, Name, Hervorhebung, Problem und Aktionen werden aktualisiert, neue kommen dazu, fehlende entfallen. Nach einem erfolgreichen Bau, Import oder F5 liest der Explorer aufgeklappte Projekte still neu (`ListFilesSilentlyAsync`) und ersetzt deren Kinder; aufgeklappte Unterordner bleiben nach Pfad aufgeklappt.
- **Aktionen.** Projekt: Als aktiv setzen, Bauen, Bauen und installieren, Deinstallieren, Aus der Liste entfernen. Pack-Knoten (jede Herkunft, sofern `CanExpand` oder ohne Problem): Als Projekt importieren (`projectCommands.ImportAsync(pack.Path, pack.Name)`). `DisabledReason` aus `ProjectsViewModel`. Die Aktionen werden bei Sprachwechsel, bei `projects.Changed` und bei `workspace.Changed` neu gebaut.
- **Kontextmenü.** In der gemeinsamen Vorlage: `ContextMenu` am Zeilen-Panel, `ItemsSource="{Binding Actions}"`, je Eintrag `Header`, `Command`, `ToolTip="{Binding DisabledReason}"`, `ToolTipService.ShowOnDisabled="True"`; ohne Aktionen kein Menü (`Visibility` über `HasActions`). Fett über `DataTrigger` auf `IsEmphasized`.
- **Tabs für Projektdateien.** `MainViewModel.OpenProjectFileAsync`: `Tabs.TryActivate("project:<id>", entryPath)`, sonst `projects.OpenFileAsync` und `Tabs.Add(TabSource.ForProject(id, displayName), opened, workspace.Schema)`. `entryPath` ist der relative Pfad mit `/`.
- **Menü.** Neues Menü `MenuProject` („Project“ / „Projekt“) als zweites Menü nach Werkzeuge: Neues Projekt, Projekt öffnen, Pack importieren, Trenner, Bauen (`Ctrl+B`), Bauen und installieren (`Ctrl+Shift+B`), mit `InputGestureText`. Zwei `KeyBinding` am Fenster. Das Menü ist nur sichtbar, wenn `Projects` nicht `null` ist. Der vorhandene Test für das Menü Werkzeuge bleibt unverändert grün.
- **Statuszeile.** Neues `StatusBarItem` mit `Projects.StatusMessage`, nach dem Hinweis.
- **F5.** `RefreshCommand` ruft nach `workspace.RefreshAsync()` `projects.Reload()`.
- **Composition Root.** `ProjectListStore` und `Load` neben der Session (nur mit Profil); Warnung über `SettingsLoadMessage.Build(…, "Project list")` bei `Corrupt`, `NewerVersion`, `Unreadable`. `ProjectsController`, `ProjectDialogs` (Owner = Hauptfenster), `ProjectsViewModel` entstehen vor `MainViewModel` und werden übergeben.
- `TestGame.NewMain(...)`-Helfer für `MainViewModel` mit Projekten und `FakeProjectDialogs`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// ExplorerViewModelTests
[Fact] public void The_projects_group_comes_first_and_lists_the_projects()
// two projects, second active -> Groups[0] is ProjectsGroupNode named texts.ExplorerProjectsGroup(2); children names; [1].IsEmphasized

[Fact] public void Projects_are_shown_while_the_game_is_not_found()
// workspace without game + one project -> Message == texts.ExplorerGameNotFound, ShowTree true, Groups.Count == 1

[Fact] public void A_broken_project_node_shows_its_problem_and_disables_its_actions()
// HasProblem, ToolTip contains texts.ProjectProblemText(Corrupt); Actions: build disabled with that reason; "remove from list" enabled

[Fact] public void Expanding_a_project_shows_folders_before_files()
// pack\db\units_tables\a.tsv, pack\b.lua -> children [db (folder), b.lua]; runner title == texts.OperationOpenProject(name)

[Fact] public void Opening_a_project_file_calls_the_delegate_with_id_and_relative_file()
[Fact] public void A_change_of_the_list_keeps_expanded_projects_and_updates_names()
// expand project A; SetActive(B); rename A on disk + Reload -> A still expanded with its children, new name, B emphasized

[Fact] public void A_pack_node_offers_import_and_a_project_node_its_five_actions()
// headers equal the texts in order; uninstall DisabledReason == texts.UninstallNothing

// ExplorerViewTests
[Fact] public void The_message_is_shown_above_the_tree()                    // both visible when Message and Groups exist
[Fact] public void The_context_menu_lists_the_actions_and_marks_the_active_project()

// MainViewModelTests / MainWindowTests
[Fact] public void A_project_file_opens_in_a_tab_once()                     // second call activates; tab.PackPath starts with "project:"
[Fact] public void Refresh_reloads_the_projects()
[Fact] public void The_project_menu_is_second_and_binds_the_commands()      // headers in order incl. separator; Ctrl+B and Ctrl+Shift+B bound once each
[Fact] public void The_status_bar_shows_the_last_project_message()
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~ExplorerViewModelTests|FullyQualifiedName~ExplorerViewTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~MainWindowTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben Klassen, `SessionCoordinatorTests`, `AppTextsTests`, `ControlTemplatesTests`, `AppOutputTests`.
- [ ] **Step 5: Commit** — `feat: show projects in the explorer with their commands`

---

### Task 8: Session für Projekte

**Files:**
- Modify: `SessionCoordinator.cs`, `Projects/ProjectsController.cs`, `App.xaml.cs`
- Test: `SessionCoordinatorTests.cs`, `ProjectsControllerOperationsTests.cs` (ergänzen)

**Interfaces:**
- Consumes: `SessionState`, `SessionTab` (unverändert; ein Projekt-Tab ist `SessionTab("project:<guid D>", <relativer Pfad mit />)`, ein aufgeklapptes Projekt steht als `project:<guid D>` in `ExpandedPacks`), `TabSource.TryGetProjectId`, Task 7 (`ExpandedProjectIds`, `ApplyRestoredProjects`).
- Produces:

```csharp
public sealed record RestoredProjectTab(Guid ProjectId, string Name, OpenedEntry Entry);
public sealed record ProjectRestoreResult(IReadOnlyList<(Guid Id, ProjectFileList Files)> Expanded, IReadOnlyList<RestoredProjectTab> Tabs);

// ProjectsController
/// Silent, on a pool thread. Unknown or unusable projects and unreadable files are left out.
public Task<ProjectRestoreResult> RestoreAsync(IReadOnlyList<Guid> expanded, IReadOnlyList<(Guid ProjectId, string RelativeFile)> tabs);

// SessionCoordinator: new optional last constructor parameter
ProjectsController? projects = null
```

Festlegungen:
- `Capture`: Tabs wie bisher (`PackPath` trägt den Schlüssel der Quelle); `ExpandedPacks` = aufgeklappte Packs, danach `project:<guid>` je aufgeklapptem Projekt.
- `RestoreAsync` teilt den gespeicherten Zustand: Pack-Tabs und Pack-Pfade gehen wie bisher an `workspace.RestoreAsync` (mit einem `SessionState`, der nur sie enthält), Projekt-Tabs und Projekt-Schlüssel an `projects.RestoreAsync`. Danach werden die Tabs in der gespeicherten Reihenfolge angelegt: für jeden gespeicherten Tab der wiederhergestellte mit gleichem Schlüssel und Pfad (ohne Rücksicht auf Groß-/Kleinschreibung); was fehlt, entfällt. Der aktive Tab ist der, dessen gespeicherter Eintrag aktiv war; fehlt er, der erste.
- Ein Projekt-Tab, dessen Datei fehlt (`ReadProblem = Unreadable`), entfällt. Ein Tab mit Dekodierproblem wird als Hinweis-Tab wiederhergestellt.
- Ohne `projects` verhält sich der Coordinator wie bisher; Projekt-Einträge im Zustand entfallen dann.
- Ein Projekt-Tab wird nur für Tabellen mit Schema dekodiert; das Schema ist das des Controllers zum Zeitpunkt des Wiederherstellens.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Project_tabs_and_expanded_projects_are_captured()
// open a pack entry and a project file, expand the project -> Capture().Tabs == [(packPath, entry), ("project:<id>", "db/units_tables/a.tsv")],
// ExpandedPacks ends with "project:<id>"

[Fact] public void Project_tabs_are_restored_in_their_old_order_and_missing_ones_are_dropped()
// saved: [project file A, pack entry, project file B (file deleted), file of an unknown project], active index 2 (the deleted one)
// -> tabs after restore: [A, pack entry]; selected is the first; project expanded with its children; no exception; Dirty raised once

[Fact] public void Without_a_projects_controller_project_entries_are_ignored()
[Fact] public void RestoreAsync_of_the_controller_skips_unusable_projects_and_unreadable_files()
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~SessionCoordinatorTests|FullyQualifiedName~ProjectsControllerOperationsTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben Klassen, `MainViewModelTests`.
- [ ] **Step 5: Commit** — `feat: restore project tabs and expanded projects after a restart`

---

### Task 9: Doku

**Files:**
- Modify: `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`, `Backlog.txt`, `CHANGELOG.md`

- [ ] **Step 1: Spec.** In 5.4 ein Unterabschnitt „Festlegungen aus Plan 3“:
  1. `ProjectsController` und `ProjectsViewModel` liegen in der App; der Controller liefert fertige Meldungstexte.
  2. Welche Dateien zu einem Projekt gehören, entscheidet `ProjectFiles.List` für Bau und Explorer gemeinsam. Versteckte Dateien, Systemdateien und Sperr- oder Sicherungsdateien von Editoren (`.~lock.*`, `~$*`, `*.bak`, `*.tmp`, `*~`) gehen nie ins Pack; der Bau nennt sie.
  3. Tabs kennen eine allgemeine Quelle. Ein Projekt-Tab hat den Schlüssel `project:<GUID>` und den relativen Dateipfad; so steht er auch in der Session, ohne neues Feld.
  4. Die Gruppe „Projekte“ ist sichtbar, sobald ein Spielprofil existiert, auch wenn das Spiel nicht gefunden ist. Die Meldung des Explorers steht dann über dem Baum.
  5. Die Vanilla-Packnamen für die Namensprüfung stammen aus dem Katalog (Herkunft Spiel). Ist das Spiel nicht gefunden, prüft erst das Installieren.
  6. Wird der Pack-Name eines installierten Projekts geändert, deinstalliert „Bauen und installieren“ zuerst das alte Pack.
  7. Offene Projekt-Tabs zeigen den Stand beim Öffnen. Eine Markierung für außen geänderte Dateien kommt mit Plan 4.
  8. Die Textansicht entsteht beim ersten Umschalten, höchstens 5 Mio. Zeichen.
  9. `ProgressRunner` bricht die Arbeit ab, wenn das Fortschrittsfenster nicht geöffnet werden kann.
  Dazu 5.9 angleichen: Speichern, Alle speichern, Rückgängig, Wiederholen und „Ins Projekt übernehmen“ ausdrücklich Plan 4 zuordnen.
- [ ] **Step 2: Backlog.** Streichen: die Zeile zum `ProgressRunner`-Abbruch („Aus Plan 1“) und die Zeile zu versteckten Dateien und Sperrdateien („Aus Teil 2“; der Teil „Rohdatei unter `db\<tabelle>\`, die sich nicht dekodieren lässt, als Warnung“ bleibt als eigene Zeile). Ergänzen: Explorer zeigt ignorierte Dateien nicht; Projekt-Tabs ohne Markierung bei Änderung von außen (Plan 4); Bau- und Import-Meldungen der Library sind nicht übersetzt; Fortschritt beim Installieren unbestimmt; Dialog „Neues Projekt“ merkt sich den letzten Elternordner nicht; Pack-Name und Abhängigkeiten eines Projekts lassen sich nur in `twmt-project.json` ändern.
- [ ] **Step 3: Changelog,** Abschnitt „Unreleased / Neu“, aus Nutzersicht: Mod-Projekte (anlegen, öffnen, Liste, aktives Projekt); Pack als Projekt importieren mit Vorschau und Bericht; Bauen und „Bauen und installieren“ mit Schutz fremder Dateien, Deinstallieren; Projektdateien in Tabs; Textansicht für Tabellen und Loc-Dateien aus jeder Quelle; Wiederherstellen nach Neustart.
- [ ] **Step 4: Commit** — `docs: record plan 3 in the spec, backlog and changelog`

---

## Manuelle Abnahme durch den Nutzer

Vorbereitung: App starten, Spiel wird gefunden. Ein leerer Ordner für Tests, z. B. `D:\Modding\TWMT-Test`.

1. Explorer zeigt oben die Gruppe „Projekte (0)“, darunter die Packs wie bisher.
2. Menü Projekt, Neues Projekt: Name „Test Mod“ eintippen, Pack-Name füllt sich mit `test_mod.pack`. Ordner wählen (`…\TWMT-Test\test-mod`). OK. Das Projekt erscheint fett (aktiv). Statuszeile meldet das Anlegen.
3. Neues Projekt mit Pack-Name `data.pack`: OK bleibt gesperrt, die Fehlerzeile nennt den Grund.
4. Rechtsklick auf ein kleines Workshop-Pack, „Als Projekt importieren“: Vorschau zeigt Zahlen. Ordner wählen, OK. Fortschritt läuft, Projekt erscheint. Gibt es binär gebliebene oder ausgelassene Einträge, öffnet sich ein Bericht.
5. Importiertes Projekt aufklappen: Ordner und Dateien wie im Pack, Tabellen als `.tsv`.
6. Doppelklick auf eine `.tsv`-Tabelle: Tab mit Raster. Umschalter „Text“ zeigt die TSV-Form, Text lässt sich markieren und kopieren. Zurück auf „Raster“.
7. Dieselbe Textansicht bei einer Vanilla-Tabelle aus `db.pack` und bei einer Loc-Datei.
8. Rechtsklick auf das importierte Projekt, „Bauen“: Statuszeile meldet das gebaute Pack. Im Projektordner liegt `build\<name>.pack`.
9. Eine `.tsv` außerhalb der App öffnen (Editor), einen Zahlenwert durch einen Buchstaben ersetzen, speichern. „Bauen“: Bericht nennt Datei und Zeile. Das alte Pack in `build\` ist unverändert (Datum prüfen). Wert zurücksetzen.
10. Dieselbe `.tsv` in LibreOffice oder Excel geöffnet lassen und bauen: Der Bau gelingt, die Meldung nennt eine ignorierte Sperrdatei.
11. „Bauen und installieren“: Pack liegt in `data`. Im Launcher aktivieren, Spiel starten: Die Mod wirkt wie das Original. Spiel beenden.
12. Erneut „Bauen und installieren“: klappt (eigenes Pack wird ersetzt).
13. Eine fremde Datei gleichen Namens: Pack in `data` von Hand durch eine andere Datei ersetzen. „Bauen und installieren“ lehnt ab und nennt den Grund; die Datei bleibt unverändert. Danach die Datei von Hand löschen.
14. „Deinstallieren“: Datei ist aus `data` verschwunden. Erneut: Eintrag ist deaktiviert, Tooltip nennt den Grund.
15. Zweites Projekt per Rechtsklick „Als aktiv setzen“: Hervorhebung wechselt. `Strg+B` baut das aktive.
16. „Aus der Liste entfernen“: Projekt verschwindet, Ordner bleibt auf der Platte. Menü Projekt, Projekt öffnen, Ordner wählen: Projekt ist wieder da.
17. Projektordner im Windows-Explorer umbenennen, in der App F5: Projekt zeigt Warnpunkt, Tooltip nennt den Grund, Bauen ist deaktiviert. Ordner zurückbenennen, F5: wieder normal.
18. Zwei Projektdateien und eine Pack-Datei in Tabs öffnen, ein Projekt aufklappen, App schließen und starten: Tabs in gleicher Reihenfolge, aktiver Tab und aufgeklapptes Projekt sind wieder da.
19. Import abbrechen (großes Pack wählen, im Fortschrittsfenster Abbrechen): kein neues Projekt, kein Restordner neben dem Ziel.
20. Sprache in den Einstellungen umschalten: Menü Projekt, Kontextmenüs, Gruppenname und Umschalter „Raster“/„Text“ wechseln sofort.
21. Dunkles Theme: Dialog „Neues Projekt“, Bericht und Kontextmenü sind lesbar.
22. Einstellungen, Spielordner auf einen falschen Ordner setzen: Meldung „Spiel nicht gefunden“ steht über dem Baum, Projekte bleiben sichtbar, „Bauen“ geht, „Bauen und installieren“ ist deaktiviert mit Grund. Spielordner zurücksetzen.
