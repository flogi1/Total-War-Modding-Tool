# Interface-Rework, Plan 1: Leisten und Kachelzeile

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Menü auf Grundpunkte reduzieren, Kachelzeile und kleine Werkzeugleiste nach PMT-Vorbild einbauen (Kataloge, Einstellungen, Ziehen, Positionen), Zeilenbefehle in eine Dokument-Leiste des Tabellen-Tabs verschieben, Einstellungsseiten Layout, Kachelzeile und Werkzeugleiste.

**Architecture:** Kataloge und Listenlogik (`TileCatalog`, `ToolbarCatalog`, `ToolbarLayout`, `ReorderList`) sowie die neuen Felder liegen in `TotalWarModTool.Settings` (pur, getestet). Der PMT-Shell-Code (`TileBar`, `ToolbarBuilder`, `ReorderDragHost`, `ToolbarItemView`, `ToolbarPositionGrip`, `ToolIcons`) und `ReorderListBox` werden als Kopie nach `src/TotalWarModTool.App/Shell/` bzw. `SettingsUi/` übernommen und nach Checkliste A2 angepasst: keine statischen Texte (alle Beschriftungen kommen als Funktionen oder `AppTexts` herein), `GlobalSettings` statt `AppSettings`, Enums statt Strings für Größe und Position. Das Hauptfenster baut Kachelzeile und Werkzeugleiste in Code aus Katalog und Einstellungen (`MainWindow.Shell.cs`, reine Verdrahtung); die Zuordnung Kennung → Befehl liegt im `MainViewModel`.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9.2, STA-Helper `Sta` der App-Tests. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-16-interface-rework.md` (Abschnitte 6.1 bis 6.3, 6.5, 6.9, 8, 9, 10). PMT-Quelle (read-only Kopie im Repo): `docs/vorlage-pmt/`. Jeder Vorlagenpfad „PMT `src/...`“ / „PMT `tests/...`“ in diesem Plan liegt dort unter demselben Pfad (z. B. `docs/vorlage-pmt/src/ParadoxModTool.Shell/TileBar.cs`). Die Kopie wird nie gebaut und nie geändert; sie enthält nur `.cs`-Dateien.

## Global Constraints

- Code, Bezeichner und Kommentare Englisch; Commit-Nachrichten Englisch; Plan und Spec Deutsch.
- `Directory.Build.props`: `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, `LangVersion` 12.0. Keine Build-Einstellungen in csproj-Dateien.
- Keine neuen NuGet-Pakete. Keine statischen Singletons für Services. Keine Logik im Code-behind außer View-Verdrahtung.
- UI-Texte nur über `AppTexts` (`T(key, fallback)`, Inline-Fallback Englisch); jeder neue Key in `locales/en.json` und `locales/de.json`; `AppTextsTests.Locale_files_are_complete` bleibt grün.
- Farben nur über Theme-Tokens (`DynamicResource "Theme.<Key>"`, Tokens aus `ThemePalette.All`). Die einzige feste Farbe ist das rote Abzeichen (`TileBar.BadgeBrush`, wie in PMT).
- Kennungen der Kataloge werden in `settings.json` gespeichert: nie umbenennen.
- `settings.json` bleibt `SchemaVersion` 1; fehlende Felder ergeben die Defaults. Neue Felder in diesem Plan nur die, die dieser Plan nutzt (`PathBarVisible`, `FilterBarVisible`, `ColorizeWholeTab` aus Spec 6.9 kommen mit Plan 3).
- Libraries test-first: Test schreiben, RED sehen, implementieren, GREEN, Commit. App-Tests laufen über `Sta.Run` / `Sta.RunAsync` (STA-Thread).
- Die bestehende Test-Suite bleibt grün. Baseline vor Task 1: `build.bat` und `test.bat` laufen durch; Ergebnis im ersten Commit-Text nennen.
- Subagenten führen nur Library-Tests aus (`dotnet test tests/TotalWarModTool.Settings.Tests`); App-Tests (`tests/TotalWarModTool.App.Tests`) laufen einmal am Ende mit dem Nutzer (Memory-Regel). Bei App-Tasks heißt „Test läuft“: Build grün und der Test ist geschrieben; der Lauf kommt am Ende.
- Abweichungen von der Spec im selben Commit in der Spec nachtragen (Abschnitt „Änderungen beim Schreiben von Plan 1“).

## Review Focus

1. Eine gespeicherte `settings.json` mit einer Kennung, die der Katalog nicht kennt (`"tileOrder":["newBuilding","gone","newTrait"]`): Laden darf nicht scheitern, die Kennung verschwindet still, die Reihenfolge der bekannten bleibt (Test in Task 2).
2. `toolbarOrder` ohne Trennlinien und ohne einige Kennungen: fehlende Kennungen werden an ihrer Katalogposition ergänzt, keine Trennlinie wird erfunden (Test in Task 2).
3. Fenster ohne Projekte (`Projects == null`): die Werkzeugleiste zeigt nur die Einträge, deren Befehl existiert, und Trennlinien fallen daneben zusammen; kein `NullReferenceException` beim Aufbau (Test in Task 5).
4. Position „Links“ mit eingeschalteter Beschriftung: senkrecht nie Beschriftung, Tooltip trägt Name und Kürzel (Test in Task 5).
5. Kachel „Neues Gebäude“, deren Befehl gerade nicht ausführbar ist (kein aktives Projekt): Kachel deaktiviert, Tooltip nennt den Grund aus `Wizards.NewBuildingDisabledReason`, und sie wird wieder aktiv, sobald `CanExecuteChanged` feuert, ohne Neuaufbau der Leiste (Test in Task 5).

---

### Task 1: Kataloge und Listenlogik in `Settings`

**Files:**
- Create: `src/TotalWarModTool.Settings/TileCatalog.cs`, `src/TotalWarModTool.Settings/ToolbarCatalog.cs`, `src/TotalWarModTool.Settings/ToolbarLayout.cs`, `src/TotalWarModTool.Settings/ReorderList.cs`
- Test: `tests/TotalWarModTool.Settings.Tests/TileCatalogTests.cs`, `ToolbarCatalogTests.cs`, `ToolbarLayoutTests.cs`, `ReorderListTests.cs`
- Vorlage: PMT `src/ParadoxModTool.Settings/{TileCatalog,ToolbarCatalog,ToolbarLayout,ReorderList}.cs`

**Interfaces:**
- Produces:
  - `public static class TileIds { public const string NewBuilding = "newBuilding"; public const string NewTrait = "newTrait"; }`
  - `public sealed record TileItem(string Id, bool DefaultVisible, bool HasMenu);`
  - `public static class TileCatalog { IReadOnlyList<TileItem> All; IReadOnlyList<string> DefaultOrder; bool IsKnown(string id); TileItem? Find(string id); }`
  - `public static class ToolbarItemIds { NewProject="newProject", OpenProject="openProject", ImportPack="importPack", NewTable="newTable", NewLoc="newLoc", Save="save", SaveAll="saveAll", Undo="undo", Redo="redo", RefreshPacks="refreshPacks", Build="build", BuildAndInstall="buildAndInstall" }`
  - `public static class ToolbarCatalog { const string SeparatorPrefix = "sep:"; IReadOnlyList<string> All; IReadOnlyList<string> DefaultOrder; bool IsKnown(string id); bool IsSeparator(string? id); string Separator(int n); }`
  - `public static class ToolbarLayout { IReadOnlyList<string> VisibleSequence(IReadOnlyList<string> order, Func<string, bool> isVisible); }`
  - `public static class ReorderList { bool MoveNextTo(IList<string> order, string id, string targetId); bool Swap(IList<string> order, int i, int j); List<string> Complete(IEnumerable<string>? order, IReadOnlyList<string> catalog, Func<string, bool>? isExtra = null); }`

`ToolbarCatalog.DefaultOrder` ist genau:

```
newProject openProject importPack newTable newLoc sep:1 save saveAll sep:2 undo redo sep:3 refreshPacks sep:4 build buildAndInstall
```

Kein `ToolbarGroup`, kein `TileBadge`, kein `RequiresGuiTools` (PMT-Reste ohne Nutzer hier). Kommentare auf Englisch neu schreiben.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// TileCatalogTests
[Fact] public void The_catalog_lists_building_and_trait_in_this_order() =>
    Assert.Equal(["newBuilding", "newTrait"], TileCatalog.DefaultOrder);
[Fact] public void Both_tiles_are_visible_by_default_and_have_no_menu() =>
    Assert.All(TileCatalog.All, t => Assert.Equal((true, false), (t.DefaultVisible, t.HasMenu)));
[Fact] public void Find_and_IsKnown_reject_unknown_ids() =>
    Assert.Equal((false, (TileItem?)null), (TileCatalog.IsKnown("gone"), TileCatalog.Find("gone")));

// ToolbarCatalogTests
[Fact] public void The_default_order_has_twelve_entries_and_four_separators() =>
    Assert.Equal("newProject openProject importPack newTable newLoc sep:1 save saveAll sep:2 undo redo sep:3 refreshPacks sep:4 build buildAndInstall",
        string.Join(' ', ToolbarCatalog.DefaultOrder));
[Theory, InlineData("sep:1", true), InlineData("sep:12", true), InlineData("sep:", false), InlineData("sep:0", false), InlineData("sep:x", false), InlineData(null, false)]
public void IsSeparator_accepts_only_sep_followed_by_a_positive_number(string? id, bool expected) => Assert.Equal(expected, ToolbarCatalog.IsSeparator(id));
[Fact] public void Every_id_of_the_default_order_is_known_or_a_separator() =>
    Assert.All(ToolbarCatalog.DefaultOrder, id => Assert.True(ToolbarCatalog.IsKnown(id) || ToolbarCatalog.IsSeparator(id)));

// ToolbarLayoutTests
[Fact] public void Hidden_items_drop_and_separators_collapse_at_start_end_and_in_a_row() =>
    Assert.Equal(["save", "sep:2", "undo"], ToolbarLayout.VisibleSequence(["sep:1", "newProject", "save", "sep:2", "sep:3", "undo", "sep:4"], id => id != "newProject"));
[Fact] public void An_order_of_only_separators_is_empty() =>
    Assert.Empty(ToolbarLayout.VisibleSequence(["sep:1", "sep:2"], _ => true));

// ReorderListTests
[Fact] public void MoveNextTo_puts_a_later_item_before_the_target_and_an_earlier_item_behind_it()
{
    var a = new List<string> { "a", "b", "c" }; Assert.True(ReorderList.MoveNextTo(a, "c", "a")); Assert.Equal(["c", "a", "b"], a);
    var b = new List<string> { "a", "b", "c" }; Assert.True(ReorderList.MoveNextTo(b, "a", "c")); Assert.Equal(["b", "c", "a"], b);
}
[Fact] public void MoveNextTo_rejects_the_same_target_and_unknown_ids()
{
    var list = new List<string> { "a", "b" };
    Assert.False(ReorderList.MoveNextTo(list, "a", "a")); Assert.False(ReorderList.MoveNextTo(list, "x", "a")); Assert.Equal(["a", "b"], list);
}
[Fact] public void Complete_keeps_known_ids_in_order_drops_unknown_and_duplicates_and_appends_missing() =>
    Assert.Equal(["b", "sep:1", "a", "c"], ReorderList.Complete(["b", "gone", "sep:1", "b", "a"], ["a", "b", "c"], ToolbarCatalog.IsSeparator));
[Fact] public void Complete_of_null_is_the_catalog() => Assert.Equal(["a", "b"], ReorderList.Complete(null, ["a", "b"]));
```

- [ ] **Step 2: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.Settings.Tests --filter "FullyQualifiedName~Catalog|FullyQualifiedName~ToolbarLayout|FullyQualifiedName~ReorderList"`
Expected: Compile-Fehler (Typen fehlen).

- [ ] **Step 3: Die vier Dateien aus der PMT-Vorlage übernehmen**

`ToolbarLayout.VisibleSequence`, `ReorderList.*`, `ToolbarCatalog.IsSeparator/Separator` unverändert (Algorithmus wie PMT). Kataloge mit den TWMT-Kennungen oben. Namespace `TotalWarModTool.Settings`.

- [ ] **Step 4: GREEN prüfen**

Run: wie Step 2. Expected: alle grün.

- [ ] **Step 5: Commit**

```bash
git add src/TotalWarModTool.Settings tests/TotalWarModTool.Settings.Tests
git commit -m "feat(settings): add tile and toolbar catalogs with the reorder helpers"
```

---

### Task 2: Neue Felder in `GlobalSettings` und im Store

**Files:**
- Modify: `src/TotalWarModTool.Settings/GlobalSettings.cs`, `src/TotalWarModTool.Settings/GlobalSettingsStore.cs`
- Test: `tests/TotalWarModTool.Settings.Tests/GlobalSettingsBarsTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces in `GlobalSettings` (jede Änderung meldet `Changed` mit dem Property-Namen, nur bei echter Änderung):
  - `public enum TileSize { Large, Medium, Compact }`, `public enum ToolbarPosition { Top, Left, Right, Bottom }` (eigene Datei `BarEnums.cs` oder in `GlobalSettings.cs`, Builder-Wahl)
  - `IReadOnlyList<string> TileOrder { get; set; }` — Setter: `ReorderList.Complete(value, TileCatalog.DefaultOrder)`; `Changed("TileOrder")` nur wenn die Folge sich ändert.
  - `IReadOnlyCollection<string> HiddenTiles { get; }`, `bool IsTileVisible(string id)`, `void SetTileVisible(string id, bool visible)` → `Changed("HiddenTiles")`
  - `IReadOnlyCollection<string> IconOnlyTiles { get; }`, `bool IsTileIconOnly(string id)`, `void SetTileIconOnly(string id, bool iconOnly)` → `Changed("IconOnlyTiles")`
  - `TileSize TileSize { get; set; }` (Default `Large`), `bool TileLabelsVisible` (true), `bool TileBarVisible` (true)
  - `IReadOnlyList<string> ToolbarOrder { get; set; }` — Setter: `ReorderList.Complete(value, ToolbarCatalog.All, ToolbarCatalog.IsSeparator)`; Default `ToolbarCatalog.DefaultOrder`.
  - `IReadOnlyCollection<string> HiddenToolbarItems { get; }`, `bool IsToolbarItemVisible(string id)`, `void SetToolbarItemVisible(string id, bool visible)` → `Changed("HiddenToolbarItems")`
  - `ToolbarPosition ToolbarPosition { get; set; }` (Default `Top`), `bool ToolbarLabelsVisible` (true)
  - `int SettingsCategory { get; set; }` (Default 0; negative Werte werden 0)
- Store: Datei-Felder `TileOrder`, `HiddenTiles`, `IconOnlyTiles` (`List<string>?`), `TileSize`, `ToolbarPosition` (Text wie `Theme`, unbekannt → Default), `TileLabelsVisible`, `TileBarVisible`, `ToolbarLabelsVisible` (`bool?`), `ToolbarOrder` (`List<string>?`), `HiddenToolbarItems` (`List<string>?`), `SettingsCategory` (`int?`). Unbekannte Kennungen in `HiddenTiles`/`HiddenToolbarItems` fallen beim Laden weg.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Defaults_are_the_catalog_orders_everything_visible_large_top_labels_on()
{
    var s = new GlobalSettings();
    Assert.Equal(TileCatalog.DefaultOrder, s.TileOrder); Assert.Equal(ToolbarCatalog.DefaultOrder, s.ToolbarOrder);
    Assert.Equal((TileSize.Large, ToolbarPosition.Top, true, true, true, 0), (s.TileSize, s.ToolbarPosition, s.TileLabelsVisible, s.TileBarVisible, s.ToolbarLabelsVisible, s.SettingsCategory));
    Assert.True(s.IsTileVisible("newTrait")); Assert.True(s.IsToolbarItemVisible("build")); Assert.False(s.IsTileIconOnly("newTrait"));
}
[Fact] public void Setting_an_order_normalizes_it_and_raises_Changed_once()
{
    var s = new GlobalSettings(); var changes = new List<string>(); s.Changed += (_, n) => changes.Add(n);
    s.TileOrder = ["newTrait", "gone", "newTrait"];
    s.TileOrder = ["newTrait"];                                  // same normalized result: no event
    Assert.Equal(["newTrait", "newBuilding"], s.TileOrder); Assert.Equal(["TileOrder"], changes);
}
[Fact] public void Toolbar_order_keeps_separators_and_appends_missing_ids_at_the_end()
{
    var s = new GlobalSettings { ToolbarOrder = ["build", "sep:7", "save"] };
    Assert.Equal(["build", "sep:7", "save", "newProject", "openProject", "importPack", "newTable", "newLoc", "saveAll", "undo", "redo", "refreshPacks", "buildAndInstall"], s.ToolbarOrder);
}
[Fact] public void Hidden_and_icon_only_sets_raise_Changed_only_on_a_real_change()
{
    var s = new GlobalSettings(); var changes = new List<string>(); s.Changed += (_, n) => changes.Add(n);
    s.SetTileVisible("newTrait", false); s.SetTileVisible("newTrait", false); s.SetTileIconOnly("newTrait", true); s.SetToolbarItemVisible("undo", false);
    Assert.Equal(["HiddenTiles", "IconOnlyTiles", "HiddenToolbarItems"], changes);
    Assert.Equal((false, true, false), (s.IsTileVisible("newTrait"), s.IsTileIconOnly("newTrait"), s.IsToolbarItemVisible("undo")));
}
[Fact] public void The_store_round_trips_every_bar_field()
{
    using var dir = new TempDir(); var store = new GlobalSettingsStore(dir.Path);
    var s = new GlobalSettings { TileOrder = ["newTrait"], TileSize = TileSize.Compact, TileLabelsVisible = false, TileBarVisible = false,
        ToolbarOrder = ["build", "sep:2", "save"], ToolbarPosition = ToolbarPosition.Left, ToolbarLabelsVisible = false, SettingsCategory = 3 };
    s.SetTileVisible("newBuilding", false); s.SetTileIconOnly("newTrait", true); s.SetToolbarItemVisible("undo", false);
    store.Save(s); var l = store.Load().Settings;
    Assert.Equal(s.TileOrder, l.TileOrder); Assert.Equal(s.ToolbarOrder, l.ToolbarOrder);
    Assert.Equal((TileSize.Compact, false, false, ToolbarPosition.Left, false, 3), (l.TileSize, l.TileLabelsVisible, l.TileBarVisible, l.ToolbarPosition, l.ToolbarLabelsVisible, l.SettingsCategory));
    Assert.Equal((false, true, false), (l.IsTileVisible("newBuilding"), l.IsTileIconOnly("newTrait"), l.IsToolbarItemVisible("undo")));
}
[Fact] public void An_old_file_without_the_fields_loads_with_defaults_and_unknown_ids_are_dropped()
{
    using var dir = new TempDir();
    File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"language":"de","tileOrder":["newBuilding","gone","newTrait"],"hiddenTiles":["gone"],"tileSize":"Huge","toolbarPosition":"Middle"}""");
    var l = new GlobalSettingsStore(dir.Path).Load();
    Assert.Equal(LoadStatus.Loaded, l.Status);
    Assert.Equal(["newBuilding", "newTrait"], l.Settings.TileOrder); Assert.Empty(l.Settings.HiddenTiles);
    Assert.Equal((TileSize.Large, ToolbarPosition.Top, ToolbarCatalog.DefaultOrder), (l.Settings.TileSize, l.Settings.ToolbarPosition, l.Settings.ToolbarOrder));
}
```

- [ ] **Step 2: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.Settings.Tests --filter "FullyQualifiedName~GlobalSettingsBars"`
Expected: Compile-Fehler.

- [ ] **Step 3: Felder im Modell und im Store ergänzen**

Muster wie `Theme`/`SelectedGame` (private Felder, Vergleich, `Changed`). Sets als `HashSet<string>(StringComparer.Ordinal)`; `SetMember`-Helfer wie in PMT `AppSettings`. Enum-Parsing über Namensvergleich wie `ParseTheme` (eine generische `ParseEnum<T>(text, fallback)`). Beim Laden: `TileOrder`/`ToolbarOrder` über die Setter (normalisieren), Hidden-Mengen nur mit `TileCatalog.IsKnown` bzw. `ToolbarCatalog.IsKnown`.

- [ ] **Step 4: GREEN prüfen, ganze Settings-Suite**

Run: `dotnet test tests/TotalWarModTool.Settings.Tests`
Expected: alle grün, inklusive der alten `GlobalSettingsStoreTests`.

- [ ] **Step 5: Commit**

```bash
git add src/TotalWarModTool.Settings tests/TotalWarModTool.Settings.Tests
git commit -m "feat(settings): store the tile bar and toolbar layout"
```

---

### Task 3: Shell-Komponenten (Kopie aus PMT) mit Tests

**Files:**
- Create: `src/TotalWarModTool.App/Shell/ToolIcons.cs`, `ReorderDragHost.cs`, `ToolbarItemView.cs`, `ToolbarPositionGrip.cs`, `ToolbarBuilder.cs`, `TileBar.cs`
- Test: `tests/TotalWarModTool.App.Tests/Shell/ToolIconsTests.cs`, `ReorderDragHostTests.cs`, `ToolbarItemViewTests.cs`, `ToolbarPositionGripTests.cs`, `ToolbarBuilderTests.cs`, `TileBarTests.cs`; Helfer `tests/TotalWarModTool.App.Tests/Shell/Tree.cs` (Kopie PMT `Tree.cs`)
- Vorlage: PMT `src/ParadoxModTool.Shell/*.cs`, `tests/ParadoxModTool.Shell.Tests/*.cs`

**Interfaces:**
- Consumes: Task 1, Task 2 (`GlobalSettings`, `TileSize`, `ToolbarPosition`).
- Produces (Namespace `TotalWarModTool.App.Shell`):
  - `public static class ToolIcons { const double DefaultSize = 24; bool Has(string id); string? PathData(string id); FrameworkElement Create(string id, double size, string brushKey, string? disabledBrushKey = null); FrameworkElement CreateInForeground(string id, double size); }` — Pfade für alle 14 Kennungen (`TileIds` + `ToolbarItemIds`), 24×24-Minisprache; PMT-Pfade für `save`, `saveAll`, `undo`, `redo`, `new`→`newProject`, `open`→`openProject` übernehmen, die übrigen neu zeichnen (einfache Strichformen; `refreshPacks` Kreispfeil, `build` Hammer oder Zahnrad, `buildAndInstall` Hammer mit Pfeil nach unten, `importPack` Kiste mit Pfeil hinein, `newTable` Raster mit Plus, `newLoc` Sprechblase mit Plus, `newBuilding` Haus, `newTrait` Stern). Unbekannte Kennung → `Fallback`-Quadrat.
  - `public sealed class ReorderDragHost : Grid` — wie PMT, `DataFormat = "twmt-reorder-id"`.
  - `public static class ToolbarItemView { FrameworkElement Content(string id, string label, bool showLabel); FrameworkElement Separator(bool vertical); TextBlock? LabelOf(FrameworkElement content); bool SetLabel(FrameworkElement content, string label); }`
  - `public sealed class ToolbarPositionGrip : Button` — Konstruktor `(Func<ToolbarPosition> current, Func<ToolbarPosition, string> label, string toolTip)`; `event EventHandler<ToolbarPosition>? PositionChosen`; `ContextMenu BuildMenu()` mit vier Einträgen in Reihenfolge Top, Left, Right, Bottom, aktueller mit Häkchen.
  - `public sealed record ToolbarBuild(IReadOnlyList<string> Order, Func<string, bool> IsVisible, Func<string, FrameworkElement?> Element, bool Vertical, FrameworkElement? Grip = null, Action<string, string>? MoveNextTo = null);`
  - `public static class ToolbarBuilder { const double VerticalWidth = 44; const string ListKey = "toolbar"; Panel Build(ToolbarBuild build); IReadOnlyList<string> PlacedIds(Panel panel); }`
  - `public sealed record TileSource(Func<string, string> Label, Func<string, string> Tip, Func<string, bool> IsOffered, Func<string, bool> IsEnabled, Func<string, string?> DisabledTip, Func<string, ContextMenu?> Menu);`
  - `public sealed class TileBar : UserControl { const double LargeWidth = 104, LargeHeight = 78, MediumWidth = 84, MediumHeight = 62, CompactSize = 44; const string ListKey = "tiles"; void Bind(GlobalSettings settings, TileSource source); event EventHandler<string>? TileClicked; IReadOnlyList<string> VisibleIds; bool IsVisibleTile(string id); Button TileButton(string id); int BadgeOf(string id); void SetBadge(string id, int count); string? WarningOf(string id); void SetWarning(string id, string? tip); bool MoveTile(string dragged, string target); void Rebuild(); void RefreshState(); }`
    - `RefreshState()` ist neu gegenüber PMT: setzt für jede gezeigte Kachel `IsEnabled = source.IsEnabled(id)` und den Tooltip neu, ohne Neuaufbau.
    - Neu bauen bei `settings.Changed` mit Namen `TileOrder`, `HiddenTiles`, `TileSize`, `IconOnlyTiles`, `TileLabelsVisible`. Kein `Reloaded` (gibt es in TWMT nicht).

- [ ] **Step 1: PMT-Tests kopieren und auf TWMT umschreiben**

Alle Tests aus `ParadoxModTool.Shell.Tests` übernehmen (Namen beibehalten, snake_case erlaubt), `AppSettings` → `GlobalSettings`, `TileIds.Project` → `TileIds.NewBuilding`, `TileIds.CuTool` → `TileIds.NewTrait`, Strings `"Medium"` → `TileSize.Medium`, `Sta.Run` der App-Tests statt der PMT-`Sta`. `Tree.Descendants<T>` als Helfer kopieren. Dazu neu:

```csharp
// TileBarTests
[Fact] public void RefreshState_updates_enabled_and_tooltip_without_rebuilding() => Sta.Run(() =>
{
    var enabled = false;
    var bar = Bar(new GlobalSettings(), Source(enabled: _ => enabled));
    var tile = bar.TileButton(TileIds.NewBuilding);
    Assert.False(tile.IsEnabled); Assert.Equal("disabled", tile.ToolTip);
    enabled = true; bar.RefreshState();
    Assert.Same(tile, bar.TileButton(TileIds.NewBuilding)); Assert.True(tile.IsEnabled); Assert.Equal("T:newBuilding", tile.ToolTip);
});
// ToolIconsTests
[Fact] public void Every_catalog_id_has_a_drawable_icon() =>
    Assert.All(TileCatalog.DefaultOrder.Concat(ToolbarCatalog.All), id => Assert.True(ToolIcons.Has(id), id));
// ToolbarPositionGripTests
[Fact] public void Menu_lists_four_positions_with_the_current_one_checked() => Sta.Run(() =>
{
    var grip = new ToolbarPositionGrip(() => ToolbarPosition.Right, p => "L:" + p, "tip");
    var items = grip.BuildMenu().Items.OfType<MenuItem>().ToList();
    Assert.Equal(["L:Top", "L:Left", "L:Right", "L:Bottom"], items.Select(i => i.Header));
    Assert.Equal([false, false, true, false], items.Select(i => i.IsChecked));
});
```

- [ ] **Step 2: Build prüfen (RED)**

Run: `dotnet build tests/TotalWarModTool.App.Tests`
Expected: Compile-Fehler (Shell-Typen fehlen).

- [ ] **Step 3: Die sechs Dateien kopieren und anpassen**

Anpassungen je Datei: Namespace; `AppSettings` → `GlobalSettings`; `settings.TileSize == "Compact"` → `== TileSize.Compact`; `ToolbarPositionGrip` ohne `SettingsTexts` (Beschriftungen über die Konstruktor-Funktionen); `ReorderDragHost.DataFormat = "twmt-reorder-id"`; Kommentare Englisch; `TileBar.RefreshState()` ergänzen. `TileStyle()` baut auf `TryFindResource(typeof(Button))` auf (Theme-Knopfstil aus `Controls.xaml`). Theme-Tokens: `Theme.Separator`, `Theme.PanelBackground`, `Theme.Accent`, `Theme.WindowBackground`, `Theme.TextDisabled`, `Theme.TextMuted`, `Theme.Warning`, `Theme.Text` existieren alle in `ThemePalette`.

- [ ] **Step 4: Build grün, Tests geschrieben**

Run: `dotnet build tests/TotalWarModTool.App.Tests`
Expected: Build grün. (App-Tests laufen am Ende mit dem Nutzer.)

- [ ] **Step 5: Commit**

```bash
git add src/TotalWarModTool.App/Shell tests/TotalWarModTool.App.Tests/Shell
git commit -m "feat(app): add the tile bar, toolbar builder and reorder drag host from PMT"
```

---

### Task 4: Texte für Menü, Kacheln, Werkzeugleiste und Einstellungsseiten

**Files:**
- Create: `src/TotalWarModTool.App/AppTexts.Shell.cs`
- Modify: `src/TotalWarModTool.App/AppTexts.cs` (Menü), `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/AppTextsTests.cs` (bestehender Locale-Test), neu `ShellTextsTests.cs`

**Interfaces:**
- Produces in `AppTexts`:
  - Menü: `MenuHelp` (`menu.help`, "Help"), `MenuSettings` bleibt (Key `menu.settings`, Fallback "Settings"; alter Key `menu.tools.settings` aus beiden Locale-Dateien entfernen), `MenuOpenLogFolder` Key → `menu.help.openLogFolder`. `MenuTools` entfällt.
  - Kacheln: `string TileLabel(string id)` (`newBuilding` → `MenuToolsNewBuilding`, `newTrait` → `MenuToolsNewTrait`, sonst `id`), `string TileTip(string id)` (Keys `tile.newBuilding.tip` "Create a building from a template", `tile.newTrait.tip` "Create a trait from a template").
  - Werkzeugleiste: `string ToolbarLabel(string id)` → die heutigen Menütexte (`ProjectNewTitle`, `MenuProjectOpen`, `MenuProjectImport`, `FileNewTableTitle`, `FileNewLocTitle`, `MenuProjectSave`, `MenuProjectSaveAll`, `MenuEditUndo`, `MenuEditRedo`, `OperationRefreshPacks`, `BuildMessageTitle`, `ProjectActionBuildAndInstall`); `string? ToolbarGesture(string id)` → `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+Z`, `Ctrl+Y`, `F5` (neuer Key `menu.tools.refreshGesture`), `Ctrl+B`, `Ctrl+Shift+B`, sonst null; `string ToolbarTip(string id)` → `ToolbarToolTip(label, gesture)` oder nur label.
  - Grip: `ToolbarPositionTitle` (`toolbar.position`, "Toolbar position"), `ToolbarPositionLabel(ToolbarPosition)` (`toolbar.position.top` "Top", `.left` "Left", `.right` "Right", `.bottom` "Bottom").
  - Einstellungen: `SettingsCategoryLayout` ("Layout"), `SettingsCategoryTiles` ("Tile bar"), `SettingsCategoryToolbar` ("Toolbar"); Layout-Seite: `SettingsToolbarPosition` ("Position of the toolbar"), `SettingsToolbarLabels` ("Show labels in the toolbar"), `SettingsTileBarVisible` ("Show the tile bar"), `SettingsTileSize` ("Tile size"), `SettingsTileSizeLarge/Medium/Compact` ("Large", "Medium", "Compact, icon only"), `SettingsTileLabels` ("Show tile labels"); Kachelseite: `SettingsTileOrderHint` ("Drag a tile or use the arrows to change the order. Hidden tiles keep their place."), `SettingsTileVisible` ("Visible"), `SettingsTileIconOnly` ("Icon only"), `SettingsTileReset` ("Restore the default order"); Werkzeugleisten-Seite: `SettingsToolbarOrderHint` ("Drag an entry or use the arrows. Separators are entries of their own."), `SettingsToolbarSeparator` ("Separator"), `SettingsToolbarAddSeparator` ("Add separator"), `SettingsToolbarReset` ("Restore the default order"); `ReorderListBox`: `ReorderMoveUp` ("Move up"), `ReorderMoveDown` ("Move down"), `ReorderRemove` ("Remove").
  - Keys nach Muster `settings.<page>.<name>`, `toolbar.<name>`, `tile.<id>.tip`, `reorder.<name>`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void Every_toolbar_id_has_a_label_and_the_gestures_match_the_menu()
{
    var texts = new AppTexts(new UiText(Locales));   // wie in AppTextsTests: Locales = Ordner locales/ im Repo
    Assert.All(ToolbarCatalog.All, id => Assert.False(string.IsNullOrWhiteSpace(texts.ToolbarLabel(id)), id));
    Assert.Equal(("Ctrl+S", "F5", (string?)null), (texts.ToolbarGesture("save"), texts.ToolbarGesture("refreshPacks"), texts.ToolbarGesture("newProject")));
    Assert.Equal("Save (Ctrl+S)", texts.ToolbarTip("save")); Assert.Equal("New project", texts.ToolbarTip("newProject"));
}
[Fact] public void Both_tiles_have_label_and_tip() =>
    Assert.All(TileCatalog.DefaultOrder, id => Assert.NotEqual(texts.TileLabel(id), texts.TileTip(id)));
```

Dazu bleibt `AppTextsTests.Locale_files_are_complete` der Wächter für `en`/`de`.

- [ ] **Step 2: RED prüfen**

Run: `dotnet build tests/TotalWarModTool.App.Tests`
Expected: Compile-Fehler (`ToolbarLabel` fehlt).

- [ ] **Step 3: Texte anlegen, Locale-Dateien ergänzen**

Jeder neue Key in `en.json` und `de.json` (deutsche Übersetzung sinngemäß: „Kachelzeile“, „Werkzeugleiste“, „Trennlinie“, „Nur Symbol“, „Standardreihenfolge wiederherstellen“ …).

- [ ] **Step 4: Build grün**

Run: `dotnet build tests/TotalWarModTool.App.Tests`
Expected: grün.

- [ ] **Step 5: Commit**

```bash
git add src/TotalWarModTool.App/AppTexts*.cs locales tests/TotalWarModTool.App.Tests
git commit -m "feat(app): add the texts of the tile bar, toolbar and layout settings"
```

---

### Task 5: Hauptfenster: Menü, Kachelzeile, Werkzeugleiste

**Files:**
- Modify: `src/TotalWarModTool.App/MainWindow.xaml` (Menü, alte `Toolbar`-Border entfernen, `TileBar` und `ToolbarHost` einfügen), `src/TotalWarModTool.App/MainWindow.xaml.cs`, `src/TotalWarModTool.App/MainViewModel.cs`, `src/TotalWarModTool.App/App.xaml.cs` (Settings in den `MainWindow`-Konstruktor geben)
- Create: `src/TotalWarModTool.App/MainWindow.Shell.cs` (partial), `src/TotalWarModTool.App/ToolbarBinding.cs`
- Test: `tests/TotalWarModTool.App.Tests/MainWindowTests.cs` (vier Toolbar-Tests ersetzen), `MainViewModelTests.cs`, `TestGame.cs` (`Settings` an `MainWindow` geben)

**Interfaces:**
- Consumes: Tasks 1 bis 4.
- Produces:
  - `public sealed record ToolbarBinding(ICommand Command, string Label, string ToolTipPath);` — `ToolTipPath` ist der Property-Pfad am `MainViewModel` für den Tooltip (z. B. `"ProjectBuildToolTip"`), oder null, dann gilt `Texts.ToolbarTip(id)`.
  - `MainViewModel.ToolbarBinding? ToolbarBindingFor(string id)` — null, wenn der Befehl in dieser Zusammenstellung fehlt (`Projects`/`Documents`/`ProjectFiles` null). Tabelle:

    | Id | Command | ToolTipPath |
    |---|---|---|
    | newProject | `Projects.NewProjectCommand` | `Projects.ProjectsDisabledReason` |
    | openProject | `Projects.OpenProjectCommand` | `Projects.ProjectsDisabledReason` |
    | importPack | `Projects.ImportPackCommand` | `ProjectImportToolTip` |
    | newTable | `NewTableCommand` (nur mit `HasProjectFiles`) | `ProjectNewTableToolTip` |
    | newLoc | `NewLocCommand` (nur mit `HasProjectFiles`) | `ProjectNewFileToolTip` |
    | save / saveAll | `Documents.SaveCommand` / `SaveAllCommand` | `ProjectSaveToolTip` |
    | undo / redo | `Documents.UndoCommand` / `RedoCommand` | null |
    | refreshPacks | `RefreshCommand` | null |
    | build | `Projects.BuildActiveCommand` | `ProjectBuildToolTip` |
    | buildAndInstall | `Projects.BuildAndInstallActiveCommand` | `ProjectBuildAndInstallToolTip` |

    Ist der Tooltip-Wert null oder leer, zeigt der Knopf `Texts.ToolbarTip(id)` (Name und Kürzel). Das löst ein `IValueConverter`-freier Weg: `MainViewModel.string ToolbarToolTip(string id)` liefert den aktuellen Text, und das Fenster setzt ihn bei `PropertyChanged` des View-Models neu (Builder-Wahl: Binding mit `TargetNullValue` auf den statischen Text ist ebenso erlaubt).
  - `MainWindow(MainViewModel viewModel, GlobalSettings settings)`. Aufrufer: `App.xaml.cs` Zeile 221 und `MainWindowTests.Create`/`CreateWithProjects` (`game.Settings`); sonst erzeugt niemand das Fenster.
  - `MainViewModel.Wizards` hat einen Setter (`SetProperty`); das Fenster hängt sich bei `PropertyChanged("Wizards")` an die beiden Befehle und baut die Kachelzeile neu, weil die Wizards im Composition Root erst nach dem Fenster gesetzt werden können.
  - `MainWindow.Shell.cs`: `TileBarControl` (x:Name in XAML), `ToolbarHost` (Border, x:Name), `BuildToolbar()`, `ApplyToolbarPosition()`, Tile-Quelle: Label `Texts.TileLabel`, Tip `Texts.TileTip`, `IsOffered = viewModel.HasWizards`, `IsEnabled = command.CanExecute(null)`, `DisabledTip = Wizards.NewBuildingDisabledReason / NewTraitDisabledReason`, `Menu = null`; `TileClicked` → `command.Execute(null)`; `CanExecuteChanged` beider Befehle → `TileBarControl.RefreshState()`. Kachelzeile `Visibility` folgt `settings.TileBarVisible` und `HasWizards`.
  - Werkzeugleiste: Elemente einmal je Kennung erzeugt (`Button { Content = ToolbarItemView.Content(id, label, showLabel), Command, Focusable = false, Tag = id }`, `ToolTipService.ShowOnDisabled = true`), Pool im Fenster; `BuildToolbar()` ruft `ToolbarBuilder.Build(new ToolbarBuild(settings.ToolbarOrder, id => IsSeparator || settings.IsToolbarItemVisible(id), id => pool[id], vertical, grip, MoveToolbarItem))`; senkrecht in `ScrollViewer`; `showLabel = !vertical && settings.ToolbarLabelsVisible`. Neu bauen bei `settings.Changed` mit `ToolbarOrder`, `HiddenToolbarItems`, `ToolbarPosition`, `ToolbarLabelsVisible` und bei `texts.PropertyChanged` (Beschriftungen).
  - XAML-Reihenfolge im `DockPanel`: `Menu` (Top), `TileBarControl` (Top), `StatusBar` (Bottom), `ToolbarHost` (Dock aus `settings.ToolbarPosition`), `Layout` (Fill). So steht die Leiste bei Links/Rechts/Unten zwischen Kachelzeile und Statusleiste, ohne Kinder umzuhängen. Rahmen des `ToolbarHost` je Position wie PMT.
  - Menü: `Projekt` (wie heute, plus `Separator` nach Importieren und nach Neue Loc-Datei), `Bearbeiten`, `Einstellungen` (`MenuItem` ohne Kinder, `Command = OpenSettingsCommand`), `Hilfe` (`MenuOpenLogFolder`), `Debug`. Menü `Werkzeuge` samt Wizard- und Refresh-Einträgen entfällt. `F5` bleibt als `KeyBinding`.

- [ ] **Step 1: Alte Toolbar-Tests durch neue ersetzen (failing)**

Die vier Tests `The_toolbar_has_nine_buttons_...`, `The_toolbar_buttons_cannot_take_the_focus...`, `The_toolbar_is_hidden_without_documents`, `A_row_button_follows_the_selected_tab...` entfernen (die Zeilenbefehle testet Task 6). Neu in `MainWindowTests`:

```csharp
[Fact] public void The_menu_has_project_edit_settings_help_and_no_tools_menu() => Sta.RunAsync(async () =>
{
    using var setup = await CreateWithProjects(withFiles: true);
    var texts = setup.Setup.Game.Texts;
    var headers = Descendants<Menu>(setup.Setup.Window).Single().Items.OfType<MenuItem>().Where(m => m.Visibility == Visibility.Visible).Select(m => m.Header).ToList();
    Assert.Equal([texts.ProjectMessageTitle, texts.MenuEdit, texts.MenuSettings, texts.MenuHelp], headers);   // Debug ist im Testlauf sichtbar oder nicht: IsDebugBuild beachten
    Assert.DoesNotContain(texts.MenuToolsNewBuilding, Descendants<MenuItem>(setup.Setup.Window).Select(m => m.Header));
});
[Fact] public void The_toolbar_shows_the_default_order_with_separators_and_binds_the_commands() => Sta.RunAsync(async () =>
{
    using var setup = await CreateWithProjects(withFiles: true);
    var vm = setup.Setup.ViewModel; var host = (Border)setup.Setup.Window.FindName("ToolbarHost");
    var panel = Assert.IsType<WrapPanel>(host.Child);
    Assert.Equal(ToolbarCatalog.DefaultOrder, ToolbarBuilder.PlacedIds(panel));
    var build = Descendants<Button>(panel).Single(b => Equals(b.Tag, "build"));
    Assert.Same(vm.Projects!.BuildActiveCommand, build.Command); Assert.False(build.Focusable); Assert.True(ToolTipService.GetShowOnDisabled(build));
    Assert.Equal("Build (Ctrl+B)", build.ToolTip);                         // kein Sperrgrund: Name und Kürzel
});
[Fact] public void Without_projects_only_the_available_entries_are_placed_and_separators_collapse() => Sta.RunAsync(async () =>
{
    using var setup = await Create();                                      // Projects, Documents, ProjectFiles sind null
    var panel = (Panel)((Border)setup.Window.FindName("ToolbarHost")).Child;
    Assert.Equal(["refreshPacks"], ToolbarBuilder.PlacedIds(panel));
});
[Fact] public void Hiding_an_entry_and_moving_the_toolbar_left_rebuild_it_vertically_without_labels() => Sta.RunAsync(async () =>
{
    using var setup = await CreateWithProjects(withFiles: true);
    var settings = setup.Setup.Game.Settings; var host = (Border)setup.Setup.Window.FindName("ToolbarHost");
    settings.SetToolbarItemVisible("undo", false); Pump(setup.Setup.Window);
    Assert.DoesNotContain("undo", ToolbarBuilder.PlacedIds((Panel)host.Child));
    settings.ToolbarPosition = ToolbarPosition.Left; Pump(setup.Setup.Window);
    Assert.Equal(Dock.Left, DockPanel.GetDock(host));
    var panel = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(host.Child).Content);
    Assert.All(Descendants<Button>(panel).Where(b => b.Tag is string), b => Assert.Equal(Visibility.Collapsed, ToolbarItemView.LabelOf((FrameworkElement)b.Content)!.Visibility));
    Assert.Equal("Save (Ctrl+S)", Descendants<Button>(panel).Single(b => Equals(b.Tag, "save")).ToolTip);
});
[Fact] public void The_tiles_show_the_wizards_and_follow_their_enabled_state() => Sta.RunAsync(async () =>
{
    using var setup = await CreateWithWizards();                           // Helfer wie in ObjectWizardControllerTests: Wizards gesetzt, kein aktives Projekt
    var bar = (TileBar)setup.Window.FindName("TileBarControl");
    Assert.Equal(["newBuilding", "newTrait"], bar.VisibleIds);
    var tile = bar.TileButton("newBuilding");
    Assert.False(tile.IsEnabled); Assert.Equal(setup.ViewModel.Wizards!.NewBuildingDisabledReason, tile.ToolTip);
    /* aktives Projekt anlegen, CanExecuteChanged feuert */ Pump(setup.Window);
    Assert.Same(tile, bar.TileButton("newBuilding")); Assert.True(tile.IsEnabled);
});
[Fact] public void The_tile_bar_is_collapsed_without_wizards_or_when_switched_off() => Sta.RunAsync(async () =>
{
    using var setup = await Create();
    var bar = (TileBar)setup.Window.FindName("TileBarControl");
    Assert.Equal(Visibility.Collapsed, bar.Visibility);
});
```

In `MainViewModelTests`:

```csharp
[Fact] public void ToolbarBindingFor_maps_every_catalog_id_and_returns_null_for_missing_parts()
{
    // with projects, documents and files: every id of ToolbarCatalog.All has a binding
    // bare view model (no projects): only "refreshPacks" has one
}
```

- [ ] **Step 2: Build prüfen (RED)**

Run: `dotnet build tests/TotalWarModTool.App.Tests`
Expected: Compile-Fehler (`ToolbarHost`, `TileBarControl`, `ToolbarBindingFor` fehlen).

- [ ] **Step 3: `ToolbarBinding` und `ToolbarBindingFor` im `MainViewModel`**

Tabelle oben; `HasProjectFiles` für `newTable`/`newLoc`, `HasDocuments` für save/saveAll/undo/redo, `HasProjects` für Projekt-Einträge.

- [ ] **Step 4: XAML umbauen und `MainWindow.Shell.cs` schreiben**

Menü wie oben. Alte `Toolbar`-Border raus. `<shell:TileBar x:Name="TileBarControl" DockPanel.Dock="Top" />`, `<Border x:Name="ToolbarHost" BorderBrush="{DynamicResource Theme.Separator}" />` an der Stelle nach der `StatusBar`. Konstruktor ruft `InitializeShell(settings)`: Tile-Quelle binden, Pool bauen, `ApplyToolbarPosition()`, Ereignisse anhängen. `App.xaml.cs` und `TestGame`/`MainWindowTests.Create` geben `settings` mit.

- [ ] **Step 5: Build grün**

Run: `dotnet build` (ganze Solution)
Expected: grün, keine Warnungen (TreatWarningsAsErrors).

- [ ] **Step 6: Commit**

```bash
git add src/TotalWarModTool.App tests/TotalWarModTool.App.Tests
git commit -m "feat(app): tile bar, catalog-driven toolbar and a menu with only the basic entries"
```

---

### Task 6: Dokument-Leiste des Tabellen-Tabs

**Files:**
- Modify: `src/TotalWarModTool.App/Tabs/TabsView.xaml` (DataTemplates `EditableRowsTabViewModel`, `TableTabViewModel`, `LocTabViewModel`), `src/TotalWarModTool.App/Tabs/TabsViewModel.cs`, `src/TotalWarModTool.App/MainViewModel.cs`
- Test: `tests/TotalWarModTool.App.Tests/TabsViewTests.cs`

**Interfaces:**
- Consumes: `RowCommandsViewModel` (unverändert: folgt dem gewählten Tab, liefert Befehle und Tooltips mit Grund).
- Produces: `TabsViewModel.RowCommands { get; set; }` vom Typ `RowCommandsViewModel?`; `MainViewModel` setzt `Tabs.RowCommands = RowCommands` im Konstruktor. Ein gemeinsames `DataTemplate x:Key="RowCommandBar"` in `TabsView.xaml`: `WrapPanel` mit fünf `Button` (Insert, Duplicate, Delete, Copy, Paste), `Content`/`Command`/`ToolTip` über `{Binding DataContext.RowCommands.<X>, RelativeSource={RelativeSource AncestorType={x:Type local:TabsView}}}`, `Focusable="False"`, `ToolTipService.ShowOnDisabled="True"`, Trennlinie zwischen Delete und Copy. Die Leiste steht in den drei Vorlagen als erstes `DockPanel.Dock="Top"`-Element (vor der Zeile mit `Header` und `ViewSwitch`); `Visibility` folgt `RowCommands != null`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
[Fact] public void An_editable_tab_shows_the_row_command_bar_bound_to_the_row_commands() => Sta.Run(() =>
{
    // TabsViewModel mit RowCommands = new RowCommandsViewModel(tabs, texts); ein EditableRowsTabViewModel hinzufügen und wählen; TabsView zeigen
    var bar = Descendants<WrapPanel>(view).Single(p => p.Name == "RowCommandBar");
    var buttons = bar.Children.OfType<Button>().ToList();
    Assert.Equal([texts.GridInsertRow, texts.GridDuplicateRows, texts.GridDeleteRows, texts.GridCopy, texts.GridPaste], buttons.Select(b => b.Content));
    Assert.Same(rows.DeleteRowsCommand, buttons[2].Command); Assert.Equal(texts.ToolbarSelectRows, buttons[2].ToolTip);
    Assert.All(buttons, b => Assert.False(b.Focusable));
});
[Fact] public void A_read_only_vanilla_table_shows_the_bar_with_copy_enabled_only() => Sta.Run(() => { /* TableTabViewModel; Copy enabled, Delete tooltip == texts.ToolbarReadOnly */ });
[Fact] public void An_object_tab_has_no_row_command_bar() => Sta.Run(() => { /* ObjectTabViewModel; keine WrapPanel namens RowCommandBar */ });
```

- [ ] **Step 2: Build RED**

Run: `dotnet build tests/TotalWarModTool.App.Tests` — Expected: Compile-Fehler (`RowCommands` an `TabsViewModel` fehlt).

- [ ] **Step 3: Property, Verdrahtung und Vorlage**

`TabsViewModel.RowCommands` als `ObservableObject`-Property (`SetProperty`), `MainViewModel`-Konstruktor setzt es nach dem Anlegen von `RowCommands`. Vorlage wie oben, `x:Name="RowCommandBar"` am `WrapPanel`.

- [ ] **Step 4: Build grün**

Run: `dotnet build` — Expected: grün.

- [ ] **Step 5: Commit**

```bash
git add src/TotalWarModTool.App tests/TotalWarModTool.App.Tests
git commit -m "feat(app): move the row commands into a document bar of the table tabs"
```

---

### Task 7: `ReorderListBox` und die Einstellungsseiten Layout, Kachelzeile, Werkzeugleiste

**Files:**
- Create: `src/TotalWarModTool.App/SettingsUi/ReorderListBox.cs` (Kopie PMT `ParadoxModTool.SettingsUi/ReorderListBox.cs`), `src/TotalWarModTool.App/SettingsUi/BarSettingsPages.cs` (Code-gebaute Inhalte der drei Seiten)
- Modify: `src/TotalWarModTool.App/SettingsUi/SettingsWindow.xaml` (drei neue `ListBoxItem`, drei `ContentControl`-Hosts mit `DataTrigger` auf `SelectedCategory` 2, 3, 4), `SettingsWindow.xaml.cs` (Hosts befüllen), `SettingsViewModel.cs`
- Test: `tests/TotalWarModTool.App.Tests/ReorderListBoxTests.cs` (Kopie der PMT-Tests, sofern vorhanden, sonst die vier unten), `SettingsWindowTests.cs`, `SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: Tasks 1 bis 4, `ToolIcons`.
- Produces:
  - `public sealed record ReorderRow(string Id, string Label, bool Muted = false, string? MutedHint = null, bool IsSeparator = false, FrameworkElement? Icon = null, FrameworkElement? Trailing = null, Action? Remove = null);`
  - `public sealed class ReorderListBox : ItemsControl` — Konstruktor `(Func<IReadOnlyList<string>> order, Action<IReadOnlyList<string>> commit, Func<string, ReorderRow> row, ReorderTexts texts)` mit `public sealed record ReorderTexts(string MoveUp, string MoveDown, string Remove)`; `DataFormat = "twmt-reorder-id"`; `Rebuild()`, `CurrentIds`, `Row(id)`, `UpButton(id)`, `DownButton(id)`, `MoveUp(id)`, `MoveDown(id)`, `MoveNextTo(id, targetId)`.
  - `SettingsViewModel`: `const int LayoutCategory = 2, TilesCategory = 3, ToolbarCategory = 4`; `SelectedCategory` schreibt `settings.SettingsCategory` und liest es beim Start; `GlobalSettings Settings { get; }` (für die Code-gebauten Seiten); Properties `ToolbarPosition`, `ToolbarLabelsVisible`, `TileBarVisible`, `TileSize`, `TileLabelsVisible` als Durchreichung mit `PropertyChanged` bei `settings.Changed`.
  - `SettingsWindow`: Layout-Seite in XAML (Radios für Position und Größe über `ToolbarPosition`/`TileSize`-Bindings mit einem `EnumToBooleanConverter` oder je ein `RadioButton` mit `Command`, Builder-Wahl; Häkchen über die Bool-Properties). Kachel- und Werkzeugleisten-Seite als `ContentControl`, dessen Inhalt der Code-behind mit `BarSettingsPages.Tiles(viewModel)` bzw. `.Toolbar(viewModel)` füllt: Hinweis, `ReorderListBox` (Zeilen wie PMT `TilesPage.BuildRow` / `ToolbarsPage.BuildRow`, Icons über `ToolIcons.Create(id, 16, "Theme.Text")`), Knöpfe „Trennlinie hinzufügen“ (nächste freie `sep:n`), „Standard wiederherstellen“ (`ToolbarOrder = DefaultOrder` bzw. `TileOrder = DefaultOrder` plus alle `SetTileIconOnly(id, false)`). `Rebuild()` bei `settings.Changed` für die Listenfelder.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
// ReorderListBoxTests (STA)
[Fact] public void Rows_follow_the_order_and_arrows_move_them_through_commit()
[Fact] public void The_first_row_cannot_move_up_and_the_last_cannot_move_down()
[Fact] public void MoveNextTo_uses_the_reorder_rule()
[Fact] public void A_separator_row_shows_its_remove_button()
// SettingsWindowTests (STA)
[Fact] public void The_category_list_has_five_entries_in_order()        // General, Game, Layout, Tile bar, Toolbar
[Fact] public void The_tiles_page_lists_both_tiles_and_a_visibility_check_writes_the_settings()
[Fact] public void The_toolbar_page_adds_a_separator_with_the_next_free_number_and_reset_restores_the_default()
[Fact] public void The_layout_page_writes_position_and_size_at_once()  // Radio "Left" → settings.ToolbarPosition == Left
// SettingsViewModelTests
[Fact] public void SelectedCategory_is_stored_in_the_settings_and_read_back()
```

- [ ] **Step 2: Build RED** — `dotnet build tests/TotalWarModTool.App.Tests`, Compile-Fehler.

- [ ] **Step 3: `ReorderListBox` kopieren** — `SettingsTexts`-Zugriffe durch `ReorderTexts` ersetzen, Namespace `TotalWarModTool.App`, Kommentare Englisch.

- [ ] **Step 4: Seiten und View-Model** — wie in Interfaces. `SettingsWindow`-Breite auf 760×520 anheben, damit die Listen Platz haben.

- [ ] **Step 5: Build grün** — `dotnet build`.

- [ ] **Step 6: Commit**

```bash
git add src/TotalWarModTool.App tests/TotalWarModTool.App.Tests
git commit -m "feat(app): settings pages for layout, tile bar and toolbar with a reorderable list"
```

---

### Task 8: App-Tests, Changelog, Spec-Nachtrag, Testcheckliste

**Files:**
- Modify: `CHANGELOG.md` (Abschnitt Unreleased), `docs/specs/2026-10-16-interface-rework.md` (Abschnitt „Änderungen beim Schreiben von Plan 1“), `Backlog.txt`

- [ ] **Step 1: Ganze Suite mit dem Nutzer**

Run: `test.bat` (Nutzer anwesend, nichts anderes baut).
Expected: alle grün. Rote App-Tests aus Tasks 3, 5, 6, 7 hier beheben; jede Korrektur, die eine Spec-Festlegung ändert, in Step 3 nachtragen.

- [ ] **Step 2: Changelog**

Unreleased / Neu: „Kachelzeile für Werkzeuge (Neues Gebäude, Neuer Trait) mit Größe, Reihenfolge und Sichtbarkeit in den Einstellungen“, „Werkzeugleiste mit allen Grundfunktionen, frei sortierbar, oben, links, rechts oder unten“, „Einstellungsseiten Layout, Kachelzeile, Werkzeugleiste“. Geändert: „Das Menü hat nur noch Projekt, Bearbeiten, Einstellungen und Hilfe; die Werkzeuge sind Kacheln“, „Die Zeilenbefehle stehen über dem Raster statt unter dem Menü“.

- [ ] **Step 3: Spec-Nachtrag und Backlog**

In der Spec: neue Felder `PathBarVisible`, `FilterBarVisible`, `ColorizeWholeTab` kommen mit Plan 3 (nicht in diesem Plan); `MainWindow` bekommt `GlobalSettings`; Tile-Zustand über `RefreshState`. Backlog: „Kacheln mit Abzeichen und Untermenü haben noch keinen Nutzer (API aus PMT übernommen)“.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md docs/specs/2026-10-16-interface-rework.md Backlog.txt
git commit -m "docs: changelog, spec notes and backlog for plan 1 of the interface rework"
```

---

## Selbstprüfung (beim Schreiben erledigt)

- Spec-Abdeckung Plan 1: 6.1 Menü (Task 5), 6.2 Kachelzeile (Tasks 1–5, 7), 6.3 Werkzeugleiste (Tasks 1–5, 7), 6.5 Dokument-Leiste Tabellen-Tab (Task 6), 6.9 Seiten Layout/Kachelzeile/Werkzeugleiste (Task 7), 9 Fehlerfälle „unbekannte Kennung“ und „Fenster zu schmal“ (Task 2 Tests, WrapPanel/ScrollViewer in Task 5), 10 Tests Settings und App (Tasks 1, 2, 3, 5, 7), Parity Katalog ↔ Befehle (Task 5, `MainViewModelTests`).
- Nicht in Plan 1 (bewusst): Pfadleiste, Kette, Kategorien, Filter, Tastenkürzel-Seite (Pläne 2 bis 4).
- Typen: `TileSize`/`ToolbarPosition` als Enums in Task 2, in Tasks 3, 5, 7 gleich verwendet. `ToolbarBuilder.PlacedIds` aus Task 3 in Task 5 genutzt. `ToolbarItemView.LabelOf` aus Task 3 in Task 5 genutzt. `Texts.ToolbarTip` aus Task 4 in Task 5 genutzt.
