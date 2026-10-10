# Teil 4.2a Plan 4: Kopieren als, Neues Gebäude, Neuer Trait – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** „Kopieren als“ im Objekt-Tab legt ein Objekt unter neuem Schlüssel als Projektzeilen an (Wurzel, eigene Abschnittszeilen, Unterobjekte, Spieltexte), in einem Undo-Schritt. Die Assistenten „Neues Gebäude“ (Volk, Vorlage, Schlüssel, neue Kette oder Stufe in bestehender Kette) und „Neuer Trait“ (Vorlage, Schlüssel) im Menü Werkzeuge sind dünne Hüllen darum.

**Architecture:** Die Fachlogik liegt in `Objects` und ist rein: Kopierregel mit `"copy": "owned"` und neuen Zahlschlüsseln (`ObjectCopy`, `RandomIntegerKeys`), die Änderungen einer Kopie und ihre Schlüsselkollisionen (`ObjectCopy.Changes`, `ObjectCopy.TakenKeys`), die Assistenten-Datei des Spielprofils (`WizardDefinitionLoader`) und die Assistenten-Regeln (`BuildingWizard`, `TraitWizard`). Die App nutzt die Schreibkette des Übernehmens: `ObjectApplyController` bekommt einen gemeinsamen Schreibpfad (Fragen, zweiter Plan, Raster-Sperre, Lauf B, Schritt, Herkunft) und einen Kopierpfad (Lauf A für Kopien). `ObjectWizardController` lädt die Vorlagen über den Runner, zeigt die Dialoge und öffnet das neue Objekt in einem neuen Tab.

**Tech Stack:** C# 12, .NET 8, WPF, AvalonEdit (vorhanden), xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md`: 2.1 Ziel 6, 2.3 (Erfolgskriterium „Neues Gebäude“), 4, 5.4, 5.8, 5.9, 7 (Zeilen „Kein aktives Projekt“, „Neuer Schlüssel beim Kopieren schon vorhanden“, „Vorlage ohne Kette“), 8, 10 (Annahmen 1 und 3, **Ergebnis des Spikes vor Plan 4**), 11 (**Entscheidungen vor Plan 4**). Vorgänger: `docs/plans/2026-10-10-teil-4-2a-plan-3a-app-uebernehmen.md` (Übernehmen, Fakes, Testregel). Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-4-2a-copy-wizards` von `master` (Stand nach dem Commit dieses Plans). Vor Task 1 die Baseline festhalten: `build.bat` ohne Warnung, `dotnet test tests/TotalWarModTool.Objects.Tests` und `dotnet test tests/TotalWarModTool.Editing.Tests` grün; Zahlen in den Task-1-Commit-Text.

**Testregel** (mit dem Nutzer vereinbart): Subagenten starten nie `dotnet test TotalWarModTool.slnx` und nie das ganze App-Testprojekt. Library-Tests sind frei (`dotnet test tests/<Projekt>.Tests`). Einzelne Testklassen der App sind erlaubt (`dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~<Klasse>"`), immer nur ein Lauf zur Zeit. Es arbeitet immer nur ein bauender Subagent; Reviewer bauen und testen nicht. Echtdaten-Tests laufen mit `TWMT_WH3_DIR=D:\SteamLibrary\steamapps\common\Total War WARHAMMER III` und sonst übersprungen. Den vollen Lauf macht der Controller einmal am Ende, nach dem Okay des Nutzers.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch. Spec, Backlog, Changelog Deutsch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors`, Nullable und `ImplicitUsings` sind an.
- Keine neue Projektreferenz. `Objects` bekommt die Logik, `Editing`, `Projects`, `UndoRedo`, `Tables`, `Schema` bleiben unverändert.
- Spielregeln nur über Daten: Tabellen- und Feldnamen der Assistenten kommen aus `profiles/warhammer_3/wizards.json`, nie aus Konstanten im Code. Die einzige Spielregel im Code ist die Ketten- und Stufenlogik von `BuildingWizard.Compose` (Spec 5.8).
- Namenszusatz der Assistenten: der Wert `nameSuffix` der Assistenten-Datei, ausgeliefert ` (copy)`. Er ist kein UI-Text und wird nicht übersetzt.
- Zufallsschlüssel: Bereich 1 000 000 000 bis 2 147 483 647 (beide eingeschlossen), nur für Schlüsselfelder vom Typ `I32` oder `I64` in Zeilen eines Abschnitts mit `"copy": "owned"`, die nicht das Feld auf die Wurzel sind. `Random` wird übergeben: in `Objects` als Parameter, in der App über `ObjectsController.KeyRandom` (Standard `Random.Shared`, Tests setzen `new Random(<seed>)`).
- Jede Kopie (Kopieren als, beide Assistenten) prüft vor dem Schreiben jeden eingefügten Schlüssel gegen die Ladereihenfolge und gegen die übrigen Zeilen der Kopie (`ObjectCopy.TakenKeys`); ein belegter Schlüssel bricht ab, nichts wird geschrieben.
- Fachlogik in UI-freien Klassen; Views sind Hüllen, Code-behind nur Verdrahtung. Keine statischen Singletons; alles entsteht in `App.xaml.cs`.
- Libraries loggen nicht. App-Controller loggen über `IAppLog`: jede Kopie mit Tabelle, Quellschlüssel, neuem Schlüssel, Zeilen- und Dateizahlen; jeden Abbruch mit Grund. Keine Zellwerte, keine Spieltexte.
- Alle UI-Texte über `AppTexts`, jeder neue Key in `locales/en.json` und `locales/de.json`; `AppTextsTests` bleibt grün. Präfixe: `objects.copyAs.*` (Kopieren als; `objects.copy.*` ist schon die Dateikopie-Frage), `objects.wizard.*`, `menu.tools.newBuilding`, `menu.tools.newTrait`.
- Spielbegriffe (Tabellen, Schlüssel, Namen aus Loc) in Meldungen, Tooltips und Listen über `DisplayText.Safe` oder `MessageText.EscapeControlCharacters`.
- Schreibgrenze wie Spec 4: geschrieben wird nur über `ProjectWriter.Apply` und `FileContentsStep`. Kopien schreiben nur `Insert`; es entsteht nie eine Dateikopie (Spec 5.8).
- Jede vom Nutzer gestartete Operation läuft über `IOperationRunner`; Dialoge laufen zwischen den Läufen auf dem UI-Thread.
- Farben nur über Theme-Tokens und `DynamicResource`; keine neuen Tokens.
- Test-first: failing Test → RED → implementieren → GREEN → Commit. Dateisystem-Tests im `TempDir`; nichts unter `%APPDATA%\TotalWarModTool` oder `%LocalAppData%\TotalWarModTool`. View-Tests über `Sta.Run`, Controller-Tests mit `FakeOperationRunner` und `FakeObjectDialogs`.
- Ein Commit pro Task, eigene Pfade ausdrücklich stagen, Attribution-Zeile der eigenen Sitzung am Ende.
- Weicht die Umsetzung von der Spec ab, wird die Spec im selben Commit angepasst: Unterabschnitt `#### Festlegungen aus Plan 4` unter 5.8 (Task 1 legt ihn an, jede Task ergänzt ihre Punkte). Tests in diesem Plan legen Namen und Erwartungen fest; wo ein Test nur als Name mit Kommentar steht, ist der Kommentar die Erwartung. Widerspricht eine Erwartung einer Regel des Plans oder der Spec, wird das gemeldet, nicht still entschieden.

## Review Focus

1. **Kopieren als bei ungeübernommenem Text.** „Kopieren als“ wechselt den Tab zum neuen Objekt. Erwartung: vorher fragt `LeaveAsync` „Übernehmen, Verwerfen, Abbrechen“; Abbrechen kopiert nichts. Kopiert wird der Stand des letzten Aufbaus, nie der ungeübernommene Text. → Task 5 (`Copy_as_with_unapplied_text_asks_first_and_cancel_copies_nothing`).
2. **Abgeleitete Schlüssel gibt es schon.** Beispiele: die Kette `<neu>_chain`, eine Trait-Stufe `<neu>_1`, ein Zufallsschlüssel, ein Schlüssel, den die Kopie zweimal erzeugt. Erwartung: Lauf A meldet jeden belegten Schlüssel mit Tabelle, nichts wird geschrieben, kein Schritt. Der Dialog prüft nur den eingegebenen Wurzelschlüssel. → Task 2 (`TakenKeys`-Tests), Task 5 (`A_taken_derived_key_is_reported_and_nothing_is_written`).
3. **Höchste Stufe nicht eindeutig.** 53 Ketten haben mehrere Stufen auf der höchsten `level` (Spike). Erwartung: Der Nutzer wählt die Stufe, auf die das neue Gebäude folgt. Standard ist die höchste `level`, bei Gleichstand die erste nach Schlüssel (ordinal). `level` wird die gewählte Stufe plus 1. → Task 4 (`Stages_of_a_chain_are_sorted_by_level_then_key`, `A_stage_in_an_existing_chain_follows_the_chosen_stage`), Task 6 (`The_stage_list_preselects_the_last_stage`).
4. **Erwartete Hinweise erscheinen als Problem.** Die Kopie der Vorlage-Kette meldet deren Stufen als `ForeignKeyRows`. Erwartung: Dieser Hinweis entfällt in „Neue Kette“. Die Ergebniszeile nennt nur Hinweise, die der Nutzer bewerten muss (`ForeignKeyRows`, `DepthExceeded`, `NoChain`), nie `HiddenSection`. → Task 4 (`A_new_chain_drops_the_note_about_the_stages_of_the_template_chain`), Task 5 (`The_result_line_of_a_copy_names_the_rows_left_out`).
5. **Undo einer Kopie.** Erwartung: ein Strg+Z entfernt alle Zeilen und Dateien der Kopie, auch beim Assistenten mit Kette. Der Tab des neuen Objekts wird gewählt und neu gebaut und zeigt dann „nicht gefunden“. → Task 5 (`Undo_of_a_copy_removes_every_row_and_rebuilds_the_tab`), Task 6 (`Undo_of_a_new_building_removes_the_level_and_the_chain`).

---

### Task 1: Kopierregel `"copy": "owned"` und neue Zahlschlüssel

Spec 5.8, Spike vor Plan 4, Entscheidungen vor Plan 4.

**Files:**
- Modify: `src/TotalWarModTool.Objects/CategoryDefinition.cs` (`SectionRule.CopyOwned`, Parser)
- Modify: `src/TotalWarModTool.Objects/ObjectCopy.cs` (`NewIntegerKey`, `Rename`-Parameter, Klassifizierung, `CopyRow`)
- Create: `src/TotalWarModTool.Objects/RandomIntegerKeys.cs`
- Modify: `profiles/warhammer_3/objects/building_levels_tables.json` (`"copy": "owned"` im Abschnitt `building_units_allowed_tables`)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (5.8 Kopierregel: Regel 2a „Abschnitt mit `copy: owned`“; neuer Unterabschnitt `#### Festlegungen aus Plan 4`)
- Modify: `Backlog.txt` (Eintrag „Kopierregel für eigene Schlüsselzeilen …“ unter „Aus Teil 4.2a Plan 1“ löschen)
- Test: `tests/TotalWarModTool.Objects.Tests/CategoryDefinitionsTests.cs`, `ObjectCopyTests.cs`, `RandomIntegerKeysTests.cs` (neu), `ObjectsRealDataTests.cs`

**Interfaces:**
- Consumes: `ObjectCopy.Rename` (heute), `ObjectText.RuleFor(category, table, field)`, `FieldValue.Type`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public sealed record SectionRule(string Table, string? FieldName, string? TitleKey, SectionMode Mode, string? Pattern, bool Hidden,
    bool CopyOwned = false);

/// <summary>A new value for an integer key field of a copied row (table with suffix, field name). Returns the value as text.</summary>
public delegate string NewIntegerKey(string table, string field);

public static class ObjectCopy
{
    public static CopyResult Rename(ObjectView source, ObjectId newId, Func<ObjectId, ObjectView> build, Func<string, CategoryDefinition?> categoryOf,
        ReferenceGraph graph, NewIntegerKey newIntegerKey);
}

public static class RandomIntegerKeys
{
    public const long Min = 1_000_000_000;
    public const long Max = int.MaxValue;
    public const int MaxDraws = 100;

    /// <summary>
    /// Draws uniformly in [Min, Max]. A value that taken(table, field, value) reports, or that this allocator handed out before (for any table and field),
    /// is drawn again. Throws InvalidOperationException after MaxDraws draws for one call.
    /// </summary>
    public static NewIntegerKey Create(Random random, Func<string, string, string, bool> taken);
}
```

Regeln:
- JSON: `"copy": "owned"` setzt `CopyOwned`. Fehlt das Feld oder ist es `null`, ist es `false`. Jeder andere Wert ist ein Problem mit Detail `copy must be "owned".`. `"hidden": true` zusammen mit `"copy": "owned"` ist ein Problem mit Detail `a hidden section cannot be copied.`.
- Klassifizierung in dieser Reihenfolge: versteckt (wie heute) → `CopyOwned` → `Owned` → Regeln 2 bis 4 wie heute.
- `CopyRow` behandelt jedes Schlüsselfeld außer dem Feld auf die Wurzel so: Wert gleich altem Wurzelschlüssel → neuer Schlüssel (wie heute). Sonst, wenn die Zeile über `CopyOwned` kopiert wird und das Feld vom Typ `I32` oder `I64` ist → `newIntegerKey(section.SourceTable, field.Name)`. Sonst bleibt der Wert.
- Bestehende Aufrufer (`ObjectCopyTests.Rename`-Helfer, zwei Echtdaten-Tests) übergeben einen Allocator, der wirft (`(t, f) => throw new InvalidOperationException(...)`), wo kein `CopyOwned` vorkommt.

- [ ] **Step 1: Failing Tests**

```csharp
// CategoryDefinitionsTests
[Fact] public void A_section_with_copy_owned_loads()                 // "copy": "owned" → rule.CopyOwned true; ohne Feld false
[Fact] public void A_copy_value_other_than_owned_is_a_problem()      // "copy": "all" → Problem, Detail enthält "copy must be \"owned\"."
[Fact] public void A_hidden_section_cannot_be_copied()               // hidden true + copy owned → Problem "a hidden section cannot be copied."

// ObjectCopyTests (Mini-Tabelle units_tables: key I32 (Schlüssel), building → Root.level_name, unit; Kategorie mit CopyOwned für units_tables)
[Fact] public void A_section_marked_copy_owned_is_copied_with_new_integer_keys()
// Zwei Zeilen key 7 und 8; Allocator liefert "1000000001", dann "1000000002" und zeichnet (table, field) auf.
// Ergebnis: Abschnitt units_tables mit 2 Zeilen, Felder key = 1000000001 bzw. 1000000002, unit unverändert, kein Feld building;
// Allocator zweimal mit ("units_tables", "key") gefragt; kein CopyNote für units_tables.
[Fact] public void An_unmarked_section_with_its_own_integer_key_stays_a_note()
// Gleiche Daten ohne Kategorie-Regel → kein Abschnitt units_tables, Note ForeignKeyRows units_tables 2, Allocator nie gefragt.

// RandomIntegerKeysTests (new Random(42))
[Fact] public void Values_lie_in_the_high_range()                     // 1000 Aufrufe: jeder Wert long.Parse in [Min, Max]
[Fact] public void A_taken_value_is_drawn_again()                     // taken meldet den ersten gezogenen Wert belegt → Ergebnis ist ein anderer Wert
[Fact] public void A_value_is_never_handed_out_twice()                // 1000 Aufrufe → 1000 verschiedene Werte
[Fact] public void It_gives_up_after_max_draws()                      // taken immer true → InvalidOperationException
```

Echtdaten (`ObjectsRealDataTests.Copying_the_barracks_follows_the_copy_rule`): Allocator `RandomIntegerKeys.Create(new Random(1), (t, f, v) => store.Get(t, order, ct).FindByField(f, v).Count > 0)`; die Kategorie aus `profiles` lädt `copy: owned`. Neue Erwartungen: `copy.Sections` enthält `building_units_allowed_tables`, kein Note für diese Tabelle, jede kopierte Zeile hat einen `key`, den der Quellabschnitt nicht hat.

- [ ] **Step 2: RED prüfen:** `dotnet test tests/TotalWarModTool.Objects.Tests` → die neuen Tests scheitern (Kompilierfehler `CopyOwned`, `RandomIntegerKeys`, `NewIntegerKey`).
- [ ] **Step 3: Implementieren** wie in Interfaces und Regeln. Kategorie-Datei ergänzen. In der Spec 5.8 Regel 2a einfügen und `#### Festlegungen aus Plan 4` anlegen (erster Punkt: Zufallsschlüssel nur bei `CopyOwned`, Bereich, Prüfung gegen die Ladereihenfolge über den Allocator). Backlog-Eintrag löschen.
- [ ] **Step 4: GREEN:** `dotnet test tests/TotalWarModTool.Objects.Tests` grün; Echtdaten-Lauf mit `TWMT_WH3_DIR` grün; `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~ShippedCategoryTests"` grün.
- [ ] **Step 5: Commit** `feat: copy rows of sections marked copy owned with new random integer keys`

---

### Task 2: Änderungen einer Kopie und belegte Schlüssel

Spec 5.8 („Übernehmen eines `ParsedObject` gegen eine leere Baseline“), Review Focus 2.

**Files:**
- Modify: `src/TotalWarModTool.Objects/ObjectCopy.cs`
- Test: `tests/TotalWarModTool.Objects.Tests/ObjectCopyTests.cs`, `ObjectsRealDataTests.cs`

**Interfaces:**
- Consumes: `CopyResult` (Task 1), `ObjectDiff.Compute`, `ReferenceGraph.KeyFields`.
- Produces:

```csharp
/// <summary>A key an insert of a copy would write although the load order or another row of the same copy has it. Table with suffix; KeyValues in key field order.</summary>
public sealed record TakenKey(string Table, IReadOnlyList<string> KeyValues);

public static class ObjectCopy
{
    /// <summary>
    /// Every object of the copy diffed against a view without root (new ObjectView(object.Id, null, [], [], [], order, [])) with categoryOf(object.Id.Table),
    /// concatenated in object order: Rows, Texts, Diagnostics. Pure.
    /// </summary>
    public static ObjectChanges Changes(CopyResult copy, LoadOrder order, Func<string, CategoryDefinition?> categoryOf, ReferenceGraph graph,
        ReferenceExists? referenceExists);

    /// <summary>
    /// The inserts whose key (cells at graph.KeyFields(table)) keyExists(table, keyValues) reports, or that an earlier insert of the same table in
    /// changes already has. Tables without key fields are skipped. Each (table, key) once, in order of first appearance.
    /// </summary>
    public static IReadOnlyList<TakenKey> TakenKeys(ObjectChanges changes, ReferenceGraph graph, Func<string, IReadOnlyList<string>, bool> keyExists);

    /// <summary>The copy with suffix appended to every text of the column: root texts of objects of the table and texts of rows of its sections.</summary>
    public static CopyResult AppendToTexts(CopyResult copy, string table, string column, string suffix);
}
```

- [ ] **Step 1: Failing Tests** (`ObjectCopyTests`, Mini-Daten aus `BlockFixture` und der Trait-Fixture der Datei):

```csharp
[Fact] public void Changes_of_a_copy_insert_every_object_in_order()
// Trait-Kopie mit 2 Stufen: Rows[0] ist Insert traits_tables t9, danach die Stufe t9_1 mit ihren Effekten, dann t9_2; alle Kind == Insert; keine Fehler.
[Fact] public void Changes_of_a_copy_carry_the_texts_of_the_sub_objects()
// Stufe mit Text onscreen_name → TextChange Insert mit Key "trait_levels_onscreen_name_t9_1".
[Fact] public void A_key_that_exists_in_the_load_order_is_taken()
// keyExists meldet ("traits_tables", ["t9"]) → TakenKeys == [TakenKey("traits_tables", ["t9"])].
[Fact] public void Two_inserts_with_the_same_key_are_taken_once()
// ObjectChanges mit zwei Inserts gleichen Schlüssels, keyExists immer false → genau ein TakenKey.
[Fact] public void Inserts_of_a_table_without_key_fields_are_never_taken()
[Fact] public void A_suffix_is_appended_to_the_texts_of_the_column()
// AppendToTexts(copy, "trait_levels_tables", "onscreen_name", " (copy)") → Texte der Stufen enden auf " (copy)", andere Spalten unverändert,
// Texte der Wurzel (Tabelle traits_tables) unverändert.
```

Echtdaten: `Copying_the_barracks_follows_the_copy_rule` und `Copying_a_trait_copies_its_levels_as_sub_objects` rechnen `Changes` und prüfen `TakenKeys(changes, graph, (t, k) => store.Get(t, order, ct).FindByKey(k).Count > 0)` ist leer.

- [ ] **Step 2: RED prüfen:** `dotnet test tests/TotalWarModTool.Objects.Tests` → Kompilierfehler `Changes`, `TakenKeys`, `AppendToTexts`.
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN:** Objects-Tests grün, Echtdaten-Lauf grün.
- [ ] **Step 5: Commit** `feat: compute the inserts of a copy and the keys it would take twice`

---

### Task 3: Assistenten-Datei im Spielprofil

Entscheidungen vor Plan 4 (Tabellen- und Feldnamen als Daten).

**Files:**
- Create: `src/TotalWarModTool.Objects/WizardDefinitions.cs`
- Create: `profiles/warhammer_3/wizards.json`
- Test: `tests/TotalWarModTool.Objects.Tests/WizardDefinitionsTests.cs` (neu)

**Interfaces:**
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public sealed record TextRef(string Table, string Column);

public sealed record BuildingWizardDefinition(string LevelTable, string ChainField, string LevelField, string ChainTable, string ChainKeySuffix,
    string UpgradeTable, string UpgradeFromField, string UpgradeToField, string VariantTable, string VariantBuildingField, string VariantCultureField,
    string VariantSubcultureField, string SubcultureTable, string SubcultureCultureField, TextRef NameText)
{
    /// <summary>"table" or "table.field" for every name the graph does not know (Latest(table) null or no such field), in declaration order.</summary>
    public IReadOnlyList<string> MissingIn(ReferenceGraph graph);
}

public sealed record TraitWizardDefinition(string TraitTable, TextRef NameText)
{
    public IReadOnlyList<string> MissingIn(ReferenceGraph graph);
}

public sealed record WizardDefinitions(string NameSuffix, BuildingWizardDefinition? Building, TraitWizardDefinition? Trait);

/// <summary>Definitions null and Problem null: the file does not exist. Definitions null and Problem set: the file cannot be used.</summary>
public sealed record WizardLoadResult(WizardDefinitions? Definitions, string? Problem);

public static class WizardDefinitionLoader
{
    public const int SchemaVersion = 1;
    public const string FileName = "wizards.json";

    /// <summary>Reads the file like CategoryDefinitions reads a category file (shared read, never throws).</summary>
    public static WizardLoadResult Load(string path);
}
```

Dateiinhalt (genau so ausliefern):

```json
{
  "schemaVersion": 1,
  "nameSuffix": " (copy)",
  "building": {
    "levelTable": "building_levels_tables",
    "chainField": "chain",
    "levelField": "level",
    "chainTable": "building_chains_tables",
    "chainKeySuffix": "_chain",
    "upgradeTable": "building_upgrades_junction_tables",
    "upgradeFromField": "from",
    "upgradeToField": "to",
    "variantTable": "building_culture_variants_tables",
    "variantBuildingField": "building",
    "variantCultureField": "culture",
    "variantSubcultureField": "subculture",
    "subcultureTable": "cultures_subcultures_tables",
    "subcultureCultureField": "culture",
    "nameText": { "table": "building_culture_variants_tables", "column": "name" }
  },
  "trait": {
    "traitTable": "character_traits_tables",
    "nameText": { "table": "character_trait_levels_tables", "column": "onscreen_name" }
  }
}
```

Regeln: `schemaVersion` muss 1 sein (Detail `schemaVersion must be 1.`). `nameSuffix` fehlt → `""`. `building` und `trait` sind einzeln optional; ein vorhandener Abschnitt mit fehlendem oder leerem Pflichtfeld macht die ganze Datei unbrauchbar (Detail `<abschnitt>.<feld> is missing.`). Ungültiges JSON: Detail `Invalid JSON: <Meldung>`.

- [ ] **Step 1: Failing Tests**

```csharp
[Fact] public void A_missing_file_gives_neither_definitions_nor_a_problem()
[Fact] public void A_complete_file_loads_both_wizards()                 // Werte wie oben, NameSuffix " (copy)"
[Fact] public void A_file_with_only_the_trait_wizard_has_no_building_wizard()
[Fact] public void A_missing_field_makes_the_file_unusable()           // building ohne chainField → Problem "building.chainField is missing."
[Fact] public void A_wrong_schema_version_makes_the_file_unusable()
[Fact] public void Broken_json_makes_the_file_unusable()
[Fact] public void MissingIn_names_unknown_tables_and_fields()          // Mini-Graph ohne Feld "level" → ["building_levels_tables.level"]
[Fact] public void The_shipped_file_names_only_tables_and_fields_of_the_shipped_schema()
// RepoRoot.Find(): profiles/warhammer_3/wizards.json und schemas/schema_wh3.ron → kein Problem, Building und Trait gesetzt, MissingIn beide leer.
```

- [ ] **Step 2: RED prüfen:** `dotnet test tests/TotalWarModTool.Objects.Tests` → Kompilierfehler.
- [ ] **Step 3: Implementieren**, Datei anlegen.
- [ ] **Step 4: GREEN:** Objects-Tests grün.
- [ ] **Step 5: Commit** `feat: read the table and field names of the wizards from the game profile`

---

### Task 4: Regeln des Gebäude-Assistenten

Spec 5.8 „Neues Gebäude“, Spike vor Plan 4, Review Focus 3 und 4.

**Files:**
- Create: `src/TotalWarModTool.Objects/BuildingWizard.cs`
- Modify: `src/TotalWarModTool.Objects/ObjectCopy.cs` (`CopyNoteKind.NoChain`)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Festlegungen: Stufenwahl statt fester höchster Stufe; Upgrade-Zeilen der Vorlage entfallen in beiden Modi; alle Kultur-Varianten der Vorlage werden kopiert; Vorlagen nur über Varianten mit Kultur oder Subkultur, Fraktions-Varianten zählen nicht)
- Test: `tests/TotalWarModTool.Objects.Tests/BuildingWizardTests.cs` (neu), `ObjectsRealDataTests.cs`

**Interfaces:**
- Consumes: `BuildingWizardDefinition` (Task 3), `ObjectCopy.AppendToTexts` (Task 2), `MergedTable` (`Definition`, `Rows`, `FieldIndex`), `LocTable` (`TryGet`), `ObjectBuilder.DisplayName(loc, id)`.
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public enum CopyNoteKind { ForeignKeyRows, DepthExceeded, HiddenSection, NoChain }

/// <summary>A choice of a wizard list: the key and its game text (null without one).</summary>
public sealed record WizardChoice(string Key, string? Name);

public sealed record BuildingStage(string LevelKey, long Level, string? Name);

public sealed class BuildingCatalog
{
    /// <summary>Every subculture with its name (DisplayName of the subculture), sorted by Name ?? Key (OrdinalIgnoreCase, then Key ordinal).</summary>
    public IReadOnlyList<WizardChoice> Subcultures { get; }

    /// <summary>The levels with a variant whose subculture is this one or whose culture is the culture of this one; each once; Name from the loc text of
    /// the first matching variant (NameText column, loc key of the variant's ObjectId); sorted like Subcultures. Empty for an unknown subculture.</summary>
    public IReadOnlyList<WizardChoice> TemplatesOf(string subculture);

    public bool HasLevel(string levelKey);

    /// <summary>A level of the level table names this chain (ordinal). Chains without levels are not known here; TakenKeys catches them.</summary>
    public bool HasChain(string chainKey);

    /// <summary>The chain of the level; null when the level is unknown or its chain is empty.</summary>
    public string? ChainOf(string levelKey);

    /// <summary>The levels of the chain sorted by Level, then LevelKey (ordinal). Name like in TemplatesOf.</summary>
    public IReadOnlyList<BuildingStage> StagesOf(string chain);

    /// <summary>
    /// The subculture to preselect for a view: null unless view.Id.Table is the level table. The first row of the variant section with a subculture
    /// gives it; else the first row with a culture gives the first subculture of that culture in Subcultures order; else null.
    /// </summary>
    public string? PreselectFor(ObjectView? view);
}

public static class BuildingWizard
{
    /// <summary>table(name) returns the merged table of the load order. Reads the level, variant and subculture tables.</summary>
    public static BuildingCatalog Catalog(BuildingWizardDefinition definition, Func<string, MergedTable> table, LocTable loc);

    public static ObjectId ChainIdFor(BuildingWizardDefinition definition, string newLevelKey);

    /// <summary>
    /// The copy of a building: levelCopy (ObjectCopy.Rename of the template), chainCopy (Rename of the template's chain to ChainIdFor; null for a
    /// stage in an existing chain or a template without chain), after (null: new chain; else the stage the new building follows).
    /// </summary>
    public static CopyResult Compose(BuildingWizardDefinition definition, CopyResult levelCopy, CopyResult? chainCopy, BuildingStage? after,
        string nameSuffix);
}
```

Regeln von `Compose` (die einzige Spielregel im Code):
- Aus dem Wurzelobjekt der Stufe (`levelCopy.Objects[0]`) entfallen alle Abschnitte mit `SourceTable == UpgradeTable`, in beiden Modi.
- Neue Kette (`after == null`), `chainCopy != null`: Wurzelfeld `ChainField` = `chainCopy.Objects[0].Id.KeyValues[0]`, `LevelField` = `0`. Objekte: Stufe, übrige Objekte von `levelCopy`, alle Objekte von `chainCopy`. Hinweise: die von `levelCopy` und die von `chainCopy` ohne `(ForeignKeyRows, LevelTable)`.
- Neue Kette, `chainCopy == null` (Vorlage ohne Kette): `ChainField` bleibt, `LevelField` = `0`, zusätzlicher Hinweis `CopyNote(NoChain, ChainTable, 0)`.
- Bestehende Kette (`after != null`): `LevelField` = `after.Level + 1` (InvariantCulture), `ChainField` bleibt. Neuer Abschnitt `ParsedSection(UpgradeTable, UpgradeToField, [ParsedRow([ParsedField(UpgradeFromField, after.LevelKey, 0)], [], 0)], 0)`.
- Danach `ObjectCopy.AppendToTexts(result, NameText.Table, NameText.Column, nameSuffix)`.
- Wurzelfelder werden per Name ersetzt (OrdinalIgnoreCase); fehlt ein Feld, ist das eine `InvalidOperationException` (die Definition passt nicht zum Schema; die App prüft vorher `MissingIn`).

- [ ] **Step 1: Failing Tests** (`BuildingWizardTests`; Mini-Schema über `TestSchema.Build` mit `levels` (level_name*, chain → chains.key, level I32), `chains` (key*), `variants` (building* → levels.level_name, culture*, subculture*, faction*), `subcultures` (subculture*, culture), `upgrades` (from* → levels, to* → levels); Tabellen über `TestSources.WritePack` und `TableStore` wie in `ObjectBuilderTests`; `LocTable` über den internen Konstruktor; eine Definition mit diesen Namen):

```csharp
[Fact] public void Subcultures_are_listed_with_their_names_sorted_by_name()
[Fact] public void Templates_of_a_subculture_are_the_levels_with_a_variant_of_it_or_of_its_culture()
// Variante (l1, culture c1), (l2, subculture sc1), (l3, culture c2), (l4, faction f1); sc1 gehört zu c1 → TemplatesOf("sc1") == [l1, l2] (nach Name)
[Fact] public void A_template_is_listed_once_even_with_two_matching_variants()
[Fact] public void Stages_of_a_chain_are_sorted_by_level_then_key()   // Stufen (b, 1), (a, 1), (c, 0) → c, a, b
[Fact] public void The_catalog_knows_the_chains_of_its_levels()       // HasChain("ch1") true, HasChain("ch9") false
[Fact] public void The_preselection_comes_from_the_variant_of_the_shown_building()
[Fact] public void The_preselection_falls_back_to_the_first_subculture_of_the_culture()
[Fact] public void The_preselection_is_null_for_another_kind_of_object()
[Fact] public void A_new_chain_points_the_level_at_the_copied_chain_at_level_zero_and_drops_the_upgrades()
[Fact] public void A_new_chain_drops_the_note_about_the_stages_of_the_template_chain()
// chainCopy.Notes enthält (ForeignKeyRows, levels, 3) und (ForeignKeyRows, other, 1) → Ergebnis enthält nur (ForeignKeyRows, other, 1)
[Fact] public void A_template_without_chain_gets_no_chain_and_a_note()
[Fact] public void A_stage_in_an_existing_chain_follows_the_chosen_stage()
// after = BuildingStage("l3", 2, null) → level = "3", chain unverändert, Abschnitt upgrades.to mit einer Zeile from = l3, keine anderen Upgrade-Abschnitte
[Fact] public void The_name_texts_get_the_suffix()
```

Echtdaten (`ObjectsRealDataTests`, Definition aus `profiles/warhammer_3/wizards.json`):

```csharp
[RealDataFact] public void The_building_wizard_copies_barracks_3_as_a_stage_after_it()
// Rename(wh_main_emp_barracks_3 → twmt_test_barracks_4), Compose(after = Stufe barracks_3 aus StagesOf), Changes, TakenKeys leer;
// genau eine Upgrade-Zeile (from barracks_3, to twmt_test_barracks_4); level = höchste Stufe + 1; building_units_allowed eingefügt.
[RealDataFact] public void The_building_wizard_copies_barracks_3_with_a_new_chain()
// zusätzlich Rename der Kette auf twmt_test_barracks_4_chain; keine Upgrade-Zeile; level 0; chain = twmt_test_barracks_4_chain;
// kein Hinweis für building_levels_tables; TakenKeys leer.
[RealDataFact] public void The_empire_has_templates()   // TemplatesOf("wh_main_sc_emp_empire") enthält wh_main_emp_barracks_3; Invariante statt fester Zahl
```

- [ ] **Step 2: RED prüfen:** Objects-Tests → Kompilierfehler.
- [ ] **Step 3: Implementieren.** Festlegungen in die Spec.
- [ ] **Step 4: GREEN:** Objects-Tests grün, Echtdaten-Lauf grün.
- [ ] **Step 5: Commit** `feat: list building templates and stages and compose a new building from a copy`

---

### Task 5: Kopieren als (App)

Spec 5.8 („Kopieren als“), 5.9, 7, Review Focus 1, 2, 4, 5. Backlog „`ObjectsController.CategoryOf` und `ResolveOrder`“.

**Files:**
- Modify: `src/TotalWarModTool.App/Objects/ObjectApplyController.cs` (gemeinsamer Schreibpfad herausgezogen; Verhalten des Übernehmens unverändert)
- Create: `src/TotalWarModTool.App/Objects/ObjectApplyController.Copy.cs` (partial: Kopierpfad, „Kopieren als“)
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.cs` (`CategoryOf`, `CaptureOrder`, `IntegerKeys`, `ApplyEnvironmentReason` intern sichtbar)
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.Tabs.cs` (`CopyHandler` an neue Tabs; `OpenObject`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectTabViewModel.cs` (`CopyCommand`, `CopyDisabledReason`, `CopyHandler`, `SetApplyResult` mit Hinweisen)
- Modify: `src/TotalWarModTool.App/Objects/IObjectDialogs.cs`, `ObjectDialogs.cs` (`AskNewKey` über `NameInputWindow`)
- Modify: `src/TotalWarModTool.App/Tabs/TabsView.xaml` (Knopf „Kopieren als“ hinter „Übernehmen“, gleiche Form mit `ToolTipService.ShowOnDisabled`)
- Modify: `src/TotalWarModTool.App/Objects/ObjectEditorView.xaml` (Kontextmenü des Editors: Ausschneiden, Kopieren, Einfügen wie bisher plus „Kopieren als“ an `CopyCommand`)
- Modify: `src/TotalWarModTool.App/AppTexts.ObjectsApply.cs` oder neue `AppTexts.ObjectsCopy.cs`, `locales/en.json`, `locales/de.json`
- Create: `src/TotalWarModTool.App/Objects/ObjectKeyRules.cs`
- Modify: `src/TotalWarModTool.App/App.xaml.cs` (`objects.CopyHandler = objectApply.CopyAsync;` neben `ApplyHandler`)
- Modify: `Backlog.txt` (Eintrag „`ObjectsController.CategoryOf` und `ResolveOrder` …“ löschen)
- Create: `tests/TotalWarModTool.App.Tests/ObjectApplySetup.cs` (der `Setup`-Record, `CreateAsync`, `ShowK1Async`, `Edit` und `ApplyAsync` aus `ObjectApplyControllerTests` hierher verschoben, `internal static`; `ObjectApplyControllerTests` nutzt sie unverändert weiter)
- Test: `tests/TotalWarModTool.App.Tests/ObjectCopyAsTests.cs` (neu), `FakeObjectDialogs.cs`, `ObjectTabViewModelTests.cs`, `ObjectEditorViewTests.cs`

**Interfaces:**
- Consumes: Task 1 und 2 (`Rename`, `RandomIntegerKeys`, `Changes`, `TakenKeys`), `ProjectSnapshotReader.Read`, `SnapshotRequest.For`, `ProjectWritePlan.Compute`, `ProjectWriter.Apply`.
- Produces:

```csharp
namespace TotalWarModTool.App;

public interface IObjectDialogs
{
    /// <summary>The new key for a copy of objectName; the box starts with suggestion, validate gives the reason a key is refused or null. Null: cancel.</summary>
    string? AskNewKey(string objectName, string suggestion, Func<string, string?> validate);
}

/// <summary>The checks of a new key, shared by "Copy as" and the wizards.</summary>
internal static class ObjectKeyRules
{
    /// <summary>Empty → objects.copyAs.key.empty; any char.IsWhiteSpace → objects.copyAs.key.whitespace; existsIn(key) not null →
    /// objects.copyAs.key.exists with that text (escaped); else null.</summary>
    public static string? Problem(AppTexts texts, string key, Func<string, string?> existsIn);
}

public sealed partial class ObjectsController
{
    /// <summary>The random source of new integer keys; Random.Shared unless a test sets a seeded one.</summary>
    internal Random KeyRandom { get; set; }

    /// <summary>Set by the composition root like ApplyHandler (ObjectApplyController.CopyAsync), copied to every new tab through RunGuardedAsync.</summary>
    internal Func<ObjectTabViewModel, Task>? CopyHandler { get; set; }

    internal CategoryDefinition? CategoryOf(string table);

    /// <summary>The load order without a root pack, as a closure that reads the files when it runs (inside the runner); null without a game.</summary>
    internal Func<LoadOrder>? CaptureOrder();

    /// <summary>A new allocator for one copy: RandomIntegerKeys.Create(KeyRandom, (t, f, v) => stores.Tables.Get(t, order, ct).FindByField(f, v).Count > 0).</summary>
    internal NewIntegerKey IntegerKeys(ObjectStores stores, LoadOrder order, CancellationToken ct);

    /// <summary>Opens the object in a new tab, selects it and builds it.</summary>
    internal ObjectTabViewModel OpenObject(ObjectRef target);
}

public sealed class ObjectTabViewModel
{
    public ICommand CopyCommand { get; }

    /// <summary>The first that applies: environment (as Apply), root pack not in the load order, not Ready, stale, no root row
    /// (objects.copyAs.reason.noRoot), not exactly one key value (objects.copyAs.reason.compositeKey). Unapplied text is no reason: the handler asks first.</summary>
    public string? CopyDisabledReason { get; }

    public string CopyToolTip { get; }

    internal Func<ObjectTabViewModel, Task>? CopyHandler { get; set; }

    /// <summary>notes: the copy notes to name ("not copied: 3 rows in technologies"); HiddenSection is never named.</summary>
    public void SetApplyResult(WriteSummary summary, IReadOnlyList<string> createdFiles, int warnings, IReadOnlyList<CopyNote>? notes = null);
}

public sealed partial class ObjectApplyController
{
    /// <summary>"Copy as" of the tab (the CopyCommand).</summary>
    public Task CopyAsync(ObjectTabViewModel tab);

    /// <summary>What a written copy delivered: the recorded step and its plan, the notes of the copy.</summary>
    internal sealed record CopyDone(FileContentsStep Step, WritePlan Plan, IReadOnlyList<CopyNote> Notes);

    /// <summary>
    /// Run A ("objects.copyAs.checking"): copy(stores, order, ct) → ObjectCopy.Changes (no reference checks) → errors → TakenKeys → snapshot → plan;
    /// then the shared write path (questions, second plan, grid lock, run B, history Record, FilesChanged). Shows every refusal itself; null when
    /// nothing was written. stillValid is asked after the questions, together with "the active project is still the one passed".
    /// </summary>
    internal Task<CopyDone?> WriteCopyAsync(Func<ObjectStores, LoadOrder, CancellationToken, CopyResult> copy, LoadOrder order, ProjectState project,
        ObjectStores stores, string description, string item, Func<bool> stillValid);

    /// <summary>Remembers where a step came from, so that an undo selects and rebuilds that tab (as for an apply).</summary>
    internal void RememberOrigin(FileContentsStep step, ObjectTabViewModel tab, ObjectRef shown);

    /// <summary>What every write ends with after the caller showed its result: DocumentsController.CheckExternalChangesAsync, then ObjectsController.CheckStaleAsync.</summary>
    internal Task CheckAfterWriteAsync();
}
```

Ablauf von `CopyAsync(tab)`:
1. Läuft schon ein Übernehmen oder eine Kopie (`_applying`), oder ist `CopyDisabledReason` gesetzt: nichts (Log mit Grund).
2. `await LeaveAsync(tab)`; false → nichts.
3. Schlüssel fragen: `AskNewKey(DisplayKey, <schlüssel>_copy, validate)`. `validate` = `ObjectKeyRules.Problem(texts, key, existsIn)`; `existsIn` fragt `merged.FindByKey([key])` und liefert den Quellnamen der ersten Zeile (`Provenance.Source.Name`) oder null. `merged` = `stores.Tables.Get(table, result.Order, None)`, einmal vor dem Dialog geholt (die Tabelle ist vom Aufbau im Cache). null → nichts.
4. `WriteCopyAsync` mit `copy = (s, o, ct) => ObjectCopy.Rename(result.View, newId, id => s.Builder.Build(id, o, null, ct), _objects.CategoryOf, s.Graph, _objects.IntegerKeys(s, o, ct))`, `order = result.Order`, Beschreibung `"Copy: <table> <alt> as <neu>"`, `stillValid` = Tab zeigt noch dasselbe Objekt und dasselbe Ergebnis.
5. Erfolg: `tab.NavigateTo(newId, newTab: false)` (Verlauf wächst, Aufbau über den `BuildHandler`), dann `RememberOrigin(step, tab, tab.Current)` (genau der Eintrag nach der Navigation, mit `RootPackPath`, sonst findet `TabOf` den Tab beim Undo nicht), `tab.SetApplyResult(plan.Summary, angelegte Dateien, 0, notes)`, zuletzt `await CheckAfterWriteAsync()`. Log: Tabelle, alter und neuer Schlüssel, Zeilen, Dateien.

Meldungen von `WriteCopyAsync`: Fehler der Differenz wie beim Übernehmen (`ErrorsMessage`), belegte Schlüssel als `objects.copyAs.taken.title` und `objects.copyAs.taken.message` mit Zeilen `<tabelle>: <schlüssel mit " | ">` (gekappt mit `CappedLines`). Bei Fehlern des Plans und des Schreibens gilt dasselbe Verhalten wie beim Übernehmen; `MarkStale` nur, wenn ein Tab übergeben wurde. Der gemeinsame Schreibpfad ist eine private Methode, die `WriteAsync` und `WriteCopyAsync` nutzen. `ObjectApplyControllerTests` bleiben unverändert grün.

Texte (en / de):
- `objects.copyAs` „Copy as“ / „Kopieren als“
- `objects.copyAs.title` „Copy {0} as“ / „{0} kopieren als“
- `objects.copyAs.label` „New key“ / „Neuer Schlüssel“
- `objects.copyAs.key.empty` „Enter a key.“ / „Gib einen Schlüssel ein.“
- `objects.copyAs.key.whitespace` „A key cannot contain spaces or tabs.“ / „Ein Schlüssel darf keine Leerzeichen oder Tabs enthalten.“
- `objects.copyAs.key.exists` „This key already exists in {0}.“ / „Diesen Schlüssel gibt es schon in {0}.“
- `objects.copyAs.reason.noRoot` „The object does not exist, there is nothing to copy.“ / „Das Objekt gibt es nicht, es gibt nichts zu kopieren.“
- `objects.copyAs.reason.compositeKey` „Objects with a composite key cannot be copied.“ / „Objekte mit zusammengesetztem Schlüssel lassen sich nicht kopieren.“
- `objects.copyAs.checking` „Preparing the copy“ / „Kopie wird vorbereitet“
- `objects.copyAs.taken.title` „The copy cannot be written“ / „Die Kopie lässt sich nicht schreiben“
- `objects.copyAs.taken.message` „{0} keys of the copy already exist. Choose another key:“ / „{0} Schlüssel der Kopie gibt es schon. Wähle einen anderen Schlüssel:“
- `objects.copyAs.note.foreign` „not copied: {0} rows in {1}“ / „nicht kopiert: {0} Zeilen in {1}“
- `objects.copyAs.note.depth` „too deep, not copied: {0} rows in {1}“ / „zu tief, nicht kopiert: {0} Zeilen in {1}“
- `objects.copyAs.note.noChain` „the template has no chain“ / „die Vorlage hat keine Kette“

- [ ] **Step 1: Failing Tests** (`ObjectCopyAsTests` mit dem Aufbau von `ObjectApplyControllerTests.CreateAsync` (Methode dorthin `internal static` machen oder in eine gemeinsame Hilfsklasse `ObjectApplySetup` verschieben); `FakeObjectDialogs` bekommt `NewKeyAnswer` (`Func<Func<string, string?>, string?>`, Standard null) und `NewKeyAsks`):

```csharp
[Fact] public async Task Copy_as_writes_the_root_and_its_rows_and_opens_the_copy_in_the_same_tab()
// k1 → "k9": Projektdatei db\x_tables\Flo.tsv hat k9, db\y_tables\Flo.tsv zwei Zeilen x = k9; tab.Current.KeyValues == ["k9"], History.Count 2;
// ein Schritt in der History; ResultLine nicht null.
[Fact] public async Task The_new_key_dialog_refuses_an_empty_a_spaced_and_an_existing_key()
// validate("") / ("a b") / ("k2") → die drei Texte; validate("k9") → null; Vorschlag "k1_copy".
[Fact] public async Task Copy_as_with_unapplied_text_asks_first_and_cancel_copies_nothing()
[Fact] public async Task Cancelling_the_key_dialog_copies_nothing()
[Fact] public async Task A_taken_derived_key_is_reported_and_nothing_is_written()
// Fake-Copy über WriteCopyAsync mit einer Zeile, deren Schlüssel k2 ist → Meldung objects.copyAs.taken.title, kein Schritt, keine Datei.
[Fact] public async Task The_result_line_of_a_copy_names_the_rows_left_out()
// WriteCopyAsync mit Notes ForeignKeyRows z_tables 3 und HiddenSection y_tables 1 → ResultLine enthält "z_tables" und nicht "y_tables".
[Fact] public async Task Undo_of_a_copy_removes_every_row_and_rebuilds_the_tab()
// Undo → beide Projektdateien im Papierkorb (neu angelegt), Tab gewählt, Aufbau der Kopie zweimal im Runner.
[Fact] public async Task Copy_as_is_disabled_without_a_project_on_a_stale_tab_and_for_a_missing_object()

// ObjectTabViewModelTests
[Fact] public void The_copy_reason_follows_the_order_of_the_spec()
// ObjectEditorViewTests (STA)
[Fact] public void The_context_menu_of_the_editor_offers_copy_as()
```

- [ ] **Step 2: RED prüfen:** `dotnet test tests/TotalWarModTool.App.Tests --filter "FullyQualifiedName~ObjectCopyAsTests"` → Kompilierfehler.
- [ ] **Step 3: Implementieren.** Erst den gemeinsamen Schreibpfad herausziehen und `--filter "FullyQualifiedName~ObjectApplyControllerTests"` grün halten, dann den Kopierpfad. Festlegungen in die Spec (Kopieren nur bei genau einem Schlüsselwert; keine Referenzprüfung bei Kopien, weil Ziele innerhalb der Kopie noch nicht existieren; Schlüsselprüfung im Dialog nur für den Wurzelschlüssel, abgeleitete Schlüssel in Lauf A). Backlog-Eintrag löschen.
- [ ] **Step 4: GREEN:** die Klassen `ObjectCopyAsTests`, `ObjectApplyControllerTests`, `ObjectTabViewModelTests`, `ObjectEditorViewTests`, `AppTextsTests` je einzeln grün.
- [ ] **Step 5: Commit** `feat: copy an object under a new key from its tab`

---

### Task 6: Assistent „Neues Gebäude“ (App)

Spec 5.8 „Neues Gebäude“, 2.3, 7 („Kein aktives Projekt“, „Vorlage ohne Kette“), Review Focus 3 und 5.

**Files:**
- Create: `src/TotalWarModTool.App/Objects/ObjectWizardController.cs`
- Create: `src/TotalWarModTool.App/Objects/NewBuildingViewModel.cs`, `NewBuildingWindow.xaml`, `NewBuildingWindow.xaml.cs`
- Modify: `src/TotalWarModTool.App/Objects/ObjectsController.cs` (lädt `wizards.json` aus `<ProfilesDirectory>\<profil>\` neben den Kategorien; `internal WizardDefinitions? Wizards`, Problem ins Log; `internal ObjectTabViewModel? SelectedObjectTab` = gewählter Tab, wenn er ein Objekt-Tab ist; `internal string? EnvironmentReason()` = Spielordner oder Schema fehlt, sonst beschäftigt, sonst null)
- Modify: `src/TotalWarModTool.App/Objects/IObjectDialogs.cs`, `ObjectDialogs.cs` (`AskNewBuilding`)
- Modify: `src/TotalWarModTool.App/MainViewModel.cs` (`Wizards` mit `PropertyChanged`), `src/TotalWarModTool.App/MainWindow.xaml` (Menü Werkzeuge, vor „Einstellungen“: „Neues Gebäude“, Tooltip mit Sperrgrund), `src/TotalWarModTool.App/App.xaml.cs`
- Modify: AppTexts-Datei, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/ObjectWizardControllerTests.cs` (neu), `NewBuildingViewModelTests.cs` (neu), `FakeObjectDialogs.cs` (`BuildingAnswer`: `Func<NewBuildingInput, NewBuildingAnswer?>`, Standard null; `BuildingAsks`), `ObjectsTestSchema.cs` (Mini-Gebäude-Schema wie in `BuildingWizardTests`, Vanilla-Pack dazu und `WriteWizards(TestGame game, string json)`, das die Datei unter `game.Paths.ProfilesDirectory\<profil>\wizards.json` ablegt)

**Interfaces:**
- Consumes: Task 3 und 4, `ObjectApplyController.WriteCopyAsync`, `RememberOrigin`, `ObjectsController.CaptureOrder`, `CategoryOf`, `IntegerKeys`, `OpenObject`.
- Produces:

```csharp
namespace TotalWarModTool.App;

/// <summary>What the building dialog shows. ValidateKey refuses an empty key, spaces and tabs, and a level key the catalog has.</summary>
public sealed record NewBuildingInput(BuildingCatalog Catalog, string? PreselectedSubculture, Func<string, string?> ValidateKey);

/// <summary>After null: a new chain. Else the stage the new building follows.</summary>
public sealed record NewBuildingAnswer(string Template, string Key, BuildingStage? After);

public interface IObjectDialogs
{
    NewBuildingAnswer? AskNewBuilding(NewBuildingInput input);
}

public sealed class NewBuildingViewModel : ObservableObject
{
    public NewBuildingViewModel(AppTexts texts, NewBuildingInput input);
    public IReadOnlyList<WizardChoice> Subcultures { get; }
    public WizardChoice? SelectedSubculture { get; set; }          // preselected, else the first
    public string Filter { get; set; }                             // over key and name, contains, OrdinalIgnoreCase
    public IReadOnlyList<WizardChoice> FilteredTemplates { get; }
    public WizardChoice? SelectedTemplate { get; set; }            // stays while listed, else the first listed
    public string Key { get; set; }                                // "<template>_copy" until the user types; follows the template until then
    public bool NewChain { get; set; }                             // default true
    public IReadOnlyList<BuildingStage> Stages { get; }            // StagesOf(ChainOf(template)); empty without chain
    public BuildingStage? SelectedStage { get; set; }              // default the last stage
    public bool CanChooseExistingChain { get; }                    // Stages.Count > 0; false sets NewChain true
    public string? Error { get; }                                  // no template → objects.wizard.noTemplate; else ValidateKey(Key)
    public bool CanAccept { get; }
    public NewBuildingAnswer? Result { get; }                      // null while Error is set
}

public sealed class ObjectWizardController : ObservableObject
{
    public ObjectWizardController(ObjectsController objects, ObjectApplyController apply, ProjectsController projects, IOperationRunner runner,
        IObjectDialogs dialogs, IAppLog log, AppTexts texts);
    public ICommand NewBuildingCommand { get; }

    /// <summary>The first that applies: no usable project (objects.apply.reason.noProject), no game or schema (as ObjectsController), busy, no
    /// building wizard in the profile (objects.wizard.missing), names unknown to the schema (objects.wizard.schemaMismatch with MissingIn).</summary>
    public string? NewBuildingDisabledReason { get; }

    public Task NewBuildingAsync();
}
```

Ablauf von `NewBuildingAsync`:
1. Sperrgrund gesetzt → nichts. `EnsureStores()`, `CaptureOrder()` und das aktive Projekt auf dem UI-Thread.
2. Runner „objects.wizard.loading“: Ladereihenfolge, `BuildingWizard.Catalog(def, t => stores.Tables.Get(t, order, ct), stores.Loc.Get(order, ct))`.
3. Vorauswahl: `catalog.PreselectFor(_objects.SelectedObjectTab?.Result?.View)`.
4. `AskNewBuilding(new NewBuildingInput(catalog, preselected, key => ObjectKeyRules.Problem(texts, key, k => catalog.HasLevel(k) ? <kurzer Name der Stufentabelle> : catalog.HasChain(k + def.ChainKeySuffix) ? <kurzer Name der Kettentabelle> : null)))` → null: nichts (Log). Die Kettenprüfung gilt immer, auch bei „Stufe in bestehender Kette“ (einfacher, und der Schlüssel bleibt für einen späteren Wechsel frei); `TakenKeys` in Lauf A bleibt das Netz für alles andere.
5. `WriteCopyAsync`: Vorlage bauen (`s.Builder.Build(levelId, o, null, ct)`), `Rename` auf `Key`; bei neuer Kette und `catalog.ChainOf(template)` nicht null: Kette bauen und auf `ChainIdFor(def, Key)` umbenennen; `Compose(def, levelCopy, chainCopy, answer.After, wizards.NameSuffix)`. Beschreibung `"New building: <key>"`. `stillValid` = `() => true` (das Projekt prüft `WriteCopyAsync`).
6. Erfolg: `var shown = new ObjectRef(LevelTable, [Key], null)`, `tab = OpenObject(shown)`, `RememberOrigin(step, tab, shown)`, `tab.SetApplyResult(plan.Summary, angelegte Dateien, 0, notes)`, `await apply.CheckAfterWriteAsync()`.

Sperrgrund und Befehle: `ObjectWizardController` hört auf `projects.Changed`, `projects.OperationStateChanged` und `objects.Changed`, meldet dann `PropertyChanged` für beide Sperrgründe und ruft `RaiseCanExecuteChanged` der Befehle. Das Menü bindet `Header`, `Command` und `ToolTip` (Sperrgrund, `ToolTipService.ShowOnDisabled`) an `Wizards.*` des `MainViewModel`; `App.xaml.cs` erzeugt den Controller nach dem `ObjectApplyController` und setzt `viewModel.Wizards`.

Dialog-Aufbau (`NewBuildingWindow`, Breite 560, modal, Owner Hauptfenster): Volk (ComboBox, Anzeige `Name ?? Key`), Filter (TextBox), Vorlagen (ListBox, Anzeige `Name — Key` bzw. `Key`), Schlüssel (TextBox), Kette: RadioButton „Neue Kette“ und „Stufe in bestehender Kette nach“ mit ComboBox der Stufen (`Name ?? LevelKey`, dazu `level`), Fehlerzeile (`Theme.Error`), OK (`IsDefault`, `IsEnabled = CanAccept`), Abbrechen (`IsCancel`). Code-behind nur Verdrahtung wie `NewTableWindow`.

Texte (en / de):
- `menu.tools.newBuilding` „New building“ / „Neues Gebäude“
- `objects.wizard.building.title` „New building“ / „Neues Gebäude“
- `objects.wizard.subculture` „Race“ / „Volk“
- `objects.wizard.filter` „Filter“ / „Filter“
- `objects.wizard.template` „Template“ / „Vorlage“
- `objects.wizard.key` „Key“ / „Schlüssel“
- `objects.wizard.newChain` „New chain“ / „Neue Kette“
- `objects.wizard.existingChain` „Stage in the existing chain after“ / „Stufe in der bestehenden Kette nach“
- `objects.wizard.noTemplate` „Choose a template.“ / „Wähle eine Vorlage.“
- `objects.wizard.loading` „Loading the templates“ / „Vorlagen werden geladen“
- `objects.wizard.missing` „The game profile has no wizard for this.“ / „Das Spielprofil hat für das keinen Assistenten.“
- `objects.wizard.schemaMismatch` „The schema does not know: {0}“ / „Das Schema kennt nicht: {0}“

- [ ] **Step 1: Failing Tests**

```csharp
// NewBuildingViewModelTests (Katalog aus Mini-Daten wie BuildingWizardTests, über eine Hilfe in ObjectsTestSchema)
[Fact] public void The_preselected_subculture_lists_its_templates()
[Fact] public void The_key_follows_the_template_until_the_user_types()
[Fact] public void The_stage_list_preselects_the_last_stage()
[Fact] public void A_template_without_chain_allows_only_a_new_chain()
[Fact] public void An_existing_level_key_is_refused()
[Fact] public void A_key_whose_chain_key_exists_is_refused()          // Schlüssel "ch1" mit ChainKeySuffix "" bzw. passendem Suffix → Grund nennt die Kettentabelle
[Fact] public void The_result_names_template_key_and_stage()

// ObjectWizardControllerTests (Test-Spielordner mit Mini-Gebäude-Schema, Vanilla-Pack, wizards.json im Test-Profilordner)
[Fact] public async Task A_new_building_with_a_new_chain_writes_level_variant_and_chain_in_one_step_and_opens_it()
[Fact] public async Task A_new_building_as_a_stage_writes_one_upgrade_row_from_the_chosen_stage()
[Fact] public async Task The_name_of_the_new_building_ends_with_the_suffix()
[Fact] public async Task Undo_of_a_new_building_removes_the_level_and_the_chain()
[Fact] public async Task Cancelling_the_dialog_writes_nothing()
[Fact] public async Task The_wizard_is_disabled_without_a_project_and_without_a_wizard_file()
[Fact] public async Task The_shown_building_preselects_its_subculture()
```

- [ ] **Step 2: RED prüfen:** `--filter "FullyQualifiedName~NewBuildingViewModelTests"` und `--filter "FullyQualifiedName~ObjectWizardControllerTests"` → Kompilierfehler.
- [ ] **Step 3: Implementieren.** Festlegungen in die Spec (Stufenwahl im Dialog; Name nur über `nameText`; neues Gebäude öffnet in neuem Tab).
- [ ] **Step 4: GREEN:** die beiden Klassen und `AppTextsTests`, `MainWindowTests` je einzeln grün.
- [ ] **Step 5: Commit** `feat: add the new building wizard to the tools menu`

---

### Task 7: Assistent „Neuer Trait“ (Objects und App)

Spec 5.8 „Neuer Trait“, Annahme 3 für Traits.

**Files:**
- Create: `src/TotalWarModTool.Objects/TraitWizard.cs`
- Create: `src/TotalWarModTool.App/Objects/NewTraitViewModel.cs`, `NewTraitWindow.xaml`, `NewTraitWindow.xaml.cs`
- Modify: `ObjectWizardController.cs`, `IObjectDialogs.cs`, `ObjectDialogs.cs`, `MainWindow.xaml` (Menüpunkt unter „Neues Gebäude“), AppTexts, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.Objects.Tests/TraitWizardTests.cs` (neu), `ObjectsRealDataTests.cs`, `tests/TotalWarModTool.App.Tests/NewTraitViewModelTests.cs` (neu), `ObjectWizardControllerTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace TotalWarModTool.Objects;

public static class TraitWizard
{
    /// <summary>Every row of the trait table; Name = loc text of the first key (ordinal) with the prefix
    /// "<NameText.Table without _tables>_<NameText.Column>_<trait key>_"; sorted by Name ?? Key (OrdinalIgnoreCase, then Key ordinal).</summary>
    public static IReadOnlyList<WizardChoice> Templates(TraitWizardDefinition definition, MergedTable traits, LocTable loc);

    public static CopyResult Compose(TraitWizardDefinition definition, CopyResult traitCopy, string nameSuffix);   // AppendToTexts(NameText)
}
```

```csharp
namespace TotalWarModTool.App;

public sealed record NewTraitInput(IReadOnlyList<WizardChoice> Templates, Func<string, string?> ValidateKey);
public sealed record NewTraitAnswer(string Template, string Key);

public interface IObjectDialogs { NewTraitAnswer? AskNewTrait(NewTraitInput input); }

public sealed class NewTraitViewModel : ObservableObject   // Filter, FilteredTemplates, SelectedTemplate, Key, Error, CanAccept, Result wie NewBuildingViewModel
{
    public NewTraitViewModel(AppTexts texts, NewTraitInput input);
}

public sealed class ObjectWizardController
{
    public ICommand NewTraitCommand { get; }
    public string? NewTraitDisabledReason { get; }   // wie NewBuildingDisabledReason, mit Trait-Definition
    public Task NewTraitAsync();                     // Ablauf wie NewBuildingAsync ohne Volk und Kette; Beschreibung "New trait: <key>"
}
```

`NewTraitAsync`: Runner „objects.wizard.loading“ lädt `TraitWizard.Templates(def, stores.Tables.Get(TraitTable, order, ct), stores.Loc.Get(order, ct))`. `ValidateKey = key => ObjectKeyRules.Problem(texts, key, k => templates enthält k ? <kurzer Tabellenname> : null)`. Kopie: Trait bauen, `Rename` auf den Schlüssel (Stufen mit Präfix werden Unterobjekte, Spec 5.8 Regel 3), `TraitWizard.Compose`. Erfolg wie beim Gebäude: neuer Tab, Herkunft, Ergebniszeile, `CheckAfterWriteAsync`.

Texte: `menu.tools.newTrait` „New trait“ / „Neuer Trait“, `objects.wizard.trait.title` „New trait“ / „Neuer Trait“.

- [ ] **Step 1: Failing Tests**

```csharp
// TraitWizardTests
[Fact] public void Templates_are_named_by_the_text_of_their_first_level()
[Fact] public void A_trait_without_level_text_is_listed_by_key()
[Fact] public void Compose_appends_the_suffix_to_the_level_names()
// ObjectsRealDataTests
[RealDataFact] public void The_trait_wizard_copies_a_trait_with_its_levels_without_taken_keys()
// NewTraitViewModelTests
[Fact] public void The_key_follows_the_template_until_the_user_types()
[Fact] public void An_existing_trait_key_is_refused()
// ObjectWizardControllerTests (Mini-Trait-Schema)
[Fact] public async Task A_new_trait_writes_trait_levels_and_effects_in_one_step_and_opens_it()
```

- [ ] **Step 2: RED prüfen** (Objects-Tests, dann die App-Klassen einzeln).
- [ ] **Step 3: Implementieren.**
- [ ] **Step 4: GREEN:** Objects-Tests, Echtdaten, die App-Klassen einzeln und `AppTextsTests` grün.
- [ ] **Step 5: Commit** `feat: add the new trait wizard to the tools menu`

---

### Task 8: Abschluss: Changelog, Backlog, Spec, Checkliste, voller Lauf

**Files:**
- Modify: `CHANGELOG.md` („Unreleased“, „Neu“: ein Eintrag für „Kopieren als“ und die beiden Assistenten, aus Nutzersicht)
- Modify: `Backlog.txt` (neuer Abschnitt `== Aus Teil 4.2a Plan 4 zurückgestellt ==` mit allem, was die Tasks zurückgestellt haben; mindestens: Vorlagen über Fraktions-Varianten; Kategorie-Datei für Ketten (die AI-Zeilen der Kette werden mitkopiert); Schlüsselprüfung im Dialog auf dem UI-Thread, falls die Tabelle nicht im Cache ist; weitere Assistenten nach demselben Muster)
- Modify: `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (Abschnitt 9: Plan 4 „Umgesetzt, Abnahme offen“; Abschnitt 10: Annahme 3 mit den Echtdaten-Ergebnissen der Tasks 1, 4 und 7; Annahmen 1, 7 und 8 bleiben bis zur Abnahme mit Spielstart offen)

- [ ] **Step 1:** Changelog, Backlog, Spec schreiben.
- [ ] **Step 2:** `build.bat` ohne Warnung.
- [ ] **Step 3: Commit** `docs: record plan 4 of Teil 4.2a in the changelog, the backlog and the spec`
- [ ] **Step 4:** Den Nutzer um den vollen Lauf bitten (`test.bat`, nichts anderes baut) und die manuelle Checkliste unten übergeben.

## Manuelle Checkliste (Abnahme mit dem Nutzer)

1. Objekt `wh_main_emp_barracks_2` öffnen, „Kopieren als“ im Kopf: Dialog schlägt `wh_main_emp_barracks_2_copy` vor. Leer, mit Leerzeichen und `wh_main_emp_barracks_3` werden mit Grund abgelehnt.
2. Mit `flo_barracks_copy` bestätigen: Der Tab zeigt das neue Objekt (Zurück führt zur Vorlage). Kultur-Variante, Effekte und erlaubte Einheiten sind Projektzeilen. Die Einheiten-Zeilen haben neue Zahlen in `key`. Die Ergebniszeile nennt die neuen Dateien und „nicht kopiert: 3 Zeilen in technologies_tables“.
3. Die Vorlage `wh_main_emp_barracks_2` öffnen: Ihre erlaubten Einheiten sind unverändert Vanilla (nicht überschrieben).
4. Strg+Z: Alle Zeilen der Kopie sind weg, der Tab zeigt „nicht gefunden“. Strg+Y bringt sie zurück.
5. Text im Block ändern, ohne zu übernehmen, dann „Kopieren als“: Die Frage „Übernehmen, Verwerfen, Abbrechen“ erscheint; Abbrechen kopiert nichts.
6. Werkzeuge, „Neues Gebäude“ ohne aktives Projekt: gesperrt, Tooltip nennt den Grund.
7. Mit aktivem Projekt und offenem Gebäude des Imperiums: Volk ist vorausgewählt. Vorlage `wh_main_emp_barracks_3`, Schlüssel `flo_emp_barracks_4`, „Stufe in der bestehenden Kette nach“ mit `wh_main_emp_barracks_3` (vorausgewählt). OK: neuer Tab mit dem Gebäude, `level` 3, Abschnitt „upgrades from wh_main_emp_barracks_3“, Name endet auf „ (copy)“.
8. Noch ein Gebäude mit „Neue Kette“ (`flo_emp_barracks_new`): `chain = flo_emp_barracks_new_chain`, `level 0`, keine Upgrade-Zeilen. Die Kette öffnen (Strg+Klick auf `chain`): Verfügbarkeit und Spieltexte sind kopiert.
9. Werkzeuge, „Neuer Trait“: Vorlage `wh2_dlc09_trait_settra_title`, Schlüssel `flo_trait`. Trait, 3 Stufen und ihre Effekte sind Projektzeilen; die Stufennamen enden auf „ (copy)“.
10. Pack bauen und installieren, Spiel starten, Imperium-Kampagne: Das Gebäude aus Punkt 7 ist als Stufe 4 der Kaserne baubar, das Gebäude aus Punkt 8 erscheint als eigene Kette (Annahme 1 und 3). Das Spiel startet ohne Fehler, obwohl die neue Kette die AI-Zeilen der Vorlage-Kette mitbringt (Annahme 7). Die Vanilla-Technologien der Kaserne (`wh_main_tech_emp_barracks_4` bis `_6`) lassen sich wie vorher erforschen, obwohl die Kopie in `technology_required_building_levels_junctions` steht (Annahme 8). Nach dem Test wieder deinstallieren.
11. Dunkles Theme und Sprache Deutsch: Dialoge und Menüpunkte lesbar und übersetzt.
