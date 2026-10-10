# Teil 4.2a Plan 2: Library `Editing` (Snapshot, Schreibplan, Dateikopie, Schreiben, Undo-Schritt) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die Library `Editing` schreibt die Differenz eines Objekts (`ObjectChanges` aus Plan 1) in die Projektdateien des aktiven Projekts: Zieldatei pro Änderung nach den Regeln der Spec, Überschreibzeilen, Dateikopie beim Löschen fremder Zeilen, Rückfragen als Daten, atomares Schreiben mit Fingerprint-Prüfung und Rückschreiben, ein Undo-Schritt mit vorherigen Inhalten. Keine App-Änderung.

**Architecture:** Drei Stufen in `Editing`: `ProjectSnapshotReader.Read` (Side Effects: liest die Projektdateien der betroffenen Tabellen, die Loc-Dateien und die Pack-Einträge, die eine Dateikopie braucht), `ProjectWritePlan.Compute` (rein: Snapshot plus Änderungen plus Antworten → Dateien mit neuem Text, Rückfragen, Fehler), `ProjectWriter.Apply` (Side Effects: prüft Fingerprints, schreibt jede Datei atomar über `ProjectFileWriter` und `ProjectFileOps`, schreibt bei Fehler zurück, liefert `FileContentsStep`). `FileContentsStep` (`IUndoableAction`) hält pro Datei den vorherigen und den neuen Text. Vorbereitend in `Objects`: `RowChange.PreviousCells`, `RowLayout` (Abbildung zwischen Schemaversionen, aus `TableStore` herausgelöst), Leerzeichen am Ende bleiben erhalten.

**Tech Stack:** C# 12, .NET 8, xUnit 2.9.2. Kein neues NuGet-Paket (AvalonEdit kommt in Plan 3).

**Spec:** `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`: 4 (Schreibgrenze), 5.1 (Module), 5.4 (Zielregeln), 5.5 (Dateikopie, erster Teil), 5.6 (Schreiben und Undo), 7 (Fehlerfälle), 8 (Tests `Editing`, `UndoRedo`), 10 (Annahme 5), 11. Vorgänger: `docs/plans/2026-10-10-teil-4-2a-plan-1-objects.md` und Spec-Abschnitt „Festlegungen aus Plan 1“ unter 5.3. `Backlog.txt` Zeilen 263 und 265 (Leerzeichen am Ende, Farbkanal-Regel, Loc-Datei über Schlüssel, `OverriddenEntries`). Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-4-2a-editing` (von `master`, Stand `9dd82ee` oder neuer). Baseline vor der ersten Änderung festhalten: `dotnet build TotalWarModTool.slnx` ohne Warnung, `dotnet test tests/TotalWarModTool.Objects.Tests` und `dotnet test tests/TotalWarModTool.Editing.Tests` grün (Zahlen in den Status von Task 1 eintragen).

## Global Constraints

- Kein neues NuGet-Paket. `TreatWarningsAsErrors`, Nullable und `ImplicitUsings` sind an (`Directory.Build.props`).
- `Editing` bekommt die Projektreferenzen `Objects`, `Projects`, `PackFiles` (Spec 5.1; `Objects` referenziert `Editing` nicht, kein Zyklus). `Projects`, `UndoRedo`, `Tables`, `Schema`, `Storage` bleiben unverändert. `Objects` ändert sich nur in Task 1. Keine Library referenziert die App.
- Schreibgrenze (Spec 4): nur Dateien unter `pack\` des Projektordners, nur über `ProjectFileWriter.WriteText` (bestehende Datei) und `ProjectFileOps.Create` (neue Datei), nie eine Datei, die nicht zuvor gelesen und per Fingerprint geprüft wurde. Beim Undo wandert eine angelegte Datei über `ProjectTrash.MoveToTrash` in den Papierkorb. Vanilla-, Workshop- und fremde Packs werden nie geschrieben.
- Die Library loggt nicht und liefert keine UI-Texte: Fehler und Fragen sind Daten mit `Kind` und kurzem technischem Englisch (`Detail`); die App übersetzt nach `Kind`.
- Reihenfolgen deterministisch: Dateien im Plan ordinal nach `RelativeFile`, Zeilen werden angehängt in der Reihenfolge der Änderungen in `Rows` (Umsetzung: nur diese Reihenfolge, nicht nach `RowChange.Line` sortiert; `Delete` einer Tabelle vor `Set` und `Insert`, Review 2026-10-10). Tabellen- und Feldnamen `OrdinalIgnoreCase`, Pack-Pfade `OrdinalIgnoreCase` mit `/` (wie `SourceReader.PackPathOf`), Werte und Schlüssel ordinal.
- Relative Dateien im Plan sind unterhalb von `pack\` mit `\` (wie `ProjectFileItem.RelativeFile`, `FileStep.FilesAfterDo`). `RowProvenance.EntryPath` einer Projektzeile ist `pack\db\x_tables\a.tsv` (relativ zum Projektordner): das Präfix `pack\` wird abgeschnitten, Vergleich `OrdinalIgnoreCase`.
- Text wird als UTF-8 ohne BOM geschrieben, Zeilenenden wie `TableTsv.Write` sie liefert (`\n`). `TableTsv.Read` und `TableTsv.Write` sind der einzige Weg von Text zu Zeilen und zurück.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in `TempDir` (`tests/Shared/TempDir.cs`); Packs über `PackBuilder` (`tests/Shared`). Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`, nichts in Spiel-, Workshop- oder RPFM-Ordnern. Der Papierkorb der Tests ist ein Ordner im `TempDir`.
- Subagenten starten nur die Tests der Library, die sie ändern (`tests/TotalWarModTool.Objects.Tests`, `tests/TotalWarModTool.Editing.Tests`); die volle Suite läuft einmal am Ende mit dem Nutzer (Memory-Regel).
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Weicht die Umsetzung von der Spec ab, wird die Spec im selben Commit angepasst: Unterabschnitt `#### Festlegungen aus Plan 2` unter 5.6 (Task 2 legt ihn an). Die Tests in diesem Plan legen Namen und Erwartungen fest; wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Widerspricht eine Erwartung einer Regel des Plans oder der Spec, wird das gemeldet, nicht still entschieden.
- Kein Changelog-Eintrag: dieser Plan hat keine nutzersichtbare Änderung (Plan 3 trägt „Übernehmen“ ein).

## Review Focus

1. **Zieldatei in älterer Schemaversion** (Projektdatei `x_tables` v1 ohne das Feld `extra`, Überschreibzeile oder Insert aus der aktuellen v2). Erwartung: die Zeile wird über die Feldnamen auf die Spalten der Datei abgebildet, Felder, die die Datei nicht kennt, fallen weg; ein `Set`, das ein Feld ändert, das die Datei nicht hat, ist ein Fehler, nie ein stilles Weglassen. → Task 3 (`An_override_row_is_mapped_onto_the_columns_of_an_older_file`, `A_set_of_a_field_the_file_version_lacks_is_an_error`).
2. **Mehrdeutige Zeilen in der Datei** (Tabelle, deren einziger Schlüssel das Feld auf die Wurzel ist, zwei Zeilen mit gleichem Schlüssel; Tabelle ohne Schlüssel mit zwei identischen Zeilen). Erwartung: die Zeile wird über `PreviousCells` gefunden, bei identischen Zeilen die erste; nie werden beide geändert oder gelöscht. → Task 1 (`A_set_and_a_delete_carry_the_previous_cells`), Task 4 (`A_row_whose_only_key_is_the_root_field_is_found_by_its_previous_cells`, `Of_two_identical_keyless_rows_only_the_first_is_deleted`).
3. **Überschreibkette über Projekt, Mod und Vanilla** (Projektzeile überschreibt Mod-Zeile überschreibt Vanilla-Zeile, `Delete`). Erwartung: Zeile aus der Projektdatei entfernt, je eine Kopie der Mod- und der Vanilla-Datei mit entfernter Zeile, beide Kopien in der Rückfrage; dieselbe Quelldatei bei zwei Löschungen ergibt eine Kopie. → Task 4 (`Deleting_a_row_that_overrides_two_layers_copies_both_files`, `Two_deletes_from_the_same_source_file_make_one_copy`).
4. **Datei zwischen Snapshot und Schreiben verändert oder gesperrt** (Fingerprint abweichend; zweite Datei gesperrt). Erwartung: bei Abweichung wird nichts geschrieben; bei Sperre der zweiten Datei wird die erste zurückgeschrieben und die Meldung nennt jede Datei mit Zustand. → Task 7 (`A_changed_fingerprint_aborts_before_anything_is_written`, `A_locked_second_file_rolls_the_first_back`).
5. **Undo mit seither geänderter oder gelöschter Datei** (Datei nach dem Übernehmen im Raster gespeichert; angelegte Datei gelöscht). Erwartung: der Schritt bleibt in der History (`StepDeferredException`), nichts wird geschrieben, die Meldung nennt die Datei; eine angelegte Datei wandert beim Undo in den Papierkorb und kommt beim Redo zurück. → Task 6 (`Undo_leaves_the_step_when_a_file_changed_since`, `Undo_leaves_the_step_when_a_created_file_is_gone`, `Undo_moves_a_created_file_to_the_trash_and_redo_brings_it_back`).

---

### Task 1: `Objects` vorbereiten (`RowChange.PreviousCells`, `RowLayout`, Leerzeichen am Ende)

Vier kleine Änderungen in `Objects`, die der Schreibplan braucht (Backlog 263 und 265, Spec 5.4 „wie beim Zusammenführen in `TableStore`“, Spec 5.5 „jede dieser Schichten“).

**Files:**
- Modify: `src/TotalWarModTool.Objects/ObjectChanges.cs` (`RowChange.PreviousCells`)
- Modify: `src/TotalWarModTool.Objects/ObjectDiff.cs` (`PreviousCells` füllen; Leerzeichen am Ende erhalten)
- Create: `src/TotalWarModTool.Objects/RowLayout.cs` (`MapFields`, `Remap`, `IsIdentity` aus `TableStore`)
- Modify: `src/TotalWarModTool.Objects/TableStore.cs` (ruft `RowLayout`, eigene Kopien entfernt; setzt `OverriddenLayers`)
- Modify: `src/TotalWarModTool.Objects/MergedTable.cs` (`RowProvenance.OverriddenLayers`)
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectDiffTests.cs`, `tests/TotalWarModTool.Objects.Tests/RowLayoutTests.cs`, `tests/TotalWarModTool.Objects.Tests/FileReplacementTests.cs`
- Modify: `Backlog.txt` (Zeile 263 entfernen, erledigt)

**Interfaces:**
- Consumes: `RowChange`, `ObjectDiff.Compute`, `TableStore.MapFields`/`Remap`/`IsIdentity`/`Convert` (private, werden verschoben), `RowProvenance.OverriddenEntries` (Plan 1 Task 5), `CellText.Format`, `CellText.TryParse`, `CellText.DefaultValue`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

/// <summary>... PreviousCells holds every field of the baseline row in schema order as text (Set, Delete); null for Insert.</summary>
public sealed record RowChange(RowChangeKind Kind, string Table, string? RootField, string? RootValue, IReadOnlyList<string> Cells,
    IReadOnlyList<string> ChangedFields, RowProvenance? Provenance, int Line, IReadOnlyList<string>? PreviousCells = null);

/// <summary>One overridden layer of a row: the source and the file (pack entry, or project file below the project folder) that held the key.</summary>
public sealed record OverriddenLayer(TableSource Source, string EntryPath);

public sealed record RowProvenance(...)
{
    /// <summary>Every file of every source in Overridden that held the key, earliest first; a source with several files gives several layers. Empty for rows nothing overrides.</summary>
    public IReadOnlyList<OverriddenLayer> OverriddenLayers { get; init; } = [];

    /// <summary>The entry paths of OverriddenLayers, in order (kept for the readers of Plan 1).</summary>
    public IReadOnlyList<string> OverriddenEntries => OverriddenLayers.Select(l => l.EntryPath).ToList();
}

/// <summary>Maps a row between two definitions of one table by field name, as the store does when it merges layouts.</summary>
public static class RowLayout
{
    /// <summary>For every target field the index of the field of the same name (OrdinalIgnoreCase) in source, -1 when none. Null when a target field has an unsupported type.</summary>
    public static int[]? MapFields(TableDefinition target, TableDefinition source);

    /// <summary>True when mapping is the identity and every type matches: Remap would copy the row.</summary>
    public static bool IsIdentity(int[] mapping, TableDefinition target, TableDefinition source);

    /// <summary>The row in the target layout: same-named fields copied (a changed type through its text, the default when the text does not fit), the rest CellText.DefaultValue.</summary>
    public static object?[] Remap(TableDefinition target, TableDefinition source, int[] mapping, object?[] row);
}
```

Regeln:

- `PreviousCells` für `Set` und `Delete`: die Baseline-Zellen als Text (`CellText.Format`), exakt wie gespeichert (ohne `TrimEnd`), in Schemareihenfolge der aktuellen Definition. `Insert`: null. Der Parameter hat einen Standardwert, damit bestehende Aufrufe und Tests bauen.
- Leerzeichen am Ende (Backlog 263): Endet der gespeicherte Wert auf Whitespace (`char.IsWhiteSpace`) und der geparste Wert ist gleich dem gespeicherten ohne dieses Ende, gilt er als unverändert (bisher). Endet der gespeicherte Wert auf Whitespace, ist der geparste Wert ein anderer und endet nicht selbst auf Whitespace, hängt `Compute` das alte Ende (`baseline[baseline.TrimEnd().Length..]`) an den neuen Wert, bevor er in `Cells` kommt. Loc-Texte unverändert (dort gibt es kein Abschneiden).
- `RowLayout` übernimmt die drei privaten Funktionen aus `TableStore` unverändert (`Convert` bleibt privat in `RowLayout`); `TableStore` ruft `RowLayout.MapFields`, `RowLayout.IsIdentity`, `RowLayout.Remap`. Verhalten der Store-Tests unverändert.
- `OverriddenLayers`: `TableStore` kennt beim Überschreiben die Quelle jedes Eintragspfads (`KeyState.History` und `HistoryEntries` laufen parallel) und setzt statt `OverriddenEntries` jetzt `OverriddenLayers` mit `(Quelle, Pfad)`. `OverriddenEntries` bleibt als abgeleitete Eigenschaft, damit die Tests aus Plan 1 unverändert bauen. Grund: zwei Mod-Packs haben Eintragspfade mit demselben Präfix (`db/...`); ohne die Quelle am Pfad kann Plan 2 eine Dateikopie nicht dem richtigen Pack zuordnen.

- [ ] **Step 1: Failing Tests** in `ObjectDiffTests.cs` (Fixture wie die bestehenden Diff-Tests, `BlockFixture`):

```csharp
[Fact] public void A_set_and_a_delete_carry_the_previous_cells()
// Baseline-Zeile create_cost "100", Text ändert auf "200": RowChange Set mit PreviousCells == alle Baseline-Zellen (create_cost "100"), Cells create_cost "200".
// Zeile im Text entfernt: Delete mit PreviousCells == Cells (Baseline). Insert: PreviousCells null.

[Fact] public void A_trailing_space_of_the_stored_value_survives_an_edit()
// Baseline-Wert note "alpha " (Leerzeichen am Ende), Text "note = beta": Set mit Cells[note] == "beta ", ChangedFields enthält note.
// Baseline "alpha ", Text "note = alpha": keine Änderung (bestehende Regel bleibt).
// Baseline "alpha ", Text "note = beta " kann der Block nicht liefern (er schneidet ab); Compute bekommt "beta" -> "beta ".
```

und `RowLayoutTests.cs`:

```csharp
[Fact] public void Fields_are_mapped_by_name_and_missing_ones_get_defaults()
// target v2 Felder key, extra(I32), note; source v1 Felder note, key: MapFields == [1, -1, 0]; Remap(["n", "k"]) == ["k", 0, "n"].

[Fact] public void A_changed_type_goes_through_its_text()
// target Feld n I32, source Feld n StringU8 "12" -> 12; "abc" -> 0 (Default).

[Fact] public void Identity_is_recognised_only_for_equal_layouts()
// gleiche Definition: IsIdentity true; eine Spalte vertauscht oder Typ anders: false.
```

und in `FileReplacementTests.cs` neben `Overridden_layers_keep_their_entry_paths`:

```csharp
[Fact] public void Overridden_layers_carry_their_source_with_the_entry_path()
// Vanilla v.pack (db/x_tables/v), Mod m.pack (db/x_tables/m), Projektdatei a.tsv, alle mit k1: Projektzeile hat OverriddenLayers == [(v.pack, "db/x_tables/v"), (m.pack, "db/x_tables/m")]; OverriddenEntries == die beiden Pfade.
```

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.Objects.Tests --filter "FullyQualifiedName~RowLayoutTests|FullyQualifiedName~A_set_and_a_delete_carry|FullyQualifiedName~A_trailing_space"`: Build-Fehler (`RowLayout`, `PreviousCells` fehlen).

- [ ] **Step 3: Implementieren** – `RowLayout.cs` (Funktionen aus `TableStore` verschieben, `public static`), `OverriddenLayer` und `OverriddenLayers` in `MergedTable.cs`, `TableStore` umstellen (`KeyState` hält Paare), `RowChange` erweitern, `ObjectDiff`: an den Stellen, die `RowChange` für `Set` und `Delete` bauen, die Baseline-Zellen als Text mitgeben; die Whitespace-Regel neben der bestehenden `TrimEnd`-Gleichheit (Zeile ~483) als `KeepTrailingWhitespace(baseline, parsed)`.

- [ ] **Step 4: GREEN** – `dotnet test tests/TotalWarModTool.Objects.Tests` ganz (Echtdaten-Tests übersprungen ohne `TWMT_WH3_DIR`); `dotnet build TotalWarModTool.slnx` ohne Warnung.

- [ ] **Step 5: Backlog** – Zeile 263 (Leerzeichen am Ende) aus `Backlog.txt` entfernen; Zeile 265 bleibt bis Task 8.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.Objects/ObjectChanges.cs src/TotalWarModTool.Objects/ObjectDiff.cs src/TotalWarModTool.Objects/RowLayout.cs src/TotalWarModTool.Objects/TableStore.cs src/TotalWarModTool.Objects/MergedTable.cs tests/TotalWarModTool.Objects.Tests/ObjectDiffTests.cs tests/TotalWarModTool.Objects.Tests/RowLayoutTests.cs tests/TotalWarModTool.Objects.Tests/FileReplacementTests.cs Backlog.txt`, `git commit -m "feat: carry the previous cells, the trailing whitespace and the overridden layers through the diff and share the row layout mapping"`.

---

### Task 2: Modell und Snapshot (`ProjectSnapshot`, `ProjectSnapshotReader`)

Die Daten, mit denen der Schreibplan rechnet, und der eine Leser dafür. Spec 5.4 (erster Absatz), 5.5 (Quelldatei lesen), 7 („Quelldatei der fremden Zeile nicht lesbar“, „Loc-Datei des Projekts mit Diagnose“).

**Files:**
- Modify: `src/TotalWarModTool.Editing/TotalWarModTool.Editing.csproj` (Referenzen `Objects`, `Projects`, `PackFiles`)
- Modify: `tests/TotalWarModTool.Editing.Tests/TotalWarModTool.Editing.Tests.csproj` (Referenzen `Objects`, `Projects`, `PackFiles`)
- Create: `src/TotalWarModTool.Editing/WriteModel.cs` (alle Records und Enums dieses Plans außer `FileContentsStep`)
- Create: `src/TotalWarModTool.Editing/ProjectSnapshotReader.cs`
- Create: `tests/TotalWarModTool.Editing.Tests/WriteFixture.cs` (Mini-Schema, Graph, Helfer zum Schreiben von Projektdateien und Packs im `TempDir`)
- Test: `tests/TotalWarModTool.Editing.Tests/ProjectSnapshotReaderTests.cs`
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Unterabschnitt `#### Festlegungen aus Plan 2` unter 5.6 anlegen)

**Interfaces:**
- Consumes: `ObjectChanges`, `RowChange` (`Provenance.Source`, `Provenance.EntryPath`, `Provenance.OverriddenLayers` aus Task 1), `TableSource`, `SourceKind`, `ProjectFiles.List`, `ProjectFileItem`, `ProjectLayout.FromRelativeFile`, `PackFolderPaths.Resolve`, `FileFingerprint.TryRead`, `TableTsv.Read`/`ReadLoc`, `TsvTable`, `TsvLoc`, `PackReader.ReadIndex`/`ReadEntry`, `PackIndex.Find`, `NewFiles.Adopt`, `DbTablePath.TryGetTableName`.
- Produces:

```csharp
namespace TotalWarModTool.Editing;

/// <summary>Answers whether a Vanilla or Mod pack of the load order holds an entry with this pack path (with '/', compared OrdinalIgnoreCase). A project file whose pack path it answers true for is a copy (Spec 5.4).</summary>
public delegate bool PackEntryExists(string packPath);

/// <summary>What the snapshot must hold for one ObjectChanges: the tables whose project files are read, whether loc files are read, and the pack entries that a file copy needs.</summary>
public sealed record SnapshotRequest(IReadOnlySet<string> Tables, bool Loc, IReadOnlyList<PackEntryRequest> PackEntries)
{
    /// <summary>Tables of every RowChange (OrdinalIgnoreCase); Loc when Texts is not empty; for every Delete one PackEntryRequest per distinct (Source.Path, EntryPath): its own Provenance.Source with Provenance.EntryPath when the source is not Project, and every OverriddenLayer whose Source is not Project. Duplicates removed, order of first appearance.</summary>
    public static SnapshotRequest For(ObjectChanges changes);
}

public sealed record PackEntryRequest(TableSource Source, string EntryPath);

/// <summary>A project table file: RelativeFile below pack\ with '\', its pack path, fingerprint, text and parsed table. IsCopy: a Vanilla or Mod pack of the load order has an entry with the same pack path.</summary>
public sealed record SnapshotTableFile(string RelativeFile, string PackPath, FileFingerprint Fingerprint, string Text, TsvTable Table, bool IsCopy);

public sealed record SnapshotLocFile(string RelativeFile, string PackPath, FileFingerprint Fingerprint, string Text, TsvLoc Loc, bool IsCopy);

/// <summary>A pack entry adopted as a project file would be (NewFiles.Adopt, version of the source): RelativeFile is where the copy goes.</summary>
public sealed record SnapshotPackEntry(TableSource Source, string EntryPath, string RelativeFile, TsvTable Table);

public enum SnapshotProblemKind { ProjectFileUnreadable, ProjectFileInvalid, PackUnreadable, PackEntryMissing, PackEntryUnreadable, PackEntryNotAdoptable }

/// <summary>File is the project file (relative, '\') or the pack entry path; Source the pack for pack problems. Detail is short technical English.</summary>
public sealed record SnapshotProblem(SnapshotProblemKind Kind, string File, TableSource? Source, string Detail);

/// <summary>The read state the plan computes from. Tables holds every project table file of every requested table (copies included), Locs every project loc file when requested, PackEntries one per request that could be read. Problems: anything that could not be read; the plan then refuses (nothing is written on a problem).</summary>
public sealed record ProjectSnapshot(IReadOnlyList<SnapshotTableFile> Tables, IReadOnlyList<SnapshotLocFile> Locs,
    IReadOnlyList<SnapshotPackEntry> PackEntries, IReadOnlyList<SnapshotProblem> Problems)
{
    public static readonly ProjectSnapshot Empty = new([], [], [], []);
}

public static class ProjectSnapshotReader
{
    /// <summary>Reads the project files of the requested tables (every file below pack\db\&lt;table&gt;\ that ProjectFiles.List reports as Table), the loc files (Kind Loc) when requested, and the requested pack entries. Reads only; throws only for arguments and cancellation.</summary>
    public static ProjectSnapshot Read(string projectFolder, GameSchema schema, SnapshotRequest request, PackEntryExists isPackEntry, CancellationToken cancellationToken);
}
```

Regeln:

- Projektdateien einer Tabelle: `ProjectFiles.List(projectFolder, ct).Files` mit `Kind == Table` und `DbTablePath.TryGetTableName(PackPath)` gleich der Tabelle (`OrdinalIgnoreCase`). Fehlt der Pack-Ordner: leere Listen, kein Problem. `Problems` der Liste (nicht lesbare Ordner) werden zu `ProjectFileUnreadable` mit dem Ordner als `File`.
- Jede Datei: Fingerprint über `FileFingerprint.TryRead` auf dem aufgelösten Pfad (`PackFolderPaths.Resolve(projectFolder, relativeFile).FullPath`), Text mit `File.ReadAllText` (UTF-8), `TableTsv.Read(text, schema)`; eine Diagnose ist `ProjectFileInvalid` mit `TsvDiagnosis.Detail` und Zeile im Detail. Eine Datei, deren Meta-Zeile eine andere Tabelle nennt als ihr Ordner, ist `ProjectFileInvalid` (wie `SourceReader.ReadTable`). Lesefehler (`IOException`, `UnauthorizedAccessException`): `ProjectFileUnreadable`.
- `IsCopy = isPackEntry(PackPath)`.
- Loc: alle Dateien mit `Kind == Loc`, `TableTsv.ReadLoc`.
- Pack-Einträge: `PackReader.ReadIndex(source.Path)` einmal pro Pack (Dictionary nach Pfad); `Index is null` → `PackUnreadable` mit `Detail`. `index.Find(entryPath)` null → `PackEntryMissing`. `PackReader.ReadEntry(index, entry, ct)` mit `Bytes is null` → `PackEntryUnreadable`. `NewFiles.Adopt(entryPath, bytes, schema, newName: null)`: `Reason` gesetzt oder `File.Kind != Table` → `PackEntryNotAdoptable`; sonst Text = UTF-8 des `Content`, `TableTsv.Read` → `Table` (Diagnose → `PackEntryNotAdoptable`). `RelativeFile = File.RelativeFile`.
- Der Leser liest nichts unter dem Projektordner außer `pack\` und schreibt nichts.

- [ ] **Step 1: Fixture** `WriteFixture.cs`: Schema über `TestSchema`-Muster (eigene Kopie in `Editing.Tests`, da `TestSchema` intern in `Objects.Tests` ist): Tabelle `x_tables` v1 (`key` StringU8 Key, `building` StringU8 Ref `building_levels_tables.key`, `note` StringU8) und v2 (v1 plus `extra` I32); `y_tables` (keyless: `building` Ref, `a`, `b`); `z_tables` (`building` Ref als einziger Schlüssel, `value` I32); `colour_tables` (`key` Key, `c_r`, `c_g`, `c_b` I32 Farbgruppe); `building_levels_tables` (`key` Key, `create_cost` I32). Graph `ReferenceGraph.Build(schema)`. Helfer: `WriteProjectTable(folder, table, version, name, rows)` und `WriteProjectLoc(folder, name, rows)` wie `TestSources` in `Objects.Tests`; `WritePack(path, (name, table, version, rows)[])` über `PackBuilder` und `DbTableEncoder.Encode`; `Vanilla(path)`, `Mod(path)`, `Project(folder)` als `TableSource`; `Change(kind, table, cells, previous, provenance, line)` als Kurzform für `RowChange`.

- [ ] **Step 2: Failing Tests** in `ProjectSnapshotReaderTests.cs`:

```csharp
[Fact] public void The_request_lists_tables_loc_and_the_pack_entries_of_foreign_deletes()
// Changes: Set x_tables (Projekt), Delete y_tables aus Vanilla v.pack Eintrag db/y_tables/data__, Delete x_tables aus Mod m.pack (EntryPath db/x_tables/m) mit OverriddenLayers [(v.pack, "db/x_tables/data__")], ein TextChange:
// Tables == {x_tables, y_tables}, Loc true, PackEntries == [(v.pack, db/y_tables/data__), (m.pack, db/x_tables/m), (v.pack, db/x_tables/data__)] ohne Doppelte.

[Fact] public void Every_project_file_of_a_requested_table_is_read_with_text_and_fingerprint()
// Projekt mit db\x_tables\a.tsv (v2, 1 Zeile), db\x_tables\b.tsv (v1, 2 Zeilen), db\y_tables\c.tsv; Request Tables {x_tables}: Tables hat a und b (ordinal sortiert), Fingerprint.Length == Dateigröße, Text == Dateiinhalt, Table.Version 2 und 1, Table.Rows.Count 1 und 2; y fehlt; Problems leer.

[Fact] public void A_project_file_whose_pack_path_is_in_a_pack_is_a_copy()
// isPackEntry gibt true für "db/x_tables/data__": Datei db\x_tables\data__.tsv IsCopy true, db\x_tables\a.tsv false.

[Fact] public void A_missing_pack_folder_gives_an_empty_snapshot()
// Projektordner ohne pack\: Tables, Locs, Problems leer.

[Fact] public void An_invalid_project_file_is_a_problem_not_an_exception()
// db\x_tables\a.tsv mit Text "garbage": Problems == [ProjectFileInvalid, File "db\x_tables\a.tsv"], Tables leer.

[Fact] public void Loc_files_are_read_only_when_requested()
// text\db\p.loc.tsv mit 2 Zeilen: Request Loc false -> Locs leer; Loc true -> eine SnapshotLocFile mit Loc.Rows.Count 2, PackPath "text/db/p.loc".

[Fact] public void A_pack_entry_is_adopted_in_its_own_version()
// Vanilla-Pack mit db/x_tables/data__ in v1, 3 Zeilen: PackEntries[0].RelativeFile == @"db\x_tables\data__.tsv", Table.Version == 1, Table.Rows.Count == 3, Table.PackPath == "db/x_tables/data__".

[Fact] public void A_missing_pack_or_entry_is_a_problem()
// Pfad ohne Datei: PackUnreadable mit Source; Pack ohne den Eintrag: PackEntryMissing mit File == entryPath.
```

- [ ] **Step 3: RED** – `dotnet test tests/TotalWarModTool.Editing.Tests --filter "FullyQualifiedName~ProjectSnapshotReaderTests"`: Build-Fehler (Typen fehlen).

- [ ] **Step 4: Implementieren** – csproj-Referenzen, `WriteModel.cs` mit den Records oben (die Records der Tasks 3 bis 5 kommen dort dazu), `ProjectSnapshotReader.Read` und `SnapshotRequest.For`.

- [ ] **Step 5: GREEN** – Filter wie oben; dann `dotnet test tests/TotalWarModTool.Editing.Tests` ganz; `dotnet build TotalWarModTool.slnx` ohne Warnung.

- [ ] **Step 6: Spec** – `#### Festlegungen aus Plan 2` unter 5.6 anlegen mit: `ProjectSnapshot` hält Text und geparste Tabelle jeder Projektdatei der betroffenen Tabellen (Kopien eingeschlossen, markiert) und die adoptierten Pack-Einträge für Dateikopien; die Kopie-Erkennung bekommt die App als `PackEntryExists`; der Leser liest Pack-Einträge über `PackReader` direkt (ohne Retry; ein gesperrtes Pack ist ein Fehler vor dem Schreiben, Spec 7).

- [ ] **Step 7: Commit** – `git add src/TotalWarModTool.Editing/TotalWarModTool.Editing.csproj src/TotalWarModTool.Editing/WriteModel.cs src/TotalWarModTool.Editing/ProjectSnapshotReader.cs tests/TotalWarModTool.Editing.Tests/TotalWarModTool.Editing.Tests.csproj tests/TotalWarModTool.Editing.Tests/WriteFixture.cs tests/TotalWarModTool.Editing.Tests/ProjectSnapshotReaderTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: read the project files and pack entries an object write needs into a snapshot"`.

---

### Task 3: Schreibplan für Tabellen (`ProjectWritePlan`: Set, Insert, Zielregeln, Rückfrage)

Spec 5.4 für Tabellenänderungen ohne Löschen. Rein.

**Files:**
- Modify: `src/TotalWarModTool.Editing/WriteModel.cs` (Plan-Records)
- Create: `src/TotalWarModTool.Editing/ProjectWritePlan.cs`
- Test: `tests/TotalWarModTool.Editing.Tests/ProjectWritePlanTests.cs`

**Interfaces:**
- Consumes: `ProjectSnapshot` (Task 2), `RowChange` mit `PreviousCells` und `RowLayout` (Task 1), `ReferenceGraph.Latest`/`KeyFields`, `CellText.TryParse`/`Format`/`DefaultValue`/`ColourChannelFields`/`IsColourChannelValue`, `TableTsv.Write`, `NewFiles.NameProblem`, `NewFiles.Table` (Pfad einer neuen Datei), `ProjectLayout.ToRelativeFile`.
- Produces:

```csharp
namespace TotalWarModTool.Editing;

public enum PlannedFileKind { Changed, Created, Copy }

/// <summary>One file the writer will write. Previous is the fingerprint of the snapshot (null for Created and Copy: the file does not exist), PreviousText its text (null then). SourceEntry names the pack entry a Copy comes from.</summary>
public sealed record PlannedFile(string RelativeFile, PlannedFileKind Kind, FileFingerprint? Previous, string? PreviousText, string NewText, string? SourceEntry = null);

/// <summary>Several project files of one table (or, with IsLoc, several loc files) could take the change: the App asks and answers with one of Candidates (relative files, ordinal). Lines are the block lines of the changes that wait for it.</summary>
public sealed record TargetFileQuestion(string Table, bool IsLoc, IReadOnlyList<string> Candidates, IReadOnlyList<int> Lines);

/// <summary>Deleting a foreign row copies its file: the App shows the copy (RelativeFile), the source (Source, EntryPath), the row count of the source and the keys (JoinedKey of the key fields, or the first cell for a keyless table) that the copy will lack, and asks once for all copies.</summary>
public sealed record FileCopyQuestion(TableSource Source, string EntryPath, string RelativeFile, int RowCount, IReadOnlyList<string> DeletedKeys);

/// <summary>TargetFiles maps the Table of a TargetFileQuestion (or LocKey for the loc question) to the chosen relative file. CopiesConfirmed true answers every FileCopyQuestion.</summary>
public sealed record WriteAnswers(IReadOnlyDictionary<string, string> TargetFiles, bool CopiesConfirmed)
{
    public const string LocKey = "*loc";
    public static readonly WriteAnswers None = new(new Dictionary<string, string>(), false);
}

public enum WriteErrorKind
{
    SnapshotProblem, KeyAlreadyInFile, RowNotInFile, FieldNotInFileVersion, InvalidValue, ColourChannelOutOfRange, TableNotWritable, NoAnswer,
}

/// <summary>Line is the block line of the change (0 when none applies), RelativeFile the file (null when none applies). Detail is short technical English.</summary>
public sealed record WriteError(WriteErrorKind Kind, int Line, string? RelativeFile, string Detail);

public sealed record WriteSummary(int RowsSet, int RowsInserted, int RowsDeleted, int TextsSet, int TextsInserted, int FilesChanged, int FilesCreated, int Copies);

/// <summary>Files is empty while Questions or Errors are not: nothing is written then. Files are sorted ordinally by RelativeFile.</summary>
public sealed record WritePlan(IReadOnlyList<PlannedFile> Files, IReadOnlyList<TargetFileQuestion> TargetQuestions,
    IReadOnlyList<FileCopyQuestion> CopyQuestions, IReadOnlyList<WriteError> Errors, WriteSummary Summary)
{
    public bool HasQuestions => TargetQuestions.Count > 0 || CopyQuestions.Count > 0;
    public bool HasErrors => Errors.Count > 0;
}

public static class ProjectWritePlan
{
    /// <summary>The fallback name part of a new file when the project name is not a valid name (Spec 5.4 rule 3).</summary>
    public const string FallbackFileName = "twmt";

    /// <summary>Pure. Throws ArgumentException when changes.HasErrors. Computes twice in practice: once with WriteAnswers.None to collect the questions, once with the answers.</summary>
    public static WritePlan Compute(ObjectChanges changes, ProjectSnapshot snapshot, GameSchema schema, ReferenceGraph graph, string projectName, WriteAnswers answers);
}
```

Regeln (Spec 5.4, Festlegungen):

- Snapshot-Probleme: jedes `SnapshotProblem` wird ein `WriteError(SnapshotProblem, 0, File, Detail)`; mit Fehlern ist `Files` leer.
- Zieldatei einer Tabellenänderung (`Set`, `Insert`): 1. `Provenance.Source.Kind == Project`: die Datei aus `Provenance.EntryPath` ohne `pack\`; fehlt sie im Snapshot, ist das `RowNotInFile`. 2. Sonst genau eine Datei der Tabelle im Snapshot mit `IsCopy == false`: diese. 3. Sonst keine: neue Datei `db\<tabelle>\<name>.tsv` mit `<name>` = `projectName.Trim()`, wenn `NewFiles.NameProblem` dafür null ist, sonst `FallbackFileName`; Pfad über `NewFiles.Table(schema, table, name)` (`Reason` gesetzt → `TableNotWritable`); gibt es die Datei schon im Snapshot (als Kopie), wird sie trotzdem genommen (Spec: „nimmt das Tool sie“). Eine neue Datei entsteht in der neuesten Schemaversion (`graph.Latest`). 4. Sonst mehrere: `answers.TargetFiles[table]` wenn vorhanden und einer der Kandidaten, sonst eine `TargetFileQuestion(table, false, Kandidaten ordinal, Zeilen)`; eine Antwort, die kein Kandidat ist, ist `NoAnswer`.
- `Set` einer Projektzeile: Zeile in der Datei finden (unten), die `ChangedFields` ersetzen: jedes Feld über den Namen auf die Dateidefinition abbilden; fehlt es dort, `FieldNotInFileVersion` (Detail nennt Feld und Version). Wert über `CellText.TryParse(typ, text)`; Fehler `InvalidValue`.
- `Set` einer Vanilla- oder Mod-Zeile (Überschreibzeile) und `Insert`: `Cells` (aktuelle Version) über `RowLayout.MapFields(dateiDefinition, aktuelleDefinition)` auf die Datei abbilden: Zelle aus dem Text geparst, fehlende Felder `CellText.DefaultValue`. Die Zeile wird angehängt. Hat die Tabelle Schlüsselfelder (`graph.KeyFields(table)`, als Namen) und die Datei schon eine Zeile mit denselben Schlüsselwerten: `KeyAlreadyInFile` (Detail: Schlüssel und Datei). Tabellen ohne Schlüsselfelder prüfen nichts.
- Zeile in der Datei finden (für `Set` einer Projektzeile; Task 4 nutzt dieselbe Funktion für `Delete`): jede Dateizeile über `RowLayout.Remap(aktuell, dateiDefinition, mapping, zeile)` in die aktuelle Version bringen und formatieren (`CellText.Format`). Mit Schlüsselfeldern: Kandidaten sind die Zeilen, deren Schlüsselwerte (als Text) den Schlüsselwerten von `PreviousCells` gleichen (ordinal); genau einer: der; mehrere: der erste, dessen ganze Zellen `PreviousCells` gleichen, sonst der erste Kandidat; keiner: `RowNotInFile`. Ohne Schlüsselfelder: die erste Zeile, deren ganze Zellen `PreviousCells` gleichen; keine: `RowNotInFile`. `PreviousCells == null` bei `Set` oder `Delete` ist ein Programmfehler (`ArgumentException`).
- Farbkanal (Backlog 265): für jedes `Set`- und `Insert`-Feld, dessen Index in `CellText.ColourChannelFields(aktuelleDefinition)` liegt, muss der geparste Wert `CellText.IsColourChannelValue` erfüllen, sonst `ColourChannelOutOfRange` mit Zeile und Feld. Lehnt `TableTsv.Write` dennoch ab (`Text == null`): `TableNotWritable` mit `Detail`.
- Neuer Text einer Datei: `TableTsv.Write(table, dateiVersion, dateiPackPfad, dateiDefinition, zeilen)`; für neue Dateien `PackPath = db/<tabelle>/<name>`. Eine Datei, deren Zeilen sich nicht ändern, kommt nicht in `Files`.
- Summary: `RowsSet` zählt `Set`-Änderungen (Überschreibzeile und Projektzeile), `RowsInserted` die `Insert`, `FilesChanged` die `PlannedFile` mit `Kind == Changed`, `FilesCreated` die `Created`.
- Solange eine Frage offen ist (`TargetQuestions` oder `CopyQuestions` nicht leer), ist `Files` leer; die Fragen werden alle gesammelt (eine pro Tabelle), nicht nur die erste.

- [ ] **Step 1: Failing Tests** in `ProjectWritePlanTests.cs` (Snapshot direkt gebaut: `SnapshotTableFile` aus `TableTsv.Write`-Text plus `TableTsv.Read`, Fingerprint beliebig, zum Beispiel `new FileFingerprint(relativeFile, text.Length, DateTime.UnixEpoch)`; Helfer `Snapshot(params SnapshotTableFile[])`, `TableFile(relativeFile, table, version, rows, isCopy = false)`, `Rows(string text)` liest den Plan-Text zurück):

```csharp
[Fact] public void A_set_of_a_project_row_replaces_the_changed_fields_in_its_file()
// Datei db\x_tables\a.tsv v2 Zeilen [k1,b1,"n",1], [k2,b1,"m",2]; Set k1 note "x" (ChangedFields [note], PreviousCells [k1,b1,n,1], Provenance Project EntryPath @"pack\db\x_tables\a.tsv"):
// Files == eine Changed-Datei a.tsv, Previous == Fingerprint des Snapshots, PreviousText == alter Text, Rows(NewText) == [k1,b1,x,1],[k2,b1,m,2]; Summary RowsSet 1, FilesChanged 1.

[Fact] public void An_override_row_of_a_vanilla_row_is_appended_to_the_only_project_file()
// Datei a.tsv v2 mit [k2,...]; Set k1 (Provenance Vanilla) Cells [k1,b1,"x",5]: NewText hat 2 Zeilen, letzte == [k1,b1,x,5].

[Fact] public void An_override_row_is_mapped_onto_the_columns_of_an_older_file()
// Datei a.tsv in v1 (key, building, note); Set k1 Cells [k1,b1,"x",5] (v2 mit extra): angehängte Zeile == [k1,b1,x], extra fällt weg; Version der Datei bleibt 1 (Meta-Zeile "#x_tables;1;...").

[Fact] public void A_set_of_a_field_the_file_version_lacks_is_an_error()
// Projektzeile in v1-Datei, Set ChangedFields [extra]: Errors == [FieldNotInFileVersion, Line der Änderung, a.tsv], Files leer.

[Fact] public void An_insert_without_a_project_file_creates_one_named_after_the_project()
// Snapshot ohne x_tables-Datei, Insert k9, projectName "Flo Mod": Files == [Created db\x_tables\Flo Mod.tsv], Previous null, PreviousText null, Rows == [[k9,...]], Version 2 (neueste); FilesCreated 1.

[Fact] public void An_invalid_project_name_falls_back_to_twmt()
// projectName "a/b": Datei db\x_tables\twmt.tsv. projectName "   ": twmt.

[Fact] public void A_copy_does_not_count_as_the_project_file_of_the_table()
// Snapshot: data__.tsv (IsCopy true) und sonst nichts; Insert: neue Datei nach Regel 3, nicht die Kopie. Snapshot: data__.tsv (Kopie) und a.tsv: a.tsv ohne Frage.

[Fact] public void Several_project_files_ask_which_one_and_the_answer_decides()
// a.tsv und b.tsv, Insert k9 (Line 7): ohne Antwort TargetQuestions == [(x_tables, false, [a.tsv, b.tsv], [7])], Files leer; mit TargetFiles {x_tables: b.tsv}: Zeile in b.tsv. Antwort "c.tsv" (kein Kandidat): Errors [NoAnswer].

[Fact] public void A_key_that_the_target_file_already_holds_is_an_error()
// a.tsv hat k9; Insert k9: Errors == [KeyAlreadyInFile, Line, a.tsv], Detail enthält "k9"; Files leer. Keyless y_tables: zwei gleiche Inserts gehen beide durch.

[Fact] public void A_colour_channel_out_of_range_is_an_error_before_writing()
// colour_tables Insert mit c_r "300": Errors == [ColourChannelOutOfRange, Line, Datei]; "255" geht durch.

[Fact] public void Snapshot_problems_become_errors_and_nothing_is_planned()
// Snapshot mit einem SnapshotProblem: Errors == [SnapshotProblem], Files leer.

[Fact] public void Changes_with_errors_are_refused()
// ObjectChanges mit Error-Diagnose: Assert.Throws<ArgumentException>.

[Fact] public void Files_are_sorted_and_an_unchanged_file_is_not_planned()
// Set in b.tsv und Insert in a.tsv (Regel 1 und Antwort): Files == [a.tsv, b.tsv]; eine dritte Datei c.tsv ohne Änderung fehlt.
```

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.Editing.Tests --filter "FullyQualifiedName~ProjectWritePlanTests"`: Build-Fehler.

- [ ] **Step 3: Implementieren** – `ProjectWritePlan.Compute` mit privaten Schritten: `Group` (Änderungen pro Tabelle), `ResolveTarget` (Regeln 1 bis 4, sammelt Fragen), `LocateRow`, `ApplyTableChange`, `RenderFiles`. Ein veränderlicher Arbeitsstand pro Datei (`List<object?[]> Rows`, `bool Changed`, Definition, Version, PackPath), am Ende `TableTsv.Write`. Löschen und Loc kommen in Task 4 und 5 in dieselben Schritte.

- [ ] **Step 4: GREEN** – Filter wie oben, dann `dotnet test tests/TotalWarModTool.Editing.Tests` ganz; Build ohne Warnung.

- [ ] **Step 5: Spec** – „Festlegungen aus Plan 2“ ergänzen: Name der neuen Datei ist der getrimmte Projektname oder `twmt` (kein weiteres Bereinigen); Zeilen werden über Schlüsselwerte und `PreviousCells` gefunden; Farbkanalprüfung vor dem Schreiben; eine Datei ohne Änderung wird nicht geschrieben; `Files` bleibt leer, solange eine Frage offen ist.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.Editing/WriteModel.cs src/TotalWarModTool.Editing/ProjectWritePlan.cs tests/TotalWarModTool.Editing.Tests/ProjectWritePlanTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: plan the project files an object's row changes go into"`.

---

### Task 4: Löschen und Dateikopie (`ProjectWritePlan`: Delete, Überschreibkette, `FileCopyQuestion`)

Spec 5.5 erster Teil und 7 („Projektzeile überschreibt Vanilla-Zeile und wird gelöscht“, „Löschen einer fremden Zeile“).

**Files:**
- Modify: `src/TotalWarModTool.Editing/ProjectWritePlan.cs`
- Test: `tests/TotalWarModTool.Editing.Tests/ProjectWritePlanTests.cs` (Klasse `ProjectWritePlanDeleteTests` in eigener Datei `ProjectWritePlanDeleteTests.cs`)

**Interfaces:**
- Consumes: Task 3 (`Compute`, `LocateRow`, Arbeitsstand pro Datei), `SnapshotPackEntry`, `RowChange.Provenance` (`Source`, `EntryPath`, `OverriddenLayers`), `ObjectId.JoinedKey`-Form (Schlüssel mit `|` verbunden, wie `ObjectId`).
- Produces: keine neuen Typen; `PlannedFileKind.Copy`, `FileCopyQuestion`, `WriteAnswers.CopiesConfirmed`, `WriteSummary.RowsDeleted`/`Copies` werden gefüllt.

Regeln (Festlegungen):

- `Delete` einer Projektzeile (`Provenance.Source.Kind == Project`): Zeile in ihrer Datei (`EntryPath` ohne `pack\`) finden wie in Task 3 und entfernen. Eine Datei, deren letzte Zeile entfernt wird, bleibt als Datei mit Kopfzeilen bestehen (kein Löschen von Dateien in diesem Plan; Backlog).
- Jede überschriebene Schicht `OverriddenLayer(Source, EntryPath)` der gelöschten Zeile wird ebenfalls bedient: Schicht `Project` → wie oben in dieser Projektdatei (`EntryPath` ohne `pack\`), Zeile über die Schlüsselwerte von `PreviousCells` (ohne Zellenvergleich; die Zellen der unteren Schicht sind andere); Schicht `Vanilla` oder `Mod` → Dateikopie.
- `Delete` einer Vanilla- oder Mod-Zeile: Dateikopie der Quelle (`Provenance.Source`, `Provenance.EntryPath`) und ebenso jede überschriebene Schicht.
- Dateikopie: der `SnapshotPackEntry` mit `(Source.Path, EntryPath)`; fehlt er (Programmfehler, der Request war unvollständig), `WriteError(SnapshotProblem, Line, EntryPath, "pack entry not in snapshot")`. Arbeitsstand der Kopie: `Table.Rows` der adoptierten Datei, Version und `PackPath` der Quelle; die Zeile wird über die Schlüsselwerte entfernt (Tabelle mit Schlüsselfeldern; mehrere Treffer: alle, denn im Spiel gilt der Schlüssel), ohne Schlüsselfelder über die ganzen Zellen (`PreviousCells`, erster Treffer). Keine Zeile gefunden: `RowNotInFile` mit `RelativeFile` der Kopie. Zwei Löschungen aus derselben Quelldatei nutzen denselben Arbeitsstand (eine Kopie). Steht im Snapshot schon eine Projektdatei mit derselben `RelativeFile` (unerwartet, Spec „Gibt es die Kopie schon“): dann ist das die Zieldatei (`Changed`), keine Kopie.
- Jede Kopie ist eine `FileCopyQuestion(Source, EntryPath, RelativeFile, RowCount der Quelle, DeletedKeys)`; `DeletedKeys` ist pro entfernter Zeile der Schlüssel (Schlüsselwerte mit `|` verbunden, ohne Schlüsselfelder die erste Zelle). Ohne `answers.CopiesConfirmed` sind alle Kopien Fragen und `Files` ist leer; mit `CopiesConfirmed` kommen sie als `PlannedFile(RelativeFile, Copy, null, null, Text, EntryPath)` in `Files`. Fragen werden auch dann gesammelt, wenn `TargetQuestions` offen sind (die App stellt beide Dialoge nacheinander, Spec 5.9).
- Loc-Löschung gibt es nicht (Plan 1: `TextChange` kennt kein Delete).
- Summary: `RowsDeleted` zählt `Delete`-Änderungen (eine pro `RowChange`, nicht pro Schicht); `Copies` die `PlannedFile` mit `Kind == Copy`.

- [ ] **Step 1: Failing Tests** `ProjectWritePlanDeleteTests.cs` (Helfer `PackEntry(source, entryPath, relativeFile, table, version, rows)` baut `SnapshotPackEntry` über `TableTsv.Write`/`Read`):

```csharp
[Fact] public void Deleting_a_project_row_removes_it_from_its_file()
// a.tsv [k1],[k2]; Delete k1 (Project): Rows(NewText) == [[k2]]; Summary RowsDeleted 1; keine Fragen.

[Fact] public void Deleting_the_last_row_keeps_the_file_with_its_header()
// a.tsv [k1]; Delete k1: NewText == TableTsv.Write(..., []) (Kopf und Meta-Zeile), Datei bleibt Changed.

[Fact] public void Deleting_a_vanilla_row_asks_for_a_copy_and_the_copy_lacks_the_row()
// PackEntry v.pack db/x_tables/data__ -> db\x_tables\data__.tsv v1 Zeilen [k1],[k2],[k3]; Delete k2 (Vanilla, Line 4):
// ohne CopiesConfirmed: CopyQuestions == [(v.pack, db/x_tables/data__, db\x_tables\data__.tsv, 3, [k2])], Files leer;
// mit CopiesConfirmed: Files == [Copy data__.tsv, Previous null, SourceEntry db/x_tables/data__], Rows == [[k1],[k3]], Version 1; Summary Copies 1, RowsDeleted 1.

[Fact] public void Two_deletes_from_the_same_source_file_make_one_copy()
// Delete k1 und k3 aus data__: eine CopyQuestion mit DeletedKeys [k1, k3], RowCount 3; eine Copy-Datei mit Rows [[k2]].

[Fact] public void Deleting_a_row_that_overrides_two_layers_copies_both_files()
// Projektzeile k1 in a.tsv mit OverriddenLayers [(v.pack, "db/x_tables/data__"), (m.pack, "db/x_tables/m")]; PackEntries für beide:
// Files (confirmed) == [a.tsv Changed ohne k1, data__.tsv Copy ohne k1, m.tsv Copy ohne k1] (ordinal sortiert), CopyQuestions zwei.

[Fact] public void An_overridden_project_layer_is_served_by_key()
// Mod-Zeile k1 in m.pack überschreibt Projektzeile k1 in a.tsv (OverriddenLayers [(Project, @"pack\db\x_tables\a.tsv")]), Zellen der Projektzeile anders als PreviousCells: a.tsv verliert k1, m.tsv Kopie ohne k1.

[Fact] public void A_row_whose_only_key_is_the_root_field_is_found_by_its_previous_cells()
// z_tables Datei [b1,1],[b1,2]; Delete PreviousCells [b1,2] (Project): Rows == [[b1,1]].

[Fact] public void Of_two_identical_keyless_rows_only_the_first_is_deleted()
// y_tables Datei [b1,a,b],[b1,a,b]; Delete PreviousCells [b1,a,b]: Rows hat noch eine Zeile [b1,a,b].

[Fact] public void A_missing_row_in_the_source_is_an_error()
// Delete k9 aus data__, das k9 nicht hat: Errors == [RowNotInFile, Line, db\x_tables\data__.tsv].

[Fact] public void Copy_questions_are_collected_beside_target_questions()
// a.tsv und b.tsv, Insert k9 und Delete Vanilla k1: TargetQuestions eine, CopyQuestions eine, Files leer.
```

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.Editing.Tests --filter "FullyQualifiedName~ProjectWritePlanDeleteTests"`: rote Tests.

- [ ] **Step 3: Implementieren** – `Delete` in `ApplyTableChange`; Schichten über eine private `Layers(RowChange)`-Aufzählung (eigene Quelle plus `OverriddenLayers`); Kopien als Arbeitsstand mit `Kind Copy`, Schlüssel nach `RelativeFile`.

- [ ] **Step 4: GREEN** – Filter, dann `dotnet test tests/TotalWarModTool.Editing.Tests` ganz; Build ohne Warnung.

- [ ] **Step 5: Spec** – „Festlegungen aus Plan 2“: eine Projektdatei ohne Zeilen bleibt bestehen; überschriebene Projektschichten werden über den Schlüssel bedient; in einer Kopie werden alle Zeilen mit dem Schlüssel entfernt; Kopie- und Zieldatei-Fragen werden gemeinsam gesammelt; `DeletedKeys`-Form.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.Editing/ProjectWritePlan.cs tests/TotalWarModTool.Editing.Tests/ProjectWritePlanDeleteTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: delete rows from every layer and copy a foreign file into the project with the row removed"`.

---

### Task 5: Schreibplan für Spieltexte (`ProjectWritePlan`: Loc-Zielregeln)

Spec 5.4 letzter Teil (Loc) und Backlog 265 (Datei über Loc-Schlüssel).

**Files:**
- Modify: `src/TotalWarModTool.Editing/ProjectWritePlan.cs`
- Test: `tests/TotalWarModTool.Editing.Tests/ProjectWritePlanLocTests.cs`

**Interfaces:**
- Consumes: `TextChange` (`Kind`, `Key`, `Text`, `Source`, `Line`), `SnapshotLocFile`, `TableTsv.WriteLoc`, `LocRow`, `NewFiles.Loc(name)` (Pfad der neuen Datei), `NewFiles.LocFolder`.
- Produces: keine neuen Typen; `TargetFileQuestion` mit `IsLoc == true` und `Table == WriteAnswers.LocKey`; `WriteSummary.TextsSet`/`TextsInserted`.

Regeln:

- Zieldatei einer Textänderung: 1. die Snapshot-Loc-Datei (Kopie oder nicht), deren `Loc.Rows` den `Key` hält (ordinal); mehrere: die ordinal erste. Dort wird `Text` der Zeile ersetzt, `Tooltip` bleibt. 2. Sonst genau eine Loc-Datei mit `IsCopy == false`: anhängen `LocRow(Key, Text, Tooltip: false)`. 3. Sonst keine: neue Datei `NewFiles.Loc(name).File.RelativeFile` (`text\db\<name>.loc.tsv`, `PackPath` `text/db/<name>.loc`), Name wie Task 3. 4. Sonst mehrere: `answers.TargetFiles[WriteAnswers.LocKey]` oder eine `TargetFileQuestion(WriteAnswers.LocKey, true, Kandidaten, Zeilen)`, eine für alle Textänderungen.
- `TextChange.Source` entscheidet nichts: die Datei kommt vom Schlüssel (Plan 1: `TextChange` trägt keinen `EntryPath`). Ein `Set` eines Vanilla-Texts ohne Projektdatei mit dem Schlüssel wird so zur angehängten Überschreibzeile.
- Summary: `TextsSet` zählt `Set`, `TextsInserted` `Insert`.

- [ ] **Step 1: Failing Tests** `ProjectWritePlanLocTests.cs` (Helfer `LocFile(relativeFile, rows, isCopy = false)`):

```csharp
[Fact] public void A_text_whose_key_a_project_loc_file_holds_is_replaced_there()
// text\db\a.loc.tsv [(x_name_k1, "Old", true)], text\db\b.loc.tsv leer; Set x_name_k1 "New": Files == [a.loc.tsv Changed], Rows == [(x_name_k1, New, true)]; keine Frage trotz zweier Dateien.

[Fact] public void A_text_of_a_vanilla_entry_is_appended_to_the_only_project_loc_file()
// a.loc.tsv [(other, "o", false)]; Set x_name_k1 "New" (Source Vanilla): Rows == [(other,o,false),(x_name_k1,New,false)]; TextsSet 1.

[Fact] public void Without_a_loc_file_a_new_one_is_created_named_after_the_project()
// Snapshot ohne Loc; Insert x_name_k9 "N", projectName "Flo": Files == [Created text\db\Flo.loc.tsv], Meta-Zeile "#Loc;1;text/db/Flo.loc", eine Zeile; TextsInserted 1.

[Fact] public void A_loc_copy_does_not_count_as_the_project_loc_file()
// data__.loc.tsv (IsCopy) ohne den Schlüssel und sonst nichts: Regel 3, neue Datei.

[Fact] public void Several_loc_files_ask_once_for_all_texts()
// a.loc.tsv und b.loc.tsv ohne die Schlüssel, zwei Inserts (Lines 3, 9): TargetQuestions == [("*loc", true, [a, b], [3, 9])]; mit Antwort b: beide Zeilen in b.loc.tsv.

[Fact] public void Table_and_loc_changes_of_one_write_land_in_their_own_files()
// Set x_tables (Projektzeile in a.tsv) und Set Loc (a.loc.tsv hält Schlüssel): Files == [db\x_tables\a.tsv, text\db\a.loc.tsv].
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ProjectWritePlanLocTests`.

- [ ] **Step 3: Implementieren** – Loc-Arbeitsstand (`List<LocRow>`) pro Datei, `ResolveLocTarget`, `TableTsv.WriteLoc` am Ende.

- [ ] **Step 4: GREEN** – Filter, dann `dotnet test tests/TotalWarModTool.Editing.Tests` ganz; Build ohne Warnung.

- [ ] **Step 5: Spec** – „Festlegungen aus Plan 2“: Loc-Regel 1 nimmt auch eine Kopie; eine angehängte Loc-Zeile hat `Tooltip false`; ersetzen behält `Tooltip`; Loc-Frage unter dem Schlüssel `*loc`.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.Editing/ProjectWritePlan.cs tests/TotalWarModTool.Editing.Tests/ProjectWritePlanLocTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: plan the loc file a text change goes into by its key"`.

---

### Task 6: Undo-Schritt (`FileContentsStep`)

Spec 5.6 Punkt 3, 7 („Undo, Datei seither geändert“, „Undo, Datei seither gelöscht“). Liegt in `Editing` (Spec 5.1).

**Files:**
- Create: `src/TotalWarModTool.Editing/FileContentsStep.cs`
- Test: `tests/TotalWarModTool.Editing.Tests/FileContentsStepTests.cs`

**Interfaces:**
- Consumes: `IUndoableAction`, `StepDeferredException`, `ProjectFileWriter.WriteText`/`Probe`, `ProjectFileOps.Create`/`RemoveEmptyFolders`, `ProjectTrash.MoveToTrash`/`Restore`, `TrashedFile`, `FileFingerprint`, `FileWriteProblem`, `FileOpProblem`, `TimeProvider`.
- Produces:

```csharp
namespace TotalWarModTool.Editing;

/// <summary>One file of a step as the write left it: Previous is the text before (null: the file did not exist), Next the text written, After the fingerprint after the write, CreatedFolders the folders the write created (outermost first, relative to the project folder; empty for an existing file).</summary>
public sealed record FileContentsEntry(string RelativeFile, string? Previous, string Next, FileFingerprint After, IReadOnlyList<string> CreatedFolders);

/// <summary>A step did not run because a file is not in the state the step left it in (changed or gone). Nothing was changed; the history keeps the step.</summary>
public sealed class FileContentsChangedException(string relativeFile, string message) : StepDeferredException(message, null)
{
    public string RelativeFile { get; } = relativeFile;
}

/// <summary>
/// The undo step of one object write. Undo writes Previous back (a file with Previous null goes to the trash and its created folders are
/// removed when empty), Redo writes Next back (a trashed file is restored from the trash). Before anything is touched, every file is probed
/// against the fingerprint the step last left; a mismatch throws FileContentsChangedException (step stays). A locked file before the first
/// write throws StepDeferredException (step stays); a failure after the first write throws InvalidOperationException whose message names every
/// file and its state (step dropped). Do and Undo run on the UI thread when the history asks; the first execution is the writer's job.
/// </summary>
public sealed class FileContentsStep : IUndoableAction
{
    public FileContentsStep(Guid projectId, string projectFolder, string description, IReadOnlyList<FileContentsEntry> files, string trashRoot, TimeProvider time);

    public Guid ProjectId { get; }
    public string Description { get; }
    /// <summary>The files of the step (below pack\, with '\'), ordinal.</summary>
    public IReadOnlyList<string> Files { get; }
    /// <summary>Files of this step that are in the trash right now (after an undo of created files).</summary>
    public IReadOnlyList<TrashedFile> InTrash { get; }

    public void Do();
    public void Undo();
}
```

Regeln (Festlegungen):

- Zustand pro Datei: `Current` (Fingerprint, wie der Schritt die Datei zuletzt hinterließ, null während sie im Papierkorb ist), `Trash` (`TrashedFile?`), `Folders` (angelegte Ordner, outermost first). Anfang: `Current = After`, `Trash = null`, `Folders = CreatedFolders`.
- Prüfung vor jedem Lauf, für alle Dateien, bevor etwas geschrieben wird: `ProjectFileWriter.Probe`; Datei mit `Current != null` muss `Present` sein mit `Length` und `LastWriteTimeUtc` gleich `Current`; Datei mit `Current == null` (im Papierkorb) muss `Missing` sein. Sonst `FileContentsChangedException(relativeFile, "the file was changed since the step left it: <file>")` beziehungsweise `"... is gone: <file>"`. `Unknown` (Link, Ordner an der Stelle) → `StepDeferredException` mit dem `Detail`.
- `Undo`, pro Datei in Reihenfolge: `Previous != null` → `ProjectFileWriter.WriteText(projectFolder, file, Previous)`; `Previous == null` → `ProjectTrash.MoveToTrash(trashRoot, projectId, projectFolder, file, time)` dann `ProjectFileOps.RemoveEmptyFolders(projectFolder, Folders)`. `Redo` (`Do`): `Previous != null` → `WriteText(Next)`; `Previous == null` → `ProjectTrash.Restore(trash, projectFolder, trashRoot)`, `Folders` aus `CreatedFolders` des Ergebnisses. Nach jeder gelungenen Datei `Current` aus dem Fingerprint des Ergebnisses (`FileWriteResult.Fingerprint`, `FileOpResult.Fingerprint`), beim Papierkorb `Current = null`.
- Fehler bei der ersten Datei mit `FileWriteProblem.Locked` oder `FileOpProblem.Locked` ohne `LeftBehind` → `StepDeferredException` (nichts geändert). Jeder andere Fehler, oder ein Fehler nach der ersten Datei → `InvalidOperationException` mit Meldung `"<file>: <state>"` pro Datei (`written`, `trashed`, `restored`, `unchanged`, `failed: <detail>`), Zeilen mit `; ` verbunden; der Zustand der gelungenen Dateien bleibt gesetzt.
- Die Ordnerentfernung beim Undo ist best effort (`RemoveEmptyFolders` wirft nicht).

- [ ] **Step 1: Failing Tests** `FileContentsStepTests.cs` (`TempDir` mit Projektordner `proj\pack\...` und Papierkorb `trash\`; `FakeTimeProvider` aus `tests/Shared`; Helfer `Written(folder, relativeFile, text)` schreibt über `ProjectFileOps.Create` und liefert `FileContentsEntry` mit `After` und `CreatedFolders`):

```csharp
[Fact] public void Undo_writes_the_previous_text_back_and_redo_the_next()
// Datei a.tsv "old" -> Schritt mit Previous "old", Next "new" (Datei enthält "new"): Undo -> Datei "old"; Redo -> "new"; kein Papierkorb-Eintrag.

[Fact] public void Undo_moves_a_created_file_to_the_trash_and_redo_brings_it_back()
// Previous null, Datei und Ordner db\x_tables neu (CreatedFolders [db, db\x_tables]): Undo -> Datei weg, InTrash hat einen Eintrag, Ordner db\x_tables und db weg; Redo -> Datei zurück mit Text Next, InTrash leer.

[Fact] public void Undo_leaves_the_step_when_a_file_changed_since()
// Datei nach dem Schreiben verändert (anderer Text, Länge anders): Undo wirft FileContentsChangedException mit RelativeFile; Datei unverändert.
// Datei wird in ActionHistory aufgenommen (Record), Undo über die History: Schritt bleibt NextUndo.

[Fact] public void Undo_leaves_the_step_when_a_created_file_is_gone()
// Previous null, Datei gelöscht: FileContentsChangedException; nichts im Papierkorb.

[Fact] public void Nothing_is_written_when_the_second_file_changed()
// Zwei Dateien, zweite verändert: Undo wirft FileContentsChangedException, erste Datei hat noch Next.

[Fact] public void A_locked_first_file_defers_the_step()
// a.tsv mit FileShare.None offen gehalten: Undo wirft StepDeferredException (nicht FileContentsChangedException); nach dem Schließen gelingt Undo.

[Fact] public void A_failure_after_the_first_file_drops_the_step_and_names_every_file()
// a.tsv frei, b.tsv gesperrt: Undo wirft InvalidOperationException, Message enthält "a.tsv: written" und "b.tsv: failed"; a.tsv hat Previous.

[Fact] public void Redo_after_undo_checks_the_file_the_undo_left()
// Undo, dann Datei von außen geändert: Redo wirft FileContentsChangedException.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~FileContentsStepTests`.

- [ ] **Step 3: Implementieren** – `FileContentsStep` mit privater `Entry`-Klasse (veränderlicher Zustand), `Run(bool undo)`: `ProbeAll`, dann `Apply` pro Datei.

- [ ] **Step 4: GREEN** – Filter, dann `dotnet test tests/TotalWarModTool.Editing.Tests` ganz; Build ohne Warnung.

- [ ] **Step 5: Spec** – „Festlegungen aus Plan 2“: Prüfung aller Dateien vor dem ersten Schreiben; ein Fehler nach der ersten Datei lässt den Schritt fallen und nennt jede Datei (Spec 7 „Tausch scheitert“ gilt für das Übernehmen, nicht für Undo); `FileContentsChangedException` ist eine `StepDeferredException`.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.Editing/FileContentsStep.cs tests/TotalWarModTool.Editing.Tests/FileContentsStepTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: undo and redo an object write from the held file contents"`.

---

### Task 7: Schreiben (`ProjectWriter.Apply`)

Spec 5.6 Punkte 1 und 2, 7 („Projektdatei hat sich geändert“, „Tausch scheitert“, „Abbruch im Runner vor dem ersten Tausch“).

**Files:**
- Modify: `src/TotalWarModTool.Editing/WriteModel.cs` (`WriteOutcome`, `WriteFailure`, `WrittenFileState`)
- Create: `src/TotalWarModTool.Editing/ProjectWriter.cs`
- Test: `tests/TotalWarModTool.Editing.Tests/ProjectWriterTests.cs`

**Interfaces:**
- Consumes: `WritePlan`, `PlannedFile` (Task 3), `FileContentsStep`, `FileContentsEntry` (Task 6), `ProjectFileWriter.Probe`/`WriteText`, `ProjectFileOps.Create`, `ProjectTrash.MoveToTrash`.
- Produces:

```csharp
namespace TotalWarModTool.Editing;

public enum WriteFailureKind { FileChanged, FileUnknown, WriteFailed, RollbackFailed }

public enum WrittenFileState { Unchanged, Written, RolledBack, RollbackFailed }

/// <summary>Kind FileChanged/FileUnknown: nothing was written, File names it. WriteFailed: every file is RolledBack or Unchanged. RollbackFailed: at least one file stays Written or RollbackFailed; Files says which. Detail is short technical English.</summary>
public sealed record WriteFailure(WriteFailureKind Kind, string? File, string Detail, IReadOnlyList<(string RelativeFile, WrittenFileState State)> Files);

/// <summary>Exactly one of Step and Failure is set.</summary>
public sealed record WriteOutcome(FileContentsStep? Step, WriteFailure? Failure);

public static class ProjectWriter
{
    /// <summary>Throws ArgumentException for a plan with questions, errors or no files. Cancellation is honoured only before the first write.</summary>
    public static WriteOutcome Apply(WritePlan plan, Guid projectId, string projectFolder, string description, string trashRoot, TimeProvider time, CancellationToken cancellationToken);
}
```

Regeln (Festlegungen, Abweichung von Spec 5.6 Punkt 2 im selben Commit festhalten):

1. Prüfung: für jede `PlannedFile` `ProjectFileWriter.Probe`. `Previous != null` → `Present` mit gleicher `Length` und `LastWriteTimeUtc`, sonst `FileChanged` („the file changed since the object was built: <file>“). `Previous == null` → `Missing`, sonst `FileChanged` („a file appeared at the target: <file>“). `Unknown` → `FileUnknown` mit `Detail`. Nichts geschrieben. Danach `cancellationToken.ThrowIfCancellationRequested()` (einziger Abbruchpunkt).
2. Schreiben in Planreihenfolge: `Changed` → `ProjectFileWriter.WriteText(projectFolder, file, NewText)`; `Created` und `Copy` → `ProjectFileOps.Create(projectFolder, file, UTF-8 ohne BOM von NewText, CancellationToken.None)`. Jede Datei ist für sich atomar (temp plus Tausch in der Library). **Abweichung:** die Spec sieht erst alle temp-Dateien, dann die Tausche vor; `Projects` bietet dafür keine öffentliche Stufe und bleibt unverändert (Spec 5.1), deshalb pro Datei schreiben und bei Fehler zurückschreiben (Spec sagt dasselbe für den Fehlerfall).
3. Fehler bei Datei k: Rückschreiben der Dateien 1 bis k-1 in umgekehrter Reihenfolge: `Changed` → `WriteText(PreviousText)`; `Created`/`Copy` → `ProjectTrash.MoveToTrash` (die Datei verschwindet aus dem Projekt; der Papierkorb hält sie). Gelingt alles: `WriteFailed` mit `Detail` des Fehlers und `Files` (`RolledBack` für 1..k-1, `Unchanged` für k..n). Scheitert ein Rückschreiben: `RollbackFailed`, `Files` nennt jede Datei (`Written` für nicht zurückgeschriebene, `RollbackFailed` für die gescheiterte).
4. Erfolg: `FileContentsStep(projectId, projectFolder, description, entries, trashRoot, time)` mit `FileContentsEntry(file, PreviousText, NewText, Fingerprint des Ergebnisses, CreatedFolders des Ergebnisses oder [])`. Der Schritt wird nicht eingetragen; die App ruft `Record` (Spec 5.6).

- [ ] **Step 1: Failing Tests** `ProjectWriterTests.cs` (`TempDir`, Plan aus `PlannedFile`s direkt gebaut; Fingerprint `Previous` über `ProjectFileWriter.Fingerprint` nach dem Anlegen der Datei; Hilfsfunktion `Plan(params PlannedFile[])` mit leeren Fragen und Fehlern):

```csharp
[Fact] public void Every_file_of_the_plan_is_written_and_the_step_holds_the_previous_texts()
// a.tsv "old" (Changed, Previous Fingerprint), db\y_tables\n.tsv (Created), db\z_tables\c.tsv (Copy): Step != null, Dateien haben NewText; Step.Files == 3 ordinal; Undo über den Schritt stellt "old" her und schickt n.tsv und c.tsv in den Papierkorb.

[Fact] public void A_changed_fingerprint_aborts_before_anything_is_written()
// a.tsv nach dem Fingerprint verändert, b.tsv Created: Failure.Kind FileChanged, File a.tsv, b.tsv existiert nicht, a.tsv hat den fremden Text.

[Fact] public void A_file_that_appeared_at_a_created_target_aborts()
// Created n.tsv, Datei existiert schon: FileChanged.

[Fact] public void A_locked_second_file_rolls_the_first_back()
// a.tsv Changed, b.tsv Changed mit FileShare.None offen: Failure WriteFailed, Files == [(a, RolledBack), (b, Unchanged)]; a.tsv hat PreviousText; kein Step.

[Fact] public void A_created_file_is_trashed_on_rollback()
// n.tsv Created zuerst, dann gesperrte b.tsv: n.tsv weg, Papierkorb hat einen Eintrag, Files [(n, RolledBack), (b, Unchanged)].

[Fact] public void A_plan_with_questions_or_errors_is_refused()
// Assert.Throws<ArgumentException> für Plan mit TargetQuestion, mit Error, mit leeren Files.

[Fact] public void Cancellation_before_the_first_write_writes_nothing()
// Token schon abgebrochen: OperationCanceledException, Dateien unverändert.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ProjectWriterTests`.

- [ ] **Step 3: Implementieren** – `ProjectWriter.Apply` mit `ProbeAll`, `WriteAll`, `RollBack`.

- [ ] **Step 4: GREEN** – Filter, dann `dotnet test tests/TotalWarModTool.Editing.Tests` ganz; Build ohne Warnung.

- [ ] **Step 5: Spec** – 5.6 Punkt 2: Satz ergänzen „(Umsetzung Plan 2: jede Datei für sich atomar über `ProjectFileWriter` und `ProjectFileOps`, dann Rückschreiben; siehe Festlegungen)“ und in „Festlegungen aus Plan 2“ die Regel 3 oben (Papierkorb für angelegte Dateien beim Rückschreiben, `WriteFailure`-Zustände).

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.Editing/WriteModel.cs src/TotalWarModTool.Editing/ProjectWriter.cs tests/TotalWarModTool.Editing.Tests/ProjectWriterTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: write the planned project files atomically with fingerprint checks and roll back on failure"`.

---

### Task 8: Abschluss (Durchstich-Test, Backlog, Build)

Ein Test über alle Stufen auf echten Pfaden im Temp-Ordner, Backlog pflegen, Solution bauen.

**Files:**
- Test: `tests/TotalWarModTool.Editing.Tests/ObjectWriteEndToEndTests.cs`
- Modify: `Backlog.txt` (Zeile 265 entfernen; neue Einträge unten)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Abschnitt 10, Annahme 5: „Plan 2 hält `Previous` und `Next` im Schritt; Messung in Plan 3“)

**Interfaces:**
- Consumes: alles aus Task 2 bis 7, `ObjectDiff.Compute`, `ObjectTextParser.Parse`, `ObjectBuilder`, `TableStore`, `LocStore` (für die Baseline aus Mini-Daten wie `ObjectBuilderTests`).
- Produces: nichts Neues.

- [ ] **Step 1: Failing Test** `ObjectWriteEndToEndTests.cs`:

```csharp
[Fact] public void A_block_edit_is_written_rebuilt_and_undone()
// Vanilla-Pack mit building_levels_tables [b1, 100] und x_tables [k1,b1,"n",1]; Projektordner mit db\x_tables\a.tsv [k2,b1,"m",2]; Ladereihenfolge Vanilla, Project.
// Baseline über ObjectBuilder für building_levels_tables:b1; Text aus ObjectText.RenderBlock; im Text create_cost 100 -> 150 und Zeile k1 (x_tables) note "n" -> "x" und Zeile k2 entfernt.
// Parse, ObjectDiff.Compute, SnapshotRequest.For, ProjectSnapshotReader.Read (isPackEntry: Pfade des Vanilla-Packs), Compute ohne Antworten -> keine Fragen (eine Projektdatei, keine fremde Löschung), Compute, ProjectWriter.Apply.
// Danach: a.tsv hat [k2 weg], Überschreibzeilen b1 (building_levels: neue Datei db\building_levels_tables\<proj>.tsv mit [b1,150]) und k1 (angehängt an a.tsv mit note x).
// Neuaufbau über ObjectBuilder (TableStore.Clear): create_cost 150, Zeile k1 mit Provenance Project und Overridden [Vanilla], keine Zeile k2.
// Step.Undo: Dateien wie vorher (a.tsv alter Text, neue Datei im Papierkorb); Neuaufbau zeigt wieder 100, k1 "n", k2.

[Fact] public void Deleting_a_vanilla_row_copies_the_file_and_the_rebuilt_object_lacks_the_row()
// wie oben, Zeile k1 (Vanilla) entfernt: Compute ohne Antworten -> eine CopyQuestion; mit CopiesConfirmed -> Copy db\x_tables\data__.tsv ohne k1; nach Apply und Neuaufbau fehlt k1 (die Kopie ersetzt die Vanilla-Datei, Plan 1 Task 5).
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ObjectWriteEndToEndTests`: rot oder Build-Fehler, je nach fehlendem Helfer (Fixture ergänzen).

- [ ] **Step 3: GREEN** – Nur Testcode und Fixture anpassen. Scheitert der Test an einer Regel der Library, ist das ein Befund: melden, dann als eigener Fix mit Test beheben (nicht still im Durchstich-Test umgehen).

- [ ] **Step 4: Backlog** – Zeile 265 entfernen (erledigt durch Task 1, 3, 5). Neu unter „Aus Teil 4.2a Plan 2 (Editing) zurückgestellt“: eine Projektdatei, deren letzte Zeile gelöscht wird, bleibt mit Kopfzeilen bestehen (Löschen leerer Dateien beim Übernehmen); `Set` eines Felds, das die Dateiversion nicht hat, ist ein Fehler (Alternative: Datei auf die neueste Version heben, mit Rückfrage); Pack-Einträge liest der Snapshot ohne Retry (gesperrtes Pack = Fehler vor dem Schreiben; `PackAccess.ReadIndex` mit Retry, wenn das in der Praxis stört); `ProjectWriter` schreibt pro Datei atomar statt in zwei Phasen (Staging-API in `Projects`, wenn ein Teilzustand je auftritt); Speicher der History (Spec Annahme 5) in Plan 3 messen.

- [ ] **Step 5: Verifikation** – `dotnet build TotalWarModTool.slnx` ohne Warnung; `dotnet test tests/TotalWarModTool.Editing.Tests` und `dotnet test tests/TotalWarModTool.Objects.Tests` grün. Die volle Suite (`test.bat`) läuft am Ende mit dem Nutzer, nichts anderes baut dabei.

- [ ] **Step 6: Commit** – `git add tests/TotalWarModTool.Editing.Tests/ObjectWriteEndToEndTests.cs tests/TotalWarModTool.Editing.Tests/WriteFixture.cs Backlog.txt docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "test: write, rebuild and undo a block edit end to end and note the deferred follow-ups"`.

---

## Manuelle Abnahme durch den Nutzer

Keine UI in diesem Plan. Abnahme ist der grüne volle Testlauf (`test.bat`) auf `feature/teil-4-2a-editing` und der Blick auf die „Festlegungen aus Plan 2“ in der Spec. Die Abnahme mit echtem Spiel (Annahme 1, Dateikopie im Spiel) kommt mit Plan 3.

## Nächste Phase

Nach Plan 2: Review der Library (starkes Modell), dann Plan 3 (App Editor und Übernehmen) schreiben, sobald die Interfaces aus diesem Plan existieren. Umsetzung der Tasks nach Task-Brief: mittleres Modell reicht (Tabelle `docs/checkliste.md` Abschnitt 15).
