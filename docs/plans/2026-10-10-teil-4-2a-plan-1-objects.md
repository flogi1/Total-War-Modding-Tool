# Teil 4.2a Plan 1: Library `Objects` (Blocktext, Parser, Differenz, Kopie, Dateiersetzung) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die Library `Objects` liefert den editierbaren Blocktext eines Objekts (Text getrennt von Anhängseln), parst ihn zurück, berechnet die Differenz zum Aufbau als Zeilen- und Textänderungen, benennt ein Objekt für „Kopieren als“ um und behandelt Pack-Dateien, die eine frühere Datei unter demselben Pfad ersetzen, wie das Spiel. Keine App-Änderung.

**Architecture:** Vier reine Funktionen in `Objects`: `ObjectText.RenderBlock` (View → `BlockText` mit Zeilen aus Text-Spans, Anhängsel-Spans und Zeilenbereichen pro Abschnitt), `ObjectTextParser.Parse` (Text → `ParsedObject` mit Diagnosen, tolerant), `ObjectDiff.Compute` (Baseline-View plus `ParsedObject` → `ObjectChanges`), `ObjectCopy.Rename` (View plus neuer Schlüssel → Liste von `ParsedObject`). Dazu in `TableStore`, `LocStore` und `RowProvenance` die Dateiersetzung über die Ladereihenfolge und die Eintragspfade überschriebener Schichten. `ObjectText.Render` aus 4.1 bleibt unverändert, die App nutzt es bis Plan 3.

**Tech Stack:** C# 12, .NET 8, xUnit 2.9.2. Kein neues NuGet-Paket in diesem Plan (AvalonEdit kommt in Plan 3).

**Spec:** `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`: 5.2 (Blocktext), 5.3 (Parser und Modell), 5.5 (Dateiersetzung, zweiter Absatz), 5.8 (Kopierregel), 7 (Fehlerfälle), 8 (Tests `Objects`), 10 (Annahmen 2, 3, 4). Vorgänger: `docs/specs/2026-10-09-teil-4-1-objekt-ansicht.md` 5.5 und 5.6. Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-4-2a-objects` (von `master`, Stand `c8b6728` oder neuer). Baseline: voller Lauf vom 2026-10-10 grün (App 952, Objects 117 plus 5 Echtdaten), Build ohne Warnung.

**Testregel** (mit dem Nutzer vereinbart): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei (`dotnet test tests/TotalWarModTool.Objects.Tests`). Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Kein `python`, `perl` oder anderes Skript-Laufzeitsystem. Den vollen Lauf macht der Controller einmal am Ende nach dem Okay des Nutzers. Echtdaten-Tests (`RealDataFact`) laufen nur mit `TWMT_WH3_DIR`; der Controller setzt die Variable, Subagenten setzen sie nicht. Nach jedem Task muss auch `dotnet build TotalWarModTool.slnx` ohne Warnung durchlaufen (die App referenziert `Objects`).

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog und Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors` an, Nullable an, `ImplicitUsings` an.
- `Objects` bekommt keine neue Projektreferenz und referenziert nie die App. Die Library loggt nicht und liefert keine UI-Texte: Diagnosen sind kurzes technisches Englisch (`Detail`), die App übersetzt nach `Kind`.
- Alle neuen Funktionen sind rein: kein Dateizugriff in `ObjectText`, `ObjectTextParser`, `ObjectDiff`, `ObjectCopy`. Dateizugriff bleibt in `SourceReader`, `TableStore`, `LocStore`.
- Reihenfolgen sind deterministisch (Schemareihenfolge der Felder, Textreihenfolge der Zeilen, ordinaler Vergleich für Sortierungen). Vergleiche von Tabellen- und Feldnamen ohne Rücksicht auf Schreibweise (`StringComparison.OrdinalIgnoreCase`), Werte ordinal.
- `ObjectText.Render`, `TextLine`, `TextSpan`, `RenderOptions` und `SpanKind` bleiben quellkompatibel: Die App und `tests/TotalWarModTool.App.Tests` bauen unverändert. Neue Enum-Werte in `SpanKind` nur, wenn die App sie nicht als Fehler sieht (sie hat kein `switch` mit Vollständigkeitsprüfung; prüfen mit `dotnet build TotalWarModTool.slnx`).
- `RowProvenance` bekommt nur Parameter mit Standardwert, damit bestehende Aufrufe bauen.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Tests schreiben nur in Temp-Ordner (`Path.GetTempPath()` plus GUID, in `finally` aufräumen) wie `TableStoreTests`. Mini-Schema über `TestSchema`, Packs über `TestSources.WritePack`, Projektdateien über `TestSources.WriteProjectTable` und `WriteProjectLoc`. Nichts liest oder schreibt unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`, nichts in Spiel-, Workshop- oder RPFM-Ordnern.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst. Widerspricht eine Erwartung einer Regel des Plans oder der Spec, wird das gemeldet, nicht still entschieden.
- Weicht die Umsetzung von der Spec ab, wird die Spec im selben Commit angepasst (Abschnitt „Festlegungen aus Plan 1“ unter 5.3, Task 1 legt ihn an).

## Review Focus

1. **Werte, die wie Syntax aussehen** (Feldwert mit ` = `, Wert mit führenden Leerzeichen, Loc-Text mit `"` und `\`, Schlüsselwert mit `  ` doppeltem Leerzeichen). Erwartung: der Text rendert und parst ohne Differenz; ein Wert mit Tab oder Zeilenumbruch ist ein Fehler mit Zeilennummer, nie stille Verstümmelung. → Task 1 (`A_value_containing_the_assignment_token_round_trips`), Task 2 (`A_loc_text_with_quotes_and_backslashes_round_trips`, `A_value_with_a_tab_is_an_error`).
2. **Zeilen ohne Schlüssel und doppelte Schlüssel** (Tabelle ohne Schlüsselfeld, zwei Vanilla-Zeilen mit gleichem Schlüssel, Nutzer tippt eine dritte). Erwartung: keyless Zeilen werden nach Feldsatz verglichen, doppelte nach Reihenfolge; eine Zeile mehr ist ein Fehler, nie ein stilles Überschreiben. → Task 3 (`Rows_without_key_are_matched_by_their_fields`, `Duplicate_keys_of_the_baseline_are_matched_by_position`, `One_more_duplicate_than_the_baseline_is_an_error`).
3. **Überschreibkette beim Löschen** (Projektzeile überschreibt Mod-Zeile überschreibt Vanilla-Zeile). Erwartung: `Delete` trägt Quelle, Eintragspfad und jede überschriebene Schicht mit ihrem Eintragspfad, damit Plan 2 jede Schicht trifft. → Task 5 (`Overridden_layers_keep_their_entry_paths`), Task 3 (`A_removed_row_is_a_delete_with_its_provenance` reicht die Provenance unverändert durch).
4. **Dateiersetzung mit abweichender Schreibweise** (Projektdatei `Data__.tsv` gegen Pack-Eintrag `db/x_tables/data__`; Mod-Pack ersetzt Vanilla-Datei; Projekt-Loc `data__.loc.tsv` gegen `text/db/data__.loc`). Erwartung: ersetzt ohne Rücksicht auf Schreibweise und Trennzeichen; die Zeilen der ersetzten Datei fehlen, Zeilen anderer Dateien derselben Tabelle bleiben; die Signatur ändert sich mit der Kopie. → Task 5 (`A_project_file_with_the_same_pack_path_replaces_the_vanilla_file`, `A_mod_file_replaces_the_vanilla_file_but_not_its_siblings`, `A_project_loc_file_replaces_the_pack_loc_file`).
5. **Kopie mit Unterobjekten und Tiefe** (Trait mit Stufen `t1_1`, `t1_2`, Stufen-Effekte; Stufe hat selbst ein Präfix-Unterobjekt). Erwartung: Stufen und ihre Effekte kopiert mit ersetztem Präfix, ein drittes Niveau wird als Hinweis gemeldet, nichts kopiert, was nur per Fremdschlüssel zeigt. → Task 4 (`Prefix_rows_are_copied_as_sub_objects_with_their_own_sections`, `A_third_level_is_reported_not_copied`, `Foreign_key_rows_are_reported_not_copied`).

---

### Task 1: Blocktext rendern (`ObjectText.RenderBlock`)

Der editierbare Text eines Objekts nach Spec 5.2, getrennt von den Anhängseln. `Render` aus 4.1 bleibt, bis Plan 3 die App umstellt.

**Files:**
- Modify: `src/TotalWarModTool.Objects/ObjectText.cs`
- Create: `src/TotalWarModTool.Objects/BlockText.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/BlockTextTests.cs`
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Abschnitt „Festlegungen aus Plan 1“ unter 5.3 anlegen)

**Interfaces:**
- Consumes: `ObjectView`, `ObjectRow`, `FieldValue` (`IsKey`), `LocEntry.Owner`, `ObjectSection`, `CategoryDefinition`, `SectionRule`, `ObjectText.TryParsePattern`, `ObjectText.ShortTableName`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum BlockLineKind { Head, RootField, RootText, SectionHead, RowHead, RowField, RowText, Blank, Annotation }

/// <summary>Spans is the editable text of the line (empty for Blank and Annotation), Trailing the annotations drawn behind it. Indent counts levels of two spaces. Section is the index into ObjectView.Sections for SectionHead, RowHead, RowField and RowText; Row the index into the section's Rows for the row lines.</summary>
public sealed record BlockLine(BlockLineKind Kind, IReadOnlyList<TextSpan> Spans, IReadOnlyList<TextSpan> Trailing, int Indent, int? Section = null, int? Row = null);

/// <summary>FirstLine and LastLine are inclusive line indexes of one row block (its head line and its field and text lines).</summary>
public sealed record BlockRowRange(int Section, int Row, int FirstLine, int LastLine);

/// <summary>HeadLine is the section head; FirstLine..LastLine the lines of its rows. Rows lists the row blocks in order, so the App can fold everything after RenderOptions.CollapsedPreview rows.</summary>
public sealed record BlockSectionRange(int Section, int HeadLine, int FirstLine, int LastLine, IReadOnlyList<BlockRowRange> Rows);

public sealed record BlockText(IReadOnlyList<BlockLine> Lines, IReadOnlyList<BlockSectionRange> Sections)
{
    /// <summary>The editable text: the Spans of every line joined, Indent * 2 spaces in front, lines joined with "\n". Blank and Annotation lines are empty lines.</summary>
    public string Text { get; }
}

public static partial class ObjectText
{
    /// <summary>Loc text in the block: a quote becomes \", a backslash \\, any line break \n; wrapped in quotes.</summary>
    public static string QuoteText(string text);

    public static BlockText RenderBlock(ObjectView view, CategoryDefinition? category);
}
```

Regeln (Spec 5.2, hier als Festlegungen):

- Zeile 0 `Head`: `table key` (`ShortTableName`, `DisplayKey`), ein Leerzeichen dazwischen; Trailing: Herkunft, überschriebene Herkünfte (wie `Render`). Danach eine `Annotation`-Zeile pro `ObjectWarning` (Trailing mit `SpanKind.Warning`, Spans leer). Ohne Wurzel (`Root == null`): nur Kopf und Warnungen, keine Abschnitte.
- `RootField`, Indent 1, für jedes Feld der Wurzel in Schemareihenfolge: Spans `name`, ` = `, `wert` (Kind `FieldName`, `Normal`, `Value` oder `Link` mit `Target` bei `Found`/`Unchecked` mit Target, `Warning` bei `NotFound`); Trailing: Loc-Text in Anführungszeichen bei `Found` mit `LocText` oder `LocOnly` (Kind `LocText`). Alle Felder, auch leere (`name = ` ohne Wert dahinter: Spans `name`, ` = `, leerer `Value`-Span entfällt, also nur zwei Spans).
- `RootText`, Indent 1, für jeden `LocEntry` mit `Owner.Equals(view.Id)`, in `Texts`-Reihenfolge: Spans `column`, ` = `, `QuoteText(text)` (Kinds `FieldName`, `Normal`, `LocText`); Trailing: Herkunft der Quelle.
- Pro Abschnitt in der Reihenfolge von `OrderSections` (wie `Render`, versteckte fehlen, leere fehlen): `Blank`, dann `SectionHead` Indent 0 mit Spans `table` (`ShortTableName`) oder `table.field`, wenn `view.Sections` dieselbe Tabelle mehr als einmal enthält (Kind `TableName`, der Punkt und das Feld gehören zum selben Span); Trailing: Titel-Schlüssel (Kind `Title`, `Section` gesetzt) wenn die Regel einen hat, `(N)`, Herkünfte (wie `Render`).
- Zeilen eines Abschnitts, alle Zeilen (keine `Collapsed`-Zeilen mehr), zwischen zwei Blöcken (Listenzeile mit Feldzeilen) eine `Blank`-Zeile mit Indent 1 wie in `Render`:
  - Junction-Zeile (`section.IsJunction && row.OtherEnd is not null`, keine Satzregel): `RowHead` Indent 1, Spans nur der Wert (Link oder Value wie oben); Trailing: Loc-Name, Herkunft, `duplicate`, `older layout`.
  - Satzzeile (Regel `Sentence` mit gültigem Muster): `RowHead` Indent 1, Spans wie `SentenceSpans` in `Render` (Literale `Normal`, Werte wie oben); Trailing: Loc-Texte der Platzhalterwerte in Musterreihenfolge, Herkunft. Keine Feldzeilen darunter (Spec 5.2 Regel 6: hidden fields bleiben beim Schreiben).
  - Listenzeile: `RowHead` Indent 1 mit den Schlüsselfeldern (`IsKey`, ohne das Feld `section.FieldName`) als `name = wert`, getrennt durch einen `Normal`-Span `"  "`; ohne Schlüsselfeld das **erste** Feld in Schemareihenfolge außer `section.FieldName` (Abweichung von 4.1 „erstes nicht leeres“, Spec 5.2 Regel 5). Trailing: Loc-Texte der Kopffelder, Herkunft, Marken. Dann `RowField` Indent 2 für jedes übrige Feld mit nicht leerem Text (Loc-Text als Trailing), dann `RowText` Indent 2 für jeden `LocEntry` mit `Owner.Equals(row.RowId)` (Spans wie `RootText`).
  - `RowText`-Zeilen (Indent 2) gibt es auch unter Satz- und Junction-Zeilen, wenn `row.RowId` Texte hat; nur Feldzeilen fehlen dort.
  - Loc-Einträge, deren Owner weder Wurzel noch eine Zeile ist (Referenzziele), erscheinen nirgends als Zeile; ihr Text steht als Trailing hinter dem Link (`FieldValue.LocText`), wie heute.
- `BlockText.Text` ist der Round-Trip-Text; `Sections` listet Kopfzeile und Zeilenbereiche. `Rows` eines `BlockSectionRange` endet vor der `Blank`-Zeile zwischen Blöcken.

- [x] **Step 1: Failing Tests** in `BlockTextTests.cs` (Helfer wie in `ObjectTextTests`: `Plain`, `Key`, `Reference`, `Row`, `View`, `Section`, `Junction`; dazu `Text(BlockLine)` als `string.Concat(line.Spans.Select(s => s.Text))` und `Trailing(BlockLine)` ebenso):

```csharp
[Fact] public void The_head_holds_table_and_key_and_the_provenance_is_trailing()
// View mit Wurzel aus Mod über Vanilla: Lines[0].Kind == Head, Text == "building_levels b1", Trailing == "[mod.pack] [db.pack]", Indent 0.

[Fact] public void Root_fields_are_complete_and_loc_texts_are_trailing()
// Felder chain (Found, "Chain One"), commodity (LocOnly, "Gold"), create_cost "100", empty "" (leer): vier RootField-Zeilen, Text "chain = chain1" mit Trailing "\"Chain One\"", "commodity = gold" Trailing "\"Gold\"", "create_cost = 100" Trailing "", "empty = " Trailing "" mit genau zwei Spans.

[Fact] public void Root_texts_stand_below_the_fields_as_column_equals_quoted_text()
// LocEntry Owner == Wurzel, column onscreen_name, Text Barracks, Quelle Mod: RootText-Zeile Text "onscreen_name = \"Barracks\"", Trailing "[mod.pack]", Indent 1, Kinds FieldName, Normal, LocText.

[Fact] public void Texts_of_reference_targets_are_not_lines()
// LocEntry mit Owner = Ziel der Referenz chain: keine RootText-Zeile dafür; Lines zählt nur Head, RootField-Zeilen.

[Fact] public void A_list_row_puts_keys_in_the_head_and_fields_and_texts_below()
// Abschnitt effects_tables (FieldName building) mit Zeile: building=b1, effect=e1 (Key, Found, Loc "Income"), scope=province, value=10, note="" (leer), RowId effects_tables:e1, LocEntry Owner RowId column description "More gold":
// SectionHead "effects" (Trailing "(1) [db.pack]"), RowHead Indent 1 "effect = e1" Trailing "\"Income\"  [db.pack]" , RowField "scope = province", RowField "value = 10", RowText "description = \"More gold\"", keine Zeile für note oder building.

[Fact] public void A_section_head_names_the_field_when_the_table_appears_twice()
// Zwei Abschnitte upgrades_tables mit FieldName from und to: SectionHead-Texte "upgrades.from" und "upgrades.to"; ein einzelner Abschnitt heißt nur "upgrades".

[Fact] public void A_table_without_key_uses_its_first_field_as_the_head()
// Felder building, "" (leer) als erstes übriges Feld a, dann b="x": Head "a = " (zwei Spans), RowField "b = x".

[Fact] public void A_sentence_row_is_one_line_with_its_values_and_no_field_lines()
// Regel Sentence "allows {unit}", Zeile unit=u1 (Found, Loc "Swordsmen"), xp=2: genau eine RowHead "allows u1" mit Trailing "\"Swordsmen\"  [db.pack]"; keine RowField-Zeile für xp.

[Fact] public void A_junction_row_is_only_the_other_end()
// wie Junction_rows_render_only_the_other_end in ObjectTextTests, aber RowHead Text "e1", Trailing "\"Income\"  [db.pack]".

[Fact] public void Long_sections_are_not_collapsed_and_the_ranges_cover_every_row()
// 25 Listenzeilen ohne Felder: 25 RowHead-Zeilen, kein Span mit SpanKind.Collapsed; Sections[0].Rows.Count == 25, FirstLine der ersten == HeadLine + 1, LastLine der letzten == Lines.Count - 1.

[Fact] public void Blocks_are_separated_by_a_blank_line_inside_the_row_ranges()
// Zwei Listenzeilen mit je einem Feld: Zeilen RowHead, RowField, Blank(Indent 1), RowHead, RowField; Rows[0] == (FirstLine h, LastLine h+1), Rows[1] == (h+3, h+4).

[Fact] public void Warnings_are_annotation_lines_without_text()
// Warnung RootNotFound: Lines[1].Kind == Annotation, Spans leer, Trailing ein Warning-Span; Text-Eigenschaft hat dort eine leere Zeile.

[Fact] public void Hidden_and_empty_sections_are_omitted_and_the_order_follows_the_category()
// wie A_category_orders_titles_and_hides_sections: Titel-Span in Trailing mit Kind Title und Section gesetzt.

[Fact] public void The_text_property_joins_lines_with_indent_and_newline()
// Kleine View: Text == "building_levels b1\n  create_cost = 100\n\neffects\n  effect = e1\n    value = 10".

[Fact] public void A_value_containing_the_assignment_token_round_trips()
// Wurzelfeld note = "a = b  c": RootField-Text "note = a = b  c"; (der Parser-Test in Task 2 prüft das Rückparsen).

[Theory] [InlineData("He said \"hi\"", "\"He said \\\"hi\\\"\"")] [InlineData("a\\b", "\"a\\\\b\"")] [InlineData("line1\r\nline2", "\"line1\\nline2\"")] [InlineData("x\ry", "\"x\\ny\"")]
public void QuoteText_escapes_quotes_backslashes_and_line_breaks(string text, string expected)
```

- [x] **Step 2: RED** – `dotnet test tests/TotalWarModTool.Objects.Tests --filter "FullyQualifiedName~BlockTextTests"`: Build-Fehler (Typen fehlen).

- [x] **Step 3: Implementieren** – `BlockText.cs` mit den Records (Text im Konstruktor berechnen, Indent-Leerzeichen `new string(' ', indent * 2)`), `ObjectText` wird `partial`, `RenderBlock` in `ObjectText.cs` oder einer neuen Datei `ObjectText.Block.cs`. `OrderSections`, `SentenceSpans`, `ValueSpans`, `Provenance`, `AddRowProvenance` werden geteilt: Werte-Spans ohne Loc-Text bauen (neue private Hilfsfunktion `ValueSpan(FieldValue)` liefert genau einen Span, `LocSpan(FieldValue)` den Trailing-Span oder null); `Render` behält sein Verhalten.

- [x] **Step 4: GREEN** – Filter wie oben; danach `dotnet test tests/TotalWarModTool.Objects.Tests` ganz (die alten `ObjectTextTests` bleiben grün) und `dotnet build TotalWarModTool.slnx` ohne Warnung.

- [x] **Step 5: Spec** – In `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` unter 5.3 den Unterabschnitt `#### Festlegungen aus Plan 1` anlegen mit: `Render` bleibt für die App bis Plan 3, `RenderBlock` liefert `BlockText` (Zeilenarten, Bereiche, `Text`); Satzzeilen zeigen keine Feldzeilen; Head einer keyless Zeile ist das erste Feld in Schemareihenfolge; Texte von Referenzzielen sind nur Anhängsel; Warnungen sind Anhängsel-Zeilen ohne Text.

- [x] **Step 6: Commit** – `git add src/TotalWarModTool.Objects/BlockText.cs src/TotalWarModTool.Objects/ObjectText.cs src/TotalWarModTool.Objects/ObjectText.Block.cs tests/TotalWarModTool.Objects.Tests/BlockTextTests.cs docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (nur die Dateien, die es gibt), `git commit -m "feat: render the editable block text with trailing annotations"`.

> **Status Task 1: erledigt (2026-10-10).** Objects-Tests 136 grün, 5 übersprungen; Build ohne Warnung. Abweichung vom Plan (Ruling): Satz- und Junction-Zeilen zeigen nicht benannte, nicht leere Felder als Feldzeilen auf Stufe 2, damit der Round-Trip für Tabellen ohne Schlüssel hält (siehe Spec „Festlegungen aus Plan 1“).

---

### Task 2: Parser (`ObjectTextParser`) und Muster-Regel im Kategorie-Loader

Text → `ParsedObject` mit Diagnosen nach Spec 5.2 und 5.3. Tolerant: alle Fehler werden gesammelt.

**Files:**
- Create: `src/TotalWarModTool.Objects/ParsedObject.cs`
- Create: `src/TotalWarModTool.Objects/ObjectTextParser.cs`
- Modify: `src/TotalWarModTool.Objects/CategoryDefinition.cs` (Muster mit zwei Platzhaltern ohne Literal dazwischen ablehnen)
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectTextParserTests.cs`, `tests/TotalWarModTool.Objects.Tests/CategoryDefinitionsTests.cs`, `tests/TotalWarModTool.Objects.Tests/BlockTextTests.cs` (Round-Trip)

**Interfaces:**
- Consumes: `BlockText.Text` (Task 1), `ObjectView` (Baseline: `Id`, `Root`, `Sections`), `CategoryDefinition`, `ReferenceGraph` (`Latest`, `Incoming`, `IsJunction`, `KeyFields`, `ResolveTableName`), `ObjectText.TryParsePattern`, `ObjectText.ShortTableName`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum DiagnosticSeverity { Error, Warning }

public enum DiagnosticKind
{
    UnknownLineForm, HeadChanged, UnknownField, UnknownSection, HiddenSection, FieldWithoutRow, SentenceMismatch, FieldRepeated,
    ControlCharacter, UnterminatedText, RootFieldMissing, EmptyText, KeyRepeated, DuplicateCountExceeded, RootKeyChanged, InvalidValue,
    ReferenceNotFound, TextWithoutOwnerKey,
}

/// <summary>Line is 1-based. Detail is short technical English (the App translates by Kind and shows Detail only in logs).</summary>
public sealed record BlockDiagnostic(int Line, DiagnosticSeverity Severity, DiagnosticKind Kind, string Detail);

public sealed record ParsedField(string Name, string Text, int Line);

public sealed record ParsedText(string Column, string Text, int Line);

/// <summary>Fields hold the key fields and field lines of the row (never the field that points at the root). Texts are its loc lines.</summary>
public sealed record ParsedRow(IReadOnlyList<ParsedField> Fields, IReadOnlyList<ParsedText> Texts, int Line);

/// <summary>FieldName is the field of SourceTable that points at the root (resolved by the parser from the section head and the graph).</summary>
public sealed record ParsedSection(string SourceTable, string FieldName, IReadOnlyList<ParsedRow> Rows, int Line);

public sealed record ParsedObject(ObjectId Id, IReadOnlyList<ParsedField> Root, IReadOnlyList<ParsedText> Texts, IReadOnlyList<ParsedSection> Sections,
    IReadOnlyList<BlockDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

public static class ObjectTextParser
{
    /// <summary>Never throws on text; collects every problem. baseline.Id names the root the head must match. Lines are split at \n (a \r before it is dropped).</summary>
    public static ParsedObject Parse(string text, ObjectView baseline, CategoryDefinition? category, ReferenceGraph graph);
}
```

Festlegungen:

- Einrückung: führende Leerzeichen und Tabs; ein Tab zählt wie zwei Leerzeichen; Stufe = Leerzeichen / 2 abgerundet (3 Leerzeichen = Stufe 1). Leerzeilen (nur Whitespace) sind `Blank`.
- Kopfzeile: erste nicht leere Zeile; sie muss `ShortTableName(baseline.Id.Table)` (ohne Rücksicht auf Schreibweise) plus ein oder mehr Leerzeichen plus `baseline.Id.DisplayKey` (ordinal, Rest der Zeile nach Trim) sein, sonst `HeadChanged` und der Rest wird trotzdem geparst. Fehlt sie ganz (Text leer), `HeadChanged` an Zeile 1.
- Stufe 0 nach der Kopfzeile: Abschnittskopf `name` oder `name.field`. Auflösen: `name` ohne `_tables` wird zu `name + "_tables"`, wenn `graph.Latest` das kennt, sonst `name` selbst; der Abschnitt muss in `graph.Incoming(baseline.Id.Table)` stehen mit `TargetColumn` gleich einem Schlüsselfeld der Wurzel (wie `FindCandidates` in `ObjectBuilder`); mit `.field` genau diese Referenz, ohne Feld die einzige; gibt es mehrere Referenzen der Tabelle ohne Feldangabe, `UnknownSection` mit Detail „field required“. Versteckt in der Kategorie (`Hidden`): `HiddenSection`. Dieselbe `(Table, FieldName)` zweimal im Text: zweiter Kopf `UnknownSection` mit Detail „section repeated“; seine Zeilen gehören zum zweiten Abschnitt und werden in `ObjectDiff` nicht erreicht, weil der Fehler Übernehmen sperrt.
- Stufe 1 ohne vorherigen Abschnitt: Wurzelfeld oder Wurzeltext. Stufe 1 nach einem Abschnittskopf: Zeile des Abschnitts. Stufe 2: Feld oder Text der letzten Zeile des Abschnitts; ohne eine solche Zeile `FieldWithoutRow`. Stufe 3 oder mehr: `UnknownLineForm`. Stufe 1 vor der ersten Stufe-0-Zeile nach der Kopfzeile gilt als Wurzel, auch nach Leerzeilen.
- Feld oder Text: Name ist alles vor dem ersten ` = ` oder `=` (der Name wird getrimmt und darf keine Leerzeichen enthalten); der Wert ist der Rest ab dem ersten Zeichen nach dem `=`, mit genau einem führenden Leerzeichen entfernt, wenn vorhanden, und Leerzeichen am Ende entfernt. Ohne `=`: in einem Satz- oder Junction-Abschnitt eine Zeile (unten), sonst `UnknownLineForm`. Ist der Name ein Feld der Tabelle (`graph.Latest(table).Fields`, ohne Rücksicht auf Schreibweise): `ParsedField` mit dem Namen in Schemaschreibweise. Sonst, wenn der Wert mit `"` beginnt: Loc-Zeile, Wert wird entquotet (`\"`, `\\`, `\n`; endet er nicht mit `"` oder enthält er ein nicht maskiertes `"` im Inneren: `UnterminatedText`); `EmptyText` als Warnung bei leerem Text. Sonst `UnknownField`. Das Feld `section.FieldName` als Feldzeile in einer Abschnittszeile: `UnknownField` mit Detail „points at the root“. Ein Feld zweimal im selben Block: `FieldRepeated`. Tab oder Zeilenumbruch im Wert kann nach dem Split nicht vorkommen; ein wörtliches `\t` oder `\n` in einem Feldwert (zwei Zeichen) ist `ControlCharacter` (Grenze des TSV-Dialekts, Teil 2).
- Listenzeile (Stufe 1 im Abschnitt, Regel `List` oder keine Regel, nicht Junction): die Kopfzeile hält ein oder mehrere `name = wert`, getrennt durch zwei oder mehr Leerzeichen; Split-Regel: der Text wird an jedem Vorkommen von `  ` gefolgt von `name =`, wobei `name` ein Feld der Tabelle ist, geteilt; alles andere bleibt im Wert. Jedes Teil ist eine `ParsedField`.
- Satzzeile (Regel `Sentence`): Muster rückwärts: Literale werden der Reihe nach (ordinal) im Text gesucht, das erste Literal muss am Anfang stehen, das letzte am Ende (oder der Text endet nach dem letzten Platzhalter); Platzhalterwerte sind der Text dazwischen, getrimmt. Passt es nicht: `SentenceMismatch`. Platzhalternamen werden zu `ParsedField`s; ein Platzhalter, der kein Feld der Tabelle ist: `UnknownField`.
- Junction-Zeile (`graph.IsJunction(table)` und keine Satzregel): der ganze getrimmte Text ist der Wert des anderen Referenzfelds (das Referenzfeld der Tabelle, das nicht `FieldName` ist; gibt es keins, `UnknownLineForm`).
- Wurzelfelder, die im Text fehlen (gegen `graph.Latest(baseline.Id.Table).Fields`): `RootFieldMissing` als Warnung an der Kopfzeile, eine pro Feld.
- `ParsedObject.Id = baseline.Id`.
- Kategorie-Loader: ein Muster, in dem zwei Platzhalter direkt aufeinander folgen (`{a}{b}`), ist ungültig: Detail `pattern has two placeholders without text between them.`

- [x] **Step 1: Failing Tests** in `ObjectTextParserTests.cs` (Baseline-View über die Helfer aus `BlockTextTests`; Graph aus `TestSchema.Build` mit `building_levels_tables` (Key `level_name`, Felder `chain` ref `building_chains_tables.key`, `create_cost` I32, `note`), `effects_tables` (`building` ref `building_levels_tables.level_name`, `effect` Key, `scope`, `value` F32), `units_tables` (`building` ref, `unit` ref `units_def_tables.key`, `xp` I32, kein Key), `upgrades_tables` (Junction: `from` ref, `to` ref), Kategorie mit Sentence `allows {unit}` für `units_tables`):

```csharp
[Fact] public void The_rendered_text_parses_without_diagnostics()  // Parse(RenderBlock(view).Text) hat 0 Diagnostics; Root-Felder, Sections und Rows zählen wie die View.
[Fact] public void A_changed_head_is_an_error_and_the_rest_is_still_parsed()  // Zeile 1 "building_levels b2": HeadChanged an Zeile 1, Root-Felder trotzdem geparst.
[Fact] public void Indentation_accepts_tabs_and_odd_spaces()  // "\tcreate_cost = 5" und "   create_cost = 5" sind Stufe 1.
[Fact] public void A_field_value_keeps_inner_spacing_and_drops_trailing_spaces()  // "  note = a = b  c   " ergibt Text "a = b  c".
[Fact] public void An_unknown_field_is_an_error_with_its_line()  // "  colour = red" → UnknownField, Line 3.
[Fact] public void A_quoted_unknown_name_is_a_loc_line()  // "  onscreen_name = \"Barracks\"" → ParsedText("onscreen_name", "Barracks").
[Fact] public void A_loc_text_with_quotes_and_backslashes_round_trips()  // Text 'He said "hi" \ x' → QuoteText → Parse → gleicher Text; "\n" wird zu einem Zeilenumbruch.
[Fact] public void An_unterminated_text_is_an_error()  // "  name = \"abc" → UnterminatedText.
[Fact] public void A_value_with_a_tab_is_an_error()  // "  note = a\\tb" (wörtlich Backslash t) → ControlCharacter.
[Fact] public void A_section_head_resolves_to_table_and_field()  // "effects" → SourceTable effects_tables, FieldName building; "upgrades.to" → upgrades_tables/to.
[Fact] public void A_section_of_a_table_with_two_references_needs_the_field()  // "upgrades" allein → UnknownSection mit Detail "field required".
[Fact] public void An_unknown_or_hidden_section_is_an_error()  // "cultures" (nicht im Graph auf die Wurzel) → UnknownSection; versteckte Tabelle → HiddenSection.
[Fact] public void A_section_that_the_baseline_does_not_have_is_allowed()  // Baseline ohne effects-Abschnitt, Text mit "effects" und einer Zeile → Section geparst, keine Diagnose.
[Fact] public void A_list_row_head_splits_at_double_spaces_before_field_names()  // "  effect = e1  scope = x = y" → Fields effect=e1, scope="x = y".
[Fact] public void Field_lines_belong_to_the_last_row_and_repeats_are_errors()  // "  effect = e1\n    value = 1\n    value = 2" → FieldRepeated an Zeile 5; "    value = 1" direkt nach dem Abschnittskopf → FieldWithoutRow.
[Fact] public void The_root_pointing_field_is_not_accepted_as_a_field_line()  // "    building = b1" unter einer Zeile → UnknownField Detail enthält "root".
[Fact] public void A_sentence_row_is_matched_against_the_pattern()  // "  allows u1" → Fields unit=u1; "  forbids u1" → SentenceMismatch; "  allows u1\n    xp = 2" → xp als Feld der Zeile.
[Fact] public void A_junction_row_takes_the_whole_line_as_the_other_end()  // "  t1" im Abschnitt upgrades.from → Field to=t1.
[Fact] public void Missing_root_fields_are_warnings()  // Text ohne note → RootFieldMissing Warning, HasErrors false.
[Fact] public void Lines_with_three_levels_are_errors()  // "      x = 1" → UnknownLineForm.
[Fact] public void Every_problem_is_collected()  // Text mit drei Fehlern → drei Diagnostics mit aufsteigender Line.
```

In `CategoryDefinitionsTests`: `[InlineData("adjacent.json", "... \"pattern\": \"{a}{b}\" ...")]` zur bestehenden Theory der ungültigen Dateien, und ein Test `Adjacent_placeholders_are_rejected_with_a_detail`, der das Detail prüft.

In `BlockTextTests`: `[Fact] public void Render_then_parse_gives_no_diagnostics_for_every_kind_of_row()` – eine View mit Listenzeile (zwei Schlüssel), keyless Zeile, Satzzeile, Junction-Zeile, Wurzeltext, Zeilentext, leerem Wurzelfeld, Wert mit ` = `: `Parse(RenderBlock(view).Text, view, category, graph).Diagnostics` ist leer. (Die Differenz-Freiheit prüft Task 3.)

- [x] **Step 2: RED** – Filter `FullyQualifiedName~ObjectTextParserTests` baut nicht.
- [x] **Step 3: Implementieren** – Zeilenweise Zustandsmaschine (Zustand: vor Kopf, Wurzel, Abschnitt mit aktueller Zeile). Muster rückwärts in einer privaten Funktion `TryMatchPattern(parts, text, out values)`. Der Loader prüft nach `TryParsePattern`, ob zwei `IsPlaceholder`-Teile aufeinander folgen.
- [x] **Step 4: GREEN** – `dotnet test tests/TotalWarModTool.Objects.Tests`, `dotnet build TotalWarModTool.slnx`.
- [x] **Step 5: Commit** – `git add` der vier Dateien plus Tests, `git commit -m "feat: parse the block text into a parsed object with diagnostics"`.

> **Status Task 2: erledigt (2026-10-10).** Objects-Tests 184 grün, 5 übersprungen; Build ohne Warnung. Test-Fixture `BlockFixture.cs` (Schema, Graph, Kategorie, Views) für Task 3 und 4. Risiko (Ruling, siehe Spec „Festlegungen aus Plan 1“): ein wörtliches `\t` oder `\n` im Wert ist `ControlCharacter`; Task 6 misst, ob echte Werte es enthalten.

---

### Task 3: Differenz (`ObjectDiff`)

Baseline plus `ParsedObject` → `ObjectChanges` nach Spec 5.3 (Identität, doppelte Schlüssel, fehlende Felder bleiben, Typprüfung, Referenzwarnung, Baseline ohne Wurzel).

**Files:**
- Create: `src/TotalWarModTool.Objects/ObjectChanges.cs`
- Create: `src/TotalWarModTool.Objects/ObjectDiff.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectDiffTests.cs`, `tests/TotalWarModTool.Objects.Tests/BlockTextTests.cs` (Round-Trip ohne Differenz)

**Interfaces:**
- Consumes: `ParsedObject` (Task 2), `ObjectView`, `RowProvenance` (ab Task 5 mit `OverriddenEntries`; bis dahin leer), `ReferenceGraph`, `CellText.TryParse`, `CellText.DefaultValue`, `CellText.Format` (`Tables`).
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum RowChangeKind { Set, Insert, Delete }

/// <summary>Table with suffix. RootField is the field that points at the root and RootValue the root's key value for it (null for the root row itself). Cells hold every field of the table in schema order as text: for Set the baseline values with the changed ones replaced, for Insert the parsed values with defaults for the rest, for Delete the baseline values. ChangedFields names the fields that differ (Set only). Provenance is the baseline row's (Set, Delete) or null (Insert). Line is the text line of the row head.</summary>
public sealed record RowChange(RowChangeKind Kind, string Table, string? RootField, string? RootValue, IReadOnlyList<string> Cells,
    IReadOnlyList<string> ChangedFields, RowProvenance? Provenance, int Line);

public enum TextChangeKind { Set, Insert }

/// <summary>Key is the full loc key (table_column_ownerKey). Source is the baseline entry's source (Set) or null (Insert).</summary>
public sealed record TextChange(TextChangeKind Kind, string Key, string Text, TableSource? Source, int Line);

public sealed record ObjectChanges(IReadOnlyList<RowChange> Rows, IReadOnlyList<TextChange> Texts, IReadOnlyList<BlockDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public bool IsEmpty => Rows.Count == 0 && Texts.Count == 0;
}

/// <summary>Answers whether a value exists in the target column of a table over the load order; null means "do not check".</summary>
public delegate bool ReferenceExists(string table, string column, string value);

public static class ObjectDiff
{
    /// <summary>Pure. The diagnostics of the parsed object are carried over first, then the diff's own. With errors in the parsed object the diff is still computed where possible (so the App can show both), but callers must not apply a result with HasErrors.</summary>
    public static ObjectChanges Compute(ObjectView baseline, ParsedObject parsed, ReferenceGraph graph, ReferenceExists? referenceExists);
}
```

Festlegungen:

- Wurzel: `baseline.Root == null` → `Insert` der Wurzelzeile mit `RootField == null`; fehlende Felder bekommen `CellText.Format(type, CellText.DefaultValue(type))`; die Schlüsselfelder müssen `baseline.Id.KeyValues` gleichen, sonst `RootKeyChanged`. Mit Wurzel: `Set` nur, wenn mindestens ein Feld abweicht (ordinaler Textvergleich nach `CellText.Format` der Baseline); ein geändertes Schlüsselfeld der Wurzel ist `RootKeyChanged` (Error), das Feld zählt nicht als Änderung.
- Typprüfung jedes geparsten Feldwerts mit `CellText.TryParse(type, text, …)`: Fehlschlag → `InvalidValue` (Error, Detail aus `reason`), die Zeile bleibt in `Rows` mit dem Text. Der Vergleich mit der Baseline läuft über den normalisierten Text `CellText.Format(type, parsed)`; so ist `10` gleich `10.0` bei F32 (eine Änderung nur, wenn der Wert sich ändert).
- Abschnittszeilen: Identität pro Abschnitt `(Table, FieldName)`:
  - Tabelle mit Schlüsselfeldern: Schlüssel = Werte der Schlüsselfelder ohne `FieldName` (das ist bei allen gleich). Fehlt ein Schlüsselfeld im Text (keine `ParsedField`), ist das `UnknownLineForm` mit Detail „key field X missing“ (Error). Baseline-Zeilen mit `Duplicate`: Gruppe pro Schlüssel mit Ordnungszahl in `Rows`-Reihenfolge; geparste Zeilen desselben Schlüssels bekommen Ordnungszahlen in Textreihenfolge; mehr geparste als Baseline-Zeilen → `DuplicateCountExceeded` (Error) an der überzähligen Zeile; ein Schlüssel ohne Duplikate in der Baseline, zweimal im Text → `KeyRepeated` (Error) an der zweiten.
  - Tabelle ohne Schlüsselfelder: Identität = alle Felder (Text, nach Normalisierung) mit den Baseline-Werten; jede geparste Zeile, die keinen gleichen Baseline-Feldsatz hat (jede Baseline-Zeile höchstens einmal zugeordnet, in Reihenfolge), ist `Insert`; jede nicht zugeordnete Baseline-Zeile `Delete`. Fehlende Felder vor dem Vergleich: Standardwert (bei keyless gibt es keine „alte Zeile“, deren Werte bleiben könnten).
  - `Set`: ein Feld, das im Text fehlt, behält den Baseline-Wert; ein Feld mit gleichem normalisierten Text zählt nicht als geändert; keine Änderung → keine `RowChange`.
  - `Delete`: Baseline-Zeile ohne geparste Zeile; `Provenance` der Baseline (mit `Overridden` und `OverriddenEntries`).
  - `Insert`: `RootField = FieldName`, `RootValue` = der Schlüsselwert der Wurzel, auf den die Referenz zeigt (`TargetColumn` der `IncomingReference`, Wert aus `baseline.Id.KeyValues` an der Position des Schlüsselfelds), `Cells` in Schemareihenfolge mit `RootValue` im Feld `FieldName`.
- Abschnitte des Textes, die die Baseline nicht hat: alle Zeilen `Insert`. Abschnitte der Baseline, die im Text fehlen (ganzer Abschnitt gelöscht): alle Zeilen `Delete`; versteckte Abschnitte der Kategorie zählen nicht.
- Texte: Wurzeltexte `column` → Schlüssel `ShortTableName(root table) + "_" + column + "_" + baseline.Id.JoinedKey`; Zeilentexte → mit dem `JoinedKey` der Zeile: bei `Set`/unverändert aus `row.RowId`, bei `Insert` aus den Schlüsselwerten der geparsten Zeile in Schemareihenfolge (`RootValue` eingeschlossen). Für eine Zeile ohne `RowId` (keyless Tabelle) ist ein Text `TextWithoutOwnerKey` (Error). Vergleich mit `baseline.Texts` nach Schlüssel (ordinal): gleich → nichts; anders → `Set` mit `Source`; nicht vorhanden → `Insert`. Texte der Baseline, die im Text fehlen, bleiben (keine Änderung).
- Referenzen: für jedes geparste Feld mit `Reference` (aus `graph.Latest(table).Fields`) und nicht leerem Wert, dessen Wert sich gegenüber der Baseline-Zelle ändert oder dessen Zeile `Insert` ist: `referenceExists(ResolveTableName(ref.Table), ref.Column, value)` false → `ReferenceNotFound` (Warning). `null` als Delegat: keine Prüfung.
- Reihenfolge von `Rows`: Wurzel zuerst, dann Abschnitte in Textreihenfolge, darin Zeilen in Textreihenfolge, `Delete`s eines Abschnitts nach dessen anderen Änderungen in Baseline-Reihenfolge.

- [x] **Step 1: Failing Tests** in `ObjectDiffTests.cs` (Baseline-Views wie in Task 2, `Parse` aus Task 2 für die Texte, damit die Tests die reale Kette prüfen):

```csharp
[Fact] public void An_unchanged_text_gives_no_changes()  // Parse(RenderBlock(view).Text) → IsEmpty, keine Diagnostics.
[Fact] public void A_changed_root_field_is_a_set_with_the_full_row()  // create_cost 100 → 90: ein Set, Table building_levels_tables, RootField null, ChangedFields ["create_cost"], Cells in Schemareihenfolge mit 90, Provenance der Wurzel.
[Fact] public void A_missing_root_field_keeps_its_value()  // Text ohne note: kein Set, Warning RootFieldMissing.
[Fact] public void A_changed_root_key_is_an_error()  // level_name = b2 → RootKeyChanged, kein Set.
[Fact] public void A_baseline_without_root_makes_the_root_an_insert()  // Root null, Text mit Feldern → Insert mit Defaults für fehlende Felder (note "", create_cost "0").
[Fact] public void A_new_list_row_is_an_insert_with_the_root_value()  // Abschnitt effects, Zeile effect = e2 / value = 3 → Insert, RootField building, RootValue b1, Cells [b1, e2, "", "3"] (scope leer), Line der Kopfzeile.
[Fact] public void A_removed_row_is_a_delete_with_its_provenance()  // Zeile e1 fehlt → Delete mit derselben Provenance-Instanz wie die Baseline-Zeile (Source Vanilla, EntryPath, Overridden); die Eintragspfade der überschriebenen Schichten liefert Task 5.
[Fact] public void A_changed_key_cell_is_a_delete_and_an_insert()  // e1 → e9 in der Kopfzeile → Delete e1, Insert e9.
[Fact] public void Rows_without_key_are_matched_by_their_fields()  // units_tables: "allows u1" bleibt, "allows u2" neu, Baseline u3 fehlt → Insert u2 (xp Default 0), Delete u3, nichts für u1.
[Fact] public void Duplicate_keys_of_the_baseline_are_matched_by_position()  // zwei Baseline-Zeilen e1 (Duplicate), Text ändert value der zweiten → genau ein Set mit der Provenance der zweiten.
[Fact] public void One_more_duplicate_than_the_baseline_is_an_error()  // dritte e1-Zeile → DuplicateCountExceeded.
[Fact] public void A_repeated_key_without_baseline_duplicates_is_an_error()  // e2 zweimal, Baseline hat e2 einmal oder gar nicht → KeyRepeated.
[Fact] public void A_whole_section_missing_from_the_text_deletes_its_rows()  // Text ohne "effects" → ein Delete pro Zeile; versteckte Abschnitte lösen nichts aus.
[Fact] public void A_section_the_baseline_lacks_inserts_every_row()
[Fact] public void Values_are_compared_after_normalisation()  // value "10" gegen Baseline F32 10 → keine Änderung; "abc" in value → InvalidValue, Row bleibt mit Text.
[Fact] public void A_changed_root_text_is_a_set_and_a_new_one_an_insert()  // onscreen_name "Barracks" → "Fort": Set Key building_levels_onscreen_name_b1 mit Source; neue Spalte short_name → Insert.
[Fact] public void A_row_text_uses_the_joined_key_of_the_row()  // description der Zeile e1 geändert → Set Key effects_description_b1e1 (JoinedKey = building + effect in Schemareihenfolge).
[Fact] public void A_text_on_a_keyless_row_is_an_error()  // TextWithoutOwnerKey.
[Fact] public void A_reference_that_does_not_exist_is_a_warning()  // chain = zzz, Delegat liefert false für zzz → ReferenceNotFound Warning; unveränderte Felder werden nicht gefragt (Delegat zählt Aufrufe).
[Fact] public void Without_a_delegate_references_are_not_checked()
[Fact] public void Changes_are_ordered_root_then_sections_in_text_order()
```

In `BlockTextTests`: `[Fact] public void Render_then_parse_then_diff_is_empty_for_every_kind_of_row()` – dieselbe View wie in Task 2, `ObjectDiff.Compute(view, parsed, graph, null).IsEmpty`.

- [x] **Step 2: RED**, **Step 3: Implementieren** (eine Datei, private Hilfsklasse `RowMatcher` für Schlüssel, Ordnungszahlen und keyless Zuordnung), **Step 4: GREEN** (`dotnet test tests/TotalWarModTool.Objects.Tests`, `dotnet build TotalWarModTool.slnx`).
- [x] **Step 5: Commit** – `git commit -m "feat: compute row and text changes between the parsed block and the built object"`.

> **Status Task 3: erledigt (2026-10-10).** Objects-Tests 215 grün, 5 übersprungen; Build ohne Warnung. Abweichungen (Rulings, siehe Spec „Festlegungen aus Plan 1“): `Compute(baseline, parsed, category, graph, referenceExists)` nimmt die Kategorie (versteckte Abschnitte nie löschen); Typprüfung über `CellText.TryParse` statt `CellCheck`; `TextChange` ohne `EntryPath`; Tabellen, deren einziger Schlüssel das Feld auf die Wurzel ist, werden nach Reihenfolge zugeordnet.

---

### Task 4: Kopie (`ObjectCopy`)

„Kopieren als“ nach Spec 5.8: Kopierregel 1 bis 4, Unterobjekte über Präfix, Tiefe 2, Hinweise.

**Files:**
- Create: `src/TotalWarModTool.Objects/ObjectCopy.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectCopyTests.cs`

**Interfaces:**
- Consumes: `ObjectView`, `ParsedObject`, `ParsedSection`, `ParsedRow`, `ParsedField`, `ParsedText` (Task 2), `CategoryDefinition` (versteckte Abschnitte), `ReferenceGraph` (`IsJunction`, `KeyFields`, `Latest`).
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum CopyNoteKind { ForeignKeyRows, DepthExceeded, HiddenSection }

/// <summary>Table with suffix; Count the rows concerned.</summary>
public sealed record CopyNote(CopyNoteKind Kind, string Table, int Count);

/// <summary>Objects in write order: the copied root first, then its sub-objects. Every ParsedObject has no diagnostics and a baseline-free id (its rows are all inserts against a view without root).</summary>
public sealed record CopyResult(IReadOnlyList<ParsedObject> Objects, IReadOnlyList<CopyNote> Notes);

public static class ObjectCopy
{
    /// <summary>newId has the table of source.Id and the same number of key values. build gives the view of a sub-object (called for prefix rows only); category hides sections of the root and of sub-objects alike (looked up by table). Pure apart from build.</summary>
    public static CopyResult Rename(ObjectView source, ObjectId newId, Func<ObjectId, ObjectView> build, Func<string, CategoryDefinition?> categoryOf, ReferenceGraph graph);
}
```

Festlegungen:

- Wurzel: `ParsedObject.Id = newId`; `Root`-Felder aus `source.Root.Fields` (Text), die Schlüsselfelder mit den Werten von `newId` ersetzt (Position für Position). `Texts`: alle `LocEntry` mit `Owner.Equals(source.Id)` als `ParsedText(column, text)` (der neue Schlüssel entsteht in `ObjectDiff` aus `newId`). Quelle ohne Wurzel (`Root == null`): `ArgumentException`.
- Pro Abschnitt (`source.Sections`, versteckte nach `categoryOf(source.Id.Table)`: `CopyNote(HiddenSection, table, rows)` und nichts kopiert):
  - Regel 2 (Junction, oder die Tabelle hat Schlüsselfelder und `FieldName` ist eines davon): jede Zeile als `ParsedRow` mit allen Feldern außer `FieldName` (Text wie in der View; Schlüsselfelder zuerst, dann die übrigen, leere Werte mit `Text ""` eingeschlossen, damit nichts auf Defaults fällt) und ihren Texten (`LocEntry` mit `Owner.Equals(row.RowId)`); enthält ein anderes Schlüsselfeld den alten Wurzelschlüssel (Wert gleich `source.Id.KeyValues[k]` für ein k), wird er durch den neuen ersetzt (die Loc-Schlüssel folgen über `JoinedKey`).
  - Regel 3 (nicht Regel 2, genau ein Schlüsselfeld, dessen Wert mit `source.Id.KeyValues[0] + "_"` beginnt; nur bei Wurzeln mit einem Schlüsselwert): die Zeile wird **nicht** in den Abschnitt der Wurzel geschrieben (das Feld `FieldName` zeigt zwar auf die Wurzel, aber die Zeile ist ein eigenes Objekt). Stattdessen: `subView = build(row.RowId)`, `subId = new ObjectId(table, [newId.KeyValues[0] + rest])`, und `Rename(subView, subId, …)` rekursiv mit Tiefe 1; das Ergebnis des Unterobjekts hängt an `Objects`; in dessen Wurzelfeldern wird zusätzlich das Feld `FieldName` auf `newId.KeyValues[0]` gesetzt. Bei Tiefe 1 trifft Regel 3 nicht mehr: `CopyNote(DepthExceeded, table, rows)`.
  - Regel 4 (sonst): `CopyNote(ForeignKeyRows, table, rows)`.
- `Diagnostics` der erzeugten `ParsedObject`s leer; `Line` der Felder und Zeilen 0.
- Ein `ParsedSection` pro kopiertem Abschnitt mit `(SourceTable, FieldName)` der Quelle.

- [x] **Step 1: Failing Tests** (Schema wie Task 2 plus `traits_tables` (Key `key`), `trait_levels_tables` (Key `key`, `trait` ref `traits_tables.key`, `rank` I32), `level_effects_tables` (`level` ref `trait_levels_tables.key`, `effect` Key), `level_notes_tables` (Key `key`, `level` ref `trait_levels_tables.key`), `technologies_tables` (Key `key`, `building` ref `building_levels_tables.level_name`)):

```csharp
[Fact] public void The_root_gets_the_new_key_and_its_texts()  // b1 → b9: Root level_name = b9, Texts onscreen_name Barracks; chain unverändert.
[Fact] public void Junction_rows_and_rows_keyed_by_the_root_are_copied_without_the_root_field()  // effects (Key building+effect) und upgrades: Rows ohne building/from, Felder vollständig inkl. leerer, Texte der Zeile dabei.
[Fact] public void A_composite_key_containing_the_root_key_is_replaced()  // Tabelle mit Keys (building, culture, other): culture = "emp" bleibt; other = "b1" (gleich dem Wurzelschlüssel) wird "b9"; other = "b1_x" bleibt (kein exakter Treffer).
[Fact] public void Foreign_key_rows_are_reported_not_copied()  // technologies mit building = b1: kein Abschnitt, Note ForeignKeyRows technologies_tables 3.
[Fact] public void Hidden_sections_are_reported_not_copied()
[Fact] public void Prefix_rows_are_copied_as_sub_objects_with_their_own_sections()  // Trait t1 mit Stufen t1_1, t1_2; build liefert Views mit level_effects: Objects == [t9, t9_1, t9_2]; t9_1.Root hat key t9_1 und trait t9; t9_1.Sections enthält level_effects mit den Effekten; build wurde genau zweimal gerufen.
[Fact] public void A_third_level_is_reported_not_copied()  // level_notes mit key t1_1_a unter der Stufe t1_1 → Note DepthExceeded level_notes_tables 1, nicht in Objects.
[Fact] public void A_source_without_root_is_rejected()  // ArgumentException.
[Fact] public void The_copy_parses_and_diffs_as_inserts_against_an_empty_baseline()  // ObjectDiff.Compute(viewWithoutRoot(newId), copy.Objects[0], graph, null): Root Insert plus ein Insert pro Zeile, keine Diagnostics. (viewWithoutRoot = new ObjectView(newId, null, [], [], [], order, []).)
```

- [x] **Step 2: RED**, **Step 3: Implementieren**, **Step 4: GREEN** (`dotnet test tests/TotalWarModTool.Objects.Tests`, `dotnet build TotalWarModTool.slnx`).
- [x] **Step 5: Commit** – `git commit -m "feat: rename an object and its sub-objects for copy-as"`.

> **Status Task 4: erledigt (2026-10-10).** Objects-Tests 225 grün, 5 übersprungen; Build ohne Warnung. Abweichungen (Rulings, Spec „Festlegungen aus Plan 1“): `HiddenSection`-Hinweis wird geliefert, die App zeigt ihn nicht (Spec 5.8 Regel 1); Zeilen schlüsselloser Nicht-Junction-Tabellen werden wie Regel 2 kopiert; Tests nutzen `bonus_tables` (Schlüssel Gebäude+Kultur+other) statt „effects mit Schlüssel building+effect“. Gemeinsamer Helfer `ObjectText.RuleFor` (Parser, Differenz, Kopie).

---

### Task 5: Dateiersetzung und überschriebene Eintragspfade (`TableStore`, `LocStore`, `RowProvenance`)

Spec 5.5, zweiter Absatz: Eine Datei einer späteren Quelle mit demselben Pack-Pfad ersetzt die frühere Datei ganz. Dazu die Eintragspfade der überschriebenen Schichten für `Delete` (Review Focus 3).

**Files:**
- Modify: `src/TotalWarModTool.Objects/MergedTable.cs` (`RowProvenance`)
- Modify: `src/TotalWarModTool.Objects/TableStore.cs` (`Build`, `AddRow`, `KeyState`)
- Modify: `src/TotalWarModTool.Objects/LocStore.cs` (`Get`)
- Modify: `src/TotalWarModTool.Objects/SourceReader.cs` (Pack-Pfad eines Eintrags)
- Test: `tests/TotalWarModTool.Objects.Tests/TableStoreTests.cs`, `tests/TotalWarModTool.Objects.Tests/LocStoreTests.cs`

**Interfaces:**
- Consumes: `EntryRef.EntryPath` (Pack `db/<table>/<name>`, `text/<x>.loc`; Projekt `pack\db\<table>\<name>.tsv`, `pack\<x>.loc.tsv`), `ProjectLayout.FromRelativeFile(relativeFile)` aus `Projects` (liefert `PackPath` für einen Pfad unter `pack\`).
- Produces:

```csharp
// MergedTable.cs
/// <summary>OverriddenEntries holds the entry path of every source in Overridden, same order and count; empty for rows nothing overrides.</summary>
public sealed record RowProvenance(TableSource Source, IReadOnlyList<TableSource> Overridden, bool OlderLayout, bool Duplicate, string EntryPath = "",
    IReadOnlyList<string>? OverriddenEntries = null)
{
    public IReadOnlyList<string> OverriddenEntries { get; init; } = OverriddenEntries ?? [];
}

// SourceReader.cs (internal)
/// <summary>The pack path of an entry as the game sees it: pack entries as is; project files relative to pack\ through ProjectLayout, so db\x_tables\a.tsv gives db/x_tables/a and a.loc.tsv gives text/db/a.loc. Compared ordinal ignore case.</summary>
internal static string PackPathOf(EntryRef entry);
```

Festlegungen:

- `TableStore.Build`: vor dem Lesen die Einträge aller Listings durchgehen; ein Eintrag wird übersprungen (nicht gelesen, nicht gemerged, kein Problem), wenn ein Listing einer **späteren** Quelle einen Eintrag mit gleichem `PackPathOf` hat. Innerhalb derselben Quelle gibt es keine doppelten Pfade (Pack-Index und Dateisystem). Die Signatur (`Token` pro Quelle) bleibt, wie sie ist: Sie ändert sich mit der neuen Datei ohnehin.
- `LocStore.Get`: gleiche Regel für Loc-Einträge.
- `RowProvenance.OverriddenEntries`: `KeyState` bekommt neben `History` eine Liste der Eintragspfade; `AddRow` füllt beide.
- `ProjectLayout.FromRelativeFile` erwartet den Pfad relativ zu `pack\`; `EntryPath` eines Projekteintrags beginnt mit `pack\`, also davor abschneiden. Pfadtrenner vor dem Vergleich auf `/` normalisieren.

- [ ] **Step 1: Failing Tests** in `TableStoreTests` (Temp-Ordner, `TestSources`):

```csharp
[Fact] public void A_project_file_with_the_same_pack_path_replaces_the_vanilla_file()  // Vanilla data__ mit u1,u2; Projekt Data__.tsv mit u2 (geändert): Rows nur u2 aus Projekt, Overridden leer (die Vanilla-Datei ist weg, nicht überschrieben), u1 fehlt.
[Fact] public void A_mod_file_replaces_the_vanilla_file_but_not_its_siblings()  // Vanilla-Pack mit db/x_tables/data__ (u1) und db/x_tables/extra (u3) (PackBuilder direkt, zwei Einträge); Mod-Pack mit db/x_tables/data__ (u2): Rows u2 (Mod) und u3 (Vanilla), kein u1.
[Fact] public void A_later_project_row_still_overrides_rows_of_other_vanilla_files()  // Vanilla data__ (u1), Projekt a.tsv (u1 geändert): wie heute Set mit Overridden [Vanilla].
[Fact] public void Overridden_layers_keep_their_entry_paths()  // Vanilla data__ u1, Mod data_mod u1, Projekt a.tsv u1: Overridden [Vanilla, Mod], OverriddenEntries ["db/x_tables/data__", "db/x_tables/data_mod"].
[Fact] public void The_signature_changes_when_a_replacing_file_appears()
```

In `LocStoreTests`: `[Fact] public void A_project_loc_file_replaces_the_pack_loc_file()` – Pack `text/db/data__.loc` mit k1, k2; Projekt `data__.loc.tsv` mit k2: `TryGet(k1)` false, k2 aus Projekt. Dazu ein Test, dass `PackPathOf` für `pack\sub\a.loc.tsv` `text/db/a.loc` liefert (über `ProjectLayout`; prüfen, was `FromRelativeFile` für Unterordner liefert, und das Ergebnis im Test festhalten).

- [ ] **Step 2: RED**, **Step 3: Implementieren**, **Step 4: GREEN** – `dotnet test tests/TotalWarModTool.Objects.Tests`, dann `dotnet build TotalWarModTool.slnx` (App baut mit dem neuen optionalen Parameter).
- [ ] **Step 5: Commit** – `git commit -m "feat: let a later file with the same pack path replace the earlier one and keep overridden entry paths"`.

---

### Task 6: Echtdaten-Tests und Messung (Annahmen 2, 3, 4)

**Files:**
- Modify: `tests/TotalWarModTool.Objects.Tests/ObjectsRealDataTests.cs`
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Abschnitt 10, Ergebnisse eintragen; der Controller trägt die Zahlen nach dem Echtdaten-Lauf ein, der Implementer legt die Zeilen mit „offen“ an)

**Interfaces:** Consumes alles aus Task 1 bis 5, `RealData`, `RealDataFact`, `CategoryDefinitions.LoadAll(profiles/warhammer_3/objects)`.

- [ ] **Step 1: Tests** (keine RED-Phase nötig, sie prüfen Invarianten auf Echtdaten; sie werden ohne `TWMT_WH3_DIR` übersprungen):

```csharp
[RealDataFact] public void The_barracks_block_round_trips_without_changes()  // RenderBlock → Parse → Compute: keine Diagnostics, IsEmpty; Ausgabe: Zeilenzahl, Parse-Dauer (Stopwatch), Assert Parse unter 200 ms (Annahme 2, Grenze großzügig; die Spec nennt 50 ms als Ziel, die Ausgabe zeigt den Wert).
[RealDataFact] public void The_empire_block_round_trips_and_parses_fast()  // wh_main_emp_empire (factions_tables): wie oben, Ausgabe der Dauer.
[RealDataFact] public void A_trait_block_round_trips()  // erster Trait aus character_traits_tables mit mindestens einer Stufe.
[RealDataFact] public void Copying_the_barracks_follows_the_copy_rule()  // ObjectCopy.Rename(barracks, "twmt_test_barracks"): Objects.Count == 1; Sections enthalten building_culture_variants_tables und building_effects_junction_tables; Notes enthalten ForeignKeyRows für technologies_tables; nichts aus cai_construction_system_synergies_tables (HiddenSection-Note). Dann ObjectDiff gegen leere Baseline: nur Inserts, keine Errors. Ausgabe: Zeilen pro Tabelle.
[RealDataFact] public void Copying_a_trait_copies_its_levels_as_sub_objects()  // ein Trait mit Stufen: Objects.Count == 1 + Stufen, jedes Unterobjekt hat einen Abschnitt trait_level_effects_tables, wenn die Stufe Effekte hat.
[RealDataFact] public void No_value_ends_with_whitespace_and_texts_need_only_the_known_escapes()  // über alle Tabellen der Ladereihenfolge (wie der 4.1-Test über alle Tabellen): zählt Zellen, deren Text mit Leerzeichen oder Tab endet, und Loc-Texte mit \t; Ausgabe der Zahlen und Beispiele (bis 5); Assert: Zellen mit Leerzeichen am Ende == 0 (Annahme 4). Scheitert das, meldet der Implementer die Zahlen; die Regel in Spec 5.2 Regel 2 wird dann in Plan 1 nachgezogen (Werte mit Leerzeichen am Ende müssten in Anführungszeichen), nicht still angepasst.
```

- [ ] **Step 2: Lauf ohne Variable** – `dotnet test tests/TotalWarModTool.Objects.Tests`: die neuen Tests sind übersprungen, alles andere grün. Der Implementer setzt `TWMT_WH3_DIR` nicht.
- [ ] **Step 3: Spec** – Abschnitt 10: Annahmen 2, 3, 4 bekommen in der Spalte „Prüfung“ den Testnamen; Ergebnis „offen (Echtdaten-Lauf des Controllers)“.
- [ ] **Step 4: Commit** – `git commit -m "test: check the block round-trip, copy rule and value invariants on real data"`.

Der Controller führt danach den Echtdaten-Lauf aus (`TWMT_WH3_DIR` gesetzt, nur `tests/TotalWarModTool.Objects.Tests`) und trägt die Messwerte in Spec Abschnitt 10 ein (eigener Docs-Commit).

---

## Manuelle Abnahme durch den Nutzer

Plan 1 ändert nichts Sichtbares in der App. Abnahme ist der grüne volle Lauf plus der Echtdaten-Lauf mit den Messwerten in Spec Abschnitt 10. Der Nutzer prüft die Zahlen: Parse-Dauer der Fraktion (Ziel unter 50 ms, Grenze 200 ms) und die Kopie der Kaserne (welche Tabellen kopiert, welche gemeldet).
