# Teil 4.2a Plan 3b: Fixes aus dem Review des Merge-Stands – Task Brief

> **Für agentische Umsetzer (mittleres Modell, z. B. Sonnet):** Tasks der Reihe nach, test-first. Checkbox-Syntax (`- [ ]`) für den Stand.

**Ziel:** Drei Befunde der Stufe Important aus dem Review von `f319b20..b774f2a` beheben und eine offene Frage zum Anhängsel an der Faltzeile per Test entscheiden. Die Minor-Befunde gehen in den Backlog.

**Repo:** `C:\Users\flori\Documents\Claude Projekte\Total War Modding Tool`, Branch `master` (Stand `b774f2a`). Für die Fixes einen Branch `fix/teil-4-2a-plan-3b-review` anlegen.

**Zuerst lesen:** `CLAUDE.md`; Spec `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` Abschnitt 5.9 mit „Festlegungen aus Plan 3b“ (ab Zeile 740); Plan `docs/plans/2026-10-10-teil-4-2a-plan-3b-app-editor-darstellung.md` (Abschnitte „Global Constraints“, „Testregel“, „Manuelle Abnahme“).

## Status (Umsetzung inline, Branch `fix/teil-4-2a-plan-3b-review`)

- Task 1 erledigt ohne Fix: Alle drei Tests waren sofort grün, der Caret-Befund ist nicht reproduzierbar (AvalonEdit setzt den Cursor selbst vor das Anhängsel; Probe: `VisualLength` 18, nach `Caret.Position = (1, L+1, L+1)` steht `VisualColumn` bei L). Mit dem Nutzer entschieden: Tests als Absicherung committen (`6168777`). Mausklick und Hoch/Runter sind nicht eigens getestet.
- Task 2 erledigt (`8d9bb2f`): Schlüssel `(SourceTable, FieldName, laufende Nummer)`, `IsSectionExpanded`/`SetSectionExpanded`; `ObjectTabViewModelTests` 40/40, `ObjectEditorViewTests` 24/24 grün.
- Task 3 erledigt (`43ec96f`): beide Tests RED (Diagnostics nicht leer; Handler 2 statt 1 Aufruf), danach GREEN; `ObjectTabViewModelTests` 41, `ObjectsControllerTests` 27, `ObjectApplyControllerTests` 31 grün. Der catch-Pfad ist ungetestet, nur Review. Neuer interner Getter `ObjectsController.Stores`.
- Task 4 erledigt (`52bf7b0`): Test war RED (kein Anhängsel an der Faltzeile), `TrailingGenerator` jetzt mit `Insert(0, …)` vor der Faltung; `ObjectEditorViewTests` 25/25 grün.
- Task 5 erledigt: Backlog, Spec und Plan 3b aktualisiert. Build ohne Warnung; alle Library-Testprojekte grün; einzeln grün: ObjectEditorViewTests 25, ObjectTabViewModelTests 41, ObjectsControllerTests 27, ObjectApplyControllerTests 31, BlockFoldingTests 6, AppTextsTests 2. Volle Suite steht mit dem Nutzer aus.

## Regeln

- **Testregel:** Nie `dotnet test TotalWarModTool.slnx`, nie das ganze App-Testprojekt. App-Tests nur als einzelne Klasse: `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~<Klasse>"`. Immer nur ein Lauf zur Zeit. Vor einem Lauf prüfen, dass kein anderer `dotnet`- oder `testhost`-Prozess läuft. Library-Tests sind frei.
- Test-first je Task: failing Test, RED prüfen, implementieren, GREEN, Build ohne Warnung, Commit.
- **Weicht RED von der Erwartung ab** (der Test ist schon grün): Stopp. Nichts implementieren, dem Controller melden. Der Befund des Reviews war dann falsch.
- Kein neues NuGet-Paket. Code, Bezeichner, Kommentare und Commit-Messages Englisch; Spec, Backlog und Plan Deutsch.
- Code-behind bleibt Verdrahtung. Entscheidungen in pure oder testbare Helfer.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Weicht die Umsetzung von der Spec ab: Spec im selben Commit anpassen (Unterabschnitt „Festlegungen aus Plan 3b“).

---

### Task 1: Cursor nie hinter dem Anhängsel

**Befund:** Die Zeile hat L Zeichen Text, das `TrailingElement` liegt auf der visuellen Spalte L (`VisualLength` 1). Ende (`GetEndOfLineCaretPosition`), ein Mausklick rechts vom Text und Hoch/Runter mit gemerkter X-Position setzen den Caret auf die visuelle Spalte L+1. Der Dokument-Offset stimmt, aber der Cursor wird hinter dem Anhängsel gezeichnet. Der nächste Druck auf Links bewegt den Cursor nur optisch. Bei Umschalt+Ende ist das Anhängsel mit markiert.

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/Editor/TrailingGenerator.cs` (purer Helfer)
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (Verdrahtung)
- Modify: `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Umsetzung:**
- Helfer in `TrailingGenerator.cs`: `internal static int? ClampedCaretColumn(VisualLine line, int visualColumn)`. Er gibt die `VisualColumn` des `TrailingElement` der Zeile zurück, wenn `visualColumn` größer ist; sonst null.
- In der View `Editor.TextArea.Caret.PositionChanged` abonnieren. Visuelle Zeile über `TextView.GetVisualLine(caret.Line)` holen; ist sie null (nicht gebaut), nichts tun. Liefert der Helfer eine Spalte: `Caret.Position = new TextViewPosition(caret.Line, caret.Column, column)`. Das zweite `PositionChanged` ändert nichts mehr, deshalb keine Schleife.
- AvalonEdit setzt bei Umschalt+Ende zuerst den Caret und baut danach die Auswahl aus `Caret.Position`. Die begrenzte Spalte landet deshalb auch in der Auswahl. Das per Test prüfen, nicht annehmen.

**Tests** (Muster wie `The_caret_cannot_enter_a_trailing`, Zeile 315; Kopfzeile mit Anhängsel, L = `tab.HeadLineLength`):
```csharp
[Fact] public void End_puts_the_caret_in_front_of_the_trailing()
// Caret.Offset = 0; EditingCommands.MoveToLineEnd auf der TextArea; Pump. Caret.Offset == L und Caret.VisualColumn == L.
[Fact] public void Shift_End_does_not_select_the_trailing()
// Caret.Offset = 0; EditingCommands.SelectToLineEnd; Pump. Selection.Segments einzeln: EndVisualColumn == L (oder -1 / nicht größer als L), Selection.GetText() ohne Anhängsel.
[Fact] public void A_caret_position_behind_the_trailing_is_moved_in_front_of_it()
// Caret.Position = new TextViewPosition(1, L + 1, L + 1); Pump. Caret.VisualColumn == L, Caret.Offset == L. Steht für Mausklick und Hoch/Runter.
```
RED: Die drei Tests müssen mit Spalte L+1 scheitern. Sonst Stopp (siehe Regeln).

**Commit:** `fix: keep the caret in front of the trailing after End, a click or a vertical move`

---

### Task 2: Faltzustand an der Abschnittsidentität statt am Index

**Befund:** `ObjectTabViewModel.ExpandedSections` merkt sich Abschnittsindizes. `ObjectBuilder.cs:117` legt einen Abschnitt nur an, wenn er Zeilen hat. Löscht der Nutzer alle Zeilen eines früheren Abschnitts und übernimmt, verschieben sich alle späteren Indizes: Danach ist ein anderer Abschnitt entfaltet.

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (Zeilen 271-273 und 290-295)
- Modify: `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs` (`Expanded_sections_survive_a_new_result_and_not_a_move`, Zeile 618), `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs` (Zeilen 467, 471)
- Modify: Spec, „Festlegungen aus Plan 3b“

**Umsetzung:**
- Schlüssel eines Abschnitts: `(SourceTable, FieldName)` aus `ObjectSection` (`src/TotalWarModTool.Objects/ObjectView.cs:92`). Vorher prüfen, ob dasselbe Paar zweimal in `View.Sections` vorkommen kann (`ObjectBuilder.FindCandidates`). Wenn ja, als dritten Teil die laufende Nummer unter gleichen Paaren nehmen.
- Das ViewModel speichert Schlüssel (privates `HashSet` eines `record struct`). Die Schnittstelle zur View bleibt beim Index:
  - `public bool IsSectionExpanded(int section)`
  - `public void SetSectionExpanded(int section, bool expanded)`
  Beide übersetzen den Index über `_result.View.Sections[section]`. Ohne Ergebnis oder bei einem Index außerhalb des Bereichs: false bzw. nichts tun.
- `ExpandedSections` (`IReadOnlySet<int>`) entfällt. Die View nutzt nur noch die beiden Methoden. `PropertyChanged` für den Faltzustand bleibt, falls ein Test ihn braucht; sonst weglassen.
- Reihenfolge in `ApplyResult` beachten: `_result` wird vor `SetBlock` gesetzt (Zeile 335 vor 341). `UpdateFoldings` übersetzt den Index also schon mit dem neuen Ergebnis. Nicht umstellen.
- Das Löschen bei einem Wechsel zu einem anderen Objekt (Zeile ~497) bleibt.

**Tests:**
```csharp
// ObjectTabViewModelTests
[Fact] public void Expanded_sections_survive_a_new_result_and_not_a_move()
// umschreiben: Ergebnis mit mindestens zwei Abschnitten; Abfragen über IsSectionExpanded statt ExpandedSections.
[Fact] public void An_expanded_section_stays_expanded_when_an_earlier_section_disappears()
// Ergebnis mit den Abschnitten A, B, C; SetSectionExpanded(2, true) (C). ApplyResult mit A, C. IsSectionExpanded(1) true, IsSectionExpanded(0) false.
// ObjectEditorViewTests
// Long_sections_are_folded_…: Assert.Empty/Contains(0, tab.ExpandedSections) durch IsSectionExpanded(0) ersetzen.
```

**Commit:** `fix: remember unfolded sections by table and field instead of by index`

---

### Task 3: Prüfung beim Tippen nicht bei veraltetem Schema, keine Stores im Timer, Ausnahmen geloggt

**Befund:**
- Nach einem Schemawechsel verwirft `OnWorkspaceChanged` die Stores und markiert die Tabs als veraltet.
- Danach läuft `Check` beim Tippen weiter.
- `ObjectsController.Check` ruft `EnsureStores` auf. Das baut die Stores samt `ReferenceGraph.Build` synchron im Timer-Tick neu.
- Der Parser prüft dann die alte View gegen den neuen Graphen.
- `ObjectApplyController.cs:324` ruft `Check()` jetzt vor der Ablehnung „veraltet“.
- Weder im Tick noch im Handler gibt es ein try/catch. Eine Ausnahme aus Parse oder Diff ginge bei jeder Tipp-Pause an den globalen Handler.

**Files:**
- Modify: `src/TotalWarModTool.App/Tabs/TabViewModels.cs` (`internal bool IsSchemaStale => _schemaStale;`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`Check`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.cs` (`Check`, Zeilen 219-234)
- Modify: `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectsControllerTests.cs`

**Umsetzung:**
- `ObjectTabViewModel.Check()`: Ist `IsSchemaStale`, den Handler nicht rufen. Ein vorhandenes `_checked` auf null setzen und `RaiseCheckChanged()` aufrufen. `ApplyDisabledReason` meldet dann weiter „veraltet“.
- `ObjectsController.Check`: `_stores` direkt nutzen statt `EnsureStores()`. Ohne Stores gibt die Methode null zurück. Die Prüfung legt nie Stores an; das tut nur der Build.
- Den Rumpf in `try { … } catch (Exception ex) { _log.Error("The check of an object text failed.", ex); return null; }` fassen. Ein Log-Eintrag ist Pflicht (CLAUDE.md), den Text des Nutzers nicht loggen.
- `ObjectApplyController` bleibt unverändert: Mit dem frühen Ende in `Check` ist die Reihenfolge harmlos.

**Tests:**
```csharp
// ObjectTabViewModelTests
[Fact] public void A_schema_stale_tab_does_not_check_and_drops_its_check_result()
// ReadyTab mit zählendem Fake-CheckHandler, der einen Fehler liefert; Check() → ErrorCount 1. MarkSchemaStale(); Check() → Handler nicht erneut gerufen,
// Diagnostics leer, ApplyDisabledReason == Texts.ObjectsApplyStale (oder der Stale-Text, den der Getter liefert).
// ObjectsControllerTests
[Fact] public async Task The_check_does_not_create_stores()
// Setup wie A_tab_checks_its_text_against_the_stores; danach Schema oder Spielordner so ändern, dass OnWorkspaceChanged die Stores verwirft
// (Weg aus den bestehenden Stale-Tests übernehmen). Den Tab danach über ClearStale/neuen Build nicht auffrischen; tab.Text ändern, tab.Check():
// keine Ausnahme, Diagnostics leer; die Stores bleiben verworfen (über eine vorhandene interne Eigenschaft oder einen neuen internen Getter prüfen).
```
Der catch-Pfad lässt sich ohne Test-Haken im Parser nicht erzwingen. Keinen Haken bauen; im Status vermerken: „catch-Pfad ungetestet, nur Review“.

**Commit:** `fix: skip the typing check on a schema-stale tab and never build stores from it`

---

### Task 4: Anhängsel an der ersten Zeile einer Faltung (entscheiden per Test)

**Offene Frage:** `FoldingManager.Install` fügt seinen Generator vermutlich an Index 0 von `ElementGenerators` ein. Dann baut AvalonEdit an der Faltzeile zuerst das `FoldingElement` (DocumentLength > 0) und bricht ab, bevor `TrailingGenerator` drankommt. Die Faltzeile hätte kein Anhängsel. Die Backlog-Notiz „zusätzlicher Cursor-Halt zwischen Anhängsel und Faltungstext“ spricht dagegen.

**Files:** `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`; nur bei RED auch `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs`

**Test:**
```csharp
[Fact] public void The_last_shown_line_of_a_folding_keeps_its_trailing()
// LongResult() (siehe Long_sections_…); Zeile, an deren Ende die Faltung beginnt, muss intakt sein und Anhängsel haben (sonst Fixture anpassen,
// z. B. Row mit Provenienz). Pump; TextView.GetOrConstructVisualLine(Dokumentzeile).Elements enthält ein TrailingElement und danach ein FoldingElement.
```
- **GREEN sofort:** Kein Fix. Den Test trotzdem committen (`test: pin the trailing in front of a folding`) und die Backlog-Notiz zum Cursor-Halt stehen lassen.
- **RED:** In der View den `TrailingGenerator` nach `FoldingManager.Install` mit `ElementGenerators.Insert(0, …)` einfügen statt mit `Add`. Ein Element der Länge 0 bricht die Schleife nicht ab, danach baut die Faltung. GREEN, dann auch `Long_sections_…`, `The_caret_can_enter_and_leave_a_line_that_holds_only_a_trailing` und die Tests aus Task 1 grün. Commit `fix: show the trailing of the line a folding starts at`.

---

### Task 5: Backlog, Spec, Plan-Status, Verifikation

- [ ] **Backlog**, Abschnitt „Aus Teil 4.2a Plan 3b (Editor) zurückgestellt“, ergänzen:
  - Beim Tabwechsel baut `OnDataContextChanged` die `BlockLineMap` zweimal (einmal über `ShowTabText`, einmal direkt). `ApplyResult` meldet `Block` vor `Text` und baut so erst gegen den alten Dokumenttext. Mit ungeübernommenem Text kostet jeder Aufbau ein LCS (bis 8 MB) und einen Faltlauf.
  - Bei der empire-Fraktion (2138 Zeilen) liegt ein voller Abgleich über `MaxAlignedPairs` (4,57 Mio. > 4 Mio.). Mit Änderungen oben und unten bekommt der Mittelteil nach einem Tabwechsel keine Farben und Anhängsel.
  - Unterstreichungen in gefalteten Zeilen sind unsichtbar, die Faltung zeigt keine Markierung (z. B. die erste Stelle eines doppelten Schlüssels).
  - Nach jedem Tastendruck ist „Übernehmen“ 300 ms aktiv, auch wenn die letzte Prüfung Fehler hatte; der Tooltip flackert.
  - `FoldingElementGenerator.TextBrush` ist nicht gesetzt: Die Box „+ N weitere Zeilen“ nutzt das feste Grau von AvalonEdit statt eines Theme-Tokens.
  - Die Zuordnung der Faltungen (per `StartOffset`) und der Abgleich in `RememberFoldState` liegen im Code-behind; besser rein in `BlockFolding`.
  - Ein Abschnitt, der durch Übernehmen über 20 Zeilen wächst, ist danach gefaltet und verbirgt die gerade getippten Zeilen.
  - Bei mehrzeiliger Auswahl sind die Anhängsel voll markierter Zeilen optisch mit markiert (Kopieren liefert trotzdem nur Text).
- [ ] **Spec**, „Festlegungen aus Plan 3b“: Faltzustand nach `(SourceTable, FieldName)`; Caret wird vor das Anhängsel gesetzt; die Prüfung beim Tippen läuft nicht auf einem Tab mit veraltetem Schema und legt keine Stores an. Bei Task 4 RED: Reihenfolge der Generatoren.
- [ ] **Plan 3b, Status:** Zeile „Review des Merge-Stands: 3 Important behoben (Caret, Faltschlüssel, Prüfung bei veraltetem Schema), Task 4 Ergebnis …, Minor im Backlog“.
- [ ] **Changelog:** nur ändern, wenn ein bestehender Satz unter „Unreleased“ durch die Fixes falsch wird.
- [ ] **Verifikation:**
  - `dotnet build TotalWarModTool.slnx` ohne Warnung.
  - Library-Testprojekte grün.
  - Einzeln grün, nacheinander: `ObjectEditorViewTests`, `ObjectTabViewModelTests`, `ObjectsControllerTests`, `ObjectApplyControllerTests`, `BlockFoldingTests`, `AppTextsTests`.
  - Die volle Suite startet nur der Controller, nach dem Okay des Nutzers.
- [ ] **Commit:** `docs: record the review fixes of plan 3b in the backlog, the spec and the plan`

---

## Manuelle Nachprüfung (ergänzt die Abnahme von Plan 3b)

1. In einer Zeile mit Anhängsel Ende drücken: Der Cursor steht direkt hinter dem Text, vor dem Anhängsel. Ein Druck auf Links bewegt ihn um ein Zeichen.
2. Rechts neben das Anhängsel klicken: Der Cursor steht vor dem Anhängsel.
3. Mit Hoch/Runter aus einer langen Zeile in eine kurze Zeile mit Anhängsel wechseln: Der Cursor steht vor dem Anhängsel.
4. Umschalt+Ende: Die Markierung endet vor dem Anhängsel.
5. Einen späten Abschnitt entfalten, alle Zeilen eines früheren Abschnitts löschen, übernehmen: Derselbe späte Abschnitt ist noch entfaltet, kein anderer.
6. Bei geöffnetem Objekt-Tab mit ungeübernommenem Text das Schema wechseln, weitertippen: kein Absturz, keine Unterstreichung, der Knopf ist gesperrt mit dem Hinweis „veraltet“.
7. Bei einem langen Abschnitt hat die letzte sichtbare Zeile vor „+ N weitere Zeilen“ ihr Anhängsel.

## Nächste Phase

Nach den Fixes: kurzes Review (starkes Modell) nur der Fix-Commits, dann der volle Testlauf mit dem Nutzer und die Abnahme von Plan 3b samt den Punkten oben.
