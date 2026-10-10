# Teil 4.2a Plan 3b: Darstellung im Editor (Färbung, Anhängsel, Falten, Fehleranzeige, Links, Hover) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Der AvalonEdit-Editor des Objekt-Tabs bekommt zurück, was die Anzeige aus 4.1 hatte, und dazu die Prüfung beim Tippen: Färbung der Spans nach Theme-Tokens, Anhängsel (Herkunft, Spieltexte, Zeilenzahl, Titel, Warnungen) als nicht editierbare Elemente am Zeilenende, Falten langer Abschnitte mit `+N`, entprellter Parser mit Unterstreichung und Tooltip für Fehler und Warnungen, „Übernehmen“ gesperrt bei Fehlern und bei leerer Differenz, Strg+Klick und mittlere Maustaste auf Referenzwerten, Hover mit Spieltext des Ziels. `ObjectText.Render` und die alte Zeilenform verlassen die Library.

**Architecture:** Die Brücke zwischen Baseline und Editor ist `BlockLineMap`: pro Zeile des `BlockText` ein `TextAnchor` im `TextDocument`; eine Baseline-Zeile ist **intakt**, wenn die Dokumentzeile an ihrem Anker denselben Text hat. Intakte Zeilen bekommen die Spans der Baseline (Farben, Links, Anhängsel); geänderte Zeilen eine rein syntaktische Färbung (`LineSyntax`, pur) und keine Anhängsel. Darauf setzen vier AvalonEdit-Bausteine auf: `BlockColorizer` (`DocumentColorizingTransformer`), `TrailingGenerator` (`VisualLineElementGenerator`), `DiagnosticRenderer` (`IBackgroundRenderer`) und `BlockFolding` (über `FoldingManager`). Die Prüfung beim Tippen läuft im `ObjectTabViewModel` (`Check()`: Parse und Differenz ohne Referenzprüfung, über einen Handler des `ObjectsController`), entprellt von einem `DispatcherTimer` in der View. Code-behind bleibt Verdrahtung.

**Tech Stack:** C# 12, .NET 8, WPF, AvalonEdit 6.3.1.120 (schon referenziert), xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`: 5.2 (Anhängsel, Falten), 5.7 (Editor), 5.9 (Faltzustand bleibt, Theme-Tokens), 7 (Parse-Fehler, Typfehler, Referenzziel unbekannt), 8 (manuelle Checkliste), 10 (Annahme 2), Festlegungen aus Plan 1 (Anhängsel-Arten) und Plan 3a. Backlog „Aus Teil 4.2a Plan 3a“: `Render` entfernen, Knopf bei leerer Differenz sperren, Stelle des Fehlers im Editor. Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-4-2a-app-editor` (von `master`, Stand `51bf24f` oder neuer). Baseline vor der ersten Änderung: `dotnet build TotalWarModTool.slnx` ohne Warnung; `dotnet test tests/TotalWarModTool.Objects.Tests` grün; Zahl der App-Tests aus dem letzten vollen Lauf des Nutzers in den Status von Task 1 eintragen.

**Status (Umsetzung inline, Branch `feature/teil-4-2a-app-editor`):**

- Task 1 erledigt (`Objects`: 239 grün, 12 übersprungen; Echtdaten-Zeittest `The_empire_block_diffs_fast` wartet auf den Lauf mit `TWMT_WH3_DIR`; Zahl der App-Tests der Baseline: nicht gemessen, volle Suite nur mit dem Nutzer).
- Task 2 erledigt (SpanBrushes, LineSyntax, Tokens; 19 Tests der Filter grün).
- Task 3 erledigt (BlockLineMap, BlockColorizer; Abweichung: Anker mit `AfterInsertion`, siehe Spec 5.9 „Festlegungen aus Plan 3b“).
- Task 4 erledigt (TrailingText, TrailingGenerator; Abweichungen: `VisualLength` 1 statt 0, Generator nimmt `Func<ObjectTabViewModel?>`).
- Task 5 erledigt (Check beim Tippen, Sperrgründe „N Fehler“ und „Nichts geändert“; die drei Apply-Tests mit Dialog für Parse-, Typ- und Leerzeichenfehler sind auf die Ablehnung durch die Prüfung umgestellt).
- Task 6 erledigt (EditorHover, DiagnosticRenderer, `DisplayNameOf`; Test des Renderers prüft `Underlines()` und dass `Draw` etwas zeichnet, weil sich der Pen eines Zeichenvorgangs nicht auslesen lässt; `view.ToolTipAt(Point)` ist die testbare Hover-Schnittstelle).
- Task 7 erledigt (BlockFolding, `ExpandedSections`, Faltungen in der View; Faltungstitel „+ N weitere Zeilen“).

**Testregel** (mit dem Nutzer vereinbart): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei. Einzelne Testklassen der App über `--filter "FullyQualifiedName~<Klasse>"`, immer nur ein Lauf zur Zeit. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors`, Nullable und `ImplicitUsings` sind an.
- `Objects` ändert sich nur in Task 1 (Entfernen von `Render`, `TextLine`, `RenderOptions`, `SpanKind.Collapsed`; ein Echtdaten-Zeittest). `Editing`, `Projects`, `UndoRedo`, `Tables`, `Schema` bleiben unverändert. Keine Library referenziert die App.
- Fachlogik UI-frei und ohne AvalonEdit, wo es geht: `LineSyntax` (pur), `SpanBrushes` (pur), die Prüfung im `ObjectTabViewModel`. `BlockLineMap` nutzt `TextDocument` und `TextAnchor` (AvalonEdit-Dokumentmodell ohne UI; in Tests ohne STA erzeugbar). Die vier AvalonEdit-Bausteine liegen in `src/TotalWarModTool.App/Objects/Editor/` und halten nur Verdrahtung und Zeichnen; ihre Entscheidungen (welche Spans, welche Farbe, welcher Text) kommen aus den puren Klassen.
- Farben nur über Theme-Tokens und `DynamicResource` beziehungsweise `TryFindResource` auf dem Editor; zwei neue Tokens `ObjectTrailing` und `ObjectError` in `ThemePalette` (Task 2), nur dort stehen Farbwerte. `ThemeTests.Palette_has_20_tokens_with_valid_hex_values` wird auf 22 angepasst.
- Alle UI-Texte über `AppTexts`, jeder neue Key in `locales/en.json` und `locales/de.json`; Locale-Test bleibt grün. Spielbegriffe (Werte, Schlüssel, Tabellen) sind Daten: in Tooltips und Faltungstexten mit `MessageText.EscapeControlCharacters`.
- Die Prüfung beim Tippen (`Check`) ist pur und schnell genug für den UI-Thread (Annahme 2: Parse unter 50 ms bei 2138 Zeilen); sie prüft keine Referenzen (`referenceExists: null`), die kommen weiter im Lauf A des Übernehmens. Entprellung 300 ms (`ObjectEditorView.CheckDelay`, eine Konstante).
- Nichts in diesem Plan schreibt Dateien. Die Schreibgrenze aus Spec 4 bleibt beim `ObjectApplyController`.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. View-Tests über `Sta.Run` wie `ObjectEditorViewTests` (Fenster mit `Controls.xaml`, `Pump`). `BlockLineMap`-Tests ohne STA mit `new TextDocument(text)`. Nichts liest oder schreibt unter `%APPDATA%` oder `%LocalAppData%`.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Weicht die Umsetzung von der Spec ab, wird die Spec im selben Commit angepasst: Unterabschnitt `#### Festlegungen aus Plan 3b` unter 5.9 (Task 2 legt ihn an). Die Tests legen Namen und Erwartungen fest; wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Widerspricht eine Erwartung einer Regel des Plans oder der Spec, wird das gemeldet.
- Changelog „Unreleased“: der Eintrag „Objekt bearbeiten“ aus Plan 3a wird ergänzt, der Eintrag unter „Geändert“ („vorerst ohne Farben …“) entfernt (Task 9).

## Review Focus

1. **Zeile geändert, dann zurück auf den alten Text** (Nutzer tippt in einer Wertzeile und löscht die Eingabe wieder; Zeile gelöscht und neu eingefügt). Erwartung: die Zeile gilt wieder als intakt, Farben, Link und Anhängsel sind zurück; eine neu eingefügte Zeile mit demselben Text ohne Anker bleibt „geändert“ (nur syntaktische Färbung), was korrekt ist, weil ihre Herkunft unbekannt ist. → Task 3 (`A_line_edited_back_to_its_text_is_intact_again`, `A_deleted_and_retyped_line_is_not_intact`).
2. **Zeile eingefügt oder gelöscht vor anderen Zeilen** (eine neue Feldzeile oben, eine Abschnittszeile gelöscht). Erwartung: alle folgenden Baseline-Zeilen bleiben intakt und behalten Anhängsel und Links an der verschobenen Stelle; Faltungen folgen dem Text. → Task 3 (`Lines_after_an_insert_stay_intact`), Task 7 (`A_folding_follows_an_insert_above_it`).
3. **Fehler und leere Differenz sperren den Knopf, der Lauf A findet nichts Neues** (Typfehler, doppelter Schlüssel, Kopfzeile verändert; Text, der nur in Leerzeichen am Ende abweicht). Erwartung: Tooltip „N Fehler“ oder „Nichts geändert“, Übernehmen gesperrt; `ApplyAsync` prüft vorher noch einmal und meldet keinen Dialog für einen Zustand, den die Prüfung schon kennt. → Task 5 (`Errors_of_the_check_disable_apply_and_name_their_count`, `A_whitespace_only_change_disables_apply_as_no_change`, `Apply_checks_first_and_refuses_without_a_dialog_when_the_check_has_errors`).
4. **Prüfung, deren Ergebnis zum falschen Text gehört** (Prüfung läuft, Nutzer tippt weiter; neues Ergebnis kommt während ein Check-Ergebnis vorliegt). Erwartung: ein Check-Ergebnis gilt nur für den Text, für den es berechnet wurde; nach `ApplyResult` sind Diagnosen leer; Unterstreichungen zeigen nie Zeilen eines alten Textes. → Task 5 (`A_check_result_belongs_to_the_text_it_was_computed_for`, `A_new_result_clears_the_diagnostics`).
5. **Große Fraktion** (`wh_main_emp_empire`, 2138 Zeilen, Abschnitte mit hunderten Zeilen gefaltet). Erwartung: Tippen bleibt flüssig (Färbung pro sichtbarer Zeile, Prüfung entprellt und unter 50 ms, Anker-Abgleich linear); Falten und Entfalten ohne Neuaufbau. → Task 1 (`The_empire_block_diffs_fast`), manuelle Abnahme 1 und 6.

---

### Task 1: `Objects` aufräumen (`Render` entfernen) und die Differenz messen

**Files:**
- Modify: `src/TotalWarModTool.Objects/ObjectText.cs` (`Render`, `TextLine`, `RenderOptions`, `SpanKind.Collapsed` und die nur dort genutzten privaten Helfer entfernen; `DefaultCollapseThreshold`, `DefaultCollapsedPreview`, `TextSpan`, `SpanKind`, `ShortTableName`, `TryParsePattern`, `RuleFor` bleiben)
- Modify: `src/TotalWarModTool.Objects/BlockText.cs` (Kommentar: `RenderOptions` → `ObjectText.DefaultCollapsedPreview`)
- Delete: `tests/TotalWarModTool.Objects.Tests/ObjectTextTests.cs`
- Modify: `tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs` (die `Render`-Stelle auf `RenderBlock` umstellen; neuer Zeittest), `tests/TotalWarModTool.Objects.Tests/BlockTextTests.cs` (Zeile 229 ohne `Collapsed`)
- Modify: `Backlog.txt` (Zeile „`ObjectText.Render` … entfernen“ streichen)

**Interfaces:**
- Consumes: `ObjectTextParser.Parse`, `ObjectDiff.Compute(view, parsed, category, graph, null)`.
- Produces: keine neuen Typen. `SpanKind` ohne `Collapsed`.

- [ ] **Step 1: Failing Test** in `ObjectsRealDataTests.cs` (`RealDataFact`, übersprungen ohne `TWMT_WH3_DIR`; Fixture wie `The_empire_block_round_trips_and_parses_fast`):

```csharp
[RealDataFact] public void The_empire_block_diffs_fast()
// Fraktion wh_main_emp_empire: block = RenderBlock(view, category); parsed = Parse(block.Text, view, category, graph); warm (zweiter Lauf) misst
// Parse plus ObjectDiff.Compute(view, parsed, category, graph, null) zusammen: unter 50 ms; changes.IsEmpty, keine Fehler. Messwert als Ausgabe (Assert-Meldung) für Spec Abschnitt 10.
```

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.Objects.Tests` (mit `TWMT_WH3_DIR` nur durch den Controller): Build läuft, der neue Test ist übersprungen oder rot; die Löschung von `Render` ist RED, weil `ObjectTextTests` und die Echtdaten-Zeile noch bauen müssen.

- [ ] **Step 3: Implementieren** – Entfernen wie oben; `ObjectsRealDataTests` Zeile 88 bis 96 auf `RenderBlock` (`block.Sections.Any(s => s.Rows.Count > ObjectText.DefaultCollapseThreshold)` statt `Collapsed`).

- [ ] **Step 4: GREEN** – `dotnet test tests/TotalWarModTool.Objects.Tests`; `dotnet build TotalWarModTool.slnx` ohne Warnung (die App nutzt `Render` nicht mehr; sonst baut sie nicht und der Rest dieses Plans wartet).

- [ ] **Step 5: Commit** – `git add -A src/TotalWarModTool.Objects/ObjectText.cs src/TotalWarModTool.Objects/BlockText.cs tests/TotalWarModTool.Objects.Tests/ObjectTextTests.cs tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs tests/TotalWarModTool.Objects.Tests/BlockTextTests.cs Backlog.txt`, `git commit -m "refactor: drop the line rendering of 4.1 from the objects library and time the diff of the empire block"`.

---

### Task 2: Theme-Tokens, `SpanBrushes` und `LineSyntax`

Die puren Entscheidungen der Färbung: welcher `SpanKind` welche Farbe bekommt, und wie eine Zeile ohne Baseline (geändert) in Spans zerfällt. Spec 5.7 (Färbung wie 4.1), 5.9 (Tokens).

**Files:**
- Modify: `src/TotalWarModTool.App/Theme/ThemePalette.cs` (`ObjectTrailing`, `ObjectError`)
- Create: `src/TotalWarModTool.App/Objects/Editor/SpanBrushes.cs`, `src/TotalWarModTool.App/Objects/Editor/LineSyntax.cs`
- Modify: `tests/TotalWarModTool.App.Tests/ThemeTests.cs` (22 Tokens)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (`#### Festlegungen aus Plan 3b` unter 5.9 anlegen)
- Test: `tests/TotalWarModTool.App.Tests/SpanBrushesTests.cs`, `tests/TotalWarModTool.App.Tests/LineSyntaxTests.cs`

**Interfaces:**
- Consumes: `SpanKind`, `TextSpan`, `BlockLineKind`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>The theme token of every span kind, as the document view of 4.1 had it. Pure.</summary>
public static class SpanBrushes
{
    /// <summary>"Theme.ObjectVanilla" for ProvenanceVanilla, "Theme.ObjectMod", "Theme.ObjectProject", "Theme.ObjectLocText" for LocText, "Theme.Warning" for Warning, "Theme.TextMuted" for FieldName, Overridden, Duplicate and OlderLayout, "Theme.Accent" for Link, "Theme.Text" for the rest (Normal, TableName, Key, Value, Title).</summary>
    public static string ResourceKeyOf(SpanKind kind);

    /// <summary>Bold for TableName, Key and Title; strikethrough for Overridden.</summary>
    public static bool IsBold(SpanKind kind);
    public static bool IsStrikethrough(SpanKind kind);
}

/// <summary>Spans of one text line that has no baseline line (the user changed it), from its text alone. Pure; the colours follow SpanBrushes.</summary>
public static class LineSyntax
{
    /// <summary>
    /// The spans of the line text (without its line break), covering it completely: leading spaces as Normal; the head line (isHead) as
    /// TableName, Normal, Key; "name = value" as FieldName, Normal (" = "), then LocText when the value starts with a quote, else Value; a line
    /// without " = " at indent 0 as TableName (a section head), at indent 1 or 2 as Value (a sentence or junction row); an empty line as no spans.
    /// </summary>
    public static IReadOnlyList<TextSpan> Classify(string lineText, bool isHead);
}
```

Regeln:

- `ObjectTrailing`: hell `#6B7280`, dunkel `#8B949E`. `ObjectError`: hell `#C62828`, dunkel `#F28B82`. (Festlegung; Farbwerte nur hier.)
- `Classify` bestimmt die Einrückung über führende Leerzeichen (zwei pro Stufe, Tabs zählen als eine Stufe); `" = "` wird beim ersten Vorkommen geteilt, `name =` ohne Wert ergibt `FieldName`, `Normal`.

- [ ] **Step 1: Failing Tests**:

```csharp
// SpanBrushesTests
[Theory] [InlineData(SpanKind.ProvenanceVanilla, "Theme.ObjectVanilla")] [InlineData(SpanKind.Link, "Theme.Accent")] [InlineData(SpanKind.FieldName, "Theme.TextMuted")] [InlineData(SpanKind.Value, "Theme.Text")] [InlineData(SpanKind.LocText, "Theme.ObjectLocText")] [InlineData(SpanKind.Warning, "Theme.Warning")]
public void Every_span_kind_maps_to_a_token(SpanKind kind, string key)
[Fact] public void Every_kind_maps_to_a_known_palette_token()
// alle Enum-Werte: ResourceKeyOf ohne "Theme." ist ein Key aus ThemePalette.All.

// LineSyntaxTests
[Fact] public void A_field_line_splits_into_name_assignment_and_value()
// "  create_cost = 100": Spans Normal("  "), FieldName("create_cost"), Normal(" = "), Value("100"); zusammengesetzt wieder der Text.
[Fact] public void A_quoted_value_is_a_loc_text()
// "    name = \"Kaserne\"": letzter Span LocText.
[Fact] public void A_head_line_has_table_and_key()
// Classify("building_levels b1", isHead true): TableName, Normal(" "), Key.
[Fact] public void A_section_head_and_a_sentence_row_are_told_by_indent()
// "effects" → TableName; "  allows u1" → Normal("  "), Value("allows u1"); "" → leer; "  note = " → Normal, FieldName, Normal(" = ").
```

und `ThemeTests`: 22 Tokens, `ObjectTrailing` und `ObjectError` vorhanden.

- [ ] **Step 2: RED** – `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~SpanBrushesTests|FullyQualifiedName~LineSyntaxTests|FullyQualifiedName~ThemeTests"`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Spec** – Festlegungen 3b anlegen: Tokens und Werte; geänderte Zeilen werden syntaktisch gefärbt, ohne Links und Anhängsel; `Render` entfernt (Task 1).

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Theme/ThemePalette.cs src/TotalWarModTool.App/Objects/Editor/SpanBrushes.cs src/TotalWarModTool.App/Objects/Editor/LineSyntax.cs tests/TotalWarModTool.App.Tests/ThemeTests.cs tests/TotalWarModTool.App.Tests/SpanBrushesTests.cs tests/TotalWarModTool.App.Tests/LineSyntaxTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: add the trailing and error tokens and the pure colour and syntax rules of the block editor"`.

---

### Task 3: `BlockLineMap` und `BlockColorizer`

Die Zuordnung Dokumentzeile zu Baseline-Zeile über Anker, und die Färbung. Spec 5.7 (Färbung nach jeder Eingabe; Anhängsel nur für unveränderte Zeilen, dieselbe Zuordnung).

**Files:**
- Create: `src/TotalWarModTool.App/Objects/Editor/BlockLineMap.cs`, `src/TotalWarModTool.App/Objects/Editor/BlockColorizer.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (Map anlegen bei neuem `Block`, Colorizer registrieren)
- Test: `tests/TotalWarModTool.App.Tests/BlockLineMapTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: `BlockText.Lines` (`Spans`, `Trailing`, `Indent`, `Kind`), `TextDocument`, `TextAnchor` (`CreateAnchor`, `MovementType.BeforeInsertion`, `IsDeleted`, `Line`), `DocumentLine`, `SpanBrushes`, `LineSyntax`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>One resolved document line: the baseline line it is (intact) or null, and the spans to colour it with.</summary>
public sealed record ResolvedLine(BlockLine? Baseline, IReadOnlyList<TextSpan> Spans, bool IsIntact);

/// <summary>
/// Maps the lines of a TextDocument to the lines of the BlockText the text came from. One anchor per baseline line; a baseline line is intact
/// when the document line at its anchor has exactly the baseline text (indent included). Intact lines carry the spans and trailing of the
/// baseline; every other line gets LineSyntax spans and no trailing. Created for one (document, block) pair; the document's text must be
/// block.Text at creation. Lives on the thread of the document.
/// </summary>
public sealed class BlockLineMap
{
    public BlockLineMap(TextDocument document, BlockText block);

    /// <summary>The resolution of a 1-based document line, from the current state of the document.</summary>
    public ResolvedLine Resolve(int documentLineNumber);

    /// <summary>The baseline line that is intact at this document line, or null. For the trailing generator and the link lookup.</summary>
    public BlockLine? IntactLineAt(int documentLineNumber);

    /// <summary>The 1-based document line of a baseline line index while it is intact, or null (deleted or changed).</summary>
    public int? DocumentLineOf(int baselineIndex);
}

/// <summary>Colours every visual line from the map: resource brushes of the editor by SpanBrushes, bold and strikethrough. View wiring only.</summary>
internal sealed class BlockColorizer : DocumentColorizingTransformer
{
    public BlockColorizer(Func<BlockLineMap?> map, Func<string, Brush?> brushOf);
}
```

Regeln (Festlegungen):

- Anker: `document.CreateAnchor(lineStartOffset)` mit `MovementType.BeforeInsertion` und `SurviveDeletion = false`. Intakt: `!anchor.IsDeleted` und der Text der Dokumentzeile des Ankers (`document.GetText(line)`) ist ordinal gleich `Indent * 2 Leerzeichen + Spans.Text` (die Zeile, wie `BlockText.Text` sie schreibt; `Blank`- und `Annotation`-Zeilen sind leere Zeilen und gelten als intakt, wenn die Zeile leer ist). Zwei Anker in einer Dokumentzeile (eine Zeile gelöscht, die nächste rückt auf): intakt ist höchstens die, deren Text passt; passen beide (zwei leere Zeilen), die mit dem kleineren Index.
- `Resolve`: intakt → `Spans` aus der Baseline mit einem führenden `Normal`-Span der Einrückung (damit die Spans die Zeile lückenlos abdecken); sonst `LineSyntax.Classify(text, isHead: documentLineNumber == 1)`.
- Der Colorizer färbt pro Span über `ChangeLinePart`: `SetForegroundBrush(brushOf(SpanBrushes.ResourceKeyOf(kind)))`, bei `IsBold` `SetTypeface` mit `FontWeights.Bold`, bei `IsStrikethrough` `SetTextDecorations(TextDecorations.Strikethrough)`. `brushOf` ist `key => Editor.TryFindResource(key) as Brush`. **Festlegung:** `DynamicResource` greift in Zeichenroutinen nicht; ein Theme-Wechsel zur Laufzeit färbt den Editor beim nächsten Redraw um (Scrollen, Tippen, neuer Block). Backlog-Eintrag in Task 9 (Ereignis des `ThemeManager` abonnieren und `TextView.Redraw()` rufen).
- Die View: `_map = new BlockLineMap(Editor.Document, tab.Block)` direkt nach dem Setzen des Textes in `ShowTabText`, wenn `tab.Block` nicht null ist und `Editor.Text == tab.Block.Text`; sonst `_map = null` (geänderter Text ohne Block, alles syntaktisch). `Editor.TextArea.TextView.LineTransformers.Add(new BlockColorizer(() => _map, brushOf))` im Konstruktor.

- [ ] **Step 1: Failing Tests** `BlockLineMapTests.cs` (ohne STA; `BlockText` aus einer kleinen `ObjectView` wie `ObjectEditorViewTests.View`, oder von Hand aus `BlockLine`s gebaut; `document = new TextDocument(block.Text)`):

```csharp
[Fact] public void Every_line_is_intact_right_after_creation()
// für jede Zeile: Resolve(i).IsIntact, Baseline == block.Lines[i-1]; DocumentLineOf(i-1) == i.

[Fact] public void An_edited_line_is_not_intact_and_gets_syntax_spans()
// Text der Feldzeile 2 ändern ("effect = v" → "effect = w"): Resolve(2).IsIntact false, Baseline null, Spans == LineSyntax.Classify(...); DocumentLineOf(1) null; andere Zeilen intakt.

[Fact] public void A_line_edited_back_to_its_text_is_intact_again()
// ändern, dann zurück: intakt mit Baseline-Spans.

[Fact] public void Lines_after_an_insert_stay_intact()
// vor Zeile 2 eine neue Zeile "  x = 1" einfügen: Resolve(2) nicht intakt (neu), Resolve(3) == alte Zeile 2 intakt, DocumentLineOf(1) == 3.

[Fact] public void A_deleted_and_retyped_line_is_not_intact()
// Zeile 2 samt Umbruch löschen, denselben Text wieder einfügen: der Anker ist gelöscht, Resolve(2).IsIntact false.

[Fact] public void A_deleted_line_lets_the_next_one_keep_its_own_identity()
// Zeile 2 löschen: Resolve(2) ist die alte Zeile 3 (intakt), nicht die alte Zeile 2; DocumentLineOf(1) null, DocumentLineOf(2) == 2.

[Fact] public void The_head_line_is_classified_as_head_when_changed()
// Kopfzeile geändert: Spans Kinds [TableName, Normal, Key] aus LineSyntax.
```

und in `ObjectEditorViewTests.cs` (STA):

```csharp
[Fact] public void Intact_lines_are_coloured_from_the_baseline_and_changed_lines_from_their_syntax() => Sta.Run(() =>
// View mit Result, Pump; VisualLine der Feldzeile mit Link-Span: ein TextRun-Element hat Foreground == Brush von "Theme.Accent" (aus den Resources des Fensters); nach einer Änderung dieser Zeile hat kein Element mehr die Accent-Farbe, der Feldname hat Theme.TextMuted.
// Zugriff: view.Editor.TextArea.TextView.GetOrConstructVisualLine(document.GetLineByNumber(n)).Elements, Typ VisualLineText, TextRunProperties.ForegroundBrush.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~BlockLineMapTests|FullyQualifiedName~ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Spec** – Festlegungen: intakt heißt ankergleich und textgleich; eine neu getippte Zeile ohne Anker ist nie intakt; Theme-Wechsel färbt den Editor beim nächsten Redraw.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Objects/Editor/BlockLineMap.cs src/TotalWarModTool.App/Objects/Editor/BlockColorizer.cs src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs tests/TotalWarModTool.App.Tests/BlockLineMapTests.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: colour the block editor from the baseline spans of intact lines and the syntax of changed ones"`.

---

### Task 4: Anhängsel (`TrailingGenerator`)

Spec 5.2 (Anhängsel-Arten), 5.7 (nicht editierbar, nicht selektierbar, am Zeilenende hinter einem Abstand; ausgeblendet für geänderte Zeilen).

**Files:**
- Create: `src/TotalWarModTool.App/Objects/Editor/TrailingGenerator.cs`, `src/TotalWarModTool.App/Objects/Editor/TrailingText.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (Generator registrieren)
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`TranslateTitle` öffentlich für die View: `_translate`)
- Test: `tests/TotalWarModTool.App.Tests/TrailingTextTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: `BlockLine.Trailing` (Spans der Arten `ProvenanceVanilla/Mod/Project`, `Overridden`, `LocText`, `Title` mit Loc-Key, `Normal` für `(N)` und Abstände, `Duplicate`, `OlderLayout`, `Warning`), `AppTexts.ObjectsDuplicate`, `ObjectsOlderLayout`, `BlockLineMap.IntactLineAt`, `VisualLineElementGenerator`, `FormattedTextElement`/`VisualLineText`-Ableitung.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>One drawn piece of a trailing: the final text (translated, escaped) and its kind for the brush.</summary>
public sealed record TrailingPiece(string Text, SpanKind Kind);

/// <summary>The text pieces of a line's trailing, as the editor draws them. Pure: titles translated, Duplicate/OlderLayout in the current language, data escaped.</summary>
public static class TrailingText
{
    /// <summary>Empty for an empty trailing; else the pieces, with the gap ("  ") in front of the first one as a Normal piece.</summary>
    public static IReadOnlyList<TrailingPiece> Of(BlockLine line, AppTexts texts, Func<string, string> translate);
}

/// <summary>Draws the trailing of every intact line behind its text: one element, not editable, not selectable, muted (Theme.ObjectTrailing) except provenance and warning pieces, which keep their span colours.</summary>
internal sealed class TrailingGenerator : VisualLineElementGenerator
{
    public TrailingGenerator(Func<BlockLineMap?> map, AppTexts texts, Func<string, string> translate, Func<string, Brush?> brushOf);
}
```

Regeln (Festlegungen):

- Ein Element pro Zeile am Zeilenende (`GetFirstInterestedOffset` liefert das Zeilenende der intakten Zeile mit nicht leerem Trailing; `ConstructElement` baut ein `FormattedTextElement` mit `VisualLength 0`), damit der Cursor nicht hinein kann und Selektion es überspringt. Die Stücke werden zu einem `TextLine`-Lauf mit mehreren `TextRunProperties` zusammengesetzt: Farbe pro Stück `brushOf(SpanBrushes.ResourceKeyOf(kind))`, für `Normal`, `Title`, `LocText`-Anhängsel und Zähler jedoch `Theme.ObjectTrailing` (gedämpft, Spec 5.9); Herkunft (`ProvenanceVanilla/Mod/Project`), `Overridden` (durchgestrichen), `Warning` behalten ihre Farbe.
- `TrailingText.Of`: `Title` → `translate(span.Text)`; `Duplicate` → `texts.ObjectsDuplicate`; `OlderLayout` → `texts.ObjectsOlderLayout`; alles andere `MessageText.EscapeControlCharacters(span.Text)`; vor das erste Stück ein `Normal`-Stück mit zwei Leerzeichen.
- Eine `Annotation`-Zeile (Warnung des Aufbaus, leere Dokumentzeile) bekommt ihr Trailing wie jede andere intakte Zeile.

- [ ] **Step 1: Failing Tests**:

```csharp
// TrailingTextTests
[Fact] public void A_title_is_translated_and_the_gap_comes_first()
// Trailing [Title("objects.section.effects"), Normal("  "), Normal("(3)"), Normal(" "), ProvenanceVanilla("[db.pack]")] mit translate key => "T:" + key:
// Pieces Texts: ["  ", "T:objects.section.effects", "  ", "(3)", " ", "[db.pack]"], Kinds entsprechend.
[Fact] public void Duplicate_and_older_layout_use_the_texts_of_the_app()
[Fact] public void An_empty_trailing_gives_no_pieces()
[Fact] public void Control_characters_in_data_are_escaped()

// ObjectEditorViewTests
[Fact] public void Intact_lines_show_their_trailing_and_changed_lines_do_not() => Sta.Run(() =>
// Kopfzeile: VisualLine.Elements enthält ein FormattedTextElement, dessen Text "[db.pack]" enthält und VisualLength 0 hat; Dokumenttext unverändert (Editor.Text == BaselineText).
// Feldzeile ändern, Pump: ihre VisualLine hat kein FormattedTextElement mehr; die Kopfzeile hat es noch.
[Fact] public void The_caret_cannot_enter_a_trailing() => Sta.Run(() =>
// Caret auf Zeilenende der Kopfzeile, Taste Rechts: Caret.Offset == Beginn der nächsten Zeile (Element übersprungen).
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~TrailingTextTests|FullyQualifiedName~ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Spec** – Festlegungen: Anhängsel gedämpft außer Herkunft und Warnung; ein Element pro Zeile mit `VisualLength 0`.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Objects/Editor/TrailingGenerator.cs src/TotalWarModTool.App/Objects/Editor/TrailingText.cs src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs tests/TotalWarModTool.App.Tests/TrailingTextTests.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: draw the annotations of intact block lines behind their text"`.

---

### Task 5: Prüfung beim Tippen (`Check`), Fehler und leere Differenz sperren „Übernehmen“

Spec 5.7 (Parser entprellt), 7 (Parse-Fehler: Übernehmen gesperrt mit „N Fehler“), Backlog (Knopf bei leerer Differenz sperren).

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`Check`, `Diagnostics`, `CheckHandler`, Sperrgründe)
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.Tabs.cs` (`CheckHandler` setzen), `ObjectsController.cs` (`Check(tab)`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectApplyController.cs` (`ApplyAsync` ruft `tab.Check()` zuerst)
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (`DispatcherTimer` 300 ms: `Text`-Änderung startet ihn neu, Tick ruft `tab.Check()`)
- Modify: `src/TotalWarModTool.App/AppTexts.Objects.cs`, `locales/en.json`, `locales/de.json` (`ObjectsApplyErrors(int)`)
- Test: `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectsControllerTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: `ObjectTextParser.Parse`, `ObjectDiff.Compute(view, parsed, category, graph, null)`, `ObjectChanges.Diagnostics`/`HasErrors`/`IsEmpty`, `ObjectStores.Graph`.
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed class ObjectTabViewModel : TabViewModel
{
    /// <summary>Set by the controller: parses the current text against the result and diffs it without reference checks; null without stores.</summary>
    internal Func<ObjectTabViewModel, ObjectChanges?>? CheckHandler { get; set; }

    /// <summary>Runs the check for the current text now (the view calls it after the debounce, the apply before it starts). No-op without a result or a handler.</summary>
    public void Check();

    /// <summary>The diagnostics of the last check of the current text; empty when the text changed since, after a new result, or without a check.</summary>
    public IReadOnlyList<BlockDiagnostic> Diagnostics { get; }

    /// <summary>Errors among Diagnostics.</summary>
    public int ErrorCount { get; }

    /// <summary>True when the last check of the current text found no change to write.</summary>
    public bool IsDiffEmpty { get; }
}

public sealed partial class AppTexts
{
    public string ObjectsApplyErrors(int count) => F("objects.apply.reason.errors", "{0} errors in the text.", count);
}
```

Regeln (Festlegungen):

- `Check()`: `_checked = (Text, CheckHandler(this))`; `Diagnostics`, `ErrorCount`, `IsDiffEmpty` lesen aus `_checked`, aber nur wenn `_checked.Text == Text` (ordinal), sonst leer beziehungsweise false. `ApplyResult` und `MoveTo` setzen `_checked = null`. `PropertyChanged` für die drei Properties nach jedem `Check` und nach jeder Textänderung, die sie leert.
- `ApplyDisabledReason`: nach `ObjectsApplyStale` und vor `ObjectsApplyNoChanges`: `ErrorCount > 0` → `ObjectsApplyErrors(ErrorCount)`; `IsDiffEmpty` (Text abweichend, Differenz leer) → `ObjectsApplyNoChanges`.
- `ObjectsController.Check(tab)`: `_stores` null oder `tab.Result` null → null; sonst `Parse` und `Compute` mit Kategorie des Results, Graph der Stores, `referenceExists: null`. Kein Runner (synchron, UI-Thread).
- `ObjectApplyController.ApplyCoreAsync`: zuerst `tab.Check()`, dann `ApplyDisabledReason` prüfen (wie heute); ein Zustand mit Fehlern führt so nie in den Lauf A und nie in einen Dialog.
- View: `DispatcherTimer CheckDelay = 300 ms`; `OnEditorTextChanged` (Nutzereingabe) startet ihn neu; `Tick` stoppt ihn und ruft `tab.Check()`; `ShowTabText` (Text vom Tab) stoppt den Timer. `Loaded`/`Unloaded` stoppen ihn nicht (der Tab bleibt der Besitzer der Prüfung), aber `DataContextChanged` stoppt ihn.

- [ ] **Step 1: Failing Tests**:

```csharp
// ObjectTabViewModelTests
[Fact] public void Errors_of_the_check_disable_apply_and_name_their_count()
// CheckHandler liefert Changes mit 2 Error-Diagnosen; Text geändert; Check(): ErrorCount 2, ApplyDisabledReason == ObjectsApplyErrors(2), CanExecute false; Diagnostics enthält beide.
[Fact] public void A_whitespace_only_change_disables_apply_as_no_change()
// CheckHandler liefert leere Changes ohne Diagnosen; Text == Baseline + " " am Ende einer Zeile: HasUnappliedText true, nach Check IsDiffEmpty true, Grund ObjectsApplyNoChanges.
[Fact] public void A_check_result_belongs_to_the_text_it_was_computed_for()
// Check mit Fehlern; dann Text weiter ändern: Diagnostics leer, ErrorCount 0, Grund null (Handler gesetzt), bis zum nächsten Check.
[Fact] public void A_new_result_clears_the_diagnostics()
// Check mit Fehlern, ApplyResult: Diagnostics leer, IsDiffEmpty false.
[Fact] public void Check_without_a_handler_or_a_result_does_nothing()

// ObjectsControllerTests
[Fact] public async Task A_tab_checks_its_text_against_the_stores()
// Tab k1 Ready; Text: Wertzeile von y_tables auf "abc" (I32): tab.Check() → ErrorCount 1, Diagnostics[0].Kind InvalidValue mit Zeile; Text zurück → Check → 0 Fehler, IsDiffEmpty true.

// ObjectApplyControllerTests
[Fact] public async Task Apply_checks_first_and_refuses_without_a_dialog_when_the_check_has_errors()
// Text mit Fehler, kein Check vorher (Timer läuft in Tests nicht): ApplyAsync → keine Messages, keine Runner-Titel, ErrorCount > 0, Log nennt den Grund.

// ObjectEditorViewTests
[Fact] public void Typing_schedules_a_check_after_the_delay() => Sta.Run(() =>
// CheckHandler zählt; Insert; sofort 0 Aufrufe; Dispatcher für CheckDelay plus Reserve pumpen (DispatcherFrame mit Timer oder Thread.Sleep + Pump): 1 Aufruf; zweite Eingabe innerhalb der Verzögerung: insgesamt 2, nicht 3.
```

- [ ] **Step 2: RED** – Filter nacheinander: `ObjectTabViewModelTests`, `ObjectsControllerTests`, `ObjectApplyControllerTests`, `ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – dieselben Filter; `AppTextsTests` (Locale); Build ohne Warnung.

- [ ] **Step 5: Spec** – 7 „Parse-Fehler“: Umsetzung in 3b (Knopf gesperrt mit „N Fehler“); Festlegungen: Prüfung ohne Referenzen, 300 ms, Ergebnis an den Text gebunden; leere Differenz sperrt den Knopf als „Nichts geändert“ (Backlog-Eintrag streichen).

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs src/TotalWarModTool.App/Objects/ObjectsController.cs src/TotalWarModTool.App/Objects/ObjectsController.Tabs.cs src/TotalWarModTool.App/Objects/ObjectApplyController.cs src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs src/TotalWarModTool.App/AppTexts.Objects.cs locales/en.json locales/de.json tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs tests/TotalWarModTool.App.Tests/ObjectsControllerTests.cs tests/TotalWarModTool.App.Tests/ObjectApplyControllerTests.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md Backlog.txt`, `git commit -m "feat: check the block text while typing and disable apply for errors and an empty diff"`.

---

### Task 6: Unterstreichung und Tooltips (`DiagnosticRenderer`, Hover)

Spec 5.7 (Fehler und Warnungen als Unterstreichung mit Tooltip; Hover zeigt den Spieltext des Ziels), 7 (Typfehler nennt den Typ wie im Raster: der `Detail` der Diagnose), Backlog (Stelle des Fehlers im Editor).

**Files:**
- Create: `src/TotalWarModTool.App/Objects/Editor/DiagnosticRenderer.cs`, `src/TotalWarModTool.App/Objects/Editor/EditorHover.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (Renderer registrieren, `MouseHover`/`MouseHoverStopped` der `TextView` an `EditorHover`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`DisplayNameOf(ObjectId)` wieder, wie 4.1 mit Cache pro Result)
- Test: `tests/TotalWarModTool.App.Tests/EditorHoverTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: `ObjectTabViewModel.Diagnostics`, `BlockDiagnostic` (`Line`, `Severity`, `Kind`, `Detail`), `AppTexts.DiagnosticText(kind)`, `AppTexts.ObjectsTargetUnknown`, `BlockLineMap.IntactLineAt`, `ObjectBuilder.DisplayName(result.Loc, target)`, `TextView.GetPosition`, `IBackgroundRenderer`, `BackgroundGeometryBuilder`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>What the tool tip of a document position says. Pure over the tab and the map.</summary>
public static class EditorHover
{
    /// <summary>
    /// The lines of the tool tip at a 1-based document line and a 0-based column, or null: the diagnostics of the line (DiagnosticText(kind), plus
    /// " " and the escaped Detail for InvalidValue and ReferenceNotFound), then, for an intact line, the display name of a Link span under the column
    /// (tab.DisplayNameOf(target) or the short table name of the target) or ObjectsTargetUnknown for a Warning value span under the column.
    /// </summary>
    public static string? ToolTipAt(ObjectTabViewModel tab, BlockLineMap? map, int line, int column, AppTexts texts);

    /// <summary>The Link span under the column of an intact line, or null. Columns count from the start of the line, indent included.</summary>
    public static TextSpan? LinkAt(BlockLineMap? map, int line, int column);
}

/// <summary>Underlines every line that has a diagnostic: Theme.ObjectError for an error, Theme.Warning for a warning (an error wins). Wavy line under the text of the line. View wiring only.</summary>
internal sealed class DiagnosticRenderer : IBackgroundRenderer
{
    public DiagnosticRenderer(Func<IReadOnlyList<BlockDiagnostic>> diagnostics, Func<string, Brush?> brushOf);
    public KnownLayer Layer => KnownLayer.Selection;
}
```

Regeln (Festlegungen):

- `DisplayNameOf(target)`: `ObjectBuilder.DisplayName(_result.Loc, target)` maskiert, Cache pro Result wie in 4.1; null ohne Result.
- `Diagnostics` ändern sich → die View ruft `TextView.InvalidateLayer(KnownLayer.Selection)`; die Unterstreichung gilt für die ganze Zeile (`Line` ist 1-basiert; Zeilen außerhalb des Dokuments werden übersprungen).
- Hover: `TextView.MouseHover` → Position über `TextView.GetPosition(e.GetPosition(TextView) + ScrollOffset)`; `EditorHover.ToolTipAt`; Tooltip als `ToolTip` des Editors (`IsOpen = true`, `PlacementTarget = TextView`), `MouseHoverStopped` schließt ihn. `ObjectsTargetUnknown` für `Warning`-Spans mit `Target == null` innerhalb einer intakten Zeile mit Indent (nicht die Annotation-Zeile).

- [ ] **Step 1: Failing Tests**:

```csharp
// EditorHoverTests (ohne STA: Tab mit Result, Map über TextDocument)
[Fact] public void A_line_with_an_error_tells_its_kind_and_detail()
// CheckHandler liefert InvalidValue auf Zeile 2 mit Detail "expected an integer": ToolTipAt(tab, map, 2, 0) == DiagnosticText(InvalidValue) + " expected an integer".
[Fact] public void A_link_under_the_column_tells_the_display_name_or_the_table()
// Feldzeile "  chain = chain1" mit Link-Span auf chains_tables:chain1; Loc mit Namen "Chain One": ToolTipAt(..., Spalte 12) == "Chain One"; ohne Loc-Eintrag == "chains".
[Fact] public void A_changed_line_has_no_link_tool_tip()
[Fact] public void A_warning_value_tells_target_unknown()
[Fact] public void LinkAt_finds_the_span_by_column_with_the_indent()
// Spalte innerhalb "chain1" → Span Link; Spalte im Namen → null.

// ObjectTabViewModelTests
[Fact] public void DisplayNameOf_reads_the_loc_of_the_result_and_is_null_without_one()

// ObjectEditorViewTests
[Fact] public void A_diagnostic_line_is_underlined_in_the_error_colour() => Sta.Run(() =>
// CheckHandler mit Fehler auf Zeile 2, Check(), Pump: DiagnosticRenderer.Draw über einen DrawingVisual-Kontext aufrufen (RenderOpen) und prüfen, dass genau eine Geometrie gezeichnet wurde, deren Pen Theme.ObjectError ist (Renderer als internal zugänglich: view.Diagnostics); ohne Fehler keine.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~EditorHoverTests|FullyQualifiedName~ObjectTabViewModelTests|FullyQualifiedName~ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Spec** – 7 „Typfehler“: Tooltip nennt Art und `Detail`; Festlegung: Unterstreichung ganze Zeile, Fehler vor Warnung.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Objects/Editor/DiagnosticRenderer.cs src/TotalWarModTool.App/Objects/Editor/EditorHover.cs src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs tests/TotalWarModTool.App.Tests/EditorHoverTests.cs tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: underline diagnostics in the block editor and show their reason and link names on hover"`.

---

### Task 7: Falten (`BlockFolding`) mit bleibendem Faltzustand

Spec 5.2 (gefaltet ab der Klappgrenze, Vorschau, `+N`), 5.9 (Faltzustand bleibt nach Übernehmen und Neuaufbau).

**Files:**
- Create: `src/TotalWarModTool.App/Objects/Editor/BlockFolding.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`ExpandedSections` wieder: `IReadOnlySet<int>`, `SetSectionExpanded(int, bool)`; bleibt über `ApplyResult` desselben Objekts, leer nach `MoveTo`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (`FoldingManager.Install`, Faltungen nach jedem Block, Zustand zurück in den Tab)
- Test: `tests/TotalWarModTool.App.Tests/BlockFoldingTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs`, `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: `BlockText.Sections` (`BlockSectionRange.Section`, `Rows[i].LastLine`, `LastLine`), `ObjectText.DefaultCollapseThreshold` (20), `ObjectText.DefaultCollapsedPreview` (5), `AppTexts.ObjectsMoreRows(count)`, `FoldingManager`, `NewFolding`, `FoldingSection.IsFolded`/`Tag`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>A folding of one section: the offsets in the document and the section index. Pure over the block and the document line offsets.</summary>
public sealed record SectionFolding(int Section, int StartOffset, int EndOffset, int HiddenRows);

public static class BlockFolding
{
    /// <summary>
    /// One folding per section with more than threshold rows: from the end of the last line of row preview-1 to the end of the section's last
    /// line (both from lineStartOffsets, which has the start offset of every 0-based line and the text length as its last entry). Sections with
    /// at most threshold rows have none. HiddenRows is Rows.Count - preview.
    /// </summary>
    public static IReadOnlyList<SectionFolding> Compute(BlockText block, IReadOnlyList<int> lineStartOffsets,
        int threshold = ObjectText.DefaultCollapseThreshold, int preview = ObjectText.DefaultCollapsedPreview);
}
```

Regeln (Festlegungen):

- Die View installiert `FoldingManager` einmal; nach jedem neuen Block (Text gesetzt): `UpdateFoldings(foldings.Select(f => new NewFolding(f.StartOffset, f.EndOffset) { Name = texts.ObjectsMoreRows(f.HiddenRows) }), -1)` und danach pro Faltung `IsFolded = !tab.ExpandedSections.Contains(section)`; die Zuordnung Faltung → Abschnitt über `FoldingSection.Tag = section`. Bei leerem Dokument keine Faltungen (Spike).
- Zustand zurück: `TextView.VisualLinesChanged` vergleicht pro Faltung `IsFolded` mit dem Tab und ruft `SetSectionExpanded(section, !IsFolded)`, wenn anders. (Kein eigenes Ereignis des `FoldingManager`; Festlegung.)
- `ExpandedSections` bleibt über `ApplyResult` (gleiches Objekt, neuer Aufbau, Abschnittsindizes aus `OrderSections` stabil, solange die Abschnitte dieselben bleiben); `MoveTo` leert es. Ein Index, den der neue Block nicht hat, bleibt unschädlich im Set.
- Faltungen folgen Eingaben (Offsets im `FoldingManager` sind `TextSegment`s); ein Block ohne Map (Text vom Nutzer so geändert, dass kein Anker hält) behält sie trotzdem.

- [ ] **Step 1: Failing Tests**:

```csharp
// BlockFoldingTests (ohne STA)
[Fact] public void A_section_above_the_threshold_folds_after_the_preview_rows()
// Block mit Abschnitt von 25 Zeilen (Rows ohne Feldzeilen): eine SectionFolding, StartOffset == Ende der Zeile Rows[4].LastLine, EndOffset == Ende von LastLine, HiddenRows 20, Section == Index.
[Fact] public void A_section_at_the_threshold_has_no_folding()
// 20 Zeilen: keine; 21: eine.
[Fact] public void Rows_with_field_lines_fold_after_the_fifth_row_block()
// Vorschau endet nach dem letzten Zeilenblock der fünften Zeile (ihre Feldzeilen inklusive).

// ObjectTabViewModelTests
[Fact] public void Expanded_sections_survive_a_new_result_and_not_a_move()

// ObjectEditorViewTests
[Fact] public void Long_sections_are_folded_with_a_more_rows_title_and_unfolding_is_remembered() => Sta.Run(() =>
// View mit Abschnitt von 25 Zeilen: FoldingManager.AllFoldings eine, IsFolded true, Title == Texts.ObjectsMoreRows(20); IsFolded = false setzen, Pump: tab.ExpandedSections enthält den Abschnitt; tab.ApplyResult(gleiches Result), Pump: Faltung ist nicht gefaltet.
[Fact] public void A_folding_follows_an_insert_above_it() => Sta.Run(() =>
// Zeile vor dem Abschnitt einfügen: StartOffset der Faltung um die Länge verschoben, weiter gefaltet.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~BlockFoldingTests|FullyQualifiedName~ObjectTabViewModelTests|FullyQualifiedName~ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Spec** – Festlegungen: Faltzustand im Tab (`ExpandedSections`), Rückmeldung über `VisualLinesChanged`; Faltung ab Zeile 21 nach der fünften Zeile.

- [ ] **Step 6: Commit** – `git add src/TotalWarModTool.App/Objects/Editor/BlockFolding.cs src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs tests/TotalWarModTool.App.Tests/BlockFoldingTests.cs tests/TotalWarModTool.App.Tests/ObjectTabViewModelTests.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, `git commit -m "feat: fold long sections of the block and keep the fold state across rebuilds"`.

---

### Task 8: Links (Strg+Klick, mittlere Maustaste, Umschalt für neuen Tab, Handcursor)

Spec 5.7 (Strg+Klick öffnet das Ziel wie 4.1, Klick allein setzt den Cursor), 4.1 (Umschalt oder mittlere Taste: neuer Tab).

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs` (`PreviewMouseDown`/`PreviewMouseMove` der `TextView`)
- Test: `tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: `EditorHover.LinkAt` (Task 6), `ObjectTabViewModel.NavigateTo(target, newTab)`, `TextView.GetPosition`, `Keyboard.Modifiers`, `Cursors.Hand`.
- Produces: `internal bool TryOpenLinkAt(Point viewPoint, bool newTab)` auf der View (für Tests): true, wenn ein Link unter dem Punkt lag und `NavigateTo` gerufen wurde.

Regeln (Festlegungen):

- `PreviewMouseDown`: linke Taste mit Strg → `TryOpenLinkAt(point, newTab: Umschalt gedrückt)`, bei true `e.Handled = true` (kein Cursorsetzen); mittlere Taste → `TryOpenLinkAt(point, newTab: true)`. Ohne Strg setzt ein Klick nur den Cursor.
- `PreviewMouseMove` mit Strg über einem Link → `TextArea.Cursor = Cursors.Hand`, sonst null (Standard). `KeyUp` von Strg setzt den Cursor zurück.
- `NavigateTo(target, newTab: false)` fragt bei ungeübernommenem Text über den `LeaveHandler` (Plan 3a) – unverändert.

- [ ] **Step 1: Failing Tests**:

```csharp
[Fact] public void Ctrl_click_on_a_link_navigates_and_a_plain_click_does_not() => Sta.Run(() =>
// Tab mit OpenHandler/BuildHandler-Zählern; Punkt der Spalte im Linkwert über TextView.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle) - ScrollOffset;
// TryOpenLinkAt(point, false) true, History gewachsen (Navigation im Tab); auf einer Spalte im Feldnamen false; auf einer geänderten Zeile false.
[Fact] public void Shift_or_middle_opens_a_new_tab() => Sta.Run(() =>
// TryOpenLinkAt(point, true): OpenHandler einmal gerufen, History unverändert.
```

- [ ] **Step 2: RED** – Filter `FullyQualifiedName~ObjectEditorViewTests`.

- [ ] **Step 3: Implementieren.**

- [ ] **Step 4: GREEN** – Filter; Build ohne Warnung.

- [ ] **Step 5: Commit** – `git add src/TotalWarModTool.App/Objects/ObjectEditorView.xaml.cs tests/TotalWarModTool.App.Tests/ObjectEditorViewTests.cs`, `git commit -m "feat: open the target of a reference value with Ctrl+click or the middle button"`.

---

### Task 9: Abschluss (Changelog, Backlog, Spec, manuelle Checkliste)

**Files:**
- Modify: `CHANGELOG.md`, `Backlog.txt`, `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`, dieser Plan (Status je Task)

- [ ] **Step 1: Changelog** – „Neu“, Eintrag „Objekt bearbeiten“ ergänzen: Farben für Vanilla, Mod und Projekt, Herkunft und Spieltexte als nicht editierbare Anhängsel hinter der Zeile (verschwinden für Zeilen, die du geändert hast, und kommen nach dem Übernehmen zurück), Abschnitte mit mehr als 20 Zeilen gefaltet mit „+ N weitere Zeilen“ (Faltzustand bleibt nach dem Übernehmen), Fehler beim Tippen unterstrichen mit Tooltip und „Übernehmen“ gesperrt mit „N Fehler“, Strg+Klick auf einen Wert öffnet das Ziel, Umschalt oder mittlere Maustaste in einem neuen Tab, Hover zeigt den Spielnamen des Ziels. Den Satz „Färbung, Herkunftsangaben und Falten des Blocks folgen.“ und den Eintrag unter „Geändert“ („vorerst ohne Farben …“) entfernen.
- [ ] **Step 2: Backlog** – Abschnitt „Aus Teil 4.2a Plan 3b (Editor) zurückgestellt“: Theme-Wechsel zur Laufzeit färbt den Editor erst beim nächsten Redraw (Ereignis des `ThemeManager` abonnieren); eine neu getippte Zeile mit dem Text einer gelöschten Zeile gilt als geändert (ohne Anhängsel), bis neu gebaut wird; Tooltip des Links nur Name (Kurzbeschreibung, 4.1-Backlog); Faltungs-Rückmeldung über `VisualLinesChanged` statt eines Ereignisses; der Vorschlags-Katalog (4.2b) nutzt `TextArea.TextEntered`. Die erledigten Zeilen aus „Aus Plan 3a“ streichen (`Render`, leere Differenz, Stelle des Fehlers).
- [ ] **Step 3: Spec** – Abschnitt 10, Annahme 2: Ergebnis aus `The_empire_block_diffs_fast` (Controller trägt die Zahl nach dem Echtdaten-Lauf ein) und „Färben: Abnahme 3b“. Abschnitt 7 „Parse-Fehler“ und „Referenzziel unbekannt“ auf den Stand von 3b bringen (Unterstreichung, gelbe Warnung, Zähler in der Ergebniszeile besteht seit 3a). Abschnitt 9: Plan 3b erledigt.
- [ ] **Step 4: Verifikation** – `dotnet build TotalWarModTool.slnx` ohne Warnung; `dotnet test tests/TotalWarModTool.Objects.Tests` grün; die App-Testklassen dieses Plans einzeln grün (`SpanBrushesTests`, `LineSyntaxTests`, `BlockLineMapTests`, `TrailingTextTests`, `EditorHoverTests`, `BlockFoldingTests`, `ObjectEditorViewTests`, `ObjectTabViewModelTests`, `ObjectsControllerTests`, `ObjectApplyControllerTests`, `ThemeTests`, `AppTextsTests`). Die volle Suite läuft mit dem Nutzer.
- [ ] **Step 5: Commit** – `git add CHANGELOG.md Backlog.txt docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md docs/plans/2026-10-10-teil-4-2a-plan-3b-app-editor-darstellung.md`, `git commit -m "docs: record plan 3b of Teil 4.2a in the changelog, the backlog and the spec"`.

---

## Manuelle Abnahme durch den Nutzer (nach dem vollen Testlauf)

Mit WH3 und einem Projekt:

1. **Fraktion `wh_main_emp_empire`** öffnen (2138 Zeilen): Farben wie in 4.1 (Vanilla grün, Mod blau, Projekt rot, Spieltexte braun, Feldnamen gedämpft), Herkunft hinter der Kopfzeile, Abschnitte über 20 Zeilen gefaltet mit „+ N weitere Zeilen“, Entfalten per Klick auf das Faltsymbol. Tippen in einer Wertzeile: flüssig, keine spürbare Pause (Annahme 2); nach 300 ms ohne Fehler keine Unterstreichung.
2. **Anhängsel:** Zeile ändern → ihre Herkunft und ihr Spieltext verschwinden; Änderung zurücknehmen → sie sind wieder da. Cursor mit Pfeiltasten über das Zeilenende: springt zur nächsten Zeile, nie ins Anhängsel; Selektion mit Umschalt+Ende nimmt das Anhängsel nicht mit; Kopieren liefert nur Text.
3. **Fehler:** `create_cost = abc` → nach 300 ms rote Wellenlinie, Tooltip „Der Wert passt nicht zum Typ des Felds. …“, Knopf „Übernehmen“ gesperrt mit Tooltip „1 Fehler im Text.“; Zeile reparieren → Linie weg, Knopf aktiv. Unbekanntes Feld, doppelter Schlüssel, Kopfzeile per Einfügen verändert: je eine Linie.
4. **Warnung:** Referenzwert auf einen unbekannten Schlüssel ändern → gelbe Linie, Tooltip, Knopf aktiv; Übernehmen zählt „1 Warnungen“ in der Ergebniszeile.
5. **Leere Differenz:** nur ein Leerzeichen am Ende einer Wertzeile → Tab markiert, Knopf gesperrt „Nichts geändert.“
6. **Falten bleibt:** Abschnitt entfalten, Wert ändern, Übernehmen → nach dem Neuaufbau ist der Abschnitt noch entfaltet; Zurück und Vor → Faltzustand des neuen Objekts ist der Standard.
7. **Links:** Strg über einem Referenzwert zeigt die Hand; Strg+Klick öffnet das Ziel im Tab (mit ungeübernommenem Text: Frage); Umschalt+Strg+Klick oder mittlere Taste: neuer Tab; Klick ohne Strg setzt nur den Cursor. Hover über dem Wert zeigt den Spielnamen des Ziels.
8. **Theme:** Hell und Dunkel: Farben lesbar, Anhängsel gedämpft, Fehlerlinie sichtbar. Wechsel zur Laufzeit: Editor färbt beim nächsten Scrollen um (Backlog).
9. **Gebäude `wh_main_emp_barracks_2`:** Satzzeilen `allows …` färben Wert als Link; Junction-Zeilen nur Wert; Feldzeilen unter Satzzeilen.

## Nächste Phase

Nach Plan 3b: Review (starkes Modell), voller Testlauf und die Abnahme oben. Dann Plan 4 (App „Kopieren als“ und Assistenten „Neues Gebäude“, „Neuer Trait“; Spec 5.8), sobald die Interfaces aus 3a und 3b existieren. Umsetzung der Tasks nach Task-Brief: mittleres Modell; Task 3 (Anker) und Task 4 (virtuelle Elemente) sind die anspruchsvollsten Briefs, dort lohnt ein Zwischenreview.
