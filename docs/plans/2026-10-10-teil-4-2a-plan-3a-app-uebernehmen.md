# Teil 4.2a Plan 3a: App Editor-Durchstich und Übernehmen (AvalonEdit roh, Übernehmen, Rückfragen, Undo, Raster-Kopplung) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Der Objekt-Tab zeigt den Blocktext (`ObjectText.RenderBlock`) in einem AvalonEdit-Editor, der Nutzer ändert ihn durch Tippen, „Übernehmen“ (Knopf, Strg+Enter) schreibt die Differenz über `Editing` ins aktive Projekt: Parse und Differenz, Rückfragen (Zieldatei, Dateikopie) als modale Dialoge, Sperren mit Grund, Ergebniszeile, ein Undo-Schritt in der globalen History, Neuaufbau des Tabs, Raster-Tabs gekoppelt. Keine Färbung, keine Anhängsel, kein Falten, keine Fehlerunterstreichung (Plan 3b), kein „Kopieren als“ (Plan 4).

**Architecture:** Neu in der App: `ObjectApplyController` (UI-frei, Composition Root erzeugt ihn nach dem `ObjectsController`), der das Übernehmen in zwei Runner-Läufen fährt (Lauf A: Parse, Differenz, Snapshot, Plan ohne Antworten; Dialoge auf dem UI-Thread; Lauf B: Schreiben), den `FileContentsStep` in die History einträgt, den Tab neu baut, die Guards für Schließen und App-Schließen stellt und Undo eines Übernehmens auf den Tab zurückführt. `ObjectTabViewModel` bekommt Text, Baseline, `HasUnappliedText` (als `IsModified`), `ApplyCommand` mit Sperrgrund, Ergebniszeile und asynchrone Navigation mit Rückfrage. `ObjectEditorView` (AvalonEdit, Code-behind nur Verdrahtung) ersetzt `ObjectBlockView`; `ObjectBlockView` und `ObjectLines` werden entfernt. `IObjectDialogs` (WPF: `ObjectDialogs` über `ChoiceWindow`, Tests: `FakeObjectDialogs`). `DocumentsController` behandelt `FileContentsStep` beim Undo wie `FileStep` (Raster-Sperre, Außen-Änderung, Explorer). In `Objects` eine kleine Ergänzung: `TableStore.HasPackEntry` für die Kopie-Erkennung.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2. Neues NuGet-Paket `AvalonEdit` 6.3.1.120 (MIT; Nutzer 2026-10-10; Spike Annahme 6 bestanden), Version nur in `Directory.Packages.props`, Referenz nur in der App, Notiz in `THIRD-PARTY-NOTICES.md`.

**Spec:** `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`: 4 (Schreibgrenze), 5.6 (Schreiben und Undo, Festlegungen aus Plan 2), 5.7 (Editor: nur Nur-Lesen-Kopfzeile, Tastatur, „ungeübernommen“), 5.9 (App), 7 (Fehlerfälle), 8 (Tests App), 10 (Annahmen 1, 5, 6 und das Spike-Ergebnis zu 6), 11. Vorgänger: `docs/plans/2026-10-10-teil-4-2a-plan-2-editing.md` (Interfaces von `Editing`), `docs/plans/2026-10-09-teil-4-1-plan-2-app.md` (Konventionen der Objekt-Ansicht). Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-4-2a-app-apply` (von `master`, Stand `b8dbe52` oder neuer). Baseline: voller Lauf vom 2026-10-10 grün (App 952, Editing 113, Objects 262 plus 11 Echtdaten übersprungen), Build ohne Warnung.

**Testregel** (mit dem Nutzer vereinbart): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei (`dotnet test tests/<Projekt>.Tests`). Einzelne Testklassen der App sind erlaubt (`dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~<Klasse>"`), immer nur ein Lauf zur Zeit. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Einziges neues NuGet-Paket: `AvalonEdit` 6.3.1.120 (Task 1). `TreatWarningsAsErrors`, Nullable und `ImplicitUsings` sind an.
- Neue Projektreferenz der App: `TotalWarModTool.Editing` hat sie schon (Raster); `Editing` referenziert nie die App. `Objects` ändert sich nur in Task 2 (`HasPackEntry`). `Projects`, `UndoRedo`, `Tables`, `Schema`, `Storage`, `Editing` bleiben unverändert.
- Fachlogik in UI-freien Klassen (`ObjectApplyController`, `ObjectTabViewModel`), die View ist Hülle: Code-behind von `ObjectEditorView` nur Verdrahtung (Text in beide Richtungen, Nur-Lesen-Segment, Tastenbindung). Keine statischen Singletons; alles entsteht in `App.xaml.cs`.
- Libraries loggen nicht. Der `ObjectApplyController` loggt über `IAppLog`: jedes Übernehmen mit Objekt, Dateien und Zeilenzahlen, jeden abgelehnten Schritt mit Grund; keine Werte, keine Zelltexte.
- Alle UI-Texte über `AppTexts` (`T`/`F`), jeder neue Key in `locales/en.json` und `locales/de.json`; der Locale-Test (`AppTextsTests.Locale_files_are_complete`) bleibt grün. Keine `AppTexts`-Methode, deren String-Parameter als Key dient. Enum-zu-Text über eine Property pro Wert (`DiagnosticKind`, `WriteErrorKind`, `WriteFailureKind`, `SnapshotProblemKind` ist in `WriteError.Detail` enthalten und braucht keine eigenen Texte).
- Spielbegriffe (Tabellen, Schlüssel, Dateinamen, Pack-Namen) sind Daten und werden mit `DisplayText.Safe` oder `MessageText.EscapeControlCharacters` maskiert, wo sie in Meldungen, Tooltips oder die Ergebniszeile kommen.
- Farben nur über Theme-Tokens und `DynamicResource`; dieser Plan braucht keine neuen Tokens (der Editor nutzt `Theme.PanelBackground`, `Theme.Text`, `Theme.Selection`; `ObjectTrailing` und `ObjectError` kommen mit Plan 3b).
- Schreibgrenze wie Spec 4: geschrieben wird nur über `ProjectWriter.Apply` und den `FileContentsStep` (unter `pack\` des aktiven Projekts, Papierkorb unter `AppPaths.TrashDirectory`). Die App selbst ruft nie `File.Write*`.
- Jede vom Nutzer gestartete Operation läuft über `IOperationRunner`: Lauf A „Änderungen werden geprüft“ (Parse, Differenz, Snapshot, Plan), Lauf B „Änderungen werden übernommen“ (Schreiben), danach der Neuaufbau wie bisher. Die Dialoge laufen dazwischen auf dem UI-Thread. Abbrechen in Lauf B wirkt nur vor der ersten Datei (Spec 5.6).
- Der Editor hat keinen eigenen Undo-Stapel (`UndoStack.SizeLimit = 0`); Strg+Z und Strg+Y gehen an die globale History (Spec 5.7). Strg+Enter übernimmt.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in `TempDir` (über `TestGame`); Projektdateien über `ObjectsTestSchema.WriteProjectTable` oder `TableTsv.Write`. Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`. View-Tests laufen über `Sta.Run` und laden `Themes/Controls.xaml` wie `ObjectBlockViewTests`; View-Model- und Controller-Tests laufen ohne STA mit `FakeOperationRunner` (synchron) und `FakeObjectDialogs`.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Weicht die Umsetzung von der Spec ab, wird die Spec im selben Commit angepasst: Unterabschnitt `#### Festlegungen aus Plan 3a` unter 5.9 (Task 3 legt ihn an). Die Tests in diesem Plan legen Namen und Erwartungen fest; wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Widerspricht eine Erwartung einer Regel des Plans oder der Spec, wird das gemeldet, nicht still entschieden.
- Changelog „Unreleased“, Abschnitt „Neu“: ein Eintrag für das Bearbeiten im Block (Task 9), aus Nutzersicht; Plan 3b ergänzt Färbung und Falten im selben Eintrag.

## Review Focus

1. **Text, der nach dem Übernehmen nicht zum Neuaufbau passt** (Nutzer tippt weiter, während Lauf B läuft; Neuaufbau liefert einen anderen Text als der Editor zeigt). Erwartung: Eingaben sind während der Läufe gesperrt (Runner), nach dem Neuaufbau ersetzt der neue Baseline-Text den Editor-Text, `HasUnappliedText` ist false; ein Übernehmen, dessen Tab inzwischen ein anderes Objekt zeigt, wird verworfen wie ein Aufbau. → Task 3 (`A_new_result_replaces_the_text_and_clears_the_unapplied_mark`), Task 5 (`An_apply_whose_tab_moved_on_meanwhile_writes_nothing`).
2. **Abbrechen an jeder Stelle der Kette** (Fehlerdialog, Zieldatei-Frage, Kopie-Frage, Raster-Sperre, Abbruch in Lauf B). Erwartung: nichts geschrieben, Text bleibt ungeübernommen, kein Undo-Schritt, Ergebniszeile leer, Log nennt den Grund. → Task 5 (`Cancelling_the_target_question_writes_nothing`, `Cancelling_the_copy_question_writes_nothing`, `An_unsaved_grid_tab_of_a_target_file_blocks_the_apply`, `A_cancelled_write_leaves_the_text_unapplied`).
3. **Undo eines Übernehmens bei geschlossenem oder gewandertem Tab** (Tab geschlossen; Tab zeigt inzwischen ein anderes Objekt; Datei im Raster geöffnet mit ungespeicherten Änderungen). Erwartung: der Schritt bleibt in der History und läuft; ein offener Tab mit demselben Objekt wird gewählt und neu gebaut, sonst nichts gewählt; ein Raster-Tab mit ungespeicherten Änderungen an einer Datei des Schritts sperrt den Undo mit Hinweis. → Task 6 (`Undo_of_an_apply_selects_and_rebuilds_the_tab_it_came_from`, `Undo_of_an_apply_whose_tab_is_closed_runs_and_selects_nothing`, `Undo_of_an_apply_is_blocked_by_an_unsaved_grid_tab`).
4. **Schließen mit ungeübernommenem Text** (Tab schließen, Zurück, Link, Aktualisieren, App schließen; Antworten Übernehmen, Verwerfen, Abbrechen). Erwartung: Übernehmen schreibt und fährt fort, Verwerfen setzt den Text auf die Baseline zurück und fährt fort, Abbrechen lässt alles stehen; App-Schließen fragt erst die Objekt-Tabs, dann die Raster-Tabs. → Task 4 (`Back_with_unapplied_text_asks_and_discard_resets_the_text`), Task 7 (`Closing_a_tab_with_unapplied_text_asks_first`, `Closing_the_app_asks_for_unapplied_text_before_the_documents_do`).
5. **Fehler des Schreibens nach Lauf A** (Datei zwischen Snapshot und Schreiben geändert; zweite Datei gesperrt). Erwartung: Dialog nennt Art und Datei, Text bleibt ungeübernommen, kein Schritt, bei `RollbackFailed` nennt der Dialog jede Datei mit Zustand und der Tab wird als veraltet markiert; die Fingerprints sind danach ungültig, ein erneutes Übernehmen baut neu auf (Backlog Plan 2). → Task 5 (`A_file_changed_between_snapshot_and_write_is_reported_and_nothing_is_written`, `A_rollback_failure_names_every_file_and_marks_the_tab_stale`).

---

### Task 1: AvalonEdit einbinden und die Editor-View

**Status:** erledigt (Commit 996fdc4); `ObjectEditorViewTests` + `ObjectTabViewModelTests` 32/32 grün, Build ohne Warnung.

Das Paket, die View mit dem Editor, Text in beide Richtungen, Nur-Lesen-Kopfzeile, Tastenbindungen. Noch ohne Übernehmen-Logik: die View zeigt den Text eines `ObjectTabViewModel` und schreibt Eingaben zurück. Spec 5.7 (Nur-Lesen-Segmente, Tastatur), Annahme 6 (Spike-Ergebnis).

**Files:**
- Modify: `Directory.Packages.props` (`<PackageVersion Include="AvalonEdit" Version="6.3.1.120" />`)
- Modify: `src/TotalWarModTool.App/TotalWarModTool.App.csproj` (`<PackageReference Include="AvalonEdit" />`)
- Modify: `THIRD-PARTY-NOTICES.md` (Abschnitt „AvalonEdit 6.3.1.120“, MIT, Copyright 2000-2025 AlphaSierraPapa for the SharpDevelop Team, Quelle `https://github.com/icsharpcode/AvalonEdit`, Lizenztext wie beim ZstdSharp-Abschnitt)
- Create: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml`, `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`Text`, `BaselineText`, `HasUnappliedText`, `HeadLineLength`; Lines bleiben bis Task 3)
- Test: `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`

**Interfaces:**
- Consumes: `ICSharpCode.AvalonEdit.TextEditor`, `TextSegmentReadOnlySectionProvider<TextSegment>`, `TextSegmentCollection<TextSegment>`, `TextDocument`, `ObjectText.RenderBlock(view, category).Text`, `ObjectBuildResult`.
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed class ObjectTabViewModel : TabViewModel
{
    /// <summary>The text of the editor. Set by the view on every change; set by the tab when a new result comes or the text is discarded.</summary>
    public string Text { get; set; }

    /// <summary>The block text of the last result (ObjectText.RenderBlock(...).Text); empty without a result.</summary>
    public string BaselineText { get; }

    /// <summary>The block of the last result, for the views of Plan 3b; null without a result.</summary>
    public BlockText? Block { get; }

    /// <summary>Text differs from BaselineText (ordinal). Also what IsModified reports: the header shows the mark of unsaved changes.</summary>
    public bool HasUnappliedText { get; }

    public override bool IsModified => HasUnappliedText;

    /// <summary>The length of the head line of the text (first line without its line break); 0 without a result. The view makes it read-only.</summary>
    public int HeadLineLength { get; }

    /// <summary>Sets Text back to BaselineText.</summary>
    public void DiscardText();
}

/// <summary>The AvalonEdit editor of an object tab. DataContext is the ObjectTabViewModel. Pure view wiring.</summary>
public sealed partial class ObjectEditorView : UserControl
{
    /// <summary>The editor, for tests and for Plan 3b (colorizer, element generator, folding).</summary>
    internal TextEditor Editor { get; }
}
```

Regeln (Festlegungen):

- `ApplyResult(result)`: `Block = ObjectText.RenderBlock(result.View, result.Category)`, `BaselineText = Block.Text`, `Text = BaselineText`, `HeadLineLength = Länge der ersten Zeile`. `MoveTo` (anderes Objekt): `Block = null`, `BaselineText = ""`, `Text = ""`. `HasUnappliedText` ändert sich mit `Text` und `BaselineText`; `PropertyChanged` für `HasUnappliedText` und `IsModified`, wenn sich der Wert ändert.
- Die View: `TextEditor` mit `FontFamily Consolas`, `ShowLineNumbers false`, `WordWrap false`, `HorizontalScrollBarVisibility Auto`, Hintergrund `Theme.PanelBackground`, Vordergrund `Theme.Text`, Selektion `Theme.Selection` (alle `DynamicResource`). `Document.UndoStack.SizeLimit = 0`. Die `CommandBindings` und `InputBindings` der `TextArea` für `ApplicationCommands.Undo` und `ApplicationCommands.Redo` werden entfernt, damit Strg+Z und Strg+Y zum Hauptfenster durchreichen (dort hängen `Documents.UndoCommand` und `RedoCommand`). `InputBinding` Strg+Enter auf `ApplyCommand` des Tabs (Task 3 liefert den Command; bis dahin bindet die View an `ApplyCommand` als `Binding`, das null sein darf).
- Text-Synchronisation: `DataContextChanged` und `PropertyChanged(Text)` des Tabs setzen `Editor.Text`, wenn er abweicht (ordinal), mit einem Flag gegen die Rückkopplung; `Editor.TextChanged` setzt `tab.Text = Editor.Text`, wenn das Flag nicht gesetzt ist. Nach jedem Setzen von `Editor.Text` wird das Nur-Lesen-Segment auf `[0, HeadLineLength]` gesetzt (`TextSegmentCollection` leeren, ein Segment hinzufügen; kein Segment bei `HeadLineLength 0`). Ein Nur-Lesen-Segment lehnt Einfügen innerhalb der Zeile ab; Einfügen am Zeilenende (Position `HeadLineLength`) ist erlaubt, weil der Nutzer dort eine neue Zeile beginnen darf; der Parser meldet eine veränderte Kopfzeile ohnehin als `HeadChanged`.
- `Alt+Links`/`Alt+Rechts` bleiben Bindungen des `DockPanel` im `DataTemplate` (TabsView.xaml, Task 3 ändert das Template).

- [ ] **Step 1: Failing Tests** in `ObjectTabViewModelTests.cs` (bestehende Helfer der Datei nutzen; `Result()` wie in `ObjectBlockViewTests`):

```csharp
[Fact] public void A_result_sets_the_baseline_text_and_the_text()
// ApplyResult(result): BaselineText == ObjectText.RenderBlock(result.View, result.Category).Text, Text == BaselineText, HasUnappliedText false, IsModified false, HeadLineLength == Länge der ersten Zeile, Block != null.

[Fact] public void A_changed_text_is_unapplied_and_marks_the_tab_modified()
// Text = BaselineText + "\n  x = 1": HasUnappliedText true, IsModified true, PropertyChanged für beide; Text = BaselineText: wieder false.

[Fact] public void Discard_sets_the_text_back_to_the_baseline()
// nach einer Änderung DiscardText(): Text == BaselineText, HasUnappliedText false.

[Fact] public void Moving_to_another_object_clears_the_text()
// NavigateTo(anderes Ziel, newTab false) ohne BuildHandler: Text == "", BaselineText == "", Block null, HeadLineLength 0, HasUnappliedText false.
```

und `ObjectEditorViewTests.cs` (STA, Fenster wie `ObjectBlockViewTests.Create`; `Create(tab)` liefert `(ObjectEditorView View, Window Window)`):

```csharp
[Fact] public void The_editor_shows_the_text_of_the_tab_and_follows_a_new_result() => Sta.Run(() =>
// Tab mit Result: View.Editor.Text == tab.BaselineText; tab.ApplyResult(anderes Result): Editor.Text folgt.

[Fact] public void Typing_updates_the_text_of_the_tab() => Sta.Run(() =>
// View.Editor.Document.Insert(View.Editor.Document.TextLength, "\n  x = 1"): tab.Text endet auf "  x = 1", tab.HasUnappliedText true.

[Fact] public void The_head_line_is_read_only_and_the_rest_is_not() => Sta.Run(() =>
// Editor.TextArea.ReadOnlySectionProvider.CanInsert(1) false; CanInsert(tab.HeadLineLength) true; CanInsert(TextLength) true.

[Fact] public void The_editor_has_no_undo_stack_and_no_undo_command_binding() => Sta.Run(() =>
// nach Insert: Editor.CanUndo false; Editor.TextArea.CommandBindings enthält kein Binding mit ApplicationCommands.Undo oder Redo.

[Fact] public void Setting_the_text_from_the_tab_does_not_echo_back() => Sta.Run(() =>
// Zähler auf tab.PropertyChanged(Text); tab.Text = "building a\n  b = 1" (über DiscardText nach Änderung oder direkt): Editor.Text gleich, der Zähler zählt genau eine Änderung (die eigene), keine zweite durch die View.
```

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~ObjectEditorViewTests|FullyQualifiedName~ObjectTabViewModelTests"`: Build-Fehler (Typen und Properties fehlen).

- [ ] **Step 3: Implementieren** – Paketversion und Referenz, `THIRD-PARTY-NOTICES.md`, `ObjectTabViewModel` (Felder `_text`, `_baselineText`, `_block`; `UpdateUnapplied()` nach jeder Änderung), `ObjectEditorView.xaml` (nur der `TextEditor` mit den Brushes) und `.xaml.cs` (Verdrahtung wie oben; `Editor` als `internal` Property auf das benannte Element).

- [ ] **Step 4: GREEN** – Filter wie oben; `dotnet build TotalWarModTool.slnx` ohne Warnung.

- [ ] **Step 5: Commit** – `git add Directory.Packages.props THIRD-PARTY-NOTICES.md src/TotalWarModTool.App/TotalWarModTool.App.csproj src/TotalWarModTool.App/Objects/ObjectEditorView.xaml src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`, `git commit -m "feat: show the block text of an object in an AvalonEdit editor with a read-only head line"`.

---

### Task 2: `TableStore.HasPackEntry` (Kopie-Erkennung) in `Objects`

**Status:** erledigt; `Objects.Tests` 264 grün (+2), 11 Echtdaten übersprungen, Build ohne Warnung.

Der Snapshot-Leser fragt pro Projektdatei, ob ein Vanilla- oder Mod-Pack der Ladereihenfolge einen Eintrag mit demselben Pack-Pfad hat (`PackEntryExists`, Plan 2). Der `SourceReader` hält die Pack-Indizes schon; die App soll sie nicht noch einmal lesen.

**Files:**
- Modify: `src/TotalWarModTool.Objects/SourceReader.cs` (`HasEntry`)
- Modify: `src/TotalWarModTool.Objects/TableStore.cs` (`HasPackEntry`)
- Test: `tests/TotalWarModTool.Objects.Tests/TableStoreTests.cs`

**Interfaces:**
- Consumes: `SourceReader.OpenPack` (privat, gecachter `PackIndex`), `PackIndex.Find` (ignoriert Groß- und Kleinschreibung, nimmt `/` und `\`).
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public sealed class TableStore
{
    /// <summary>True when a Vanilla or Mod source of the order holds an entry with this pack path ('/' or '\', ignoring case). Project sources are not asked. A pack that cannot be read counts as not holding it. Thread-safe like Get.</summary>
    public bool HasPackEntry(LoadOrder order, string packPath, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Failing Tests** in `TableStoreTests.cs`:

```csharp
[Fact] public void HasPackEntry_finds_an_entry_of_a_vanilla_or_mod_pack_ignoring_case_and_separators()
// Vanilla-Pack mit db/x_tables/data__, Mod-Pack mit db/x_tables/m, Projektordner mit db\x_tables\a.tsv in der Ladereihenfolge:
// HasPackEntry("db/x_tables/data__") true, (@"DB\X_TABLES\M") true, ("db/x_tables/a") false (nur Projekt), ("db/y_tables/data__") false.

[Fact] public void HasPackEntry_treats_an_unreadable_pack_as_empty()
// Quelle mit Pfad ohne Datei: false, keine Exception.
```

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.Objects.Tests --filter "FullyQualifiedName~HasPackEntry"`.

- [ ] **Step 3: Implementieren** – `SourceReader.HasEntry(TableSource source, string packPath, CancellationToken ct)`: `OpenPack`, `data?.Index.Find(packPath) is not null`. `TableStore.HasPackEntry`: über `order.Sources` mit `Kind != Project`.

- [ ] **Step 4: GREEN** – `dotnet test tests/TotalWarModTool.Objects.Tests` ganz; Build ohne Warnung.

- [ ] **Step 5: Commit** – `git add src/TotalWarModTool.Objects/SourceReader.cs src/TotalWarModTool.Objects/TableStore.cs tests/TotalWarModTool.Objects.Tests/TableStoreTests.cs`, `git commit -m "feat: ask the table store whether a pack of the load order holds an entry"`.

---

### Task 3: Tab-Zustand für das Übernehmen (`ApplyCommand`, Sperrgrund, Ergebniszeile, Texte) und die View umstellen

**Status:** erledigt; `ObjectTabViewModelTests`, `AppTextsTests`, `ObjectsControllerTests`, `TabsViewTests`, `ControlTemplatesTests`, `ObjectEditorViewTests` 109/109 grün, Build ohne Warnung. Zusatz: `ApplyToolTip` am Tab (Grund oder „Übernehmen“) für den Knopf-Tooltip.

Der Tab bekommt den Command, den Grund, warum er gesperrt ist, die Ergebniszeile und die Texte; das `DataTemplate` zeigt Knopf, Ergebniszeile und den Editor statt des `FlowDocument`. `ObjectBlockView` und `ObjectLines` gehen. Spec 5.9 (Objekt-Tab), 7.

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs`
- Delete: `src/TotalWarModTool.App/Objects/ObjectBlockView.xaml`, `ObjectBlockView.xaml.cs`, `ObjectLines.cs`; `tests/TotalWarModTool.App.Tests/ObjectBlockViewTests.cs`, `ObjectLinesTests.cs`
- Modify: `src/TotalWarModTool.App/Tabs/TabsView.xaml` (`DataTemplate` für `ObjectTabViewModel`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.cs` (`DisabledReason`-Reihenfolge wird vom Tab genutzt; `Changed` löst `RaiseCommands` der Tabs aus), `ObjectsController.Tabs.cs` (`ApplyHandler` an neue Tabs, `Changed` an offene Tabs)
- Modify: `src/TotalWarModTool.App/AppTexts.Objects.cs`, `locales/en.json`, `locales/de.json`
- Modify: `tests/TotalWarModTool.App.Tests/ObjectsControllerTests.cs`, `TabsViewTests.cs`, `ControlTemplatesTests.cs` (Stellen, die `Lines` oder `ObjectBlockView` prüfen, auf `Text` umstellen)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (`#### Festlegungen aus Plan 3a` unter 5.9 anlegen)
- Test: `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`, `tests/TotalWarModTool.App.Tests/AppTextsTests.cs` (Locale-Test bleibt grün)

**Interfaces:**
- Consumes: Task 1, `WriteSummary`, `PlannedFile` (Plan 2), `LoadOrderProblem.RootPackAdded`.
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed class ObjectTabViewModel : TabViewModel
{
    /// <summary>Writes the text into the project (ObjectApplyController through ApplyHandler). Enabled when ApplyDisabledReason is null.</summary>
    public ICommand ApplyCommand { get; }

    /// <summary>
    /// Why ApplyCommand is disabled, the first that applies: no active project (ObjectsApplyNoProject), the root pack is not in the load order
    /// (ObjectsApplyRootPackNotInOrder; the result has a RootPackAdded warning), the tab is not Ready (ObjectsApplyNotReady), the tab is stale
    /// (ObjectsApplyStale), another operation runs (ProjectBusy), the text equals the baseline (ObjectsApplyNoChanges). Null when it can run.
    /// </summary>
    public string? ApplyDisabledReason { get; }

    /// <summary>The result line of the last apply ("3 rows in 2 files changed, 1 file created: db\x_tables\flo.tsv"); null until an apply, cleared by MoveTo.</summary>
    public string? ResultLine { get; }

    /// <summary>Set by the controller: whether a project is active and usable, and whether an operation runs. Asked by ApplyDisabledReason.</summary>
    internal Func<string?>? ApplyEnvironmentReason { get; set; }

    /// <summary>Set by the controller: runs the apply. Without it the command is disabled.</summary>
    internal Func<ObjectTabViewModel, Task>? ApplyHandler { get; set; }

    /// <summary>Set by the controller: asked before Back, Forward, Refresh and a link navigation while HasUnappliedText; true lets the tab move on (the text was applied or discarded).</summary>
    internal Func<ObjectTabViewModel, Task<bool>>? LeaveHandler { get; set; }

    /// <summary>The controller reports the result of an apply: the result line is set (counts, created files, warnings) and shown until MoveTo.</summary>
    public void SetApplyResult(WriteSummary summary, IReadOnlyList<string> createdFiles, int warnings);

    /// <summary>The controller says that the apply environment changed (project, runner): ApplyDisabledReason and the command ask again.</summary>
    public void RefreshApplyState();
}

public sealed partial class AppTexts
{
    public string ObjectsApply => T("objects.apply", "Apply");
    public string ObjectsApplyGesture => T("objects.applyGesture", "Ctrl+Enter");
    public string ObjectsApplyNoProject => T("objects.apply.reason.noProject", "No active project to write into.");
    public string ObjectsApplyRootPackNotInOrder => T("objects.apply.reason.rootPack", "The pack of this object is not in the load order.");
    public string ObjectsApplyNotReady => T("objects.apply.reason.notReady", "The object is not built.");
    public string ObjectsApplyStale => T("objects.apply.reason.stale", "The object is outdated. Refresh it first.");
    public string ObjectsApplyNoChanges => T("objects.apply.reason.noChanges", "Nothing changed.");
    public string ObjectsApplyChecking => T("objects.apply.checking", "Checking the changes");
    public string ObjectsApplyWriting => T("objects.apply.writing", "Applying the changes");
    /// <param name="rows">Rows set, inserted and deleted.</param><param name="files">Files changed.</param>
    public string ObjectsApplyResultChanged(int rows, int files) => F("objects.apply.result.changed", "{0} rows in {1} files changed", rows, files);
    /// <param name="texts">Loc texts set and inserted.</param>
    public string ObjectsApplyResultTexts(int texts) => F("objects.apply.result.texts", "{0} texts", texts);
    /// <param name="files">Files created (copies included).</param><param name="names">The created files, joined with ", " (data, escaped).</param>
    public string ObjectsApplyResultCreated(int files, string names) => F("objects.apply.result.created", "{0} files created: {1}", files, names);
    public string ObjectsApplyResultWarnings(int count) => F("objects.apply.result.warnings", "{0} warnings", count);
    public string ObjectsApplyResultNothing => T("objects.apply.result.nothing", "Nothing to write: the text has no change.");
}
```

Regeln (Festlegungen):

- `ApplyDisabledReason` in dieser Reihenfolge: zuerst der Umgebungsgrund des Controllers (`ApplyEnvironmentReason?.Invoke()`: `ObjectsApplyNoProject` ohne aktives, brauchbares Projekt, sonst `ProjectBusy`, wenn der Runner läuft oder der Workspace keine Operation starten kann, sonst null), dann `ObjectsApplyRootPackNotInOrder`, wenn `Result.Order.Warnings` ein `RootPackAdded` hat, dann `ObjectsApplyNotReady` für `State != Ready` oder `Result == null`, dann `ObjectsApplyStale` für `IsStale`, dann `ObjectsApplyNoChanges`, wenn `!HasUnappliedText`. **Festlegung:** Umgebungsgründe stehen vor den Tab-Gründen; der Tooltip nennt den ersten. Ohne `ApplyHandler` ist der Command deaktiviert, der Grund bleibt der oben ermittelte.
- Der Command ruft `ApplyHandler` über `RunDetached`-Muster des Tabs: der Tab hat kein Log, deshalb `async void` im Command mit Weiterreichen an den Handler, der seine Fehler selbst loggt (wie `BuildHandler`).
- `SetApplyResult`: Zeile aus `ObjectsApplyResultChanged(RowsSet + RowsInserted + RowsDeleted, FilesChanged)`, bei `TextsSet + TextsInserted > 0` plus `", " + ObjectsApplyResultTexts(n)`, bei `FilesCreated + Copies > 0` plus `", " + ObjectsApplyResultCreated(n, Namen mit MessageText.EscapeControlCharacters, ", ")`, bei `warnings > 0` plus `", " + ObjectsApplyResultWarnings(n)`. `MoveTo` setzt `ResultLine = null`.
- Navigation mit ungeübernommenem Text: `Back`, `Forward`, `RefreshCommand`, `NavigateTo(newTab: false)` fragen zuerst `LeaveHandler` (wenn `HasUnappliedText` und Handler gesetzt); liefert er false, passiert nichts. `NavigateTo(newTab: true)` fragt nicht (der Tab bleibt). Die Commands werden dafür `async void` (wie `RelayCommand` mit async-Lambda in `MainViewModel`), Tests mit synchronem Fake warten nicht.
- `DataTemplate`: Kopfzeile wie bisher plus Knopf „Übernehmen“ (`Command ApplyCommand`, `ToolTip ApplyDisabledReason` mit `ToolTipService.ShowOnDisabled True`, Tooltip ohne Grund: `ObjectsApply`), darunter die Ergebniszeile (`TextBlock` mit `ResultLine`, Stil `GridMessageLine`, versteckt bei null), dann `local:ObjectEditorView`. `Status` und `Notice` bleiben. `Lines`, `ExpandSection`, `CollapseSection`, `ExpandedSections`, `DisplayNameOf` werden aus dem Tab entfernt (Plan 3b baut Falten und Hover auf `Block` neu). Tests, die `Lines` prüfen (`ObjectsControllerTests.Show_opens_a_tab_builds_it_and_selects_it` und weitere), prüfen stattdessen `Text` (enthält den Abschnittskopf `y` und zwei Zeilen) oder `Block.Sections`.
- `ObjectsController`: `NewTab` setzt `tab.ApplyEnvironmentReason = () => ApplyEnvironmentReason()` (`_projects?.Active is not { IsUsable: true }` → `ObjectsApplyNoProject`; `IsBusy` → `ProjectBusy`; sonst null); `Changed` (Workspace, Runner-Ende, Projektwechsel über `projects.Changed` und `OperationStateChanged`) ruft `RefreshApplyState()` jedes Objekt-Tabs. `ApplyHandler` und `LeaveHandler` setzt Task 5 über eine Property des Controllers.

- [ ] **Step 1: Failing Tests** in `ObjectTabViewModelTests.cs`:

```csharp
[Fact] public void Apply_is_disabled_without_a_handler_and_names_the_first_reason()
// Tab ohne Result: ApplyDisabledReason == Texts.ObjectsApplyNotReady; ApplyCommand.CanExecute false.
// Mit Result und Text == Baseline: ObjectsApplyNoChanges. Mit Änderung, aber ohne ApplyHandler: CanExecute false, Grund null.

[Fact] public void The_environment_reason_comes_first_then_root_pack_then_state_then_stale()
// ApplyEnvironmentReason liefert "busy": Grund "busy" trotz Änderung. Ohne Umgebungsgrund und Result mit RootPackAdded-Warnung: ObjectsApplyRootPackNotInOrder.
// Ready, MarkStale(): ObjectsApplyStale. ClearStale(), Änderung, Handler gesetzt: Grund null, CanExecute true.

[Fact] public void Apply_runs_the_handler_once_with_the_tab()
// Handler zählt Aufrufe und prüft `same tab`; ApplyCommand.Execute(null): 1 Aufruf.

[Fact] public void The_result_line_lists_rows_files_texts_created_files_and_warnings()
// SetApplyResult(new WriteSummary(1, 2, 0, 1, 0, 2, 1, 0), [@"db\x_tables\flo.tsv"], 1):
// ResultLine == "3 rows in 2 files changed, 1 texts, 1 files created: db\x_tables\flo.tsv, 1 warnings"; nach NavigateTo(anderes Ziel) null.

[Fact] public void Back_with_unapplied_text_asks_and_discard_resets_the_text()
// Verlauf [a, b] auf b, Text geändert, LeaveHandler liefert false: BackCommand.Execute → Position bleibt 1, Text bleibt geändert.
// LeaveHandler liefert true nach tab.DiscardText(): Position 0, kein ungeübernommener Text. Ohne Änderung wird der Handler nicht gefragt (Zähler 0).

[Fact] public void A_new_result_replaces_the_text_and_clears_the_unapplied_mark()
// Text geändert, ApplyResult(result): Text == neue Baseline, HasUnappliedText false, IsModified false.
```

und `AppTextsTests`: der bestehende Locale-Test deckt die neuen Keys (en und de vollständig).

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ObjectTabViewModelTests`.

- [ ] **Step 3: Implementieren** – Tab, Texte (en, de), Template, Löschen der alten View und ihrer Tests, Anpassen der Controller-Tests (`Lines` → `Text`/`Block`), `ObjectsController` (Umgebungsgrund, `RefreshApplyState` bei `Changed`).

- [ ] **Step 4: GREEN** – `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~ObjectTabViewModelTests|FullyQualifiedName~AppTextsTests|FullyQualifiedName~ObjectsControllerTests|FullyQualifiedName~TabsViewTests|FullyQualifiedName~ControlTemplatesTests"` (nacheinander, nicht parallel); Build ohne Warnung.

- [ ] **Step 5: Spec** – `#### Festlegungen aus Plan 3a` unter 5.9 anlegen: Reihenfolge der Sperrgründe (Umgebung vor Tab); Fehler des Textes werden in 3a beim Übernehmen als Dialog gezeigt, nicht als Unterstreichung (3b); Ergebniszeile-Form; `ObjectBlockView`, `ObjectLines` und die Anzeige aus 4.1 entfernt, `ObjectText.Render` bleibt in der Library bis 3b (Backlog-Eintrag „`Render` entfernen“ in Task 9); Plan 3a zeigt den Block ohne Färbung, Anhängsel und Falten.

- [ ] **Step 6: Commit** – `git add -A src/TotalWarModTool.App/Objects src/TotalWarModTool.App/Tabs/TabsView.xaml src/TotalWarModTool.App/AppTexts.Objects.cs locales/en.json locales/de.json tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs tests/TotalWarModTool.App.Tests/ObjectsControllerTests.cs tests/TotalWarModTool.App.Tests/TabsViewTests.cs tests/TotalWarModTool.App.Tests/ControlTemplatesTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` plus `git rm` der gelöschten Testdateien, `git commit -m "feat: give the object tab an apply command with its reason, a result line and the editor in place of the document view"`.

---

### Task 4: Dialoge (`IObjectDialogs`, `ObjectDialogs`, `FakeObjectDialogs`) und die Texte der Diagnosen und Fehler

**Status:** erledigt; `DiagnosisTextsTests` + `AppTextsTests` 40/40 grün (inkl. Locale-Test), Build ohne Warnung.

Spec 5.5 (Rückfrage Dateikopie), 5.4 Regel 4 (Zieldatei), 5.7 („Übernehmen, Verwerfen, Abbrechen“), 7 (Fehlerdialoge). Alle Texte, die Lauf A und B dem Nutzer zeigen.

**Files:**
- Create: `src/TotalWarModTool.App/Objects/IObjectDialogs.cs`, `src/TotalWarModTool.App/Objects/ObjectDialogs.cs`
- Create: `src/TotalWarModTool.App/AppTexts.ObjectsApply.cs`
- Modify: `locales/en.json`, `locales/de.json`
- Create: `tests/TotalWarModTool.App.Tests/FakeObjectDialogs.cs`
- Test: `tests/TotalWarModTool.App.Tests/DiagnosisTextsTests.cs` (eine Theory pro Enum), `tests/TotalWarModTool.App.Tests/AppTextsTests.cs` (Locale)

**Interfaces:**
- Consumes: `ChoiceWindow(title, message, lines, buttons, defaultIndex, cancelIndex)` mit `Result`, `CappedLines.Of`, `SaveChoice`, `TargetFileQuestion`, `FileCopyQuestion`, `BlockDiagnostic`, `WriteError`, `WriteFailure`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>Everything the apply of an object asks or tells the user. Display strings are escaped by the caller. The fake records the calls.</summary>
public interface IObjectDialogs
{
    void ShowMessage(string title, string message);

    /// <summary>"Apply, Discard, Cancel" for a tab with unapplied text that is about to move on or close. Closing the question is Cancel.</summary>
    SaveChoice AskUnapplied(string objectName);

    /// <summary>Which of the candidates (relative files, display form) takes the changes of the table (or of the loc texts). Null: cancel.</summary>
    string? AskTargetFile(string tableOrLoc, bool isLoc, IReadOnlyList<string> candidates);

    /// <summary>The copies an apply makes, one line each (file, row count, deleted keys); true for "Copy and delete", false for cancel.</summary>
    bool AskCopies(IReadOnlyList<string> lines);
}

public sealed class ObjectDialogs : IObjectDialogs   // WPF, owner window like EditDialogs

public sealed partial class AppTexts
{
    // Unapplied text
    public string ObjectsUnappliedTitle => T("objects.unapplied.title", "Unapplied changes");
    public string ObjectsUnappliedMessage(string objectName) => F("objects.unapplied.message", "The text of {0} has changes that are not applied. What should happen to them?", objectName);
    public string ObjectsUnappliedDiscard => T("objects.unapplied.discard", "Discard");
    // Target file question
    public string ObjectsTargetTitle => T("objects.target.title", "Which file?");
    public string ObjectsTargetMessage(string table) => F("objects.target.message", "The project has several files for {0}. Which one takes the changes?", table);
    public string ObjectsTargetLocMessage => T("objects.target.locMessage", "The project has several loc files. Which one takes the texts?");
    // Copy question
    public string ObjectsCopyTitle => T("objects.copy.title", "Copy the file of the game?");
    public string ObjectsCopyMessage => T("objects.copy.message", "Deleting a row of the game needs a copy of its file in the project. The copy replaces the file of the game as a whole. Other mods that replace the same file will conflict. After a game update the copy can be outdated.");
    public string ObjectsCopyLine(string file, int rows, string keys) => F("objects.copy.line", "{0} ({1} rows), without: {2}", file, rows, keys);
    public string ObjectsCopyConfirm => T("objects.copy.confirm", "Copy and delete");
    // Errors before writing
    public string ObjectsApplyErrorsTitle => T("objects.apply.errors.title", "The text cannot be applied");
    public string ObjectsApplyErrorsMessage(int count) => F("objects.apply.errors.message", "{0} errors in the text:", count);
    public string ObjectsApplyErrorLine(int line, string text) => F("objects.apply.errorLine", "Line {0}: {1}", line, text);
    public string ObjectsApplyPlanErrorsMessage(int count) => F("objects.apply.planErrors.message", "{0} problems with the project files:", count);
    public string ObjectsApplyPlanErrorLine(string file, string text) => F("objects.apply.planErrorLine", "{0}: {1}", file, text);
    public string ObjectsApplyGridUnsaved(string file) => F("objects.apply.gridUnsaved", "{0} has unsaved changes in the grid. Save them first.", file);
    public string ObjectsApplyFailedTitle => T("objects.apply.failed.title", "Applying failed");
    public string ObjectsApplyFileChanged(string file) => F("objects.apply.fileChanged", "{0} changed since the object was built. Refresh the object and apply again.", file);
    public string ObjectsApplyFileUnknown(string file, string detail) => F("objects.apply.fileUnknown", "{0} cannot be checked: {1}", file, detail);
    public string ObjectsApplyWriteFailed(string detail) => F("objects.apply.writeFailed", "A file could not be written and the written files were restored: {0}", detail);
    public string ObjectsApplyRollbackFailed(string detail) => F("objects.apply.rollbackFailed", "A file could not be written and not every written file could be restored: {0} The files and their state:", detail);
    public string ObjectsApplyFileState(string file, string state) => F("objects.apply.fileState", "{0}: {1}", file, state);
    public string ObjectsFileStateUnchanged => T("objects.fileState.unchanged", "unchanged");
    public string ObjectsFileStateWritten => T("objects.fileState.written", "written");
    public string ObjectsFileStateRolledBack => T("objects.fileState.rolledBack", "restored");
    public string ObjectsFileStateRollbackFailed => T("objects.fileState.rollbackFailed", "could not be restored");
    public string ObjectsFileStateText(WrittenFileState state) => state switch { ... };
    // One property per DiagnosticKind (18) and per WriteErrorKind (8), chosen by DiagnosticText(DiagnosticKind) and WriteErrorText(WriteErrorKind):
    public string ObjectsDiagUnknownLineForm => T("objects.diag.unknownLineForm", "This line is not a field, a row, a text or a section head.");
    public string ObjectsDiagHeadChanged => T("objects.diag.headChanged", "The head line was changed. Refresh the object.");
    public string ObjectsDiagUnknownField => T("objects.diag.unknownField", "Unknown field.");
    public string ObjectsDiagUnknownSection => T("objects.diag.unknownSection", "Unknown section.");
    public string ObjectsDiagHiddenSection => T("objects.diag.hiddenSection", "This section is hidden in this view.");
    public string ObjectsDiagFieldWithoutRow => T("objects.diag.fieldWithoutRow", "A field line without a row above it.");
    public string ObjectsDiagSentenceMismatch => T("objects.diag.sentenceMismatch", "The line does not match the sentence pattern of the section.");
    public string ObjectsDiagFieldRepeated => T("objects.diag.fieldRepeated", "The field is repeated.");
    public string ObjectsDiagControlCharacter => T("objects.diag.controlCharacter", "The value contains a tab, a line break or the escape sequence of the file format.");
    public string ObjectsDiagUnterminatedText => T("objects.diag.unterminatedText", "The text has no closing quote.");
    public string ObjectsDiagRootFieldMissing => T("objects.diag.rootFieldMissing", "A field of the object is missing; it keeps its value.");
    public string ObjectsDiagEmptyText => T("objects.diag.emptyText", "The text is empty.");
    public string ObjectsDiagKeyRepeated => T("objects.diag.keyRepeated", "The key is already used by another row.");
    public string ObjectsDiagDuplicateCountExceeded => T("objects.diag.duplicateCountExceeded", "More rows with this key than the object has.");
    public string ObjectsDiagRootKeyChanged => T("objects.diag.rootKeyChanged", "The key of the object cannot be changed here. Use Copy as.");
    public string ObjectsDiagInvalidValue => T("objects.diag.invalidValue", "The value does not fit the type of the field.");
    public string ObjectsDiagReferenceNotFound => T("objects.diag.referenceNotFound", "No row with this value in the target table.");
    public string ObjectsDiagTextWithoutOwnerKey => T("objects.diag.textWithoutOwnerKey", "The text belongs to a row without a key.");
    public string DiagnosticText(DiagnosticKind kind) => kind switch { ... };
    public string ObjectsWriteErrorSnapshotProblem => T("objects.writeError.snapshotProblem", "A project file or a pack could not be read.");
    public string ObjectsWriteErrorKeyAlreadyInFile => T("objects.writeError.keyAlreadyInFile", "The key is already in the file.");
    public string ObjectsWriteErrorRowNotInFile => T("objects.writeError.rowNotInFile", "The row was not found in the file.");
    public string ObjectsWriteErrorFieldNotInFileVersion => T("objects.writeError.fieldNotInFileVersion", "The file has an older table version without this field.");
    public string ObjectsWriteErrorInvalidValue => T("objects.writeError.invalidValue", "A value does not fit the file.");
    public string ObjectsWriteErrorColourChannelOutOfRange => T("objects.writeError.colourChannel", "A colour channel must be an integer from 0 to 255.");
    public string ObjectsWriteErrorTableNotWritable => T("objects.writeError.tableNotWritable", "The table cannot be written as text.");
    public string ObjectsWriteErrorNoAnswer => T("objects.writeError.noAnswer", "No file was chosen.");
    public string WriteErrorText(WriteErrorKind kind) => kind switch { ... };
}
```

Regeln:

- `ObjectDialogs.AskUnapplied`: `ChoiceWindow` mit Knöpfen `[ObjectsApply, ObjectsUnappliedDiscard, DialogCancel]`, Standard 0, Abbrechen 2, wie `EditDialogs.AskUnsaved`. `AskTargetFile`: Knöpfe sind die Kandidaten plus `DialogCancel` (Standard 0, Abbrechen letzter); `lines` leer; Rückgabe der gewählte Kandidat oder null. `AskCopies`: Zeilen über `CappedLines.Of(lines, ReportMoreLines)`, Knöpfe `[ObjectsCopyConfirm, DialogCancel]`, Standard **1** (Abbrechen: die Kopie hat Folgen über das Projekt hinaus, Enter darf sie nicht auslösen), Abbrechen 1.
- `FakeObjectDialogs`: `Messages`, `UnappliedAsks`, `UnappliedAnswer` (Standard `Cancel`), `TargetAsks` mit `(TableOrLoc, IsLoc, Candidates)`, `TargetAnswer` als `Func<IReadOnlyList<string>, string?>` (Standard: null), `CopyAsks`, `CopiesAnswer` (Standard false), `WhileAsking` wie `FakeEditDialogs`.
- `DiagnosisTextsTests`: je eine `Theory` über alle Werte von `DiagnosticKind`, `WriteErrorKind`, `WrittenFileState`: der Text ist nicht leer und nicht der Enum-Name (wie die bestehenden Tests der Datei für andere Enums).

- [ ] **Step 1: Failing Tests** – die drei Theories in `DiagnosisTextsTests.cs`; Locale-Test.

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~DiagnosisTextsTests|FullyQualifiedName~AppTextsTests`.

- [ ] **Step 3: Implementieren** – Interface, WPF-Klasse, Fake, Texte, en und de.

- [ ] **Step 4: GREEN** – Filter wie oben; Build ohne Warnung.

- [ ] **Step 5: Commit** – `git add src/TotalWarModTool.App/Objects/IObjectDialogs.cs src/TotalWarModTool.App/Objects/ObjectDialogs.cs src/TotalWarModTool.App/AppTexts.ObjectsApply.cs locales/en.json locales/de.json tests/TotalWarModTool.App.Tests/FakeObjectDialogs.cs tests/TotalWarModTool.App.Tests/DiagnosisTextsTests.cs`, `git commit -m "feat: add the dialogs and texts of applying an object"`.

---

### Task 5: `ObjectApplyController` – Lauf A, Dialoge, Lauf B, Schritt, Neuaufbau

Der Kern des Plans. Spec 5.4 (Rückfragen anwenden), 5.5 (Kopie-Frage), 5.6 (Schreiben, Schritt mit `Record`), 5.9 (Objekt-Tab, Rückfragen, Raster-Sperre, Logging), 7.

**Files:**
- Create: `src/TotalWarModTool.App/Objects/ObjectApplyController.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.cs` (`ObjectStores` sichtbar machen, `EnsureStores`, `CategoryOf`, `ResolveOrder`), `ObjectsController.Tabs.cs` (`ApplyHandler`, `LeaveHandler` setzen)
- Modify: `src/TotalWarModTool.App/App.xaml.cs` (Erzeugen nach dem `ObjectsController`)
- Modify: `tests/TotalWarModTool.App.Tests/TestGame.cs` (`MainSetup` bekommt optional `ObjectsController`/`ObjectApplyController`? Nein: die Apply-Tests bauen beide Controller selbst wie `ObjectsControllerTests.CreateAsync`)
- Test: `tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs`

**Interfaces:**
- Consumes: `ObjectTextParser.Parse(text, baseline, category, graph)`, `ObjectDiff.Compute(baseline, parsed, category, graph, referenceExists)`, `SnapshotRequest.For`, `ProjectSnapshotReader.Read`, `ProjectWritePlan.Compute`, `ProjectWriter.Apply`, `FileContentsStep`, `TableStore.HasPackEntry` (Task 2), `TableStore.Get(table, order, ct).FindByField(column, value)`, `ReferenceGraph.ResolveTableName`, `DocumentsController.TabsOfFile`/`FirstTabWithUnsavedWork`/`CheckExternalChangesAsync`, `ActionHistory.Record`, `IObjectDialogs` (Task 4), `ObjectTabViewModel` (Task 3), `ObjectsController.BuildAsync`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>What one build made and the apply needs: schema, graph, stores and the load order of the object.</summary>
internal sealed record ObjectStores(ResolvedSchema Schema, string InstallPath, TableStore Tables, LocStore Loc, ObjectBuilder Builder, ReferenceGraph Graph);

public sealed partial class ObjectsController
{
    /// <summary>The stores of the current schema and game folder, made when missing; null without a game or a schema.</summary>
    internal ObjectStores? EnsureStores();

    /// <summary>The category of a root table; null when none is shipped or it could not be read.</summary>
    internal CategoryDefinition? CategoryOf(string table);

    /// <summary>The load order the object of the tab is built with right now; null without a game.</summary>
    internal LoadOrder? ResolveOrder(ObjectRef current);

    /// <summary>Set by the composition root: the apply of a tab (ObjectApplyController.ApplyAsync) and the question before a tab moves on (LeaveAsync). Copied to every tab the controller makes.</summary>
    internal Func<ObjectTabViewModel, Task>? ApplyHandler { get; set; }
    internal Func<ObjectTabViewModel, Task<bool>>? LeaveHandler { get; set; }
}

/// <summary>
/// Writes the text of an object tab into the active project: parses and diffs through the runner, asks the questions of the plan, writes
/// through the runner, records the undo step, rebuilds the tab. Lives on the UI thread; created by the composition root after the
/// ObjectsController and the DocumentsController. Writes only through ProjectWriter and FileContentsStep.
/// </summary>
public sealed class ObjectApplyController
{
    public ObjectApplyController(ObjectsController objects, ProjectsController projects, DocumentsController documents, ActionHistory history,
        IOperationRunner runner, IObjectDialogs dialogs, string trashRoot, IAppLog log, AppTexts texts, TimeProvider time);

    /// <summary>Raised after an apply or the undo/redo of one changed the files of the project (the explorer lists them again).</summary>
    public event EventHandler<Guid>? FilesChanged;

    /// <summary>The apply of the tab (the ApplyCommand). Does nothing when the tab's ApplyDisabledReason is not null (logged).</summary>
    public Task ApplyAsync(ObjectTabViewModel tab);

    /// <summary>Asked before a tab with unapplied text moves on: Apply (applies; true when it worked), Discard (DiscardText, true), Cancel (false).</summary>
    public Task<bool> LeaveAsync(ObjectTabViewModel tab);

    /// <summary>The tab a FileContentsStep came from, if it is still open and shows the same object; null otherwise.</summary>
    internal ObjectTabViewModel? TabOf(FileContentsStep step);
}
```

Ablauf `ApplyAsync(tab)` (Festlegungen):

1. Guards: `tab.ApplyDisabledReason is not null` → Log Info mit Grund, return. `_objects.EnsureStores()` null → return (der Grund steht am Tab). `project = _projects.Active` (brauchbar). `started = tab.Current`, `result = tab.Result!`, `text = tab.Text`, `order = result.Order` (die Reihenfolge des Aufbaus, nicht neu aufgelöst: der Snapshot gehört zur Baseline).
2. Lauf A (`_runner.RunAsync(texts.ObjectsApplyChecking, …)`, Pool-Thread): `parsed = Parse(text, result.View, category, graph)`; `parsed.HasErrors` → Ergebnis `Errors(parsed.Diagnostics)`. `changes = Compute(result.View, parsed, category, graph, referenceExists)` mit `referenceExists = (table, column, value) => stores.Tables.Get(graph.ResolveTableName(table), order, ct).FindByField(column, value).Count > 0`; `changes.HasErrors` → `Errors(changes.Diagnostics)`. `changes.IsEmpty` → Ergebnis `Nothing`. `request = SnapshotRequest.For(changes)`; `snapshot = ProjectSnapshotReader.Read(project.Folder, schema, request, path => stores.Tables.HasPackEntry(order, path, ct), ct)`; `plan = ProjectWritePlan.Compute(changes, snapshot, schema, graph, project.DisplayName, WriteAnswers.None)`. Ergebnis `Planned(changes, snapshot, plan)`.
3. Nach Lauf A auf dem UI-Thread: `!tab.Current.Equals(started)` oder `tab.Result != result` → verwerfen (Log). Abgebrochen → return (Text bleibt). `Errors` → `dialogs.ShowMessage(ObjectsApplyErrorsTitle, ObjectsApplyErrorsMessage(n) + Zeilen ObjectsApplyErrorLine(line, DiagnosticText(kind)) für die Fehler (Warnungen nicht), höchstens 10, Rest über CappedLines)`; Log Warn mit Zahl; return. `Nothing` → `dialogs.ShowMessage(ObjectsApply, ObjectsApplyResultNothing)` (Festlegung: als Meldung, denn der Knopf war aktiv, weil der Text sich in Leerzeichen am Ende unterschied). `plan.HasErrors` → `ShowMessage(ObjectsApplyErrorsTitle, ObjectsApplyPlanErrorsMessage(n) + Zeilen ObjectsApplyPlanErrorLine(file ?? "-", WriteErrorText(kind) + " " + Detail maskiert))`; return.
4. Fragen: für jede `TargetFileQuestion` → `dialogs.AskTargetFile(table oder ObjectsTargetLocMessage-Fall, isLoc, Kandidaten maskiert)`; null → Log, return. Antworten in `WriteAnswers.TargetFiles` (Loc unter `WriteAnswers.LocKey`). `plan.CopyQuestions` nicht leer → `dialogs.AskCopies(Zeilen ObjectsCopyLine(file, rows, keys mit ", "))`; false → return. Dann `plan = Compute(changes, snapshot, …, answers)` (rein, UI-Thread). Erneut Fragen (unerwartet) oder Fehler → wie oben.
5. Raster-Sperre: für jede `plan.Files` `documents.TabsOfFile(project.Id, file)` → `documents.FirstTabWithUnsavedWork` → Tab wählen, `ShowMessage(ObjectsApplyErrorsTitle, ObjectsApplyGridUnsaved(file))`, Log, return.
6. Lauf B (`texts.ObjectsApplyWriting`): `ProjectWriter.Apply(plan, project.Id, project.Folder, "Apply: " + table + " " + key, trashRoot, time, ct)`. Abgebrochen (Status Cancelled, nichts geschrieben) → return. `Failure`: `FileChanged` → `ShowMessage(ObjectsApplyFailedTitle, ObjectsApplyFileChanged(file))`; `FileUnknown` → `ObjectsApplyFileUnknown(file, detail)`; `WriteFailed` → `ObjectsApplyWriteFailed(detail)`; `RollbackFailed` → `ObjectsApplyRollbackFailed(detail)` plus eine Zeile `ObjectsApplyFileState(file, ObjectsFileStateText(state))` pro Datei, dazu `tab.MarkStale()` (die Platte ist nicht mehr die Baseline). Log Error. return (Text bleibt ungeübernommen).
7. Erfolg: `_history.Record(step)`, `_steps[step] = (tab, started)` (für `TabOf`), Log Info („Applied <table> <key>: N rows, M texts, K files, C copies“ ohne Werte), `tab.SetApplyResult(plan.Summary, created files, changes.Diagnostics.Count(Warning))`, `FilesChanged?.Invoke(project.Id)`, `await documents.CheckExternalChangesAsync()`, dann `await _objects.BuildAsync(tab)` (der neue Aufbau setzt Baseline und Text).
8. `LeaveAsync(tab)`: `!tab.HasUnappliedText` → true. `dialogs.AskUnapplied(tab.Current.DisplayKey maskiert)`: `Save` → `await ApplyAsync(tab)`; true genau dann, wenn danach `!tab.HasUnappliedText` (Übernehmen gelungen und neu gebaut); `Discard` → `tab.DiscardText()`, true; `Cancel` → false.
9. `TabOf(step)`: die gemerkte `(tab, started)`; null, wenn der Tab nicht mehr in der Tab-Liste ist oder `tab.Current != started`.
10. Verdrahtung in `App.xaml.cs` nach `documents.Tabs.ObjectOpener = objects;`: `var objectApply = new ObjectApplyController(objects, projects, documents, history, progressRunner, new ObjectDialogs(texts, () => mainWindow), paths.TrashDirectory, log, texts, TimeProvider.System); objects.ApplyHandler = objectApply.ApplyAsync; objects.LeaveHandler = objectApply.LeaveAsync; objectApply.FilesChanged += (_, id) => projectFilesController.NotifyFilesChanged(id)` – `ProjectFilesController` bekommt dafür eine kleine öffentliche Methode `NotifyFilesChanged(Guid)`, die `FilesChanged` auslöst (der Explorer hört schon darauf). Reihenfolge: `ProjectFilesController` existiert vor dem `ObjectsController` (ja, Zeile 183).
11. `ObjectsController.NewTab` setzt `tab.ApplyHandler = tab => RunDetached(() => ApplyHandler!(tab), "Applying an object")` und `tab.LeaveHandler = LeaveHandler`, wenn die Properties gesetzt sind; Tabs, die vorher entstanden (keine), bleiben ohne.

- [ ] **Step 1: Failing Tests** `ObjectApplyControllerTests.cs` (Setup wie `ObjectsControllerTests.CreateAsync` plus Projekt mit `db\x_tables\a.tsv` `[["k2", "Project label"]]` als aktives Projekt, `FakeObjectDialogs`, `ObjectApplyController` mit Papierkorb `game.Root.File("trash")`; Helfer `Edit(tab, Func<string,string>)` setzt `tab.Text`; `RowsOf(path)` liest die Projektdatei über `TableTsv.Read`; das Objekt `x_tables:k1` aus Vanilla mit Abschnitt `y_tables` (zwei Zeilen) wie in den bestehenden Tests; die Spalte `label` der `x_tables` ist das editierbare Feld):

```csharp
[Fact] public async Task Applying_a_changed_field_writes_an_override_row_rebuilds_the_tab_and_records_one_step()
// Text: "label = First" -> "label = Changed"; ApplyCommand.Execute: Runner-Titel enthalten ObjectsApplyChecking und ObjectsApplyWriting und danach ObjectsBuilding(k1);
// a.tsv hat zwei Zeilen [k2,...],[k1,"Changed"]; history.NextUndo ist FileContentsStep mit Files [db\x_tables\a.tsv]; tab.Result.View.Root.Provenance.Source.Kind == Project;
// tab.HasUnappliedText false; tab.ResultLine beginnt mit "1 rows in 1 files changed"; Dialoge: keine Fragen, keine Meldungen; FilesChanged einmal mit project.Id.

[Fact] public async Task Applying_without_a_project_file_creates_one_named_after_the_project_and_the_result_line_names_it()
// Projekt ohne Dateien: Created db\x_tables\<name>.tsv; ResultLine enthält den Dateinamen; der Explorer-Hook (FilesChanged) feuert.

[Fact] public async Task Parse_errors_show_a_message_and_write_nothing()
// Text mit Zeile "garbage": Messages == [(ObjectsApplyErrorsTitle, enthält "Line" und DiagnosticText(UnknownLineForm))]; a.tsv unverändert; history.CanUndo false; tab.HasUnappliedText true; Runner-Titel enthalten ObjectsApplyChecking, nicht ObjectsApplyWriting.

[Fact] public async Task A_type_error_of_the_diff_shows_the_line_and_the_kind()
// y_tables-Zeile value "abc" (I32): Message enthält DiagnosticText(InvalidValue) und die Zeilennummer der Zeile.

[Fact] public async Task A_text_that_differs_only_in_trailing_whitespace_says_nothing_to_write()
// Text = Baseline + "   " am Ende einer Wertzeile: Übernehmen aktiv (Text != Baseline), Lauf A ergibt IsEmpty: Message ObjectsApplyResultNothing, nichts geschrieben, kein Lauf B.

[Fact] public async Task Several_project_files_ask_which_one_and_cancelling_the_target_question_writes_nothing()
// Projekt mit a.tsv und b.tsv (ohne k1): TargetAsks == [(x_tables, false, [a, b])]; TargetAnswer null → nichts geschrieben, kein Schritt, Text bleibt; mit TargetAnswer b.tsv: Zeile in b.tsv.

[Fact] public async Task Deleting_a_vanilla_row_asks_for_the_copy_and_cancelling_the_copy_question_writes_nothing()
// Abschnittszeile y_tables [k1, 2] aus dem Text entfernt: CopyAsks eine Frage, Zeile enthält "data__" und "2 rows"; CopiesAnswer false → nichts geschrieben; CopiesAnswer true → db\y_tables\data__.tsv existiert mit einer Zeile, Neuaufbau zeigt einen y-Abschnitt mit 1 Zeile, Undo bringt die Vanilla-Sicht (2 Zeilen) zurück.

[Fact] public async Task An_unsaved_grid_tab_of_a_target_file_blocks_the_apply()
// a.tsv im Raster geöffnet, Zelle geändert (IsModified): Übernehmen → Messages enthält ObjectsApplyGridUnsaved("db/x_tables/a.tsv" maskiert), SelectedTab ist der Raster-Tab, nichts geschrieben.

[Fact] public async Task A_file_changed_between_snapshot_and_write_is_reported_and_nothing_is_written()
// Runner.OnReport im Lauf B (oder ein Hook vor Lauf B: der Test schreibt a.tsv zwischen Lauf A und B über FakeOperationRunner-Titel-Hook) ändert a.tsv: Failure FileChanged → Message ObjectsApplyFileChanged, a.tsv hat den fremden Text, kein Schritt, HasUnappliedText true.
// Umsetzungshinweis: FakeOperationRunner bekommt dafür `Action<string>? BeforeRun` (Titel), aufgerufen vor der Arbeit; das ist eine Testhelfer-Erweiterung, kein App-Code.

[Fact] public async Task A_rollback_failure_names_every_file_and_marks_the_tab_stale()
// Plan mit zwei Dateien (a.tsv geändert, neue Datei), b.tsv der zweiten Datei mit FileShare.None gesperrt vor Lauf B (BeforeRun): Message enthält ObjectsApplyWriteFailed oder ObjectsApplyRollbackFailed; bei RollbackFailed jede Datei mit ObjectsFileStateText; tab.IsStale nur bei RollbackFailed. (Der Test erzwingt WriteFailed über die Sperre; RollbackFailed wird mit einem zweiten Hook erzwungen, der a.tsv nach dem Schreiben sperrt. Reicht ein Fall nicht deterministisch, bleibt RollbackFailed als Unit-Test des Meldungstexts: `RollbackFailedMessageListsEveryFile` über eine private statische Funktion `FailureMessage(WriteFailure)` mit `InternalsVisibleTo`.)

[Fact] public async Task A_cancelled_write_leaves_the_text_unapplied()
// Runner.CancelNext vor Lauf B (BeforeRun setzt CancelNext, wenn der Titel ObjectsApplyWriting ist): nichts geschrieben, HasUnappliedText true, kein Schritt, keine Meldung.

[Fact] public async Task An_apply_whose_tab_moved_on_meanwhile_writes_nothing()
// OnReport im Lauf A navigiert den Tab zu k2: nach dem Lauf nichts geschrieben, kein Lauf B, Log hat einen Eintrag.

[Fact] public async Task Apply_is_disabled_without_an_active_project_and_the_reason_says_so()
// Projekt entfernen (projects.Remove): tab.ApplyDisabledReason == ObjectsApplyNoProject; ApplyAsync schreibt nichts.

[Fact] public async Task Leave_asks_and_applies_discards_or_cancels()
// Text geändert; UnappliedAnswer Cancel → LeaveAsync false, Text bleibt; Discard → true, Text == Baseline; Save → true, Datei geschrieben, HasUnappliedText false.

[Fact] public async Task Warnings_of_the_diff_are_counted_in_the_result_line()
// Referenz auf unbekanntes Ziel im Text (Feld building der y-Zeile? – ein Feld mit Referenz, dessen Ziel fehlt): Übernehmen gelingt, ResultLine endet auf ObjectsApplyResultWarnings(1).
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ObjectApplyControllerTests`: Build-Fehler.

- [ ] **Step 3: Implementieren** – `ObjectStores` (Record aus `ObjectsController.Stores` herausgelöst, `Graph` dazu), die drei internen Methoden, der Controller mit privaten Records `CheckOutcome` (`Errors`, `Nothing`, `Planned`) und den Schritten `CheckAsync`, `AskAsync`, `WriteAsync`, `Report`; `FakeOperationRunner.BeforeRun`; `ProjectFilesController.NotifyFilesChanged`; Verdrahtung in `App.xaml.cs`.

- [ ] **Step 4: GREEN** – Filter, dann `FullyQualifiedName~ObjectsControllerTests` (die Tabs bekommen jetzt Handler); Build ohne Warnung.

- [ ] **Step 5: Spec** – Festlegungen: zwei Runner-Läufe mit Dialogen dazwischen; `IsEmpty` nach Lauf A ist eine Meldung; die Ladereihenfolge des Übernehmens ist die des Aufbaus; `RollbackFailed` markiert den Tab veraltet; Fehlerdialog zeigt höchstens 10 Zeilen; `referenceExists` fragt den Store im Lauf A.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Objects/ObjectApplyController.cs src/TotalWarModTool.App/Objects/ObjectsController.cs src/TotalWarModTool.App/Objects/ObjectsController.Tabs.cs src/TotalWarModTool.App/Projects/ProjectFilesController.cs src/TotalWarModTool.App/App.xaml.cs tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs tests/TotalWarModTool.App.Tests/FakeOperationRunner.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: apply the text of an object tab to the active project through the runner with questions, one undo step and a rebuild"`.

---

### Task 6: Undo eines Übernehmens (`DocumentsController` und `FileContentsStep`)

Spec 5.9 (Undo wählt den Objekt-Tab, Schließen entfernt die Schritte nicht, Raster-Tabs melden die Außen-Änderung), 7 („Undo, Datei seither geändert“).

**Files:**
- Modify: `src/TotalWarModTool.App/Editing/DocumentsController.cs` (`Step`: `FileContentsStep` wie `FileStep` planen; `FileContentsChangedException` → `FileStepFileChanged`), `DocumentsController.Files.cs` (`PlanFileStep` für beide Schrittarten; `FileStepApplied` auch für `FileContentsStep`; `CheckExternalChangesAsync` danach)
- Modify: `src/TotalWarModTool.App/Objects/ObjectApplyController.cs` (hört auf `documents.FileContentsStepApplied`, wählt den Tab und baut neu)
- Test: `tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs`, `tests/TotalWarModTool.App.Tests/DocumentsControllerTests.cs`

**Interfaces:**
- Consumes: `FileContentsStep.Files`, `FileContentsStep.ProjectId`, `FileContentsChangedException.RelativeFile`, `ActionHistory.Undo/Redo`, `ObjectApplyController.TabOf`.
- Produces:

```csharp
public sealed partial class DocumentsController
{
    /// <summary>Raised after the undo or redo of a FileContentsStep ran (not when it failed or was deferred), with the step. The apply controller selects and rebuilds the tab it came from.</summary>
    public event EventHandler<FileContentsStep>? FileContentsStepApplied;
}
```

Regeln (Festlegungen):

- `PlanFileStep` wird zu `PlanStep(IUndoableAction step, bool undo)`: für `FileStep` wie bisher; für `FileContentsStep` sind die betroffenen Dateien `step.Files` (keine Tabs werden geschlossen, kein `Reopen`): ein Raster-Tab mit ungespeicherten Änderungen an einer dieser Dateien sperrt mit `FileTabUnsaved` und wird gewählt. Nach dem gelungenen Schritt: `FileStepApplied(projectId)` (Explorer listet neu, denn Dateien kommen und gehen), `FileContentsStepApplied(step)`, dann `CheckExternalChangesAsync()` (Raster-Tabs der geänderten Dateien tragen sofort „außen geändert“ oder „gelöscht“, Spec 5.9). `SelectTabOf` wählt für `FileContentsStep` nichts; das macht der Apply-Controller über das Ereignis.
- Deferred: `FileContentsChangedException changed` → Meldung `FileStepFileChanged(Shown(changed.RelativeFile))` (statt `FileStepDeferred`); der Schritt bleibt.
- Dropped (`InvalidOperationException` aus einem `FileContentsStep`): kein `DropRelatedFileSteps` (die Dateiliste eines Übernehmens ist kein Rename-Pfad); Meldung wie bisher mit `EditUndoFailedMessage(reason)`, der Reason nennt jede Datei mit Zustand (so baut der Schritt die Meldung).
- Schließen eines Objekt-Tabs entfernt nichts aus der History (anders als Raster-Tabs); `SyncTabs` fasst nur `EditableRowsTabViewModel` an, also keine Änderung nötig; ein Test sichert das.
- `ObjectApplyController`: `FileContentsStepApplied` → `TabOf(step)` → `_tabs.SelectedTab = tab` und `BuildAsync(tab)` über den `ObjectsController` (`RunDetached`); kein Tab → nichts.

- [ ] **Step 1: Failing Tests** in `ObjectApplyControllerTests.cs`:

```csharp
[Fact] public async Task Undo_of_an_apply_selects_and_rebuilds_the_tab_it_came_from()
// nach einem Übernehmen Raster-Tab wählen, documents.UndoCommand.Execute: a.tsv hat wieder eine Zeile, SelectedTab ist der Objekt-Tab, Builds(k1) um 1 gestiegen, Root.Provenance Vanilla; Redo: Projektzeile zurück, Tab neu gebaut.

[Fact] public async Task Undo_of_an_apply_whose_tab_is_closed_runs_and_selects_nothing()
// Tab schließen (CloseWithoutAsking): history.CanUndo bleibt true; Undo: Datei zurück, kein Objekt-Tab offen, SelectedTab unverändert.

[Fact] public async Task Undo_of_an_apply_is_blocked_by_an_unsaved_grid_tab()
// a.tsv im Raster geöffnet und geändert: Undo → Messages enthält FileTabUnsaved, Datei unverändert, history.NextUndo ist noch der Schritt.

[Fact] public async Task Undo_of_an_apply_marks_the_open_grid_tab_of_the_file_as_changed_outside()
// a.tsv im Raster geöffnet (ungeändert), Undo: der Raster-Tab hat IsStaleOutside true nach dem sofortigen Check.

[Fact] public async Task Undo_of_an_apply_whose_file_changed_since_keeps_the_step_and_names_the_file()
// a.tsv nach dem Übernehmen außen überschrieben (längerer Text): Undo → Message FileStepFileChanged(a.tsv), NextUndo bleibt der Schritt.
```

und in `DocumentsControllerTests.cs`:

```csharp
[Fact] public void A_file_contents_step_is_planned_like_a_file_step_and_raises_the_applied_events()
// FileContentsStep mit einer Datei direkt in die History (Record), Datei existiert: Undo über UndoCommand → FileStepApplied(projectId) und FileContentsStepApplied(step) je einmal.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ObjectApplyControllerTests|FullyQualifiedName~DocumentsControllerTests`.

- [ ] **Step 3: Implementieren** – wie oben.

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Spec** – Festlegungen: `FileContentsStep` sperrt wie `FileStep` bei ungespeicherten Raster-Tabs; nach dem Schritt sofortiger Außen-Check; Schließen des Objekt-Tabs lässt die Schritte stehen (steht in 5.9, bestätigen).

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Editing/DocumentsController.cs src/TotalWarModTool.App/Editing/DocumentsController.Files.cs src/TotalWarModTool.App/Objects/ObjectApplyController.cs tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs tests/TotalWarModTool.App.Tests/DocumentsControllerTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: undo and redo an apply from the edit menu, select its tab and guard it like a file step"`.

---

### Task 7: Guards für Schließen (Tab, App) mit ungeübernommenem Text

Spec 5.7 (Schließen und App-Schließen fragen), 5.9 (Session speichert keinen Text).

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/ObjectApplyController.cs` (`CanClose`-Kette, `RequestAppClose`)
- Modify: `src/TotalWarModTool.App/App.xaml.cs` (`window.Closing`: erst Objekte, dann Dokumente)
- Test: `tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs`

**Interfaces:**
- Consumes: `TabsViewModel.CanClose` (Delegat, den `DocumentsController` gesetzt hat), `TabsViewModel.CloseWithoutAsking`, `DocumentsController.RequestAppClose`.
- Produces:

```csharp
public sealed class ObjectApplyController
{
    /// <summary>
    /// Like DocumentsController.RequestAppClose, for the object tabs with unapplied text: true when the window may close (no such tab, or the
    /// question was answered with apply or discard before); false when an operation runs, a question is open, or the question is started now and
    /// close is called when it ended with "go on". The composition root asks this first, then the documents.
    /// </summary>
    public bool RequestAppClose(Action close);
}
```

Regeln:

- Im Konstruktor: `var previous = tabs.CanClose; tabs.CanClose = tab => tab is ObjectTabViewModel objectTab ? CanCloseObjectTab(objectTab) : previous?.Invoke(tab) ?? true;`. `CanCloseObjectTab`: ohne ungeübernommenen Text true; sonst false und (einmal pro Tab, `_closing`-Set) `LeaveAsync(tab)` → bei true `tabs.CloseWithoutAsking(tab)`.
- `RequestAppClose(close)`: `_runner.IsRunning` → `_appCloseConfirmed = false`, false. `_appCloseConfirmed` → verbrauchen, true. Kein Objekt-Tab mit ungeübernommenem Text → true. Sonst (einmal, `_closingApp`) der Reihe nach für jeden solchen Tab `LeaveAsync`; ein false bricht ab (nichts weiter gefragt); alle true → `_appCloseConfirmed = true`, `close()`.
- `App.xaml.cs`: `window.Closing += (_, closing) => { Action post = () => window.Dispatcher.BeginInvoke(window.Close); if (!objectApply.RequestAppClose(post) || !documents.RequestAppClose(post)) closing.Cancel = true; };` Ein zweites `Closing` nach „Übernehmen“ oder „Verwerfen“ findet keinen ungeübernommenen Text mehr und fragt dann die Dokumente.

- [ ] **Step 1: Failing Tests**:

```csharp
[Fact] public async Task Closing_a_tab_with_unapplied_text_asks_first()
// tabs.Close(tab) mit UnappliedAnswer Cancel: Tab bleibt offen, UnappliedAsks 1; Discard: Tab geschlossen; Save: Datei geschrieben, Tab geschlossen.
// Ein Raster-Tab schließt weiter über den Dokumente-Guard (die Kette ruft den vorigen Delegaten): Editable modified → FakeEditDialogs.UnsavedAsks 1.

[Fact] public async Task Closing_the_app_asks_for_unapplied_text_before_the_documents_do()
// Objekt-Tab mit Text, Raster-Tab modified: RequestAppClose(close) false, UnappliedAsks 1 (Discard) → close einmal gerufen; zweiter Aufruf RequestAppClose: true (verbraucht); dann documents.RequestAppClose fragt die Raster-Tabs (FakeEditDialogs.UnsavedAsks 1).
// Mit Cancel: close nie gerufen, kein zweiter Dialog.

[Fact] public async Task Closing_the_app_while_an_operation_runs_is_refused_without_asking()
// Runner.IsRunning true: RequestAppClose false, UnappliedAsks 0.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ObjectApplyControllerTests`.

- [ ] **Step 3: Implementieren** – wie oben.

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Commit** – `git add src/TotalWarModTool.App/Objects/ObjectApplyController.cs src/TotalWarModTool.App/App.xaml.cs tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs`, `git commit -m "feat: ask before a tab or the app closes with unapplied object text"`.

---

### Task 8: Window-Verdrahtung des Editors (Strg+Enter, Undo-Tasten, Fokus) und View-Test der ganzen Tab-Vorlage

Die Tastenwege und das Zusammenspiel von Editor, Hauptfenster und Template. Spec 5.7 (Tastatur).

**Files:**
- Modify: `src/TotalWarModTool.App/Tabs/TabsView.xaml` (Strg+Enter-Bindung am `DockPanel` des Templates auf `ApplyCommand`, damit sie auch außerhalb des Editors gilt; `ObjectsApplyGesture` im Tooltip des Knopfs)
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (Fokus auf den Editor, wenn der Tab aktiv wird: `Loaded` → `Editor.Focus()`, damit Tippen sofort geht)
- Test: `tests/TotalWarModTool.App.Tests/TabsViewTests.cs` (Template baut mit einem Objekt-Tab, zeigt Knopf, Ergebniszeile und Editor), `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

- [ ] **Step 1: Failing Tests**:

```csharp
// TabsViewTests
[Fact] public void The_object_tab_template_shows_the_apply_button_the_result_line_and_the_editor() => Sta.Run(() =>
// TabsView mit einem ObjectTabViewModel (Result gesetzt, SetApplyResult gerufen): im visuellen Baum ein Button mit Content Texts.ObjectsApply, ein TextBlock mit tab.ResultLine, ein ObjectEditorView mit Editor.Text == tab.Text.

// ObjectEditorViewTests
[Fact] public void Ctrl_Enter_runs_the_apply_command_of_the_tab() => Sta.Run(() =>
// ApplyHandler zählt; Text geändert; Tastendruck Strg+Enter auf dem Editor (KeyEventArgs über InputManager oder RaiseEvent auf TextArea): 1 Aufruf.

[Fact] public void Ctrl_Z_in_the_editor_is_not_consumed_by_the_editor() => Sta.Run(() =>
// nach Insert: KeyDown Strg+Z auf der TextArea → e.Handled false (das Fenster bindet die Taste), Editor.Text unverändert.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~TabsViewTests|FullyQualifiedName~ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren** – Bindungen und Fokus.

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Commit** – `git add src/TotalWarModTool.App/Tabs/TabsView.xaml src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs tests/TotalWarModTool.App.Tests/TabsViewTests.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`, `git commit -m "feat: bind Ctrl+Enter to apply and let the undo keys of the editor reach the window"`.

---

### Task 9: Abschluss (Changelog, Backlog, manuelle Checkliste, Spec-Annahmen)

**Files:**
- Modify: `CHANGELOG.md` („Unreleased“, „Neu“)
- Modify: `Backlog.txt` (Abschnitt „Aus Teil 4.2a Plan 3a (App) zurückgestellt“)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Abschnitt 10: Annahme 1 und 5 „Abnahme Plan 3a: offen“, Zeilen für die Messwerte; 9 Pläne: Plan 3 in 3a und 3b geteilt)
- Modify: `docs/plans/2026-10-10-teil-4-2a-plan-3a-app-uebernehmen.md` (Abschnitt „Manuelle Abnahme“ unten ausfüllen, Status je Task)

- [ ] **Step 1: Changelog** – Eintrag „Neu“: „Objekt bearbeiten: Der Block eines Objekts ist ein Textfeld. Du änderst Felder, Zeilen und Spieltexte durch Tippen (die Kopfzeile ist gesperrt) und übernimmst mit „Übernehmen“ oder Strg+Enter ins aktive Projekt: geänderte Zeilen des Spiels werden als Überschreibzeilen in deine Projektdatei geschrieben, neue Zeilen angehängt, geänderte Projektzeilen ersetzt. Löschst du eine Zeile des Spiels, fragt das Tool einmal pro Datei, ob es die Datei des Spiels als Kopie ins Projekt legt (die Kopie ersetzt die Datei im Spiel). Hat dein Projekt mehrere Dateien für eine Tabelle, fragt es, welche. Ein Übernehmen ist ein Schritt für Rückgängig (Strg+Z), auch nach dem Schließen des Tabs. Die Ergebniszeile nennt Zeilen und Dateien. Ein Tab mit nicht übernommenem Text trägt die Markierung ungespeicherter Änderungen; Zurück, Vor, Aktualisieren, Schließen und das Beenden der App fragen „Übernehmen, Verwerfen, Abbrechen“. Fehler im Text (unbekanntes Feld, falscher Typ, doppelter Schlüssel) werden vor dem Schreiben gemeldet; Raster-Tabs mit ungespeicherten Änderungen an einer Zieldatei sperren das Übernehmen. Färbung, Herkunftsangaben und Falten des Blocks folgen.“ Dazu unter „Geändert“: „Der Objekt-Tab zeigt den Block vorerst ohne Farben, Herkunftsangaben, Links und Aufklappen; das kommt mit dem nächsten Schritt zurück.“
- [ ] **Step 2: Backlog** – neuer Abschnitt mit: `ObjectText.Render`, `TextLine`, `RenderOptions` in `Objects` entfernen, sobald 3b die Färbung auf `BlockText` hat; Zieldatei-Frage mit mehr als etwa 6 Kandidaten braucht eine Liste statt Knöpfen; `IsEmpty`-Meldung bei reinen Leerzeichen-Änderungen (3b könnte den Knopf schon sperren, wenn die Differenz leer ist); Text-Undo im Editor (zwei Stapel, Spec 6); Speicher der History mit den Messwerten aus der Abnahme; die Schlüsselform von `FileCopyQuestion.DeletedKeys` (Plan 2 Backlog) ist jetzt in der Kopie-Frage sichtbar.
- [ ] **Step 3: Spec** – Abschnitt 9 „Pläne“: 3 → 3a (dieser Plan) und 3b (Färbung, Anhängsel, Falten, Fehleranzeige, Links, Hover, Entprellung). Abschnitt 10: Annahme 1 „Abnahme Plan 3a mit Spielstart: offen“, Annahme 5 „Messung in der Abnahme von Plan 3a: offen“.
- [ ] **Step 4: Verifikation** – `dotnet build TotalWarModTool.slnx` ohne Warnung; `dotnet test tests/TotalWarModTool.Objects.Tests` und `tests/TotalWarModTool.Editing.Tests` grün; die Testklassen der App, die dieser Plan anfasst, einzeln grün. Die volle Suite läuft mit dem Nutzer.
- [ ] **Step 5: Commit** – `git add CHANGELOG.md Backlog.txt docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md docs/plans/2026-10-10-teil-4-2a-plan-3a-app-uebernehmen.md`, `git commit -m "docs: record plan 3a of Teil 4.2a in the changelog, the backlog and the spec"`.

---

## Manuelle Abnahme durch den Nutzer (nach dem vollen Testlauf)

Mit WH3, einem Projekt und dem Spiel installiert:

1. **Editor:** `wh_main_emp_barracks_2` als Objekt öffnen. Der Block steht als Text im Tab, Kopfzeile lässt sich nicht ändern, Tippen geht sofort (Fokus). Tab-Titel zeigt die Markierung, sobald ein Zeichen anders ist; Zurück zur Baseline nimmt sie weg. Strg+Z im Editor löst das globale Rückgängig aus (Menü „Bearbeiten“), nicht ein Text-Undo.
2. **Übernehmen einer Vanilla-Zeile:** `create_cost` ändern, Strg+Enter. Fortschrittsfenster erscheint höchstens kurz; Ergebniszeile „1 Zeilen in 0 Dateien geändert, 1 Dateien angelegt: `db\building_levels_tables\<projekt>.tsv`“ (erste Datei) oder „… 1 Dateien geändert“. Tab baut neu, Zeile trägt Herkunft Projekt (im Text ohne Farbe: Plan 3b). Explorer zeigt die neue Datei. Strg+Z: Datei weg oder Zeile weg, Tab gewählt und neu gebaut; Strg+Y bringt sie zurück.
3. **Fehler:** `create_cost = abc` → Dialog „Zeile N: Der Wert passt nicht zum Typ des Felds.“, nichts geschrieben, Text bleibt. Unbekanntes Feld, doppelter Schlüssel in einem Abschnitt: je ein Dialog.
4. **Mehrere Projektdateien:** zweite Datei `db\building_levels_tables\b.tsv` anlegen (Menü Projekt, Neue Tabelle), Übernehmen fragt „Welche Datei?“ mit zwei Knöpfen; Abbrechen schreibt nichts.
5. **Dateikopie:** eine Einheit in „Einheiten“ (`building_units_allowed`) löschen, Übernehmen: Dialog „Datei des Spiels kopieren?“ nennt `db\building_units_allowed_tables\data__.tsv`, Zeilenzahl, den Schlüssel. „Kopieren und löschen“: Kopie liegt im Projekt, Tab zeigt die Einheit nicht mehr. **Annahme 1:** Pack bauen und installieren (Strg+Umschalt+B), Spiel starten, Gebäude ansehen: die Einheit fehlt. Ergebnis in Spec Abschnitt 10 eintragen. Strg+Z: Kopie wandert in den Papierkorb, Einheit ist wieder da.
6. **Raster-Kopplung:** die Projektdatei im Raster öffnen, Zelle ändern, nicht speichern; Übernehmen im Objekt-Tab → Sperre mit Hinweis, Raster-Tab gewählt. Speichern, Übernehmen → Raster-Tab trägt „außen geändert“ sofort, ohne Fensterwechsel.
7. **Schließen:** Tab mit geändertem Text schließen → Frage; App schließen mit geändertem Text und einem geänderten Raster-Tab → erst die Objekt-Frage, dann die Dokumente-Frage.
8. **Veraltet:** Projektdatei außen ändern, Fenster wechseln und zurück: Tab „veraltet“, Übernehmen gesperrt mit Tooltip; Aktualisieren mit geändertem Text fragt.
9. **Annahme 5:** zehn Dateikopien großer Tabellen (`building_effects_junction`: Zeile löschen, Übernehmen, Strg+Z, Übernehmen, …), Task-Manager: Speicher der App vor und nach, Differenz in Spec Abschnitt 10 eintragen (Erwartung unter 200 MB).
10. **Ohne Projekt:** aktives Projekt entfernen → Knopf gesperrt, Tooltip „Kein aktives Projekt“.

## Nächste Phase

Nach Plan 3a: Review (starkes Modell), voller Testlauf und die Abnahme oben mit dem Nutzer. Dann Plan 3b (Färbung aus `BlockText`-Spans, Anhängsel als `VisualLineElementGenerator`, Falten, Fehlerunterstreichung mit entprelltem Parser, Strg+Klick auf Links, Hover-Namen, Theme-Tokens `ObjectTrailing` und `ObjectError`), sobald die Interfaces aus diesem Plan existieren. Umsetzung der Tasks nach Task-Brief: mittleres Modell reicht; Task 5 ist der größte Brief.
