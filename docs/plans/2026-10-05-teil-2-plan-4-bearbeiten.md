# Teil 2 Plan 4: Tabellen bearbeiten und speichern – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tabellen und Loc-Dateien eines Projekts sind im Raster bearbeitbar: Zellen ändern mit Prüfung pro Feldtyp, Zeilen einfügen, löschen, duplizieren, kopieren und einfügen, globales Rückgängig und Wiederholen, ausdrückliches Speichern, Schutz ungespeicherter Änderungen, Erkennen von Änderungen von außen.

**Architecture:** Zwei neue UI-freie Libraries. `UndoRedo` hält die globale Command-History (Kopie aus PMT, gekürzt auf das Nötige). `Editing` hält das Dokument einer Tabelle oder Loc-Datei: Zeilen, Prüfung, Geändert-Zustand, und es liefert jeden Befehl als Undo-Schritt. Zellen werden mit denselben Regeln geprüft und geschrieben, mit denen `TableTsv` liest und schreibt; diese Regeln werden dafür in `Tables` öffentlich. In der App führt ein `DocumentsController` die History, das Speichern (über die Library `Projects`), die Rückfragen und die Prüfung auf Änderungen von außen. Ein `EditableRowsTabViewModel` zeigt ein Dokument im Raster.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`: 5.8 (Bearbeiten), 5.3 (Dialekt), 5.7 (Bauen fragt nach ungespeicherten Änderungen), 7 (Fehlerfälle), 4 (Schreibgrenze), „Festlegungen aus Plan 3“. Projektregeln: `CLAUDE.md`.

**Zuschnitt.** Abschnitt 9 der Spec nennt für Plan 4 auch Papierkorb, Dateioperationen (neue Tabelle, neue Loc-Datei, umbenennen, löschen) und „Ins Projekt übernehmen“. Dieser Plan baut sie nicht. Sie werden Plan 5, der erst geschrieben wird, wenn History, Dokument und Speichern aus diesem Plan existieren (Regel aus `CLAUDE.md`). Task 11 trägt das in die Spec ein. Nach diesem Plan ist nutzbar: importieren, im Raster ändern, speichern, bauen, installieren.

**Branch:** `feature/teil-2-bearbeiten` (von `master`, Stand `384087b` oder neuer). Baseline: 704 Library-Tests und 472 App-Tests grün, 0 übersprungen, Build ohne Warnung.

**Testregel** (mit dem Nutzer vereinbart, 2026-10-04): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei. Einzelne Testklassen der App sind erlaubt (`--filter "FullyQualifiedName~<Klasse>"`), immer nur ein Lauf zur Zeit. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors` ist an.
- Neue Projekte: `src/TotalWarModTool.UndoRedo` (keine Referenz), `src/TotalWarModTool.Editing` (Referenzen: `Schema`, `Tables`, `UndoRedo`), je ein Testprojekt, alle in `TotalWarModTool.slnx`. Neue Referenzen der App: `UndoRedo`, `Editing`. `Projects` und `Workspace` referenzieren weder `Editing` noch `UndoRedo`.
- Bestehende öffentliche Signaturen bleiben nutzbar (Überladung oder optionaler Parameter).
- Fachlogik in Libraries, die App ist Hülle: keine Logik im Code-behind außer View-Verdrahtung. Keine statischen Singletons; alles entsteht im Composition Root (`App.xaml.cs`).
- Libraries loggen nicht und liefern keine UI-Texte: Probleme sind ein Enum plus englisches `Detail`.
- Alle UI-Texte über `AppTexts` (`T`/`F`), jeder neue Key in `locales/en.json` und `locales/de.json`; der Inline-Fallback ist Englisch und gleicht `en`. Der Locale-Test bleibt grün. Keine Ellipsen in Labels. Enum-zu-Text-Methoden wählen unter einer Property pro Wert.
- Farben nur über Theme-Tokens und `DynamicResource`. Neue Fenster setzen Hintergrund und Vordergrund wie `CrashDialog`.
- Schreibgrenze wie in Spec 4. Dieser Plan schreibt nur Dateien unter `pack\` eines bekannten Projekts, und nur über die Library `Projects` (`ProjectFileWriter`, Task 5). Führt der Weg zur Datei durch eine Verknüpfung, wird nicht geschrieben.
- Immer atomar schreiben (`AtomicFile`). Kein `File.WriteAllText` auf Zieldateien.
- Speichern und „Alle speichern“ laufen über `IOperationRunner`. Die stille Prüfung auf Änderungen von außen läuft auf einem Pool-Thread.
- Kein `catch (Exception)` ohne Typfilter. View-Models bekommen kein Log; geloggt wird im Controller.
- Invariante Kultur für alle Zahlen in Daten.
- Texte aus Projektdateien (Dateinamen, Zellwerte in Meldungen) werden vor der Anzeige in Meldungen mit `DisplayText.Safe` maskiert.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in `TempDir`. Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`, nichts schreibt in Spiel-, Workshop- oder RPFM-Ordner.
- View-Tests laufen über `Sta.RunAsync` und laden `Themes/Controls.xaml` wie die vorhandenen View-Tests.
- Quelltext: Escapes wie `\uFEFF` bleiben Escapes; kein `python` oder anderes Skript-Laufzeitsystem.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst. Widerspricht eine Erwartung einer Regel des Plans, wird das gemeldet.

## Review Focus

1. **Speichern, während die Datei außen geändert, gelöscht oder gesperrt ist** (Editor, Git-Checkout, Excel hält die Datei). Erwartung: Nie still überschreiben. Geändert: Rückfrage Überschreiben, Neu laden, Abbrechen. Gelöscht: Rückfrage, dann neu anlegen. Gesperrt: Meldung, Tab bleibt „geändert“, alte Datei unverändert. → Task 5 (`A_locked_file_is_reported_and_left_unchanged`), Task 9 (`Saving_over_an_outside_change_asks_first`, `Saving_a_deleted_file_asks_before_it_is_created_again`).
2. **Schließen mit ungespeicherten Änderungen** (Tab, App, Projekt entfernen, Bauen). Erwartung: Kein Weg verliert Änderungen ohne Rückfrage; Abbrechen lässt alles, wie es ist. → Task 8 (`Closing_a_modified_tab_asks_and_cancel_keeps_it`, `Closing_the_app_with_modified_tabs_asks_once`, `Build_with_unsaved_changes_asks_to_save_all_first`).
3. **Eingaben, die der Dialekt oder das Format nicht trägt** (Text mit wörtlichem `\\t`, Zahl außerhalb des Wertebereichs, `NaN`, Farbkanal 256, einzelnes Surrogat, eingefügte Zeilen mit falschem Typ). Erwartung: Ablehnen mit Grund bei der Eingabe; nie entsteht ein Dokument, das sich nicht speichern oder nicht wieder lesen lässt. → Task 2 (`Every_refused_input_names_its_reason`), Task 3 (`Whatever_the_document_accepts_can_be_written_and_read_back`), Task 4 (`A_paste_with_one_bad_cell_changes_nothing`).
4. **Rückgängig über Tabs und über das Speichern hinweg.** Erwartung: `Strg+Z` trifft den letzten Schritt, wechselt zu dessen Tab, der Geändert-Zustand stimmt danach (auch: speichern, zurücknehmen, neuer Schritt). Ein geschlossener oder neu geladener Tab hinterlässt keine Schritte. → Task 3 (`Modified_follows_the_saved_point_through_undo_and_new_edits`), Task 7 (`Undo_switches_to_the_tab_of_the_step`, `Closing_a_tab_removes_its_steps`).
5. **Große Tabelle** (100.000 Zeilen und mehr). Erwartung: Eine Zelle ändern, eine Zeile einfügen und 1.000 Zeilen einfügen kopieren nicht die ganze Tabelle pro Schritt; Rückgängig eines großen Einfügens ist ein Schritt. → Task 3 (`A_bulk_insert_is_one_step_and_does_not_copy_the_table`).

---

### Task 1: Library `UndoRedo`

**Files:**
- Create: `src/TotalWarModTool.UndoRedo/TotalWarModTool.UndoRedo.csproj`, `IUndoableAction.cs`, `ActionHistory.cs`; `tests/TotalWarModTool.UndoRedo.Tests/TotalWarModTool.UndoRedo.Tests.csproj`, `ActionHistoryTests.cs`
- Modify: `TotalWarModTool.slnx`

**Vorlage:** `C:\Users\flori\Documents\Claude Projekte\Paradox Modding Tool\Runde 10.1\src\ParadoxModTool.UndoRedo\` (`IUndoableAction.cs`, `ActionHistory.cs`), nur lesen. Übernommen werden diese zwei Typen, mit englischen Kommentaren und den Änderungen unten. `Composite`, `Delegate`, `FileWrite`, `BinaryWrite`, `CreateFolders` werden nicht übernommen (YAGNI; Dateiaktionen kommen mit Plan 5). Die `.csproj` folgt den vorhandenen Libraries (Einstellungen kommen aus `Directory.Build.props`).

**Interfaces:**
- Produces:

```csharp
namespace TotalWarModTool.UndoRedo;

public interface IUndoableAction
{
    /// English, technical; the App does not show it in this plan.
    string Description { get; }
    void Do();
    void Undo();
}

public sealed class ActionHistory
{
    public event Action? Changed;                 // after Execute, Record, Undo, Redo, Clear, RemoveWhere (only when something was removed)
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public IUndoableAction? NextUndo { get; }     // null when empty
    public IUndoableAction? NextRedo { get; }
    public void Execute(IUndoableAction action);  // Do, then Record
    public void Record(IUndoableAction action);   // push, clear redo
    public IUndoableAction Undo();                // InvalidOperationException when empty
    public IUndoableAction Redo();
    public void Clear();
    /// Removes every matching step from both stacks; the order of the rest is kept. Returns the number removed.
    public int RemoveWhere(Func<IUndoableAction, bool> match);
}
```

Festlegungen (Abweichungen von der Vorlage):
- Keine Obergrenze (`maxEntries` entfällt). Die History lebt bis zum Schließen der App.
- Wirft `Do` in `Execute`, kommt der Schritt nicht auf den Stack; die Ausnahme geht an den Aufrufer.
- Wirft `Undo` oder `Redo` einer Aktion, ist der Schritt aus beiden Stacks entfernt (Spec 7: „der Schritt bleibt in der History nicht erhalten“), `Changed` wird ausgelöst, die Ausnahme geht an den Aufrufer.
- Wirft ein `Changed`-Handler, ist der Stack schon im neuen Zustand.

- [ ] **Step 1: Failing Tests schreiben** (`ActionHistoryTests`, mit einer privaten Testaktion, die Aufrufe zählt und auf Wunsch wirft)

```csharp
[Fact] public void Execute_runs_the_action_and_makes_it_undoable()
[Fact] public void Record_does_not_run_the_action()
[Fact] public void Undo_and_redo_move_the_step_between_the_stacks_and_return_it()
[Fact] public void A_new_step_clears_the_redo_stack()
[Fact] public void Undo_on_an_empty_history_throws_InvalidOperationException()
[Fact] public void A_failing_Do_is_not_recorded()
[Fact] public void A_failing_Undo_drops_the_step_and_rethrows()          // CanUndo false, CanRedo false afterwards, Changed raised once
[Fact] public void A_failing_Redo_drops_the_step_and_rethrows()
[Fact] public void RemoveWhere_removes_from_both_stacks_and_keeps_the_order()   // steps a b c d, undo d; remove b and d -> undo order c, a; redo empty; returns 2
[Fact] public void RemoveWhere_without_a_match_raises_nothing()
[Fact] public void Changed_is_raised_once_per_change()
```

- [ ] **Step 2: RED** — `dotnet test tests/TotalWarModTool.UndoRedo.Tests --nologo -v q` (Build-Fehler: Typen fehlen).
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — derselbe Befehl, 11 bestanden; danach die Library-Suite.
- [ ] **Step 5: Commit** — `feat: add the undo history library`

---

### Task 2: Zellregeln öffentlich machen (`Tables`)

Das Dokument muss eine Eingabe genau so prüfen, wie `TableTsv.Read` sie später liest, und eine Zelle genau so anzeigen, wie `TableTsv.Write` sie schreibt. Die Regeln liegen heute privat in `TableTsv` (`TryParseCell`, `TryAppendCell`, `TryAppendText`, `ColumnsOf`).

**Files:**
- Create: `src/TotalWarModTool.Tables/CellText.cs`
- Modify: `src/TotalWarModTool.Tables/TableTsv.cs` (nutzt `CellText`, Verhalten unverändert)
- Test: `tests/TotalWarModTool.Tables.Tests/CellTextTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace TotalWarModTool.Tables;

public static class CellText
{
    /// The text of a cell as TableTsv writes it, before the escaping of tabs and line breaks: "true"/"false", integers and
    /// floats in the invariant culture (floats in the shortest exact form), a colour as at least 6 upper-case hex digits, text as it is.
    /// Throws ArgumentException when the value does not have the type of the field (see DecodedTable for the types).
    public static string Format(FieldType type, object? value);

    /// The value for a cell text, with the rules of TableTsv.Read for one unescaped cell. False with a short English reason.
    /// A colour is normalised to the form Format writes. Text is refused when it contains a lone surrogate, a NUL,
    /// or the literal two-character sequences the dialect uses as escapes (backslash backslash t, backslash backslash n).
    public static bool TryParse(FieldType type, string text, out object? value, [NotNullWhen(false)] out string? reason);

    /// The default value of a new cell: false, 0, 0.0, "000000", "".
    public static object DefaultValue(FieldType type);

    /// The indexes of the fields that TableTsv writes as part of a colour group (r/g/b triple). Their values must be 0 to 255.
    public static IReadOnlySet<int> ColourChannelFields(TableDefinition definition);

    /// Escapes tab, line feed and carriage return the way the dialect does; Unescape is the exact inverse for text Escape produced.
    public static string Escape(string text);
    public static string Unescape(string text);
}
```

Festlegungen:
- `TableTsv` ruft für Einzelzellen `CellText` auf; es gibt danach genau eine Stelle für diese Regeln. Die vorhandenen `TableTsv`-Tests bleiben unverändert grün (Parity).
- `FieldType.Unsupported`: `Format` und `DefaultValue` werfen `ArgumentException`, `TryParse` liefert false.
- `TryParse` für `F32`: ein Wert, der als `float` unendlich würde, wird abgelehnt; `NaN` und `Infinity` nur, wenn `TableTsv.Read` sie heute annimmt (Verhalten übernehmen, nicht ändern; der Test hält den Ist-Zustand fest).
- Wie `TableTsv` den Wagenrücklauf (`\r`) in einer Zelle behandelt, steht in „Festlegungen aus Plan 1“ der Spec; `Escape`/`Unescape` folgen dem.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Theory] [InlineData(...)] public void Format_and_TryParse_round_trip_every_field_type(FieldType type, string text)
// one text per type incl. I16 -32768, I64 9223372036854775807, F32 "0.1", F64 "1E-05" (or what R gives), colour "00FF7F", empty optional string

[Fact] public void Format_writes_what_TableTsv_writes()
// one row with every field type through TableTsv.Write; each cell of the data line (unescaped) equals CellText.Format

[Fact] public void TryParse_accepts_what_TableTsv_reads()
// for a list of cell texts per type (valid and invalid): CellText.TryParse succeeds exactly when TableTsv.Read of a one-row table succeeds

[Theory] public void Every_refused_input_names_its_reason(FieldType type, string text)
// "1.5" for I32, "32768" for I16, "abc" for F64, "yes" for Boolean, "GG0000" and "" and 9 digits for ColourRgb,
// text with backslash-backslash-t, text with backslash-backslash-n, a lone surrogate (built in the test from (char)0xD800), "\0"
// -> false, reason not empty

[Fact] public void A_colour_is_normalised()                 // "ff7f" -> "00FF7F"
[Fact] public void DefaultValue_can_be_formatted_and_parsed_for_every_type()
[Fact] public void ColourChannelFields_finds_the_groups_TableTsv_writes()
// definition with x_r, x_g, x_b, plain "r","g","b", and a lone "y_r": the six grouped indexes, not the lone one;
// cross-check: the header TableTsv.Write produces has a hex column for each group
[Fact] public void Escape_and_Unescape_are_inverse_and_match_TableTsv()
```

- [ ] **Step 2: RED** — `dotnet test tests/TotalWarModTool.Tables.Tests --filter "FullyQualifiedName~CellTextTests" --nologo -v q`
- [ ] **Step 3: Implementieren** (Code aus `TableTsv` verschieben, nicht kopieren).
- [ ] **Step 4: GREEN** — das ganze Projekt `Tables.Tests` (140 alte Tests unverändert), danach die Library-Suite.
- [ ] **Step 5: Commit** — `refactor: expose the cell rules of the tsv dialect`

---

### Task 3: Library `Editing`, Dokument und Befehle

**Files:**
- Create: `src/TotalWarModTool.Editing/TotalWarModTool.Editing.csproj`, `RowsDocument.cs`, `DocumentModel.cs`, `DocumentEdit.cs`; `tests/TotalWarModTool.Editing.Tests/TotalWarModTool.Editing.Tests.csproj`, `RowsDocumentTests.cs`
- Modify: `TotalWarModTool.slnx`

**Interfaces:**
- Consumes: `CellText` (Task 2), `IUndoableAction` (Task 1), `TableTsv.Write/WriteLoc`, `TableDefinition`, `FieldDefinition`, `LocRow`.
- Produces:

```csharp
namespace TotalWarModTool.Editing;

public enum DocumentKind { Table, Loc }

public enum CellProblem { None, InvalidValue, ColourChannelOutOfRange }

/// Value is set when Problem is None. Detail is short technical English.
public sealed record CellCheck(object? Value, CellProblem Problem, string? Detail);

public enum DocumentChangeKind { CellsChanged, RowsInserted, RowsRemoved }

/// RowIndexes are ascending. For RowsInserted they are the indexes after the insert, for RowsRemoved the indexes before the removal.
/// Column is set for CellsChanged of one column, otherwise -1.
public sealed record DocumentChange(DocumentChangeKind Kind, IReadOnlyList<int> RowIndexes, int Column);

/// One undo step of one document. Do and Undo raise RowsDocument.Changed.
public sealed class DocumentEdit : IUndoableAction
{
    public RowsDocument Document { get; }
    public string Description { get; }
    public void Do();
    public void Undo();
}

public sealed class RowsDocument
{
    /// A DB table. The rows are copied into a list; the row arrays themselves are taken over and owned by the document.
    public static RowsDocument ForTable(string tableName, int version, string packPath, TableDefinition definition, IReadOnlyList<object?[]> rows);
    /// A loc file: three columns "key" (StringU16), "text" (StringU16), "tooltip" (Boolean).
    public static RowsDocument ForLoc(string packPath, IReadOnlyList<LocRow> rows);

    public DocumentKind Kind { get; }
    public string PackPath { get; }
    public string TableName { get; }                       // "Loc" for a loc file
    public int Version { get; }
    public IReadOnlyList<FieldDefinition> Columns { get; }
    public IReadOnlyList<object?[]> Rows { get; }          // live view; a row array is the identity of a row
    public bool IsModified { get; }
    public event EventHandler<DocumentChange>? Changed;    // after every Do and Undo of an edit
    public event EventHandler? ModifiedChanged;

    public CellCheck Check(int column, string text);
    public object?[] NewRow();                             // CellText.DefaultValue per column

    // Each returns a step that has NOT run yet; the caller runs it through ActionHistory.Execute.
    public DocumentEdit SetCell(int row, int column, object? value);
    public DocumentEdit InsertRows(int index, IReadOnlyList<object?[]> rows);
    public DocumentEdit DeleteRows(IReadOnlyList<int> rowIndexes);
    public DocumentEdit DuplicateRows(IReadOnlyList<int> rowIndexes);   // copies are inserted as one block after the last given row

    /// The saved point is the state after the edit that was applied last.
    public void MarkSaved();
    /// The TSV text of the file (TableTsv.Write or WriteLoc). Text is null when the dialect refuses it; Detail says why.
    public TsvWriteResult ToText();
}
```

Festlegungen:
- `IsModified` ist genau dann false, wenn der zuletzt angewandte Schritt derselbe ist wie beim letzten `MarkSaved` (Anfang: keiner). Umsetzung über eine Liste der angewandten Schritte des Dokuments, nicht über einen Zähler: speichern, zurücknehmen, neuer Schritt ergibt „geändert“.
- `Check`: `CellText.TryParse` nach dem Typ der Spalte; für Spalten aus `CellText.ColourChannelFields` zusätzlich 0 bis 255, sonst `ColourChannelOutOfRange`. Für Loc gibt es keine Farbkanäle.
- Die Befehle prüfen ihre Argumente sofort (`ArgumentOutOfRangeException` für Indexe, `ArgumentException` für eine Zeile mit falscher Zellenzahl oder einen Wert vom falschen Typ; Typprüfung über `CellText.Format`, das wirft). Der Aufrufer übergibt nur geprüfte Werte.
- `SetCell` mit dem Wert, den die Zelle schon hat, liefert trotzdem einen Schritt; das Vermeiden ist Sache des Aufrufers (Task 6).
- `DeleteRows` und `DuplicateRows`: Indexe dürfen unsortiert sein, doppelte zählen einmal. Leere Liste: `ArgumentException`.
- Ein Schritt hält nur seine eigenen Zeilen und Indexe, nie eine Kopie der Tabelle. `Undo` eines Löschens setzt dieselben Zeilen-Arrays an die alten Stellen.
- Ein Schritt, dessen `Do` zweimal hintereinander oder dessen `Undo` ohne `Do` aufgerufen wird, wirft `InvalidOperationException`.
- `ToText` für Loc baut `LocRow` aus den drei Zellen.
- `Description`: `"set cell"`, `"insert rows"`, `"delete rows"`, `"duplicate rows"`, gefolgt von Leerzeichen und `PackPath`.

- [ ] **Step 1: Failing Tests schreiben** (`RowsDocumentTests`; ein Testschema mit jedem Feldtyp und einer Farbgruppe)

```csharp
[Fact] public void SetCell_changes_the_cell_and_undo_restores_it()             // Changed: CellsChanged, [row], column, on Do and on Undo
[Fact] public void InsertRows_puts_the_rows_at_the_index_and_undo_removes_them()
[Fact] public void DeleteRows_removes_unsorted_and_duplicate_indexes_once_and_undo_restores_the_same_arrays()
[Fact] public void DuplicateRows_inserts_copies_after_the_last_row_and_the_copies_are_independent()
[Fact] public void Redo_after_undo_gives_the_same_state_for_every_command()
[Fact] public void Commands_refuse_bad_indexes_wrong_cell_counts_and_wrong_value_types()
[Fact] public void A_step_refuses_a_second_Do_and_an_Undo_before_Do()
[Fact] public void Check_uses_the_cell_rules_and_the_channel_range()           // "256" for x_r -> ColourChannelOutOfRange; "256" for a plain I32 -> None
[Fact] public void NewRow_holds_the_default_of_every_column()
[Fact] public void Modified_follows_the_saved_point_through_undo_and_new_edits()
// new: false; edit: true; MarkSaved: false; undo: true; redo: false; undo + other edit: true; ModifiedChanged raised only on a change of the value
[Fact] public void A_loc_document_has_three_columns_and_writes_a_loc_file()     // ToText equals TableTsv.WriteLoc of the same rows
[Fact] public void ToText_equals_TableTsv_Write_of_the_rows()
[Fact] public void Whatever_the_document_accepts_can_be_written_and_read_back()
// for a list of texts per column type: when Check accepts, SetCell + ToText gives text, and TableTsv.Read of it returns the same cell value
[Fact] public void A_bulk_insert_is_one_step_and_does_not_copy_the_table()
// 100,000 rows; insert 1,000: one DocumentEdit; the row arrays that were there are the same references afterwards; undo restores the count
```

- [ ] **Step 2: RED** — `dotnet test tests/TotalWarModTool.Editing.Tests --nologo -v q`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — derselbe Befehl; danach die Library-Suite.
- [ ] **Step 5: Commit** — `feat: add the editing document for tables and loc files`

---

### Task 4: Kopieren und Einfügen als Text (`Editing`)

**Files:**
- Create: `src/TotalWarModTool.Editing/ClipboardRows.cs`
- Test: `tests/TotalWarModTool.Editing.Tests/ClipboardRowsTests.cs`

**Interfaces:**
- Consumes: `CellText`, `RowsDocument.Check`, `RowsDocument.NewRow`.
- Produces:

```csharp
public enum PasteProblem { None, Empty, NoKnownColumn, DuplicateColumn, WrongCellCount, InvalidValue }

/// With Problem None: Rows are ready for RowsDocument.InsertRows. MissingColumns got their default, ExtraColumns were dropped.
/// Otherwise Rows is empty; Line is 1-based (the header is line 1) and Detail is short technical English naming column and reason.
public sealed record PasteResult(IReadOnlyList<object?[]> Rows, IReadOnlyList<string> MissingColumns, IReadOnlyList<string> ExtraColumns,
    PasteProblem Problem, int Line, string? Detail);

public static class ClipboardRows
{
    /// Header line with the column names, then one line per row; cells as CellText.Format, escaped with CellText.Escape; lines end with "\n".
    public static string Write(IReadOnlyList<FieldDefinition> columns, IEnumerable<object?[]> rows);
    public static PasteResult Read(string text, RowsDocument target);
}
```

Festlegungen:
- Die Zwischenablage trägt keine Metazeile und keine Farbgruppen als Hex-Spalte: jede Spalte des Schemas einzeln. Das Format ist das der App, nicht der RPFM-Dialekt.
- `Read`: Zeilenenden `\n` und `\r\n`; eine leere letzte Zeile zählt nicht. Spalten werden per Name zugeordnet (ordinal, Groß-/Kleinschreibung zählt). Kein einziger Name bekannt: `NoKnownColumn`. Name doppelt im Kopf: `DuplicateColumn`. Nur Kopf ohne Zeilen oder leerer Text: `Empty`.
- Eine einzige ungültige Zelle lehnt das ganze Einfügen ab (`InvalidValue`, Zeile und Spalte im `Detail`); geprüft wird mit `target.Check`, also samt Farbkanal-Grenze.
- `Write` nimmt Zeilen als `object?[]` in Spaltenreihenfolge; so dient es auch den Nur-Lese-Tabs (Task 10).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Write_then_Read_gives_the_same_rows()
[Fact] public void Columns_are_matched_by_name_in_any_order()
[Fact] public void Missing_columns_get_defaults_and_extra_columns_are_dropped_and_both_are_named()
[Fact] public void A_paste_with_one_bad_cell_changes_nothing()     // Problem InvalidValue, Rows empty, Line and column name in Detail
[Fact] public void A_wrong_cell_count_names_its_line()
[Fact] public void Text_without_a_known_column_is_refused()         // plain prose, or a header of another table
[Fact] public void Empty_text_and_a_header_alone_are_Empty()
[Fact] public void Tabs_and_line_breaks_inside_cells_survive()
[Fact] public void Windows_line_ends_are_read()
[Fact] public void A_channel_value_above_255_is_refused()
```

- [ ] **Step 2: RED** — `dotnet test tests/TotalWarModTool.Editing.Tests --filter "FullyQualifiedName~ClipboardRowsTests" --nologo -v q`
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — das ganze Projekt `Editing.Tests`; danach die Library-Suite.
- [ ] **Step 5: Commit** — `feat: copy and paste rows as text`

---

### Task 5: Projektdatei schreiben (`Projects`)

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectFileWriter.cs`
- Modify: `src/TotalWarModTool.Storage/AtomicFile.cs` (nur falls nötig für UTF-8 ohne BOM, siehe unten)
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectFileWriterTests.cs`

**Interfaces:**
- Consumes: `ProjectFile.PackFolder`, `ProjectFiles.IsLink`, `ProjectLayout.UnsafePathReason`, `AtomicFile`, `FileFingerprint`.
- Produces:

```csharp
public enum FileWriteProblem { None, UnsafePath, Link, Locked, Failed }

/// Fingerprint is the state of the file right after the write (Problem None), otherwise null. Detail is short technical English.
public sealed record FileWriteResult(FileWriteProblem Problem, string? Detail, FileFingerprint? Fingerprint);

public static class ProjectFileWriter
{
    /// Writes text as UTF-8 without BOM to pack\<relativeFile> of the project, atomically (temp file next to the target, then replace).
    /// The folder of the file must exist. Never throws for the file system; only argument errors are thrown.
    public static FileWriteResult WriteText(string projectFolder, string relativeFile, string text);

    /// The fingerprint of pack\<relativeFile>, or null when the file does not exist or cannot be asked.
    public static FileFingerprint? Fingerprint(string projectFolder, string relativeFile);
}
```

Festlegungen:
- `UnsafePath`: `relativeFile` ist leer, absolut, enthält `..`, oder der aufgelöste volle Pfad liegt nicht unter `pack\` (dieselben Prüfungen wie `ProjectFileOpener`). Es wird nichts angefasst.
- `Link`: `pack\`, ein Ordner auf dem Weg oder die Zieldatei selbst ist eine Verknüpfung (Spec „Festlegungen aus Plan 3“, Punkt 19). Es wird nichts geschrieben.
- Der Ordner der Datei fehlt: `Failed` mit Grund. Dieser Plan legt keine Ordner an (neue Dateien kommen mit Plan 5). Fehlt nur die Datei, wird sie angelegt.
- `Locked`: Das Ersetzen scheitert nach allen Versuchen von `AtomicFile` mit `IOException` oder `UnauthorizedAccessException`. Die temp-Datei ist danach gelöscht, die alte Datei unverändert.
- Temp-Datei: `<Zieldatei>.<8 Hex>.tmp` im selben Ordner (das Muster von `AtomicFile.WriteAllText`; `ProjectFiles.IsIgnored` ignoriert `*.tmp` schon). Schreibt `AtomicFile.WriteAllText` heute mit BOM oder anderer Kodierung, bekommt es eine Überladung mit `Encoding`; die alte Signatur bleibt.
- Zeilenenden: Der Text wird geschrieben, wie er kommt (`RowsDocument.ToText` liefert `\n`).

- [ ] **Step 1: Failing Tests schreiben** (alle in `TempDir`; Verknüpfungen mit dem vorhandenen `JunctionHelper`)

```csharp
[Fact] public void WriteText_replaces_the_file_and_returns_its_fingerprint()        // bytes: UTF-8 without BOM, exactly the text
[Fact] public void WriteText_creates_a_missing_file_in_an_existing_folder()
[Fact] public void A_missing_folder_is_Failed_and_nothing_is_created()
[Theory] public void Unsafe_paths_are_refused_without_touching_the_disk(string relativeFile)   // "", "..\\x.tsv", "a/../../x", "C:\\x.tsv", "\\x"
[Fact] public void A_linked_pack_folder_a_linked_sub_folder_and_a_linked_file_are_refused()     // the link target is byte-identical afterwards
[Fact] public void A_locked_file_is_reported_and_left_unchanged()                   // target opened with FileShare.None: Locked, old bytes, no *.tmp left
[Fact] public void No_temp_file_is_left_after_a_successful_write()
[Fact] public void Fingerprint_is_null_for_a_missing_file()
```

- [ ] **Step 2: RED** — `dotnet test tests/TotalWarModTool.Projects.Tests --filter "FullyQualifiedName~ProjectFileWriterTests" --nologo -v q`
- [ ] **Step 3: Implementieren.** Die Pfad- und Link-Prüfung liegt heute privat in `ProjectFileOpener` (`Workspace`); `Workspace` referenziert `Projects`, nicht umgekehrt. Die Prüfung zieht deshalb als `internal`/`public` Helfer nach `Projects` (z. B. `ProjectLayout` oder `ProjectFiles`) um, und `ProjectFileOpener` nutzt sie von dort. Die Tests von `ProjectFileOpener` bleiben unverändert grün.
- [ ] **Step 4: GREEN** — `Projects.Tests` und `Workspace.Tests` ganz; danach die Library-Suite.
- [ ] **Step 5: Commit** — `feat: write a project file atomically inside the pack folder`

---

### Task 6: Bearbeitbarer Tab (ViewModel)

**Files:**
- Create: `src/TotalWarModTool.App/Editing/EditableRowsTabViewModel.cs`, `Editing/EditRow.cs`
- Modify: `src/TotalWarModTool.App/TotalWarModTool.App.csproj` (Referenzen `UndoRedo`, `Editing`), `Tabs/TabsViewModel.cs`, `AppTexts.Projects.cs` (oder neue Datei `AppTexts.Editing.cs`), `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/EditableRowsTabViewModelTests.cs`, `TabsViewModelTests.cs` (ergänzen)

**Interfaces:**
- Consumes: `RowsDocument`, `DocumentEdit`, `ActionHistory`, `RowsTabViewModel` (Textansicht), `TabSource`.
- Produces:

```csharp
/// One row of the grid. The indexer gives the value the grid shows: a bool for Boolean columns, otherwise the cell text.
/// Setting it asks the tab to change the cell; a refused value throws FormatException with the UI message (the grid shows it).
public sealed class EditRow : INotifyPropertyChanged
{
    public object? this[int column] { get; set; }
    internal object?[] Cells { get; }
}

public sealed class EditableRowsTabViewModel : RowsTabViewModel
{
    public EditableRowsTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close,
        RowsDocument document, ActionHistory history);

    public RowsDocument Document { get; }
    public Guid ProjectId { get; }                             // from the source key
    public string RelativeFile { get; }                        // entryPath with '\'
    public string Header { get; }
    public IReadOnlyList<FieldDefinition> Columns { get; }
    public ObservableCollection<EditRow> Rows { get; }
    public bool IsModified { get; }                            // raises PropertyChanged; the tab header shows a mark

    /// Null when the text is a valid value of the column, else the UI message (expected type, plus the technical detail).
    public string? CheckCell(int column, string text);

    /// The rows the user selected, as indexes; set by the view.
    public IReadOnlyList<int> SelectedRows { get; set; }
    public ICommand InsertRowCommand { get; }                  // a default row after the last selected row, or at the end
    public ICommand DeleteRowsCommand { get; }                 // disabled without a selection
    public ICommand DuplicateRowsCommand { get; }

    /// Replaces the document after a reload from disk. The caller removes the old steps from the history.
    internal void ReplaceDocument(RowsDocument document);
}
```

`TabsViewModel` bekommt:

```csharp
/// Asked first by Add: returns the tab for an entry that gets a special tab (an editable project file), or null for the default.
public Func<TabSource, OpenedEntry, Action<TabViewModel>, TabViewModel?>? SpecialTab { get; set; }

/// Asked before a tab is closed by its command, by Ctrl+W or by the middle button; false keeps it open.
public Func<TabViewModel, bool>? CanClose { get; set; }

/// Closes without asking (the app decided it: the project left the list, a reload replaced the tab).
public void CloseWithoutAsking(TabViewModel tab);
```

Festlegungen:
- Bearbeitbar ist ein Projekt-Tab genau dann, wenn die Quelle ein Projekt ist und der Eintrag eine Tabelle in Textform (`.tsv` unter `db\<tabelle>\`) oder eine Loc-Datei in Textform (`.loc.tsv`) ist, die ohne Problem gelesen wurde. Alles andere bleibt, wie es ist (Nur-Lese-Tab oder Hinweis-Tab). Die Entscheidung trifft der `DocumentsController` (Task 7) über `SpecialTab`; dieser Task liefert Tab und Haken.
- `EditRow`-Setter: gleicher Wert wie bisher: nichts tun. Sonst `Document.Check`; bei Problem `FormatException` mit der Meldung aus `CheckCell`; sonst `history.Execute(Document.SetCell(...))`.
- Das ViewModel hört auf `Document.Changed` und führt `Rows` nach: `CellsChanged` meldet `Item[]` an den betroffenen Zeilen, `RowsInserted`/`RowsRemoved` ändern die `ObservableCollection` an genau diesen Stellen (kein `Reset`, außer bei `ReplaceDocument`). Zeilenidentität ist das Zeilen-Array.
- Der Kopf (`Header`) zeigt Tabelle, Version und Zeilenzahl wie beim Nur-Lese-Tab und folgt der Zeilenzahl.
- Textansicht: Der Text entsteht aus `Document.ToText()` und wird nach jeder Änderung beim nächsten Umschalten auf „Text“ neu gebaut (kein Dauer-Cache wie beim Nur-Lese-Tab). `RowsTabViewModel` bekommt dafür eine geschützte Methode `InvalidateText()`.
- `IsTableEntry` ist für ein Tabellen-Dokument true (Schema-Änderung markiert den Tab wie bisher).
- `CanClose`/`CloseWithoutAsking`: `CloseCommand` eines Tabs, `CloseSelectedCommand` und die mittlere Maustaste gehen über `CanClose`. `Close(tab)` bleibt als die fragende Variante bestehen; `MainViewModel.CloseTabsOfRemovedProjects` nutzt `CloseWithoutAsking`.
- Neue Texte: Geändert-Markierung (Tooltip), Zeilenbefehle, Prüfmeldungen pro erwartetem Typ (ganze Zahl mit Bereich, Kommazahl, Wahrheitswert, Farbe, Text, Farbkanal 0 bis 255).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void A_cell_shows_its_text_and_a_boolean_shows_a_bool()
[Fact] public void Setting_a_valid_text_changes_the_document_through_the_history()      // history.CanUndo, IsModified, PropertyChanged "Item[]" on the row
[Fact] public void Setting_the_same_value_records_nothing()
[Fact] public void Setting_an_invalid_text_throws_FormatException_with_the_message_and_changes_nothing()
[Fact] public void CheckCell_names_the_expected_type()                                   // one case per field type and the channel range
[Fact] public void Undo_and_redo_update_the_rows_without_a_reset()                        // CollectionChanged actions: Add/Remove only, same EditRow instances for untouched rows
[Fact] public void Insert_delete_and_duplicate_use_the_selection()
[Fact] public void Delete_and_duplicate_are_disabled_without_a_selection()
[Fact] public void The_header_follows_the_row_count_and_the_language()
[Fact] public void The_text_view_shows_the_current_state_after_an_edit()
[Fact] public void IsModified_follows_the_document()
// TabsViewModelTests
[Fact] public void SpecialTab_replaces_the_default_tab_and_null_keeps_it()
[Fact] public void CanClose_false_keeps_the_tab_for_the_command_and_for_close_selected()
[Fact] public void CloseWithoutAsking_does_not_ask()
```

- [ ] **Step 2: RED** — App-Testklassen `EditableRowsTabViewModelTests`, `TabsViewModelTests`.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben Klassen, dazu `AppTextsTests`, `TabFactoryTests`, `MainViewModelTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: add the editable tab for project tables`

---

### Task 7: `DocumentsController`, Rückgängig und Wiederholen

**Files:**
- Create: `src/TotalWarModTool.App/Editing/DocumentsController.cs`, `Editing/IEditDialogs.cs`, `Editing/EditDialogs.cs` (Meldung wie `ProjectDialogs.ShowMessage`); `tests/TotalWarModTool.App.Tests/FakeEditDialogs.cs`
- Modify: `MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`, `AppTexts`, `locales/*.json`
- Test: `tests/TotalWarModTool.App.Tests/DocumentsControllerTests.cs`, `MainWindowTests.cs` (ergänzen)

**Interfaces:**
- Consumes: Task 6, `ProjectsController.Projects` (Projektordner zu einer GUID), `TabsViewModel`.
- Produces:

```csharp
public sealed partial class DocumentsController
{
    public DocumentsController(TabsViewModel tabs, ProjectsController projects, ActionHistory history, IOperationRunner runner,
        IEditDialogs dialogs, IAppLog log, AppTexts texts);

    /// Everything the editing commands ask or tell the user. This task needs ShowMessage only; Tasks 8 and 9 add the questions.
    public interface IEditDialogs { void ShowMessage(string title, string message); }   // own file, not nested

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public IReadOnlyList<EditableRowsTabViewModel> EditableTabs { get; }
    public bool HasModifiedTabs { get; }
    /// Raised when the set of modified tabs changed.
    public event EventHandler? ModifiedChanged;
}
```

Festlegungen:
- Der Controller setzt `tabs.SpecialTab`: Regel aus Task 6; er baut das `RowsDocument` aus `OpenedEntry.Table` (`ForTable` mit `TableName`, `Version`, `EntryPath` als Pack-Pfad ohne `.tsv`, wie die Metazeile ihn trägt: über `ProjectLayout.FromRelativeFile`) oder `OpenedEntry.Loc` (`ForLoc`). Damit sind auch wiederhergestellte Session-Tabs bearbeitbar, ohne Änderung am `SessionCoordinator`.
- Schema fehlt: Projekt-Tabellen sind nicht bearbeitbar (es entsteht ohnehin ein Hinweis-Tab). Loc-Dateien brauchen kein Schema.
- `Undo`: `history.Undo()`; ist der Schritt ein `DocumentEdit`, wird der Tab seines Dokuments gewählt. `Redo` ebenso. Wirft die Aktion, zeigt der Controller eine Meldung (`MessageText`), loggt und lässt die History, wie `ActionHistory` sie hinterlassen hat.
- Wird ein bearbeitbarer Tab geschlossen (gleich auf welchem Weg, auch nach dem Speichern), entfernt der Controller dessen Schritte: `history.RemoveWhere(a => a is DocumentEdit e && e.Document == document)`. Die Spec nennt das nur für „Verwerfen“; ein geschlossenes Dokument gibt es nicht mehr, also gilt es für jedes Schließen. Task 11 trägt das in die Spec ein.
- Befehle sind deaktiviert, solange `runner.IsRunning`, und wenn die History leer ist.
- Menü: neues Menü „Bearbeiten“ (`Texts.MenuEdit`) zwischen „Werkzeuge“ und „Projekt“ mit „Rückgängig“ (`Strg+Z`) und „Wiederholen“ (`Strg+Y`). `KeyBinding` am Fenster. Hat eine Textbox den Fokus (Zelle im Editiermodus, Textansicht), behält sie ihr eigenes `Strg+Z`; das ist WPF-Standard und bleibt so.
- `MainViewModel` bekommt `Documents` (nullable wie `Projects`: ohne Projekte kein Controller).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void A_project_table_in_text_form_opens_as_an_editable_tab()
[Fact] public void A_project_loc_file_in_text_form_opens_as_an_editable_tab()
[Fact] public void A_binary_project_table_a_pack_table_and_a_broken_tsv_stay_as_they_were()
[Fact] public void Undo_switches_to_the_tab_of_the_step()                 // two tabs, edit A then select B: undo selects A and restores the cell
[Fact] public void Redo_switches_to_the_tab_of_the_step()
[Fact] public void Closing_a_tab_removes_its_steps()                      // edits in A and B, close A: one undo left, it belongs to B
[Fact] public void Undo_and_redo_are_disabled_when_empty_and_while_an_operation_runs()
[Fact] public void A_failing_undo_shows_a_message_and_does_not_throw()
[Fact] public void HasModifiedTabs_and_ModifiedChanged_follow_the_tabs()
// MainWindowTests
[Fact] public void The_edit_menu_has_undo_and_redo_with_their_gestures()
```

- [ ] **Step 2: RED** — `DocumentsControllerTests`, `MainWindowTests`.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `MainViewModelTests`, `MainViewModelWorkspaceTests`, `SessionCoordinatorTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: undo and redo across the editable tabs`

---

### Task 8: Speichern und Schutz ungespeicherter Änderungen

**Files:**
- Create: `src/TotalWarModTool.App/Editing/ChoiceWindow.xaml`(+`.cs`), `Editing/DocumentsController.Saving.cs`
- Modify: `Editing/IEditDialogs.cs`, `Editing/EditDialogs.cs`, `FakeEditDialogs.cs` (Tests), `DocumentsController.cs`, `Projects/ProjectsViewModel.cs`, `MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`, `Tabs/TabsView.xaml` (Markierung), `AppTexts`, `locales/*.json`
- Test: `DocumentsControllerSavingTests.cs`, `ProjectsViewModelTests.cs`, `MainWindowTests.cs`, `ProjectWindowsTests.cs` (ergänzen)

**Interfaces:**
- Consumes: `ProjectFileWriter.WriteText` (Task 5), `RowsDocument.ToText/MarkSaved`.
- Produces:

```csharp
public enum SaveChoice { Save, Discard, Cancel }

public interface IEditDialogs      // ShowMessage exists since Task 7; the two questions are new
{
    /// Asks what to do with unsaved changes. files are display names (escaped), at most 10 are shown, then a count.
    SaveChoice AskUnsaved(IReadOnlyList<string> files);
    /// "Save all and continue" or cancel, before a build.
    bool AskSaveAllBeforeBuild(IReadOnlyList<string> files);
    void ShowMessage(string title, string message);
}

// DocumentsController
public ICommand SaveCommand { get; }       // the selected tab, when it is editable and modified
public ICommand SaveAllCommand { get; }
/// Saves the tabs through the runner. True when every tab was saved. Problems are shown in one message.
public Task<bool> SaveAsync(IReadOnlyList<EditableRowsTabViewModel> tabs);
/// Asks for the modified tabs among these (none modified: true without asking). Save: saves, true when all were saved.
/// Discard: true. Cancel: false.
public Task<bool> ConfirmCloseAsync(IReadOnlyList<EditableRowsTabViewModel> tabs);
/// The reason why a project cannot leave the list right now, or null: it has tabs with unsaved changes.
public string? UnsavedReason(Guid projectId);
```

Festlegungen:
- Ablauf des Speicherns pro Tab: `ToText()` und `ProjectFileWriter.WriteText` laufen im Runner (ein Lauf für alle Tabs, Fortschritt „Datei n / gesamt“, abbrechbar zwischen zwei Dateien). Während des Laufs ist das Fenster gesperrt, das Dokument ändert sich also nicht. Nach dem Lauf, im UI-Thread: für jede geschriebene Datei `MarkSaved()` und den neuen Fingerprint merken (Task 9 nutzt ihn).
- `ToText` verweigert (`Text == null`): Die Datei wird nicht geschrieben, die Meldung nennt Datei und Grund. Das darf nach Task 2 und 3 nicht vorkommen, wird aber behandelt.
- Jedes `FileWriteProblem` ergibt einen eigenen Meldungstext (`Locked` mit Hinweis, die Datei im anderen Programm zu schließen). Der Tab bleibt „geändert“. Mehrere Probleme stehen in einer Meldung, höchstens 10 Zeilen, dann eine Zahl.
- Tab schließen (Befehl, `Strg+W`, mittlere Taste): `tabs.CanClose` ist synchron, das Speichern ist asynchron. Regel: Ein geänderter Tab wird über `CanClose` nie sofort geschlossen (false); der Controller startet `ConfirmCloseAsync` und schließt danach mit `CloseWithoutAsking`, wenn das Ergebnis true ist. Ein unveränderter Tab schließt sofort.
- App schließen: `Window.Closing` in `App.xaml.cs`: Gibt es geänderte Tabs, wird das Schließen abgebrochen (`e.Cancel = true`), `ConfirmCloseAsync` für alle gestartet und bei true das Fenster erneut geschlossen (ein Flag verhindert die zweite Frage). Läuft gerade eine Operation, wird nur abgebrochen. Eine Rückfrage für alle Tabs zusammen.
- „Aus der Liste entfernen“: `ProjectsViewModel.RemoveDisabledReason` nennt zusätzlich `UnsavedReason` (über einen optionalen `Func<Guid, string?>`, den der Composition Root setzt); der Befehl ist dann deaktiviert. Keine Rückfrage an dieser Stelle.
- Bauen und „Bauen und installieren“ (Spec 5.7): Hat das Projekt geänderte Tabs, fragt `ProjectsViewModel` vor dem Bau über einen optionalen `Func<Guid, Task<bool>>` („vor dem Bau“), den der Composition Root auf den Controller legt: `AskSaveAllBeforeBuild`, dann `SaveAsync` der Tabs dieses Projekts; false oder ein Speicherproblem bricht den Bau ab.
- `ChoiceWindow`: ein modales Fenster mit Meldung, Dateiliste und zwei oder drei Knöpfen mit eigenen Texten; `Esc` und das Schließen-Kreuz bedeuten Abbrechen; der sichere Knopf (Speichern) ist Standard. Theme wie `CrashDialog`.
- Geänderte Tabs tragen im Tab-Kopf eine Markierung vor dem Titel (ein Punkt in `Theme.Text`, Tooltip „ungespeicherte Änderungen“), getrennt von der Veraltet-Markierung.
- Menü „Projekt“: „Speichern“ (`Strg+S`) und „Alle speichern“ (`Strg+Umschalt+S`) über dem Trenner vor „Bauen“. Deaktiviert ohne Grund-Tooltip nur, wenn es nichts zu speichern gibt; während einer Operation mit dem vorhandenen „beschäftigt“-Text.
- Kein Snapshot beim Speichern (Spec 5.8).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Save_writes_the_file_clears_the_mark_and_keeps_the_history()        // file content equals ToText; undo still possible, then modified again
[Fact] public void Save_all_writes_every_modified_tab_in_one_run()
[Fact] public void A_locked_file_keeps_the_tab_modified_and_names_the_file()
[Fact] public void Save_is_disabled_for_an_unmodified_tab_and_for_a_read_only_tab()
[Fact] public void Closing_an_unmodified_tab_does_not_ask()
[Fact] public void Closing_a_modified_tab_asks_and_cancel_keeps_it()
[Fact] public void Closing_a_modified_tab_with_Discard_closes_it_and_leaves_the_file()
[Fact] public void Closing_a_modified_tab_with_Save_saves_then_closes()
[Fact] public void Closing_with_Save_keeps_the_tab_when_the_save_fails()
[Fact] public void Closing_the_app_with_modified_tabs_asks_once()                        // ConfirmCloseAsync over all tabs: one AskUnsaved call naming both files
[Fact] public void UnsavedReason_names_a_project_with_modified_tabs_only()
// ProjectsViewModelTests
[Fact] public void Remove_is_disabled_with_a_reason_while_the_project_has_unsaved_changes()
[Fact] public void Build_with_unsaved_changes_asks_to_save_all_first()                   // no: nothing built; yes: saved, then built
// windows
[Fact] public void The_choice_window_returns_the_clicked_button_and_cancel_for_escape()
[Fact] public void The_project_menu_has_save_and_save_all_with_their_gestures()
```

- [ ] **Step 2: RED** — `DocumentsControllerSavingTests`, `ProjectsViewModelTests`, `MainWindowTests`, `ProjectWindowsTests`.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `DocumentsControllerTests`, `TabsViewTests`, `ControlTemplatesTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: save project tables and guard unsaved changes`

---

### Task 9: Änderungen von außen

**Files:**
- Create: `src/TotalWarModTool.App/Editing/DocumentsController.External.cs`
- Modify: `DocumentsController.Saving.cs`, `Editing/IEditDialogs.cs`, `EditDialogs.cs`, `FakeEditDialogs.cs`, `Tabs/TabViewModels.cs`, `Tabs/TabsView.xaml`, `App.xaml.cs`, `AppTexts`, `locales/*.json`
- Test: `DocumentsControllerExternalTests.cs`, `TabsViewTests.cs` (ergänzen)

**Interfaces:**
- Consumes: `ProjectFileWriter.Fingerprint`, `ProjectsController.OpenFileAsync`.
- Produces:

```csharp
public enum ConflictChoice { Overwrite, Reload, Cancel }

// IEditDialogs, new members
ConflictChoice AskConflict(string file);          // the file changed outside and the tab has own changes
bool AskCreateAgain(string file);                 // the file was deleted outside

// DocumentsController
/// Compares the fingerprint of every open project file tab (editable or not) with the one taken when it was opened or saved.
/// Silent, on a pool thread; marks changed and missing files as stale. Called when the window is activated.
public Task CheckExternalChangesAsync();
/// Reads the file again and replaces the content of the tab; the steps of the old document leave the history.
public Task ReloadAsync(TabViewModel tab);
```

Festlegungen:
- Der Fingerprint eines Projekt-Tabs wird beim Anlegen des Tabs genommen (`ProjectFileWriter.Fingerprint`). Weicht seine Länge von `OpenedEntry.UncompressedSize` ab, gilt der Tab sofort als veraltet (die Datei wurde zwischen Lesen und Fingerprint geändert).
- Geprüft wird: wenn das Hauptfenster aktiv wird (`Window.Activated`, entprellt mit einem `DispatcherTimer` von 300 ms; läuft eine Prüfung oder eine Operation, entfällt diese Runde), und vor jedem Speichern.
- Datei außen geändert oder gelöscht, Tab ohne eigene Änderungen: Markierung „veraltet“ (die vorhandene Markierung, neuer Grundtext „Die Datei wurde außerhalb geändert“ bzw. „gelöscht“), im Tab eine Zeile mit dem Knopf „Neu laden“. Kein automatisches Neuladen.
- Neu laden: `ProjectsController.OpenFileAsync`; ergibt es wieder einen bearbeitbaren Inhalt, `ReplaceDocument`; sonst wird der Tab durch den Tab ersetzt, den `TabsViewModel` für das Ergebnis baut (gleiche Position, bleibt gewählt). Die Schritte des alten Dokuments verlassen die History. Der Knopf „Neu laden“ erscheint nur an einem Tab ohne eigene Änderungen. Ein Tab mit eigenen Änderungen wird nur über den Konfliktdialog beim Speichern neu geladen.
- Speichern eines Tabs, dessen Datei außen geändert wurde: `AskConflict`: Überschreiben (schreiben), Neu laden (eigene Änderungen verwerfen, neu laden), Abbrechen. Bei „Alle speichern“ wird pro betroffener Datei gefragt, vor dem Lauf; die übrigen werden gespeichert.
- Speichern eines Tabs, dessen Datei außen gelöscht wurde: `AskCreateAgain`; ja schreibt (der Ordner muss noch existieren, sonst das Problem aus Task 5).
- Nur-Lese-Tabs von Projektdateien (binäre Tabelle, Text, Hinweis) bekommen dieselbe Veraltet-Markierung, ohne „Neu laden“ in diesem Plan (schließen und neu öffnen genügt).
- Nach `F5` (`ProjectsController.Reload`) läuft dieselbe Prüfung.

- [ ] **Step 1: Failing Tests schreiben** (Dateien in `TempDir`; „außen ändern“ = Datei schreiben und mtime setzen)

```csharp
[Fact] public void An_outside_change_marks_an_unmodified_tab_stale_and_does_not_reload_it()
[Fact] public void An_outside_delete_marks_the_tab_stale_with_its_own_reason()
[Fact] public void An_unchanged_file_marks_nothing()
[Fact] public void Reload_shows_the_new_content_clears_the_mark_and_drops_the_old_steps()
[Fact] public void Reload_of_a_file_that_became_unreadable_replaces_the_tab_with_a_hint_tab_at_the_same_place()
[Fact] public void Saving_over_an_outside_change_asks_first()          // Cancel: file keeps the outside content, tab stays modified;
                                                                        // Overwrite: file has the tab content; Reload: tab has the outside content, not modified
[Fact] public void Saving_a_deleted_file_asks_before_it_is_created_again()
[Fact] public void Save_all_asks_per_conflict_and_saves_the_rest()
[Fact] public void A_save_updates_the_fingerprint_so_the_own_write_is_not_an_outside_change()
[Fact] public void A_check_is_skipped_while_an_operation_runs()
// TabsViewTests
[Fact] public void A_stale_editable_tab_shows_the_reload_button()
```

- [ ] **Step 2: RED** — `DocumentsControllerExternalTests`, `TabsViewTests`.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `DocumentsControllerSavingTests`, `DocumentsControllerTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: detect outside changes of open project files`

---

### Task 10: Raster (View), Zeilenbefehle, Kopieren und Einfügen

**Files:**
- Create: `src/TotalWarModTool.App/Editing/IClipboard.cs`, `Editing/WpfClipboard.cs`, `Editing/CellRule.cs` (eine `ValidationRule`); `tests/TotalWarModTool.App.Tests/FakeClipboard.cs`
- Modify: `Tabs/TableGrid.cs`, `Tabs/TabsView.xaml`, `Tabs/TabViewModels.cs`, `Editing/EditableRowsTabViewModel.cs`, `App.xaml.cs`, `AppTexts`, `locales/*.json`
- Test: `EditableGridTests.cs`, `EditableRowsTabViewModelTests.cs`, `TabsViewModelTests.cs` (ergänzen)

**Interfaces:**
- Consumes: `ClipboardRows` (Task 4), Task 6.
- Produces:

```csharp
/// The clipboard as text; the fake in the tests holds a string. An outside-world interface (the WPF clipboard needs an STA thread and can fail).
public interface IClipboard
{
    string? GetText();                 // null when there is no text or the clipboard cannot be opened
    bool SetText(string text);         // false when the clipboard cannot be opened
}

// EditableRowsTabViewModel (constructor gets IClipboard)
public ICommand CopyRowsCommand { get; }       // the selected rows with the header line
public ICommand PasteRowsCommand { get; }      // inserts after the last selected row, or at the end
public string? Notice { get; }                 // the result of the last paste (missing and dropped columns), or why it was refused

// RowsTabViewModel (read-only table and loc tabs)
public IReadOnlyList<int> SelectedRows { get; set; }
public ICommand CopyRowsCommand { get; }
```

Festlegungen:
- `TableGrid` bekommt einen zweiten Modus: Ist `EditColumns` (eine neue Dependency-Property, `IReadOnlyList<FieldDefinition>`) gesetzt, ist das Raster bearbeitbar (`IsReadOnly = false`, weiter `CanUserAddRows = false`, `CanUserDeleteRows = false`): pro Spalte eine `DataGridTextColumn` mit `Binding("[i]")`, `Mode = TwoWay`, `UpdateSourceTrigger = LostFocus`, `ValidatesOnExceptions = true` und einer `CellRule`, die `CheckCell(i, text)` des Tabs fragt; für `Boolean` eine `DataGridCheckBoxColumn`. Schlüsselspalten (`IsKey`) bekommen einen fetten Kopf. Ohne `EditColumns` bleibt alles wie bisher.
- Ungültige Eingabe: Die Zelle bleibt im Editiermodus (WPF-Standard bei fehlgeschlagener Regel), der Tooltip der Zelle und eine Zeile unter dem Kopf des Tabs zeigen die Meldung. `Esc` verwirft die Eingabe.
- Auswahl: `SelectionUnit = FullRow`, `SelectionMode = Extended`. `TableGrid` meldet die Indexe der gewählten Zeilen über eine Dependency-Property `SelectedRowIndexes` (TwoWay nach `SelectedRows`); das ist View-Verdrahtung.
- Tasten und Kontextmenü im bearbeitbaren Raster, wenn keine Zelle im Editiermodus ist: `Einfg` Zeile einfügen, `Entf` Zeilen löschen, `Strg+D` duplizieren, `Strg+C` kopieren, `Strg+V` einfügen. In Nur-Lese-Rastern (Tabelle und Loc aus jeder Quelle): `Strg+C` und Kontextmenü „Kopieren“. Loc-Zeilen eines Nur-Lese-Tabs werden für `ClipboardRows.Write` in drei Zellen umgesetzt (Spalten wie `RowsDocument.ForLoc`).
- Kopieren: `ClipboardRows.Write`; liefert `SetText` false, erscheint eine Meldung in `Notice`. Mehr als 100.000 gewählte Zeilen: ablehnen mit `Notice` (Zwischenablage).
- Einfügen: `GetText` → `ClipboardRows.Read(text, Document)`; Problem: `Notice` nennt Zeile und Grund (Text aus `PasteProblem` plus maskiertes `Detail`), nichts ändert sich. Erfolg: ein Schritt `InsertRows` über die History; `Notice` nennt fehlende und überzählige Spalten, wenn es welche gibt.
- Zellen als `EditRow` mit Indexer und Spaltenvirtualisierung (Spike S5). Sortieren per Klick auf den Spaltenkopf ist im bearbeitbaren Raster aus (`CanUserSortColumns = false`): Zeilenindexe der Ansicht und des Dokuments müssen gleich sein. Backlog-Eintrag in Task 11.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// EditableGridTests (Sta.RunAsync, real TableGrid in a window, Themes/Controls.xaml loaded)
[Fact] public void The_grid_builds_an_editable_column_per_field_and_a_check_box_for_booleans()
[Fact] public void Committing_a_valid_cell_changes_the_document()             // BeginEdit, set the TextBox text, CommitEdit
[Fact] public void An_invalid_cell_stays_in_edit_mode_and_shows_the_message()   // Validation.GetHasError on the cell, document unchanged
[Fact] public void Escape_discards_the_input()
[Fact] public void The_selection_reaches_the_view_model_as_row_indexes()
[Fact] public void The_read_only_grid_is_unchanged()                           // IsReadOnly, OneTime bindings as before
// EditableRowsTabViewModelTests
[Fact] public void Copy_puts_the_selected_rows_with_the_header_on_the_clipboard()
[Fact] public void Paste_inserts_the_rows_as_one_undo_step_after_the_selection()
[Fact] public void A_refused_paste_changes_nothing_and_says_why()
[Fact] public void A_paste_names_missing_and_dropped_columns()
[Fact] public void An_unavailable_clipboard_is_reported_and_nothing_changes()
// TabsViewModelTests
[Fact] public void A_read_only_table_and_a_read_only_loc_tab_copy_their_selected_rows()
```

- [ ] **Step 2: RED** — `EditableGridTests`, `EditableRowsTabViewModelTests`, `TabsViewModelTests`.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN** — dieselben, dazu `TabsViewTests`, `ControlTemplatesTests`, `DocumentsControllerTests`, `AppTextsTests`; Library-Suite.
- [ ] **Step 5: Commit** — `feat: edit, copy and paste rows in the grid`

---

### Task 11: Doku

**Files:**
- Modify: `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`, `Backlog.txt`, `CHANGELOG.md`

- [ ] **Step 1: Spec.**
  - Kopf und Abschnitt 9: fünf Pläne. Plan 4 „Bearbeiten: `UndoRedo`, `Editing`, editierbares Raster, Speichern, Änderungen von außen“. Plan 5 „Dateioperationen: Papierkorb, neue Tabelle und Loc-Datei, umbenennen, löschen, ‚Ins Projekt übernehmen‘“. Abschnitt 5.9 und 5.6 nennen Plan 5, wo sie bisher Plan 4 nennen.
  - In 5.4 ein Unterabschnitt „Festlegungen aus Plan 4“, im Stil der vorhandenen:
    1. Die Zellregeln des Dialekts liegen einmal vor (`CellText` in `Tables`); `TableTsv` und das Dokument nutzen dieselben.
    2. Ein Dokument (`RowsDocument`) trägt Tabellen und Loc-Dateien; Loc hat drei feste Spalten.
    3. „Geändert“ ist an den zuletzt angewandten Schritt gebunden, nicht an einen Zähler.
    4. Jedes Schließen eines bearbeitbaren Tabs entfernt dessen Schritte aus der History, auch nach dem Speichern.
    5. Die Zwischenablage trägt Spaltennamen und Zeilen im Format der App (ohne Metazeile, Farbkanäle einzeln).
    6. Speichern schreibt nur unter `pack\`, nie durch eine Verknüpfung, und legt keine Ordner an.
    7. Änderungen von außen: Prüfung beim Aktivieren des Fensters, nach `F5` und vor dem Speichern; nie automatisches Neuladen.
    8. „Aus der Liste entfernen“ ist gesperrt, solange das Projekt ungespeicherte Änderungen hat. Bauen fragt „alle speichern“ nur für das eigene Projekt.
    9. Im bearbeitbaren Raster ist Sortieren aus.
    10. Rückgängig in einer Zelle im Editiermodus ist das der Textbox; der globale Schritt entsteht erst mit dem Bestätigen der Zelle.
  - Dazu alles, was die Umsetzung anders entscheidet als dieser Plan.
- [ ] **Step 2: Backlog.** Ergänzen unter „Aus Teil 2 zurückgestellt“: Sortieren und Filtern im bearbeitbaren Raster; Suchen und Ersetzen in einer Tabelle; Zellbereich statt ganzer Zeilen kopieren und einfügen; Verlauf der Schritte anzeigen (Beschreibungen sind heute Englisch und unsichtbar); „Neu laden“ für Nur-Lese-Tabs von Projektdateien; Dokument auf dem Pool-Thread bauen bei sehr großen Tabellen. Streichen, was dieser Plan erledigt (z. B. „Projekt-Tabs ohne Markierung bei Änderung von außen“).
- [ ] **Step 3: Changelog,** Abschnitt „Unreleased / Neu“, aus Nutzersicht: Tabellen und Loc-Dateien eines Projekts im Raster bearbeiten, mit Prüfung der Eingabe; Zeilen einfügen, löschen, duplizieren, kopieren, einfügen; Rückgängig und Wiederholen über alle Tabs; Speichern und Alle speichern; Rückfrage bei ungespeicherten Änderungen; Hinweis, wenn eine Datei außerhalb geändert wurde.
- [ ] **Step 4: Commit** — `docs: record plan 4 in the spec, backlog and changelog`

---

## Manuelle Abnahme durch den Nutzer

Vorbereitung: App starten, Spiel wird gefunden. Ein importiertes Testprojekt mit mindestens einer Tabelle und einer Loc-Datei (z. B. aus der Abnahme von Plan 3).

1. Projekt aufklappen, eine `.tsv`-Tabelle öffnen: Raster wie bisher, Schlüsselspalten mit fettem Kopf.
2. Doppelklick in eine Zahlenzelle, Wert ändern, `Enter`: Wert steht, Tab-Kopf zeigt die Geändert-Markierung.
3. In eine Zahlenzelle einen Buchstaben tippen, `Enter`: Zelle bleibt im Editiermodus, Meldung nennt den erwarteten Typ. `Esc`: alter Wert.
4. Wahrheitswert per Kästchen umschalten: geändert.
5. `Strg+Z`: letzter Wert zurück. `Strg+Y`: wieder da. Menü „Bearbeiten“ zeigt beide Einträge.
6. Zweite Tabelle öffnen, dort etwas ändern, zum ersten Tab wechseln, `Strg+Z`: Die App wechselt zum zweiten Tab und nimmt dort die Änderung zurück.
7. Zeile wählen, `Strg+D`: Kopie darunter. `Entf`: Zeile weg. `Einfg`: leere Zeile mit Standardwerten. Jeweils ein `Strg+Z` nimmt genau einen Schritt zurück.
8. Mehrere Zeilen wählen, `Strg+C`, in Excel oder LibreOffice einfügen: Spaltennamen und Zeilen kommen an. Dort einen Wert ändern, Zeilen samt Kopf kopieren, in der App `Strg+V`: Zeilen erscheinen, ein `Strg+Z` nimmt alle zurück.
9. In Excel einen Zahlenwert durch Text ersetzen, kopieren, in der App einfügen: abgelehnt, Meldung nennt Zeile und Spalte, nichts geändert.
10. Zeilen aus einer Vanilla-Tabelle (`db.pack`) kopieren und in die gleiche Tabelle des Projekts einfügen: klappt.
11. Umschalter „Text“: zeigt den geänderten Stand. Zurück auf „Raster“.
12. `Strg+S`: Markierung weg. Datei im Editor öffnen: geänderter Wert steht drin. `Strg+Z` geht weiterhin, danach ist der Tab wieder geändert.
13. Zwei Tabs ändern, Menü Projekt, „Alle speichern“: beide Markierungen weg.
14. Tab ändern, Tab schließen: Rückfrage Speichern, Verwerfen, Abbrechen. Alle drei einmal ausprobieren.
15. Tab ändern, App schließen: eine Rückfrage. Abbrechen: App bleibt offen.
16. Tab ändern, „Bauen“: Rückfrage „alle speichern“. Nein: kein Bau. Ja: gespeichert und gebaut.
17. Tab ändern, Rechtsklick auf das Projekt: „Aus der Liste entfernen“ ist deaktiviert und nennt ungespeicherte Änderungen.
18. Tabelle offen und unverändert, Datei im Editor ändern und speichern, zur App wechseln: Tab zeigt „veraltet“ und „Neu laden“. Klick: neuer Inhalt.
19. Tab ändern, dieselbe Datei im Editor ändern und speichern, in der App `Strg+S`: Rückfrage Überschreiben, Neu laden, Abbrechen. Alle drei einmal ausprobieren.
20. Tabelle in Excel öffnen (Datei gesperrt), in der App ändern und `Strg+S`: Meldung nennt die gesperrte Datei, Tab bleibt geändert. Excel schließen, `Strg+S`: klappt.
21. Loc-Datei öffnen, Text ändern, speichern, „Bauen und installieren“, Spiel starten: geänderter Text erscheint. Dasselbe mit einem Tabellenwert.
22. Große Tabelle (z. B. importiertes Mod mit vielen Zeilen): Zelle ändern, Zeile einfügen, 1.000 Zeilen einfügen fühlen sich sofort an.
23. App schließen und starten: Projekt-Tabs kommen wieder und sind bearbeitbar.
24. Sprache umschalten: Menü „Bearbeiten“, Zeilenbefehle, Rückfragen und Meldungen wechseln. Dunkles Theme: Rückfrage-Fenster und Raster im Editiermodus sind lesbar.
