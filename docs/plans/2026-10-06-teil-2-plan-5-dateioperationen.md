# Teil 2 Plan 5: Dateien im Projekt anlegen, übernehmen, umbenennen, löschen – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein Projekt lässt sich in der App mit Inhalt füllen und pflegen: neue Tabelle, neue Loc-Datei, „Ins Projekt übernehmen“ für einen Eintrag aus Vanilla oder fremder Mod, Datei umbenennen, Datei löschen. Jede dieser Aktionen ist ein Schritt in der globalen Rückgängig-History; Löschen wandert in den Papierkorb des Tools.

**Architecture:** Die Dateiarbeit liegt in der Library `Projects`: `ProjectFileOps` (anlegen, umbenennen, nur unter `pack\`, nie durch Verknüpfungen), `ProjectTrash` (Papierkorb unter `%LocalAppData%`), `NewFiles` (Inhalt einer neuen Tabelle oder Loc-Datei, Ziel und Inhalt beim Übernehmen). In der App führt ein `ProjectFilesController` die Abläufe über die Fortschritts-Komponente aus, trägt sie als `FileStep` in die `ActionHistory` ein und hält offene Tabs dazu passend; ein `ProjectFilesViewModel` fragt den Nutzer über `IFileDialogs`. Der Explorer bekommt Aktionen an Projekt, Projektdatei und Pack-Eintrag.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`: 5.6 („Ins Projekt übernehmen“), 5.8 (Dateioperationen, Undo, Kopieren und Einfügen: „Eine neue Tabelle entsteht in der neuesten Version des Schemas“), 5.5 (Regeln pro Eintrag), 4 (Schreibgrenze), 7 (Fehlerfälle), „Festlegungen aus Plan 3“ und „aus Plan 4“. Projektregeln: `CLAUDE.md`.

**Anlass.** Abnahme von Plan 4 am 2026-10-05: Ein neu angelegtes Projekt ist leer, und in der App lässt sich darin nichts anlegen. Bearbeitbar ist bisher nur ein importiertes Pack. Dieser Plan schließt den Weg „neues Projekt, Tabelle anlegen oder aus Vanilla übernehmen, ändern, bauen“.

**Branch:** `feature/teil-2-dateioperationen` (von `master`, Stand `ad69d49` oder neuer). Baseline: 1067 Library-Tests und 740 App-Tests grün, 0 übersprungen, Build ohne Warnung.

**Testregel** (mit dem Nutzer vereinbart): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei. Einzelne Testklassen der App nur über das Skript `apptests.ps1`, immer nur ein Lauf zur Zeit. Kein `python`, `perl` oder anderes Skript-Laufzeitsystem. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers, und nie, während eine andere Sitzung baut oder testet.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors` ist an. Keine neue Projektreferenz: `Projects` referenziert weiter weder `Workspace`, `GameProfiles`, `Editing` noch `UndoRedo`.
- Bestehende öffentliche Signaturen bleiben nutzbar (Überladung oder optionaler Parameter).
- Fachlogik in Libraries, die App ist Hülle: keine Logik im Code-behind außer View-Verdrahtung. Keine statischen Singletons; alles entsteht im Composition Root (`App.xaml.cs`).
- Libraries loggen nicht und liefern keine UI-Texte: Probleme sind ein Enum plus englisches `Detail`.
- **Schreibgrenze (Spec 4):** Geschrieben, verschoben und gelöscht wird nur (a) unter `pack\` eines bekannten Projekts und (b) unter `<LocalRoot>\trash\<Projekt-GUID>\`. Jeder Pfad unter `pack\` geht durch `PackFolderPaths.ResolveForWrite`. Führt der Weg durch eine Verknüpfung, passiert nichts. Vanilla-Packs, Workshop-Packs und fremde Packs werden nur gelesen. Ein Projektordner wird nie gelöscht.
- Nie eine vorhandene Datei überschreiben: Anlegen, Übernehmen, Umbenennen und Zurückholen aus dem Papierkorb brechen ab, wenn am Ziel etwas liegt (Datei oder Ordner, Vergleich ohne Rücksicht auf Groß-/Kleinschreibung).
- Dateien entstehen atomar: temp-Datei im Zielordner (`<Ziel>.<8 Hex>.tmp`, `FileMode.CreateNew`), dann Umbenennen ohne Überschreiben. Bei Fehler bleibt keine temp-Datei.
- Jede Dateioperation der App: Eingabe prüfen, ausführen über `IOperationRunner`, bei Erfolg ein Schritt per `history.Record`, Explorer und Tabs nachführen. Bei Fehler kein Teilzustand und eine Meldung im Klartext.
- Löschen ist reversibel (Papierkorb) und fragt nicht nach (Spec 5.8).
- Alle UI-Texte über `AppTexts` (`T`/`F`), jeder neue Key in `locales/en.json` und `locales/de.json`; der Locale-Test bleibt grün. Keine Ellipsen in Labels. Farben nur über Theme-Tokens. Neue Fenster wie `Editing/ChoiceWindow`.
- Namen und Pfade aus Dateien und Packs werden vor der Anzeige mit `DisplayText.Safe` maskiert.
- Kein `catch (Exception)` ohne Typfilter. View-Models bekommen kein Log; geloggt wird im Controller.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in `TempDir` (auch der Papierkorb der Tests liegt dort). Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`, nichts schreibt in Spiel-, Workshop- oder RPFM-Ordner. Kein Test kann außerhalb des Temp-Ordners schreiben, auch nicht, wenn der geprüfte Code kaputt wäre.
- Jedes `await` in einem Test auf einen manuellen Runner oder Fake-Dialog hat ein Zeitlimit. Kein Fenster bleibt nach einem Test offen.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Widerspricht eine Erwartung einer Regel des Plans, wird das gemeldet.

## Review Focus

1. **Ein Name, den der Nutzer eintippt, führt aus `pack\` hinaus oder trifft etwas Vorhandenes** (`..\x`, `a\b`, `C:x`, `x.`, `NUL`, Name mit `;` oder Tab, ein Name, der sich von einer vorhandenen Datei nur in Groß-/Kleinschreibung unterscheidet, 300 Zeichen). Erwartung: Der Dialog sperrt OK und nennt den Grund; die Library lehnt unabhängig davon ab; nichts wird überschrieben. → Task 1 (`Unsafe_and_taken_targets_are_refused_and_nothing_is_written`), Task 3 (`Every_bad_name_has_a_reason`), Task 5 (`A_refused_name_blocks_ok_and_names_the_reason`).
2. **Rückgängig einer Dateioperation, wenn sich die Lage geändert hat** (Papierkorb-Datei von Hand gelöscht, am alten Ort liegt inzwischen eine andere Datei, die Datei ist in einem Tab offen und geändert, das Projekt hat die Liste verlassen). Erwartung: Meldung, kein Teilzustand, keine überschriebene Datei, keine verlorene Änderung; der Schritt verlässt die History nur, wenn er wirklich nicht mehr ausführbar ist. → Task 2 (`Restore_refuses_a_taken_target_and_a_missing_trash_file`), Task 4 (`Undo_of_a_file_step_with_a_modified_tab_is_refused_and_the_step_stays`, `A_step_that_cannot_run_any_more_is_dropped_with_a_message`).
3. **Löschen oder Umbenennen einer Datei, die offen ist.** Erwartung: Ein Tab ohne Änderungen wird geschlossen (Löschen) oder folgt dem neuen Namen (Umbenennen: schließen und neu öffnen); ein Tab mit Änderungen oder offener Zelle verhindert die Aktion mit Grund. → Task 4 (`Delete_closes_an_unmodified_tab_and_refuses_a_modified_one`, `Rename_reopens_the_tab_under_the_new_name`).
4. **„Ins Projekt übernehmen“ für Einträge, die das Projektformat nicht als Text trägt oder die nicht lesbar sind** (Tabelle ohne Schema, Farbkanal über 255, LZMA-Eintrag, Pack inzwischen geändert, Eintrag mit unsicherem Pfad). Erwartung: Tabelle bleibt binär mit Hinweis, nicht lesbarer Eintrag bricht mit Meldung ab, nie eine halbe Datei. → Task 3 (`An_entry_the_dialect_cannot_carry_is_adopted_as_a_binary_file_with_a_note`), Task 4 (`An_unreadable_entry_is_not_adopted_and_nothing_is_written`).
5. **Abbruch oder Fehler mitten in der Operation** (Platte voll, Zielordner gesperrt, Abbrechen im Fortschrittsfenster beim Übernehmen einer großen Datei). Erwartung: keine temp-Datei, keine neuen leeren Ordner, kein Schritt in der History, Explorer unverändert. → Task 1 (`A_failed_create_leaves_no_temp_file_and_no_new_folder`), Task 4 (`A_cancelled_adopt_leaves_nothing_behind`).

---

### Task 1: `ProjectFileOps` (anlegen, umbenennen)

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectFileOps.cs`
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectFileOpsTests.cs`

**Interfaces:**
- Consumes: `PackFolderPaths.ResolveForWrite`, `ProjectFile.PackFolder`, `ProjectFiles.IsLink`, `FileFingerprint`.
- Produces:

```csharp
namespace TotalWarModTool.Projects;

public enum FileOpProblem { None, UnsafePath, Link, TargetExists, SourceMissing, Locked, Failed }

/// Detail is short technical English. CreatedFolders are the folders this call created, outermost first (relative to pack\, with '\').
public sealed record FileOpResult(FileOpProblem Problem, string? Detail, FileFingerprint? Fingerprint, IReadOnlyList<string> CreatedFolders);

public static class ProjectFileOps
{
    /// Creates pack\<relativeFile> with exactly these bytes. Missing folders below pack\ are created. Never overwrites.
    public static FileOpResult Create(string projectFolder, string relativeFile, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// Renames pack\<from> to pack\<to> (also into another folder below pack\). Never overwrites. Missing target folders are created.
    public static FileOpResult Rename(string projectFolder, string from, string to);

    /// Removes the given folders when they are empty, innermost first; stops at the first that is not empty. relative to pack\.
    public static void RemoveEmptyFolders(string projectFolder, IReadOnlyList<string> foldersOutermostFirst);

    /// True when pack\<relativeFile> is taken: a file or a folder exists there, compared ignoring case. False for an unsafe path.
    public static bool Exists(string projectFolder, string relativeFile);
}
```

Festlegungen:
- Kein Aufruf wirft für das Dateisystem; nur Argumentfehler werfen (`null`, leerer `projectFolder`). Ein leerer oder unsicherer relativer Pfad ist `UnsafePath`.
- `pack\` selbst muss existieren (ein Projekt hat ihn; fehlt er, legt `Create` ihn an, sofern der Projektordner existiert und `pack` keine Verknüpfung ist).
- Ordner werden einzeln von außen nach innen angelegt; vor jedem Anlegen wird der Elternordner auf eine Verknüpfung geprüft (das leistet `ResolveForWrite` für vorhandene Teile; neu angelegte sind echte Ordner).
- `TargetExists`: am Ziel liegt eine Datei oder ein Ordner (Vergleich über das Dateisystem, also ohne Rücksicht auf Groß-/Kleinschreibung). Ebenso, wenn ein Ordner des Wegs als Datei existiert.
- `Create` schreibt in `<Ziel>.<8 Hex>.tmp` (`FileMode.CreateNew`) und benennt ohne Überschreiben um (`File.Move(temp, target, overwrite: false)`). Schlägt etwas fehl oder wird abgebrochen (`OperationCanceledException` geht an den Aufrufer), sind die temp-Datei und die von diesem Aufruf angelegten Ordner wieder weg.
- `Rename`: `from` und `to` gehen beide durch `ResolveForWrite`. `SourceMissing`, wenn `from` keine Datei ist. Unterscheiden sich `from` und `to` nur in Groß-/Kleinschreibung, ist das erlaubt (kein `TargetExists`). `Locked` bei `IOException`/`UnauthorizedAccessException`, während die Quelle existiert. Legt `Rename` Ordner an und scheitert danach, entfernt es sie wieder.
- `RemoveEmptyFolders` wirft nie und entfernt nie `pack\` selbst, nie eine Verknüpfung und nie einen Ordner außerhalb von `pack\`.

- [ ] **Step 1: Failing Tests schreiben** (`TempDir`; Verknüpfungen mit `JunctionHelper`)

```csharp
[Fact] public void Create_writes_the_bytes_and_returns_the_fingerprint()
[Fact] public void Create_makes_missing_folders_and_reports_them_outermost_first()        // "db\x_tables\a.tsv" in an empty pack: ["db", "db\x_tables"]
[Fact] public void Create_never_overwrites_a_file_a_folder_or_a_name_that_differs_in_case()
[Theory] public void Unsafe_and_taken_targets_are_refused_and_nothing_is_written(string relativeFile)
// "", "..\\x", "a\\..\\..\\x", "x.", "x ", "NUL.tsv", "a::$DATA", "a\\\\b", "{tmp}\\outside.tsv", "{tmpNoDrive}\\outside.tsv":
// UnsafePath; a snapshot of the whole temp dir is unchanged. No row may point outside the temp dir even if the check were missing.
[Fact] public void A_linked_pack_folder_and_a_linked_sub_folder_are_refused()               // the link target is unchanged
[Fact] public void A_failed_create_leaves_no_temp_file_and_no_new_folder()                  // target folder path blocked by a FILE named like the folder
[Fact] public void A_cancelled_create_leaves_nothing_behind()                               // a token that is already cancelled: OperationCanceledException, no file, no temp file, no new folder
[Fact] public void Rename_moves_the_file_and_creates_target_folders()
[Fact] public void Rename_refuses_a_taken_target_and_keeps_both_files()
[Fact] public void Rename_that_only_changes_the_case_works()
[Fact] public void Rename_of_a_missing_source_is_SourceMissing()
[Fact] public void Rename_of_a_locked_file_is_Locked_and_changes_nothing()
[Fact] public void RemoveEmptyFolders_stops_at_the_first_folder_that_is_not_empty_and_never_removes_pack()
[Fact] public void Exists_ignores_case_and_sees_folders()
```

- [ ] **Step 2: RED** — `dotnet test tests/TotalWarModTool.Projects.Tests --filter "FullyQualifiedName~ProjectFileOpsTests" --nologo -v q`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — `Projects.Tests` ganz; Library-Suite.
- [ ] **Step 5: Commit** — `feat: create and rename files inside the pack folder of a project`

---

### Task 2: `ProjectTrash` (Papierkorb)

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectTrash.cs`
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectTrashTests.cs`

**Interfaces:**
- Consumes: `PackFolderPaths.ResolveForWrite`, `ProjectFileOps` (Ordner anlegen und aufräumen), `TimeProvider`.
- Produces:

```csharp
/// One file in the trash. TrashFile is the full path below <trashRoot>\<project id D>\; RelativeFile is where it came from (below pack\, with '\').
public sealed record TrashedFile(Guid ProjectId, string TrashFile, string RelativeFile);

public sealed record TrashResult(FileOpProblem Problem, string? Detail, TrashedFile? File);

public static class ProjectTrash
{
    /// Moves pack\<relativeFile> to <trashRoot>\<projectId D>\<yyyyMMdd-HHmmss>-<8 hex>\<file name>. Never overwrites.
    public static TrashResult MoveToTrash(string trashRoot, Guid projectId, string projectFolder, string relativeFile, TimeProvider time);

    /// Moves the file back to pack\<RelativeFile> (missing folders are created). Never overwrites.
    public static FileOpResult Restore(TrashedFile file, string projectFolder);

    /// Deletes the entries of every project that are older than maxAge (by the time stamp in the folder name). Never throws.
    /// Returns the number of deleted entries. Touches nothing outside trashRoot.
    public static int Cleanup(string trashRoot, TimeSpan maxAge, TimeProvider time, CancellationToken cancellationToken);
}
```

Festlegungen:
- Der Papierkorb liegt in der App unter `AppPaths.LocalRoot\trash`; die Library bekommt den Pfad übergeben. `trashRoot` wird bei Bedarf angelegt.
- Der Ordnername eines Eintrags trägt die UTC-Zeit des Löschens (`yyyyMMdd-HHmmss`, invariante Kultur) und 8 Hex-Zeichen. `Cleanup` liest das Alter aus dem Namen, nicht aus Dateizeiten; ein Ordner, dessen Name nicht passt, bleibt liegen.
- `MoveToTrash` und `Restore` verschieben mit `File.Move` (das kopiert und löscht, wenn Projekt und Papierkorb auf verschiedenen Laufwerken liegen). Scheitert das Verschieben, bleibt die Quelle, und am Ziel liegt nichts (ein angelegter Eintragsordner wird wieder entfernt).
- `MoveToTrash`: `SourceMissing`, wenn die Datei fehlt; `Locked`, wenn sie gesperrt ist; `Link`/`UnsafePath` aus `ResolveForWrite`. Die Quelle ist immer eine Datei unter `pack\`; ein Ordner wird nie verschoben.
- `Restore`: `SourceMissing`, wenn die Papierkorb-Datei fehlt; `TargetExists`, wenn am alten Ort etwas liegt; der Zielpfad geht durch `ResolveForWrite`. `TrashFile` muss unter einem Ordner `…\<ProjectId D>\<Eintrag>\` liegen und darf keine Verknüpfung sein, sonst `UnsafePath` (ein gefälschter `TrashedFile` kann keine fremde Datei ins Projekt ziehen). Nach dem Zurückholen wird der leere Eintragsordner entfernt.
- `Cleanup`: geht `trashRoot\<GUID>\<Eintrag>` durch; folgt keiner Verknüpfung (ein verlinkter Projekt- oder Eintragsordner wird übersprungen); löscht nur Einträge, deren Name eine gültige Zeit trägt, die älter als `maxAge` ist; entfernt danach leere GUID-Ordner. Fehler pro Eintrag kosten nur diesen Eintrag.
- Die Altersgrenze (30 Tage, Spec 5.8) setzt die App (Task 7).

- [ ] **Step 1: Failing Tests schreiben** (`TempDir` für Projekt und Papierkorb; Zeit über einen `FakeTimeProvider` des Testprojekts oder eine kleine eigene Ableitung von `TimeProvider`)

```csharp
[Fact] public void MoveToTrash_moves_the_file_under_the_project_id_with_a_dated_folder()
[Fact] public void Two_deletes_of_the_same_name_do_not_collide()
[Fact] public void MoveToTrash_of_a_missing_file_a_locked_file_and_an_unsafe_path_changes_nothing()
[Fact] public void Restore_brings_the_same_bytes_back_and_removes_the_empty_trash_folder()
[Fact] public void Restore_refuses_a_taken_target_and_a_missing_trash_file()               // both sides unchanged
[Fact] public void Restore_refuses_a_trash_path_outside_the_project_s_trash_folder()         // a TrashedFile pointing at a file elsewhere in the temp dir: UnsafePath, file untouched
[Fact] public void Restore_recreates_missing_folders()
[Fact] public void Cleanup_deletes_entries_older_than_the_limit_and_keeps_newer_ones()
[Fact] public void Cleanup_keeps_folders_whose_name_carries_no_time_and_skips_links()
[Fact] public void Cleanup_of_a_missing_trash_root_returns_zero()
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~ProjectTrashTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — `Projects.Tests` ganz; Library-Suite.
- [ ] **Step 5: Commit** — `feat: add the trash for deleted project files`

---

### Task 3: `NewFiles` (Inhalt und Ziel neuer Dateien)

**Files:**
- Create: `src/TotalWarModTool.Projects/NewFiles.cs`
- Test: `tests/TotalWarModTool.Projects.Tests/NewFilesTests.cs`

**Interfaces:**
- Consumes: `GameSchema`, `TableTsv.Write/WriteLoc`, `EntryConversion.ToProjectFile`, `ProjectLayout`, `DbTablePath`, `EntryOpener`-Regeln nur über `ProjectLayout` (keine Referenz auf `Workspace`).
- Produces:

```csharp
/// RelativeFile is below pack\ with '\'. Note says why a table or loc entry stayed binary (null otherwise).
public sealed record NewFile(string RelativeFile, byte[] Content, ProjectFileKind Kind, string? Note);

/// Exactly one of File and Reason is set. Reason is short technical English.
public sealed record NewFileResult(NewFile? File, string? Reason);

public static class NewFiles
{
    /// The folder of loc files inside a pack.
    public const string LocFolder = "text/db";

    /// The tables a new file can be created for: in the schema, with a newest definition without unsupported field types. Sorted ordinally.
    public static IReadOnlyList<string> CreatableTables(GameSchema schema);

    /// Null when the name can be the name part of a table or loc file; otherwise a short English reason.
    public static string? NameProblem(string name);

    /// An empty table in the newest version of the schema: db/<tableName>/<name> as text.
    public static NewFileResult Table(GameSchema schema, string tableName, string name);

    /// An empty loc file: text/db/<name>.loc as text.
    public static NewFileResult Loc(string name);

    /// The file an entry of a pack becomes in a project. For a table (db/<table>/<x>) and a loc file (<folder>/<x>.loc), newName replaces
    /// the name part; for every other entry newName must be null and the path stays. The content follows EntryConversion.ToProjectFile.
    public static NewFileResult Adopt(string packPath, byte[] entryBytes, GameSchema? schema, string? newName);

    /// What Adopt needs to know before the entry is read: whether the entry gets a new name, and the name it has now.
    public static (bool Renamable, string CurrentName) AdoptName(string packPath);
}
```

Festlegungen:
- `NameProblem`: leer, länger als 100 Zeichen, enthält `\`, `/`, `.`, Leerzeichen am Anfang oder Ende, ein Zeichen, das `ProjectLayout.UnsafePathReason` für ein Segment ablehnt, ein Tab, Zeilenumbruch oder `;` (die Metazeile der TSV trägt den Pfad), oder ist ein reservierter Gerätename. Der Punkt ist verboten, weil die Endungen `.tsv` und `.loc.tsv` die Art der Datei festlegen.
- `Table`: unbekannte Tabelle, Tabelle nicht in `CreatableTables` oder `NameProblem` ergibt `Reason`. Inhalt = `TableTsv.Write(tableName, newestVersion, "db/<tableName>/<name>", definition, [])` als UTF-8 ohne BOM. Neueste Version = die höchste `Version` der Tabelle im Schema. Lehnt `TableTsv.Write` ab, ist das `Reason`.
- `Loc`: Inhalt = `TableTsv.WriteLoc("text/db/<name>.loc", [])`.
- `Adopt`: Der Pack-Pfad des Ziels entsteht aus dem alten durch Ersetzen des Namensteils (bei Loc ohne `.loc`). Er muss `ProjectLayout.UnsafePathReason` bestehen; ein Eintrag, dessen Name mit einer Projektregel kollidiert (im Pack heißt er schon `….tsv` unter `db/<tabelle>/` oder `….loc.tsv`, Spec 5.5), ergibt `Reason`. Der Inhalt kommt aus `EntryConversion.ToProjectFile(neuerPackPfad, entryBytes, schema)`: Text, wenn der Dialekt die Tabelle verlustfrei trägt, sonst binär mit `Note`. Die Metazeile trägt damit den neuen Pfad.
- `AdoptName`: `Renamable` für Tabellen und Loc-Dateien; `CurrentName` ist der Namensteil (bei Loc ohne `.loc`), sonst der Dateiname.
- Der Loc-Ordner `text/db` ist eine Konvention des Pack-Formats aller unterstützten Spiele und steht deshalb als Konstante hier, nicht im Spielprofil (Task 7 trägt das in die Spec ein).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void CreatableTables_leaves_out_tables_with_an_unsupported_field_in_the_newest_version()
[Fact] public void Table_writes_an_empty_table_in_the_newest_version_that_reads_back()      // TableTsv.Read: same table, version, zero rows; relative file "db\<t>\<name>.tsv"
[Fact] public void Loc_writes_an_empty_loc_file_that_reads_back()                             // "text\db\<name>.loc.tsv"
[Theory] public void Every_bad_name_has_a_reason(string name)
// "", " a", "a ", "a.b", "a/b", "a\\b", "..", "NUL", "a;b", a name with a tab, 101 characters -> NameProblem not null; Table and Loc return Reason
[Fact] public void Adopt_of_a_table_gets_the_new_name_in_the_path_and_in_the_meta_line()
[Fact] public void Adopt_of_a_loc_file_keeps_its_folder_and_gets_the_new_name()
[Fact] public void Adopt_of_another_file_keeps_its_path_and_bytes_and_refuses_a_new_name()
[Fact] public void An_entry_the_dialect_cannot_carry_is_adopted_as_a_binary_file_with_a_note() // colour channel 257; and a table without a schema
[Fact] public void Adopt_refuses_an_unsafe_path_and_a_name_that_collides_with_the_text_form()
[Fact] public void What_Adopt_writes_builds_back_to_the_entry()                               // EntryConversion.ToPackEntry of the adopted file equals the entry bytes except the table GUID
```

- [ ] **Step 2: RED** — `--filter "FullyQualifiedName~NewFilesTests"`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — `Projects.Tests` ganz; Library-Suite.
- [ ] **Step 5: Commit** — `feat: compute the content of new and adopted project files`

---

### Task 4: `ProjectFilesController` und `FileStep` (App)

**Files:**
- Create: `src/TotalWarModTool.App/Projects/ProjectFilesController.cs`, `Projects/FileStep.cs`
- Modify: `Workspace/WorkspaceController.cs` (rohe Bytes eines Eintrags lesen), `Editing/DocumentsController.cs` (Prüfung vor Undo/Redo eines Dateischritts), `AppPaths.cs` (`TrashDirectory`), `AppTexts`, `locales/*.json`
- Test: `tests/TotalWarModTool.App.Tests/ProjectFilesControllerTests.cs`, `DocumentsControllerTests.cs` und `WorkspaceControllerTests.cs` (ergänzen)

**Interfaces:**
- Consumes: Tasks 1 bis 3, `ProjectsController` (`Projects`, `Active`, `CanStartOperation`, `Schema`), `DocumentsController` (`Tabs`, `EditableTabs`), `ActionHistory`, `IOperationRunner`.
- Produces:

```csharp
/// One file operation in the undo history. Do and Undo run on the UI thread and throw InvalidOperationException with an English
/// detail when the step cannot run (the history then drops it).
public sealed class FileStep : IUndoableAction
{
    public Guid ProjectId { get; }
    /// The files (below pack\, with '\') that exist after Do, and those that exist after Undo. Used to find tabs and to refresh the explorer.
    public IReadOnlyList<string> FilesAfterDo { get; }
    public IReadOnlyList<string> FilesAfterUndo { get; }
    public string Description { get; }
    public void Do();
    public void Undo();
}

/// Ok false: Message says why (a ready UI text). RelativeFile is the new or renamed file when there is one. Note is a hint the user
/// should see although the action worked (a table that stayed binary).
public sealed record FileActionResult(bool Ok, string? Message, string? RelativeFile = null, string? Note = null);

public sealed class ProjectFilesController
{
    public ProjectFilesController(ProjectsController projects, WorkspaceController workspace, DocumentsController documents,
        ActionHistory history, IOperationRunner runner, string trashRoot, IAppLog log, AppTexts texts, TimeProvider time);

    public IReadOnlyList<string> CreatableTables { get; }                       // empty without a schema
    /// Null when a file action can start for this project, else the reason (no schema where one is needed is the caller's check).
    public string? DisabledReason(Guid projectId);
    public string? CheckNewName(Guid projectId, string relativeFolder, string name, string extension);   // ready UI text or null

    public Task<FileActionResult?> CreateTableAsync(Guid projectId, string tableName, string name);
    public Task<FileActionResult?> CreateLocAsync(Guid projectId, string name);
    public Task<FileActionResult?> AdoptAsync(Guid projectId, PackSource pack, string entryPath, string? newName);
    public Task<FileActionResult?> RenameAsync(Guid projectId, string relativeFile, string newName);
    public Task<FileActionResult?> DeleteAsync(Guid projectId, string relativeFile);

    /// Raised after a file action or the undo/redo of a file step changed the files of a project.
    public event EventHandler<Guid>? FilesChanged;
}

// WorkspaceController
/// Reads the uncompressed bytes of one entry through the runner. Null when nothing can be started, or the run was cancelled or failed;
/// a read problem comes back inside the result.
public Task<EntryContent?> ReadEntryBytesAsync(PackSource pack, string entryPath);

// AppPaths
public string TrashDirectory => Path.Combine(LocalRoot, "trash");
```

Festlegungen:
- Jede Methode liefert `null`, wenn gerade nichts starten kann (`CanStartOperation` false) oder der Lauf abgebrochen wurde. Die Arbeit an der Platte läuft im Runner; danach, im UI-Thread: `history.Record(step)`, `FilesChanged`, Tabs nachführen.
- Ablauf `CreateTableAsync`/`CreateLocAsync`: `NewFiles.Table/Loc` → `ProjectFileOps.Create`. Schritt: Undo = `ProjectTrash.MoveToTrash` und `RemoveEmptyFolders` für die beim Anlegen entstandenen Ordner; Redo = `ProjectTrash.Restore`.
- Ablauf `AdoptAsync`: `workspace.ReadEntryBytesAsync` (ein Leseproblem ist ein Abbruch mit Meldung), dann im Runner `NewFiles.Adopt` und `ProjectFileOps.Create`. Schritt wie beim Anlegen. `Note` trägt den Grund einer binär gebliebenen Tabelle.
- Ablauf `DeleteAsync`: `ProjectTrash.MoveToTrash`. Schritt: Undo = `Restore`, Redo = erneut `MoveToTrash` (neuer Papierkorb-Eintrag; der Schritt merkt sich den jeweils letzten).
- Ablauf `RenameAsync`: nur der Namensteil ändert sich, Ordner und Endung (`.tsv`, `.loc.tsv` oder die bisherige Endung) bleiben. `NewFiles.NameProblem` gilt für Tabellen und Loc-Dateien; für andere Dateien gilt die Segmentregel aus `ResolveForWrite`, der Punkt ist dort erlaubt. Schritt: Undo = zurück umbenennen. Der Inhalt der Datei wird nicht angefasst (die Metazeile einer TSV trägt danach noch den alten Pfad; sie ist informativ, der Bau liest den Pfad aus dem Dateinamen, und das nächste Speichern in der App schreibt sie neu).
- Offene Tabs (Review Focus 3): Vor Löschen und Umbenennen sucht der Controller die Tabs der Datei. Hat einer ungespeicherte Änderungen oder eine offene Zelle, wird abgelehnt (`Ok false`, Grund „zuerst speichern oder schließen“). Sonst: Löschen schließt die Tabs (`CloseWithoutAsking`); Umbenennen schließt sie und öffnet die Datei unter dem neuen Namen wieder, wenn sie offen war (über `MainViewModel.OpenProjectFileAsync`, als Callback im Konstruktor oder als Ereignis).
- Undo/Redo eines Dateischritts (`DocumentsController`): Ist `history.NextUndo` bzw. `NextRedo` ein `FileStep`, prüft der Controller vorher die Tabs der Dateien, die der Schritt entfernt oder umbenennt. Ungespeicherte Änderungen oder offene Zelle: Meldung, der Schritt läuft nicht und bleibt in der History. Sonst werden diese Tabs geschlossen, dann läuft der Schritt. Wirft der Schritt, greift der vorhandene Weg (Meldung, Schritt entfällt); `InvalidOperationException` trägt den Grund der Library (`Detail`), maskiert angezeigt.
- Verlässt ein Projekt die Liste, entfernt der Controller dessen Dateischritte aus der History (`RemoveWhere`): Ohne Projektordner sind sie nicht ausführbar.
- `DisabledReason`: Projekt unbekannt oder nicht benutzbar; App beschäftigt. Anlegen einer Tabelle und Übernehmen einer Tabelle brauchen ein Schema nur insoweit, als `CreatableTables` leer ist bzw. die Tabelle binär übernommen wird (kein eigener Sperrgrund).
- Alle Meldungen sind fertige UI-Texte aus `AppTexts`, je `FileOpProblem` ein Text; das technische `Detail` hängt maskiert an.
- Dateiaktionen ändern die Projektliste nicht und brauchen deshalb keine schreibbare `projects.json`.

- [ ] **Step 1: Failing Tests schreiben** (Projekt und Papierkorb in `TempDir`, `FakeOperationRunner`, echte `ActionHistory`, ein kleines Testpack über die vorhandenen Helfer)

```csharp
[Fact] public void A_new_table_exists_on_disk_is_one_undo_step_and_raises_FilesChanged()
[Fact] public void Undo_of_a_new_table_moves_it_to_the_trash_and_removes_the_folders_it_created_and_redo_brings_it_back()
[Fact] public void A_new_loc_file_lands_in_text_db()
[Fact] public void A_taken_name_is_refused_with_a_message_and_records_no_step()
[Fact] public void Adopt_of_a_table_writes_the_text_form_under_the_new_name()
[Fact] public void Adopt_of_a_script_keeps_its_path()
[Fact] public void Adopt_of_a_table_the_dialect_cannot_carry_reports_the_note()
[Fact] public void An_unreadable_entry_is_not_adopted_and_nothing_is_written()
[Fact] public void A_cancelled_adopt_leaves_nothing_behind()                                   // runner cancels: no file, no folder, no step
[Fact] public void Delete_moves_the_file_to_the_trash_and_undo_restores_the_same_bytes()
[Fact] public void Delete_closes_an_unmodified_tab_and_refuses_a_modified_one()
[Fact] public void Rename_changes_only_the_name_part_and_undo_renames_back()
[Fact] public void Rename_reopens_the_tab_under_the_new_name()
[Fact] public void Rename_of_a_table_refuses_a_name_with_a_dot_and_a_taken_name()
[Fact] public void Undo_of_a_file_step_with_a_modified_tab_is_refused_and_the_step_stays()     // create table, open it, edit a cell, undo twice: cell undone, then refused with a message; file still there
[Fact] public void A_step_that_cannot_run_any_more_is_dropped_with_a_message()                  // delete, remove the trash file by hand, undo: message, CanUndo false, nothing created
[Fact] public void Undo_of_a_delete_does_not_overwrite_a_file_created_meanwhile()
[Fact] public void File_steps_of_a_project_that_left_the_list_are_removed()
[Fact] public void Nothing_starts_while_an_operation_runs()
// WorkspaceControllerTests
[Fact] public void ReadEntryBytesAsync_returns_the_uncompressed_bytes_and_reports_a_read_problem()
```

- [ ] **Step 2: RED** — `apptests.ps1 ProjectFilesControllerTests DocumentsControllerTests WorkspaceControllerTests`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `DocumentsControllerSavingTests`, `DocumentsControllerExternalTests`, `AppPathsTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: file operations of a project as undo steps`

---

### Task 5: Dialoge und `ProjectFilesViewModel`

**Files:**
- Create: `src/TotalWarModTool.App/Projects/IFileDialogs.cs`, `Projects/FileDialogs.cs`, `Projects/NameInputViewModel.cs`, `Projects/NameInputWindow.xaml`(+`.cs`), `Projects/NewTableViewModel.cs`, `Projects/NewTableWindow.xaml`(+`.cs`), `Projects/ProjectFilesViewModel.cs`; `tests/TotalWarModTool.App.Tests/FakeFileDialogs.cs`
- Modify: `AppTexts`, `locales/*.json`
- Test: `ProjectFilesViewModelTests.cs`, `NameInputViewModelTests.cs`, `NewTableViewModelTests.cs`, `ProjectWindowsTests.cs` (ergänzen)

**Interfaces:**
- Consumes: Task 4, `ChoiceWindow` (Bestätigung), `IEditDialogs.ShowMessage`-Muster.
- Produces:

```csharp
public sealed record NewTableInput(string TableName, string Name);

public interface IFileDialogs
{
    /// Asks for a name. validate returns the text that blocks OK, or null. Null when the user cancels.
    string? AskName(string title, string label, string suggestion, Func<string, string?> validate);
    /// Asks for a table of the list (with a filter box) and a file name. Null when the user cancels.
    NewTableInput? AskNewTable(IReadOnlyList<string> tables, string suggestion, Func<NewTableInput, string?> validate);
    /// Explicit confirmation that the new file has the same name as the entry it comes from and so replaces that whole file in the game.
    bool ConfirmSameName(string file);
    void ShowMessage(string title, string message);
}

public sealed class ProjectFilesViewModel
{
    public ProjectFilesViewModel(ProjectFilesController files, ProjectsController projects, IFileDialogs dialogs, AppTexts texts);

    public Task NewTableAsync(Guid projectId);
    public Task NewLocAsync(Guid projectId);
    public Task AdoptAsync(PackSource pack, string entryPath);           // into the active project
    public Task RenameAsync(Guid projectId, string relativeFile);
    public Task DeleteAsync(Guid projectId, string relativeFile);

    public string? NewFileDisabledReason(Guid projectId);                // also: no creatable table is NOT a reason here (the dialog says it)
    public string? AdoptDisabledReason { get; }                          // no active project, active project unusable, busy
    public string? FileActionDisabledReason(Guid projectId, string relativeFile);

    /// Raised with project and file when a new or adopted file should be opened in a tab.
    public event EventHandler<(Guid ProjectId, string RelativeFile)>? OpenRequested;
    /// The result line of the last action (a note or a success text), for the status bar; null when there is none.
    public string? StatusMessage { get; }
}
```

Festlegungen:
- Vorschlag für den Namen einer neuen oder übernommenen Tabelle oder Loc-Datei: der Projektname, auf erlaubte Zeichen gebracht (Kleinbuchstaben, Ziffern, `_`; alles andere wird `_`; leer ergibt `new`). Ist der Name vergeben, hängt der Vorschlag `_2`, `_3` an.
- Die Prüfung im Dialog ruft `ProjectFilesController.CheckNewName` bei jeder Änderung; OK ist gesperrt, solange sie einen Grund liefert, der Grund steht unter dem Feld.
- „Ins Projekt übernehmen“ (Spec 5.6): Für Tabellen und Loc-Dateien fragt `AskName`. Gleicht der gewählte Name dem Namen des Quell-Eintrags, kommt zusätzlich `ConfirmSameName` mit dem Hinweis, dass die Datei dann die ganze Datei dieses Namens im Spiel ersetzt; „Nein“ führt zurück zur Namensfrage. Andere Dateien werden ohne Frage übernommen. Ziel vorhanden: Meldung, nichts passiert.
- Nach Anlegen und Übernehmen: `OpenRequested` (die Datei öffnet sich als Tab) und `StatusMessage` (bei binär gebliebener Tabelle der Hinweis mit Grund).
- Löschen fragt nicht nach. Die Statuszeile nennt die gelöschte Datei und dass „Rückgängig“ sie zurückholt.
- Eine abgelehnte Aktion (`Ok false`) zeigt `ShowMessage`.
- `NewTableWindow`: Filterfeld über einer Liste der Tabellennamen (virtualisiert; es sind über 1.500), darunter das Namensfeld und der Grund. Der Filter sucht den Text irgendwo im Namen, ohne Rücksicht auf Groß-/Kleinschreibung. Ist die Liste leer (kein Schema), zeigt das Fenster das als Text und OK bleibt gesperrt. `Enter` in der Liste wechselt ins Namensfeld. Fenster wie `ChoiceWindow` (Theme, `Esc` bricht ab).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// ProjectFilesViewModelTests (FakeFileDialogs with queued answers)
[Fact] public void New_table_asks_creates_opens_and_reports()
[Fact] public void The_suggested_name_comes_from_the_project_name_and_avoids_taken_names()    // "My Mod!" -> "my_mod_", taken -> "my_mod__2"
[Fact] public void A_cancelled_dialog_does_nothing()
[Fact] public void Adopt_of_a_table_asks_for_a_name_and_other_files_are_adopted_without_a_question()
[Fact] public void Adopt_under_the_same_name_needs_the_confirmation_and_no_returns_to_the_name_question()
[Fact] public void Adopt_into_an_existing_target_shows_a_message()
[Fact] public void A_binary_table_reports_its_reason_in_the_status()
[Fact] public void Delete_does_not_ask_and_reports_how_to_get_the_file_back()
[Fact] public void Rename_suggests_the_current_name_and_reports_a_refusal()
[Fact] public void Disabled_reasons_name_no_active_project_an_unusable_project_and_busy()
// NameInputViewModelTests / NewTableViewModelTests
[Fact] public void A_refused_name_blocks_ok_and_names_the_reason()
[Fact] public void The_filter_matches_anywhere_ignoring_case_and_an_empty_list_blocks_ok()
// ProjectWindowsTests (Sta.RunAsync)
[Fact] public void The_name_window_and_the_new_table_window_return_the_input_and_null_for_escape()
```

- [ ] **Step 2: RED** — `apptests.ps1 ProjectFilesViewModelTests NameInputViewModelTests NewTableViewModelTests ProjectWindowsTests`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `ProjectFilesControllerTests`, `ControlTemplatesTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: dialogs and commands for the file operations`

---

### Task 6: Explorer, Menü, Verdrahtung

**Files:**
- Modify: `Explorer/ExplorerViewModel.Projects.cs`, `Explorer/ExplorerViewModel.cs`, `Explorer/ProjectNodes.cs`, `Explorer/ExplorerNodes.cs`, `MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`, `AppTexts`, `locales/*.json`
- Test: `ExplorerViewModelTests.cs`, `MainWindowTests.cs`, `MainViewModelTests.cs` (ergänzen)

**Interfaces:**
- Consumes: Task 5 (`ProjectFilesViewModel`), `NodeAction`, `ExplorerViewModel.RefreshProjectFilesAsync`, `MainViewModel.OpenProjectFileAsync`.
- Produces: keine neuen Typen; `ExplorerViewModel` und `MainViewModel` bekommen einen optionalen `ProjectFilesViewModel`.

Festlegungen:
- Kontextmenü am Projektknoten, über den vorhandenen Einträgen und von ihnen getrennt: „Neue Tabelle“, „Neue Loc-Datei“. An einer Projektdatei (`ProjectFileNode`): „Umbenennen“, „Löschen“. An einem Pack-Eintrag (`EntryNode`, alle Quellen): „Ins Projekt übernehmen“. Ordnerknoten bekommen in diesem Plan keine Aktion.
- Jeder Eintrag nutzt das vorhandene Muster: `NodeAction` mit Grund, wenn deaktiviert (`ToolTipService.ShowOnDisabled`). „Ins Projekt übernehmen“ nennt ohne aktives Projekt „kein aktives Projekt“.
- Die Aktionen der Datei- und Eintragsknoten entstehen beim Aufbau der Knoten und werden bei `RefreshActions` nachgeführt wie die der Projekt- und Pack-Knoten (Zustandswechsel: aktives Projekt, beschäftigt). Für sehr große Packs (hunderttausende Einträge) entstehen die Aktionen nicht im Voraus: `EntryNode` und `ProjectFileNode` bauen ihre `Actions` erst beim Lesen (das Kontextmenü liest sie beim Öffnen) aus einer Fabrik, die der Explorer ihnen mitgibt, und bauen sie bei jedem Lesen neu, damit Grund und Zustand stimmen. `HasActions` ist für diese Knoten immer true. `RefreshActions` geht deshalb nicht über Datei- und Eintragsknoten.
- Menü „Projekt“: „Neue Tabelle“ und „Neue Loc-Datei“ für das aktive Projekt, unter „Pack importieren“. Werkzeugleiste: unverändert.
- `FilesChanged` des Controllers führt den Explorer nach: stilles Neu-Auflisten des betroffenen Projektknotens (`RefreshProjectFilesAsync`, Generations-Regel aus Plan 3 bleibt), ein zugeklapptes Projekt bleibt zu.
- `OpenRequested` öffnet die Datei über `MainViewModel.OpenProjectFileAsync`; das aktive Projekt wird dafür aufgeklappt, wenn es zu war, damit die neue Datei sichtbar ist.
- Die Statuszeile zeigt `ProjectFilesViewModel.StatusMessage` an derselben Stelle wie das Ergebnis der Projektbefehle (die jüngere Meldung gewinnt).
- Composition Root: ein `ProjectFilesController` und ein `ProjectFilesViewModel`, nur wenn es Projekte und einen `DocumentsController` gibt; `trashRoot` = `paths.TrashDirectory`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// ExplorerViewModelTests
[Fact] public void A_project_node_offers_new_table_and_new_loc_file()
[Fact] public void A_project_file_node_offers_rename_and_delete_with_reasons_when_disabled()
[Fact] public void An_entry_node_offers_adopt_and_names_the_missing_active_project()
[Fact] public void Entry_nodes_build_their_actions_only_when_asked()                         // open a pack with 10,000 entries: no action object exists until Actions is read on one node
[Fact] public void After_a_file_action_the_open_project_node_shows_the_new_file_and_a_closed_one_stays_closed()
[Fact] public void A_new_file_expands_its_project_and_opens_in_a_tab()
// MainWindowTests / MainViewModelTests
[Fact] public void The_project_menu_has_new_table_and_new_loc_file()
[Fact] public void The_status_line_shows_the_result_of_the_last_file_action()
```

- [ ] **Step 2: RED** — `apptests.ps1 ExplorerViewModelTests MainWindowTests MainViewModelTests`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `MainViewModelWorkspaceTests`, `ExplorerViewTests`, `ProjectFilesViewModelTests`, `ProjectFilesControllerTests`, `SessionCoordinatorTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: file operations in the explorer and the project menu`

---

### Task 7: Papierkorb aufräumen, Doku

**Files:**
- Modify: `src/TotalWarModTool.App/App.xaml.cs`, `Projects/ProjectFilesController.cs`; `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`, `Backlog.txt`, `CHANGELOG.md`
- Test: `ProjectFilesControllerTests.cs` (ergänzen)

- [ ] **Step 1: Failing Test schreiben**

```csharp
[Fact] public void CleanupTrashAsync_removes_entries_older_than_30_days_silently_and_logs_the_count()
// entries of 31 and 29 days in a temp trash, fake time: one removed; no runner call; a failing cleanup only logs
```

- [ ] **Step 2: RED**, **Step 3: Implementieren:** `ProjectFilesController.CleanupTrashAsync()` (still, Pool-Thread, `ProjectTrash.Cleanup(trashRoot, TimeSpan.FromDays(30), time, …)`, Ergebnis ins Log). Der Composition Root ruft es einmal nach dem Start auf, ohne den Start zu blockieren. **Step 4: GREEN** — `apptests.ps1 ProjectFilesControllerTests`; Library-Suite.
- [ ] **Step 5: Spec.**
  - Abschnitt 9: Plan 5 als umgesetzt beschreiben. Abschnitt 4 (Schreibgrenze): Papierkorb `%LocalAppData%\TotalWarModTool\trash\<Projekt-GUID>\` ausdrücklich nennen; Dateien unter `pack\` werden angelegt, umbenannt und in den Papierkorb verschoben, leere Ordner, die das Tool selbst angelegt hat, wieder entfernt.
  - In 5.4 ein Unterabschnitt „Festlegungen aus Plan 5“:
    1. Dateioperationen laufen erst und werden danach als Schritt in die History eingetragen (`Record`); Undo und Redo eines Dateischritts laufen im UI-Thread.
    2. Jeder Dateischritt ist ein Wechsel zwischen „Datei im Projekt“ und „Datei im Papierkorb“ bzw. zwischen zwei Namen. Nichts wird überschrieben.
    3. Offene Tabs: Löschen und Umbenennen schließen Tabs ohne Änderungen und lehnen bei ungespeicherten Änderungen ab; dasselbe gilt für Undo und Redo eines Dateischritts, der Schritt bleibt dann in der History.
    4. Umbenennen ändert nur den Namensteil und fasst den Inhalt nicht an; die Metazeile einer TSV wird beim nächsten Speichern neu geschrieben.
    5. Namen neuer Tabellen und Loc-Dateien: Regeln aus `NewFiles.NameProblem`, kein Punkt.
    6. Neue Tabelle: neueste Version des Schemas, nur Tabellen ohne nicht unterstützte Feldtypen. Loc-Dateien liegen unter `text/db` (Konvention des Pack-Formats, Konstante im Code).
    7. „Ins Projekt übernehmen“: Regeln pro Eintrag wie beim Import; gleicher Name wie der Quell-Eintrag braucht die Bestätigung; binär gebliebene Tabellen werden gemeldet.
    8. Papierkorb: Ordnername trägt die Zeit; Aufräumen nach 30 Tagen beim Start, still.
    9. Dateischritte eines Projekts, das die Liste verlässt, verlassen die History.
    10. Der Explorer baut die Aktionen von Datei- und Eintragsknoten erst beim Öffnen des Kontextmenüs.
  - Dazu alles, was die Umsetzung anders entscheidet als dieser Plan.
- [ ] **Step 6: Backlog.** Streichen: der offene Punkt „Plan 5“. Ergänzen: Aktionen an Ordnerknoten (neuer Ordner, Ordner umbenennen oder löschen); mehrere Dateien auf einmal löschen oder übernehmen; ganze Tabelle aus Vanilla mit Auswahl einzelner Zeilen übernehmen; Datei in einen anderen Ordner verschieben; Papierkorb ansehen und leeren; Undo und Redo großer Dateien über die Fortschritts-Komponente (laufen im UI-Thread); neue Tabelle in einer älteren Schema-Version.
- [ ] **Step 7: Changelog,** „Unreleased / Neu“, aus Nutzersicht: neue Tabelle und neue Loc-Datei im Projekt; Einträge aus dem Spiel oder aus Mods ins aktive Projekt übernehmen; Projektdateien umbenennen und löschen; alles über „Rückgängig“ zurücknehmbar; gelöschte Dateien bleiben 30 Tage im Papierkorb des Tools.
- [ ] **Step 8: Commit** — `feat: clean the trash at start and record plan 5 in the docs`

---

## Manuelle Abnahme durch den Nutzer

Vorbereitung: App starten, Spiel wird gefunden. Ein neues, leeres Projekt anlegen und als aktiv setzen.

1. Rechtsklick auf das Projekt: „Neue Tabelle“ und „Neue Loc-Datei“ stehen im Menü, ebenso im Menü „Projekt“.
2. „Neue Tabelle“: Im Filterfeld `unit` tippen, die Liste wird kürzer. Tabelle wählen, Namen lassen, OK. Die Tabelle öffnet sich als leerer, bearbeitbarer Tab; im Explorer steht sie unter `db\<tabelle>\`.
3. Im Dialog einen Namen mit Punkt, mit `\` und einen schon vergebenen Namen eintippen: OK bleibt gesperrt, der Grund steht darunter.
4. In der neuen Tabelle über die Werkzeugleiste eine Zeile einfügen, Werte eintragen, speichern.
5. „Neue Loc-Datei“: Datei entsteht unter `text\db\`, öffnet sich, Zeile einfügen, speichern.
6. In `db.pack` (Gruppe „Game“) eine kleine Tabelle suchen, Rechtsklick auf den Eintrag, „Ins Projekt übernehmen“: Namensfrage mit Vorschlag. OK: Die Tabelle steht im Projekt und öffnet sich bearbeitbar, mit allen Zeilen.
7. Dieselbe Tabelle noch einmal übernehmen und den Namen des Originals (`data__`) eintippen: zusätzliche Bestätigung mit Hinweis. „Nein“ führt zur Namensfrage zurück.
8. Ein Skript (`.lua`) aus einem Mod-Pack übernehmen: keine Namensfrage, gleiche Stelle im Projekt. Noch einmal übernehmen: Meldung „existiert schon“.
9. Ohne aktives Projekt (alle Projekte entfernen oder keines aktiv): „Ins Projekt übernehmen“ ist deaktiviert und nennt den Grund.
10. Rechtsklick auf eine Projektdatei, „Umbenennen“: neuer Name, Datei heißt im Explorer neu; war sie offen, ist der Tab unter dem neuen Namen wieder da.
11. Eine Datei öffnen, ändern (nicht speichern), „Umbenennen“ oder „Löschen“: abgelehnt mit Grund.
12. „Löschen“ einer unveränderten Datei: ohne Nachfrage weg, Tab geschlossen, Statuszeile nennt „Rückgängig“. `Strg+Z`: Datei ist wieder da.
13. Reihenfolge prüfen: Tabelle anlegen, Zeile einfügen, speichern, dann dreimal `Strg+Z`. Erwartung: erst die Zeile, dann (nach Schließen des unveränderten Tabs) verschwindet die Tabelle; mit `Strg+Y` kommt sie zurück.
14. Tabelle anlegen, Zelle ändern (nicht speichern), so oft `Strg+Z`, bis die Tabelle selbst an der Reihe wäre: Solange der Tab ungespeicherte Änderungen hat, kommt eine Meldung und die Datei bleibt.
15. Datei löschen, im Windows-Explorer am selben Ort eine neue Datei gleichen Namens anlegen, `Strg+Z`: Meldung, die neue Datei bleibt unverändert.
16. „Bauen und installieren“ mit der neuen und der übernommenen Tabelle, Spiel starten: Die Änderung wirkt.
17. `%LocalAppData%\TotalWarModTool\trash\` ansehen: Gelöschte Dateien liegen dort unter der Projekt-GUID in einem Ordner mit Datum.
18. Großes Pack aufklappen (z. B. `db.pack`): Aufklappen und Scrollen sind so schnell wie vorher.
19. Sprache umschalten und dunkles Theme: neue Menüeinträge, Dialoge und Meldungen wechseln und sind lesbar.
