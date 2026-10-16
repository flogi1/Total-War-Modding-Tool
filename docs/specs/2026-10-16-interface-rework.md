# Spec: Interface-Rework (Leisten, Kachelzeile, Kette, Kategorien, Einstellungen)

Stand: 2026-10-16. Grundlage: Brainstorming mit dem Nutzer am 2026-10-16 und die Browservorschau
`.superpowers/brainstorm/14743-1792162914/content/layout-v2.html` (Version 2, nicht im Repo). Vorbild ist das
Interface-Rework 2 des Paradox Modding Tools (PMT, Spec `docs/superpowers/specs/2026-10-09-interface-rework-2-design.md`
im PMT-Repo). Wo dieser Text eine Frage offen lässt, gilt das Verhalten von PMT.

## 1. Absicht

Der Nutzer bedient heute alles über ein Menü mit drei Ebenen. Er weiß nach „Bauen und installieren“ nicht, ob er
„Übernehmen“ noch braucht, und prüft im WH3 Mod Manager, ob das Pack in `data` liegt. Das Rework macht die
Grundfunktionen mit einem Klick erreichbar, gibt großen Werkzeugen eine Kachelzeile, die mit jedem neuen Werkzeug
wächst, und zeigt die Kette **Übernehmen → Speichern → Bauen → Installieren** mit ihrem Zustand in der Statusleiste.
Tabs bekommen Kategorien mit Farbe und eine Filterleiste, weil der Nutzer mit vielen offenen Tabs arbeitet.

Misserfolg aus Nutzersicht: „wenn es aus irgendeinem Grund kompliziert bleibt“. Maßstab ist PMT, dort funktioniert es.

## 2. Ziele

1. **Menü nur mit Grundpunkten:** Projekt | Bearbeiten | Einstellungen | Hilfe (Debug nur im Debug-Build wie heute).
   Werkzeuge verschwinden aus dem Menü.
2. **Kachelzeile** zwischen Menü und kleiner Werkzeugleiste, voller PMT-Umfang: drei Größen, Beschriftung an/aus,
   Ziehen zum Umsortieren, Ausblenden mit erhaltener Position, Abzeichen mit Zahl, Warnzeichen, Untermenü-Kacheln,
   waagerechtes Scrollen ohne Umbruch. Start-Kacheln: Neues Gebäude, Neuer Trait.
3. **Kleine Werkzeugleiste** für Grundfunktionen, voller PMT-Umfang: Position oben, links, rechts, unten; Reihenfolge,
   Trennlinien und Sichtbarkeit je Eintrag; Beschriftung an/aus; Griff ⋮ mit Positionsmenü; Ziehen in der Leiste.
4. **Pfadleiste** über dem Dokument: Pfad, „Pfad kopieren“, „Ordner öffnen“; bei Objekt-Tabs Schlüssel und Quell-Pack.
5. **Kette in der Statusleiste:** vier Schritte für das aktive Projekt mit Zustand, Tooltip mit Grund, Klick führt genau
   diesen Schritt aus.
6. **Kategorien für Tabs:** editierbare Liste (Name, Farbe, Pfadregeln), automatische Zuordnung, manuelle Zuordnung
   pro Tab, Farbbalken am Reiter, Option „ganzer Reiter“.
7. **Filterleiste über den Tabs:** Häkchen je Herkunft (Projekt, Vanilla, andere Mods) und je Kategorie.
8. **Einstellungsfenster:** neue Kategorien Layout, Kachelzeile, Werkzeugleiste, Kategorien, Tastenkürzel (nur zum
   Nachschlagen). Alles wirkt sofort.

## 3. Non-Goals

- **Spielstart und Playsets.** Eigene Spec als nächstes Projekt (`used_mods.txt` schreiben, Besitz gegenüber dem WH3
  Mod Manager, Debug-Start, Steam oder Exe). Kachelzeile und Werkzeugleiste sind Kataloge, der Knopf „Spiel starten“
  und die Playset-Auswahl kommen später als neue Einträge ohne Umbau dazu. Entschieden am 2026-10-16.
- **Geteilte Ansicht** (zwei Arbeitsbereiche). Der Vergleichsfall wird später vom Diff-Werkzeug besser bedient. Backlog.
- **Frei belegbare Tastenkürzel.** Kürzel bleiben fest. Backlog.
- **Klassisches Layout als Option.** Es gibt keine Nutzer mit altem Layout.
- **Ordner-Filter** („Nur Ordner“ aus PMT), **Sortierung der Tabs**, **Notizen, Lesezeichen, Bugs**, **Tab-Icons**,
  **Suchen und Ersetzen** (TWMT hat heute keine Suche; der Mockup-Knopf war erfunden). Backlog.
- **Deinstallieren** in der Werkzeugleiste. Bleibt im Kontextmenü am Projekt.
- **Ziehen zwischen Kachelzeile und Werkzeugleiste**, mehrere Werkzeugleisten, frei schwebende Leiste.
- **Vektor-Icons für Tabs oder Explorer.** Neue Icons gibt es nur für Kacheln und Werkzeugleiste.

## 4. Ist-Zustand (master 1e16073, gemessen 2026-10-16)

- `MainWindow.xaml` (149 Zeilen): `Menu` mit 27 `MenuItem` in vier Hauptpunkten (Projekt, Bearbeiten, Werkzeuge,
  Debug), darunter Werkzeuge ▸ Packs aktualisieren, Neues Gebäude, Neuer Trait, Einstellungen, Log-Ordner öffnen.
  Darunter eine feste Knopfzeile `Toolbar` (Zeile 89–115) mit neun Knöpfen: Speichern, Alle speichern, Rückgängig,
  Wiederholen, Zeile einfügen, Zeilen duplizieren, Zeilen löschen, Kopieren, Einfügen (`RowCommandsViewModel`), mit
  Tests in `MainWindowTests`. Keine Kachelzeile. `StatusBar` unten mit Spiel, Hinweis und Projektstatus.
  `MainWindow.xaml.cs` hat 14 Zeilen, `MainViewModel.cs` 454 Zeilen.
- Kürzel heute fest: Ctrl+S, Ctrl+Shift+S, Ctrl+B, Ctrl+Shift+B, Ctrl+Z, Ctrl+Y, Ctrl+Enter (Übernehmen).
- Tabs: `TabsView.xaml` (330 Zeilen) mit `TabControl`, `TabsViewModel` (230 Zeilen). Tab-Arten:
  `TableTabViewModel`, `LocTabViewModel`, `TextTabViewModel`, `InfoTabViewModel`, `ObjectTabViewModel`. Keine Farbe,
  kein Filter, keine Pfadleiste. Die Dokument-Leiste des Objekt-Tabs (Übernehmen, Aktualisieren, Kopieren als, Block)
  liegt in `ObjectEditorView.xaml`.
- Einstellungen: `GlobalSettings` (Language, Theme, MainWindow, SelectedGame, SchemaFolder; `settings.json`,
  `SchemaVersion` 1) und `GameSettings` (InstallPath; `game-settings.json` je Profil). `SettingsWindow.xaml` (144
  Zeilen) mit zwei Kategorien (Allgemein, Spiel), Seiten per `DataTrigger` auf `SelectedCategory`.
- Sitzung: `SessionState(Tabs, ActiveTab, ExpandedPacks)`, `SessionTab(PackPath, EntryPath, Object)`.
- Projektliste: `ProjectListEntry(Id, Path, Installed)` mit `InstalledPack(FileName, Length, LastWriteTimeUtc)` =
  Fingerprint der Datei in `data` nach dem Installieren. `PackInstaller.CopyToTemp` kopiert per `FileStream`, die
  Schreibzeit von `build\` geht dabei **nicht** mit (gemessen am Code, Zeile 299 ff.). Ein Fingerprint des gebauten
  Packs zum Zeitpunkt des Installierens wird nicht gespeichert.
- Theme: `ThemePalette.cs` mit 15 Tokens, `Controls.xaml` (1340 Zeilen) hat schon einen `ToolBar`-Style (Zeile 802).
- Texte: `AppTexts.*.cs` mit `T(key, fallback)`, `locales/en.json` und `de.json`, Locale-Test prüft Vollständigkeit.
- PMT-Code, der übernommen wird (Zeilen, PMT-Repo `Runde 10.1`): `Shell/TileBar.cs` 297, `Shell/ReorderDragHost.cs`
  155, `Shell/ToolbarBuilder.cs` 104, `Shell/ToolbarItemView.cs` 59, `Shell/ToolbarPositionGrip.cs` 68,
  `Shell/ToolIcons.cs` 133, `Settings/ReorderList.cs` 60, `Settings/TileCatalog.cs` 70, `Settings/ToolbarCatalog.cs`
  123, `Settings/ToolbarLayout.cs` 35, `SettingsUi/ReorderListBox.cs` 467, `Categories/*` 449 (7 Dateien). Dazu die
  Tests aus `ParadoxModTool.Shell.Tests` und `ParadoxModTool.Categories.Tests`. Zusammen rund 2000 Zeilen Quelle.

## 5. Verworfene Alternativen

| Entscheidung | Verworfen | Grund |
|---|---|---|
| Kette als Streifen in der Statusleiste (Mockup Variante B) | A: Ampel-Punkte an den Leisten-Knöpfen; C: Projekt-Kopf über dem Explorer mit „Nächster Schritt“ | Nutzerentscheidung 2026-10-16. B zeigt Reihenfolge und Zustand an einem Ort, ohne Platz im Explorer zu nehmen. |
| Klick auf einen Schritt führt nur diesen Schritt aus | Klick holt alle offenen Schritte davor nach, mit einer Nachfrage | Nutzer erwartet „nur dieser Schritt“. Die Nachhol-Variante versteckt, was passiert. |
| Werkzeuge nur als Kachel | Kachel und Menüeintrag | Zwei Orte für eine Funktion. PMT hat Werkzeuge ebenfalls nur als Kachel. Ausgeblendete Kacheln sind über Einstellungen erreichbar. |
| Kategorien wie PMT (editierbar, Pfadregeln, manuelle Zuordnung) | Farbe nur nach Tab-Art ohne Nutzer-Kategorien | Nutzer will Kategorien selbst vergeben. |
| Filterleiste jetzt | Filter ins Backlog | „Ziemlich wichtig fürs Arbeiten.“ |
| Fingerprint des gebauten Packs beim Installieren speichern (`InstalledFrom`, Schema 2) | Schreibzeit von `build\` mit Schreibzeit in `data` vergleichen | Der Installer kopiert per Stream, Schreibzeiten stimmen nicht überein. Längenvergleich allein erkennt gleich große Packs nicht. |
| PMT-Code per Kopie, angepasst nach Checkliste A2 | Gemeinsame Library mit PMT; neu schreiben | Wie bei `Storage` und `UndoRedo` (Fundament-Spec). Release-Zyklen bleiben getrennt. |
| Alle PMT-Kachelfunktionen im ersten Wurf | Feste Kachelzeile, Einstellbarkeit später | Nutzer: „sonst müssen wir das zwei Mal anfassen“. Der Code kommt ohnehin als Kopie. |
| Kein klassisches Layout | Umschalter wie in PMT | Keine Bestandsnutzer, YAGNI. |
| Zeilenbefehle in einer Dokument-Leiste des Tabellen-Tabs | Als Katalog-Einträge der kleinen Werkzeugleiste (auf Objekt-Tabs ausgegraut) | Nutzerentscheidung 2026-10-16. Die Leiste zeigt nur, was zum Tab passt, gleiches Muster wie die Objekt-Leiste. |
| Manuelle Zuordnungen unter `%APPDATA%` je Projekt-GUID | In `twmt-project.json` im Projektordner | Projektordner wird weitergegeben; Kategorien sind Nutzer-UI, nicht Projektinhalt (Regel „nie in Ordner schreiben, die der Nutzer weitergibt“). |

## 6. Fensteraufbau

Von oben nach unten: Menü, Kachelzeile, kleine Werkzeugleiste (bei Position oben), Arbeitsbereich (Explorer links,
Tabs rechts mit Filterleiste, Pfadleiste und Dokument-Leiste), Statusleiste. Bei Position links oder rechts steht die
Werkzeugleiste senkrecht neben dem Arbeitsbereich, bei unten über der Statusleiste.

### 6.1 Menü

| Menüpunkt | Einträge |
|---|---|
| Projekt | Neues Projekt, Projekt öffnen, Pack importieren, Trennlinie, Neue Tabelle, Neue Loc-Datei, Trennlinie, Speichern, Alle speichern, Trennlinie, Bauen, Bauen und installieren |
| Bearbeiten | Rückgängig, Wiederholen |
| Einstellungen | öffnet das Einstellungsfenster direkt (kein Untermenü) |
| Hilfe | Log-Ordner öffnen |
| Debug | wie heute, nur im Debug-Build |

Kürzel stehen wie heute als `InputGestureText` am Menüeintrag.

### 6.2 Kachelzeile

`TileCatalog` (Projekt `Settings`) mit Kennungen in Standardreihenfolge. Kennungen werden gespeichert, nie umbenennen.

| Id | Kachel | Öffnet |
|---|---|---|
| `newBuilding` | Neues Gebäude | Assistent wie heute aus dem Menü |
| `newTrait` | Neuer Trait | Assistent wie heute |

Darstellung, Größen (`Large` 104×78, `Medium` 84×62, `Compact` 44×44), Abzeichen, Warnzeichen, Untermenü-Kacheln,
Ziehen und Ausblenden: wie PMT (`TileBar`, `TileSource`, `ReorderDragHost`). Abzeichen, Warnzeichen und Untermenü haben
in diesem Spec keinen Nutzer; die Fähigkeit kommt mit der Kopie und wird nicht entfernt (Nutzerentscheidung: PMT-Umfang
komplett). Ihr erster Nutzer ist ein späteres Werkzeug (z. B. Fehlerliste mit Zahl). Die Kachelzeile lässt sich als
Ganzes ausblenden (`TileBarVisible`).

Icons: Vektor-Geometrien in `ToolIcons` (Kopie aus PMT, neue Geometrien für die TWMT-Kennungen), Strich in
`Theme.Accent`. Beschriftung = heutiger Menütext, Tooltip = heutiger Menü-Tooltip.

### 6.3 Kleine Werkzeugleiste

`ToolbarCatalog` mit Standardreihenfolge und Trennlinien (`sep:1` …):

```
newProject openProject importPack newTable newLoc | save saveAll | undo redo | refreshPacks | build buildAndInstall
```

Jeder Eintrag ruft den heutigen Befehl des `MainViewModel`. Sichtbarkeit (`HiddenToolbarItems`), Reihenfolge
(`ToolbarOrder` inklusive Trennlinien), Position (`ToolbarPosition` Top/Left/Right/Bottom), Beschriftung
(`ToolbarLabelsVisible`), Griff ⋮, Ziehen, `ToolbarLayout.VisibleSequence` (ausgeblendete fallen weg, dann Trennlinien
am Anfang, Ende und doppelte): wie PMT. Die Leiste wird in Code gebaut (`ToolbarBuilder`), nicht statisch in XAML.
Tooltips nennen Name und Kürzel („Speichern (Ctrl+S)“).

### 6.4 Pfadleiste

Über dem Inhalt jedes Tabs, unter der Reiterzeile. Datei-Tabs: vollständiger Pfad (Projektdatei) oder
`<Pack>\<Eintrag>` (Pack-Eintrag), Knopf „Pfad kopieren“, Knopf „Ordner öffnen“ (nur Projektdateien; bei Pack-Einträgen
öffnet er den Ordner des Packs). Objekt-Tabs: Tabelle, Schlüssel und Quell-Pack; „Pfad kopieren“ kopiert
`<Tabelle>:<Schlüssel>`. Die Pfadleiste lässt sich in Layout ausblenden (`PathBarVisible`, Standard an).

### 6.5 Dokument-Leiste

Objekt-Tab: bleibt wie heute in `ObjectEditorView` (Übernehmen, Aktualisieren, Kopieren als, Block).

Tabellen- und Loc-Tab: neue Dokument-Leiste mit den fünf Zeilenbefehlen aus der heutigen Knopfzeile (Zeile einfügen,
Zeilen duplizieren, Zeilen löschen, Kopieren, Einfügen; `RowCommandsViewModel`). Sie liegt wie die Objekt-Leiste im
Tab und ist nur sichtbar, wenn ein Raster offen ist (Nutzerentscheidung 2026-10-16, Weg b). Die Zeilenbefehle sind
keine Katalog-Einträge der kleinen Werkzeugleiste; Speichern, Alle speichern, Rückgängig und Wiederholen aus der
heutigen Knopfzeile werden Katalog-Einträge (6.3). Die heutige Knopfzeile `Toolbar` in `MainWindow.xaml` entfällt, ihre
Tests in `MainWindowTests` wandern zur neuen Dokument-Leiste bzw. zum `ToolbarBuilder`.

Dokument-Leisten liegen unter der Pfadleiste.

### 6.6 Statusleiste mit Kette

Links die Kette, rechts die heutigen Felder (Spiel, Hinweis, Projektstatus).

**Schritte und Zustände.** Berechnung pur in `Projects`: `BuildChain.Compute(BuildChainInput) → BuildChainState`.

| Schritt | Offen (gelber Punkt) | Erledigt (Haken) | Älter (orange) | Unbekannt (grau, Tooltip nennt Grund) |
|---|---|---|---|---|
| Übernehmen | mindestens ein Objekt-Tab hat nicht übernommenen Text (Objekt-Tabs gehören immer zum aktiven Projekt) | kein solcher Tab | – | – |
| Speichern | mindestens ein Tab hat ungespeicherte Änderungen | kein solcher Tab | – | – |
| Bauen | `build\<packName>` fehlt | Pack vorhanden, keine Datei unter `pack\` neuer als das Pack | eine Datei unter `pack\` ist neuer (Schreibzeit) als das Pack | `pack\` oder `build\` nicht lesbar |
| Installieren | Projektliste hat kein `Installed` | `Installed` passt zur Datei in `data` und `InstalledFrom` passt zum Pack unter `build\` | `InstalledFrom` passt nicht zum Pack unter `build\` | Datei in `data` fehlt oder passt nicht zu `Installed`; `InstalledFrom` fehlt (alte Projektliste) |

Eingaben: Liste der offenen Tabs mit (Projekt-Id, nicht übernommen, ungespeichert), Projektordner, Pack-Name,
Projektlisten-Eintrag, Datenordner. `pack\` wird rekursiv auf Schreibzeiten durchsucht (Fingerprint-Ansatz, keine
Inhalte). Der nächste offene Schritt ist der erste von links, der nicht „Erledigt“ ist.

**Neues Feld.** `ProjectListEntry` bekommt `InstalledFrom: InstalledPack?` = Fingerprint (Name, Länge, Schreibzeit)
des gebauten Packs zum Zeitpunkt des Installierens. `ProjectListStore.SchemaVersion` wird 2, Migration 1 → 2 setzt
`InstalledFrom` auf null (Zustand „Unbekannt“ bis zum nächsten Installieren), mit Regressionstest. `Install` liefert
beide Fingerprints zurück. „Deinstallieren“ leert beide.

**Anzeige.** Jeder Schritt ist ein Knopf mit Symbol und Text („● Übernehmen“, „Gespeichert ✓“, „Gebaut (älter)“,
„Installiert (älter)“). Erster offener Schritt fett. Tooltip nennt den Grund: betroffene Tabs (bis zu drei, dann
„und n weitere“), die neuere Datei, den Fingerprint-Unterschied. Alle vier erledigt: Kette in gedämpfter Farbe,
Tooltip „Das Spiel hat deinen aktuellen Stand.“ Kein aktives Projekt oder Projekt nicht geladen: Kette ausgeblendet.

**Klick.** Führt genau diesen Schritt aus, nie die davor:
- Übernehmen: aktiviert den ersten betroffenen Objekt-Tab und ruft dort Übernehmen (bei mehreren Tabs nacheinander,
  jeder mit seinem heutigen Dialog).
- Speichern: Alle speichern.
- Bauen: Bauen (mit heutiger Nachfrage bei ungespeicherten Änderungen).
- Installieren: nur Installieren, ohne Bauen. Ist der Bau „älter“, fragt ein Dialog „Das Pack unter `build\` ist älter
  als das Projekt. Trotzdem installieren?“ mit „Installieren“ und „Abbrechen“. Das ist neu: heute gibt es nur „Bauen und
  installieren“. Der Controller bekommt `InstallOnlyAsync`, gleiche Sperrgründe wie „Bauen und installieren“.
- Ein Schritt, der „Erledigt“ ist, ist klickbar und tut dasselbe (Bauen ist wiederholbar; Speichern ohne Änderungen
  tut nichts).

**Aktualisierung.** Neu berechnen bei: Tab-Änderung (dirty, übernommen), Tab geöffnet/geschlossen, Projektwechsel,
nach Speichern/Übernehmen/Bauen/Installieren/Deinstallieren, Fensteraktivierung, und per `FileSystemWatcher` auf
`pack\` und `build\` als Signal mit `DispatcherTimer`-Debounce (300 ms). Die Berechnung läuft mit `Task.Run`; kein
Fortschrittsfenster (automatischer Hintergrundlauf, still). Ein Lauf, der länger als 2 s dauert, wird mit Log-Eintrag
beendet und zeigt „Unbekannt“.

### 6.7 Kategorien

Library `TotalWarModTool.Categories` (net8.0, Kopie der PMT-`Categories` nach Checkliste A2, ohne UI):

- `CategoryDefinition(Id, Name, ColorHex, Rules)`; `Rules` sind Pfadmuster mit `*` und `**` relativ zum Projekt- oder
  Pack-Wurzelpfad, Vergleich OrdinalIgnoreCase, Trennzeichen `\` und `/` gleichwertig.
- `CategoryDefaults.Create()`: `tables` (Regel `db/**`), `loc` (`text/**`), `objects` (Kennung `object:` statt Pfad),
  `vanilla` (Herkunft Vanilla, keine Pfadregel), `other` (Fallback, zuletzt, nicht löschbar). Eingebaute Kategorien
  beziehen ihre Farbe aus Theme-Tokens (`Theme.CategoryTables` …), eigene tragen `ColorHex`.
- `CategoryMatcher.Normalize` (doppelte Ids, leere Namen, ungültige Farben, fehlender Fallback) und
  `CategoryMatcher.Match(categories, origin, path)`: erste Regel in Listenreihenfolge gewinnt; Vanilla-Herkunft vor
  Pfadregeln; Objekt-Tabs immer `objects`, sofern vorhanden.
- `FileOrigin`: Project, Vanilla, OtherMod. Kommt aus `TabSource` (Projektdatei, Vanilla-Pack laut Katalog, sonst
  anderes Mod-Pack). Objekt-Tabs haben Herkunft `Project`: sie schreiben immer ins aktive Projekt, egal aus welchem
  Pack die Zeile stammt (Entscheidung beim Schreiben der Spec).
- Namen: `TotalWarModTool.Objects.CategoryDefinition` existiert schon (Kategorie-Dateien der Objekt-Ansicht). Die neue
  Library nennt ihre Typen `TabCategory`, `TabCategoryDefaults`, `TabCategoryMatcher`, `TabCategoryStore`,
  `TabAssignmentStore`, damit die App-Assembly keine Mehrdeutigkeit bekommt.
- Farbe eigener Kategorien: Hex-Feld plus zwölf feste Farbfelder (aus `ThemePalette`, in beiden Themes lesbar). Kein
  `ColorDialog`, weil er `UseWindowsForms` und Typ-Aliase in der App verlangt. Entscheidung beim Schreiben der Spec.

**Speicherung.**
- Kategorie-Liste global: `%APPDATA%\TotalWarModTool\categories.json`, `schemaVersion` 1, atomar, `.bad` bei Schaden,
  Defaults bei Fehlen. Eigener Store in `Categories` (`CategoryStore`, Root-Pfad injiziert).
- Manuelle Zuordnung je Projekt: `%APPDATA%\TotalWarModTool\projects\<Projekt-GUID>\tab-categories.json`,
  `schemaVersion` 1, Schlüssel = Tab-Kennung (`PackPath|EntryPath` oder `object:<Tabelle>:<Schlüssel>`), Wert =
  Kategorie-Id. Gelöschte Kategorie → Eintrag wird beim Laden still entfernt (`Sanitize`), Tab fällt auf „Automatisch“.
  Für Tabs ohne Projekt (Vanilla-Einträge ohne aktives Projekt) gibt es keine manuelle Zuordnung; das Kontextmenü ist
  dann deaktiviert mit Tooltip.
- Filterzustand (ausgeblendete Kategorien und Herkünfte) in `SessionState` (`HiddenCategories`, `HiddenOrigins`),
  Defaults leer.

**Darstellung am Reiter.** Farbbalken 3 px links im Reiter in der Kategoriefarbe. Option `ColorizeWholeTab` (Layout,
Standard aus): ganzer Reiter halbtransparent in der Farbe. Ungespeichert bleibt wie heute markiert.

**Kontextmenü am Reiter:** „Kategorie ▸ Automatisch (Tabellen) | Tabellen | Loc-Texte | … “ mit Häkchen am
wirksamen Eintrag. Auswahl speichert sofort.

### 6.8 Filterleiste

Über der Reiterzeile in `TabsView`, ein- und ausblendbar (`FilterBarVisible`, Layout, Standard an, plus Eintrag in der
Werkzeugleiste als Toggle `filterBar`, Standard sichtbar). Inhalt: „Herkunft:“ mit Häkchen Projekt, Vanilla, Andere
Mods; „Kategorie:“ mit Häkchen je Kategorie (Farbpunkt + Name) in Listenreihenfolge. Häkchen aus = Tabs dieser Gruppe
werden ausgeblendet (bleiben offen, bleiben in der Sitzung, bleiben beim Speichern-Alle dabei). Ein Tab ist sichtbar,
wenn seine Herkunft **und** seine Kategorie sichtbar sind. Der aktive Tab wird nie ausgeblendet: Blendet ein Häkchen
den aktiven Tab aus, bleibt er sichtbar, bis der Nutzer einen anderen Tab wählt. Ausgeblendete Tabs zählt ein Text
rechts („3 ausgeblendet“), Klick darauf setzt alle Häkchen.

### 6.9 Einstellungsfenster

Kategorien in dieser Reihenfolge: Allgemein, Spiel, Layout, Kachelzeile, Werkzeugleiste, Kategorien, Tastenkürzel.

| Kategorie | Inhalt |
|---|---|
| Layout | Position der Werkzeugleiste (4 Radios), Beschriftung der Werkzeugleiste, Kachelzeile anzeigen, Kachelgröße (3 Radios), Kachel-Beschriftung, Pfadleiste anzeigen, Filterleiste anzeigen, Farbcodes färben den ganzen Reiter |
| Kachelzeile | `ReorderListBox`: Reihenfolge per Ziehen oder Pfeilen, Häkchen sichtbar, Häkchen „nur Symbol“ je Kachel |
| Werkzeugleiste | `ReorderListBox` mit Trennlinien als eigene Einträge, Häkchen sichtbar, Knopf „Trennlinie hinzufügen“, „Standard wiederherstellen“ |
| Kategorien | Liste (Name, Farbfeld, Regeln als Textzeilen), Anlegen, Umbenennen, Farbe wählen, Löschen (nicht bei `other`), Reihenfolge per Pfeilen; Hinweis, dass Regeln in Listenreihenfolge gelten |
| Tastenkürzel | Nur-Lese-Liste nach Gruppen (Hauptfenster, Tabellen-Raster, Objekt-Ansicht, Dialoge) mit Hinweis „Nur zum Nachschlagen; die Tastenkürzel lassen sich nicht ändern.“ |

Alle Werte wirken sofort über `GlobalSettings.Changed` (Leisten bauen sich neu) bzw. `CategoryStore.Changed`.

**Neue Felder in `GlobalSettings`** (Defaults ergeben das PMT-Standardlayout; `settings.json` bleibt Schema 1, fehlende
Felder = Default): `TileOrder`, `HiddenTiles`, `TileSize`, `IconOnlyTiles`, `TileLabelsVisible`, `TileBarVisible`,
`ToolbarOrder`, `HiddenToolbarItems`, `ToolbarPosition`, `ToolbarLabelsVisible`, `PathBarVisible`, `FilterBarVisible`,
`ColorizeWholeTab`, `SettingsCategory` (zuletzt gewählte Seite). Unbekannte Kennungen in gespeicherten Listen werden
beim Laden entfernt, fehlende Kennungen an ihrer Standardposition eingefügt (`Sanitize`, wie PMT).

## 7. Schreibgrenze

Neu geschrieben werden nur: `%APPDATA%\TotalWarModTool\settings.json` (neue Felder), `categories.json` (neu),
`projects\<GUID>\tab-categories.json` (neu), `session.json` (neue Felder), `projects.json` (Schema 2, `InstalledFrom`).
Die Kette liest `pack\`, `build\` und `data`, schreibt dort nichts. „Installieren“ aus der Kette schreibt wie heute nur
`data\<packName>` über `PackInstaller`. Projektordner und Spielordner bekommen keine neuen Dateien.

## 8. Architektur

| Projekt | Änderung |
|---|---|
| `Settings` | Neue Felder in `GlobalSettings` + Store, `TileCatalog`, `ToolbarCatalog`, `ToolbarLayout`, `ReorderList` (Kopie PMT) |
| `Categories` (neu) | `CategoryDefinition`, `CategoryDefaults`, `CategoryMatcher`, `FileOrigin`, `CategoryStore`, `TabCategoryStore` (Kopie PMT + Stores) |
| `Projects` | `BuildChain.Compute` (pur), `InstalledFrom` in Projektliste, Migration 1 → 2, `Install` liefert beide Fingerprints |
| `Session` | `HiddenCategories`, `HiddenOrigins` |
| `App` | `Shell/` (Kopie PMT: `TileBar`, `ReorderDragHost`, `ToolbarBuilder`, `ToolbarItemView`, `ToolbarPositionGrip`, `ToolIcons`), `SettingsUi/ReorderListBox` (Kopie), Menü, Statusleiste mit `BuildChainViewModel`, `TabsView` mit Filterleiste, Pfadleiste, Reiterfarbe, Kontextmenü; Einstellungsseiten; `InstallOnlyAsync` im Projekt-Controller |

Regeln: Leisten in Code gebaut aus Katalog + Settings, keine Logik im Code-behind außer Verdrahtung. Kette: Berechnung
in `Projects` ohne UI, ViewModel in App. Farben nur über Theme-Tokens, eigene Kategoriefarben sind Nutzerdaten
(kein Token). Keine neuen NuGet-Pakete. Theme-Tokens, die PMT-Code erwartet (`Theme.Separator`,
`Theme.PanelBackground`, `Theme.Accent`, `Theme.WindowBackground`), werden auf die 15 TWMT-Tokens abgebildet oder
ergänzt; Abbildung steht im Plan 1.

## 9. Fehlerfälle

| Fall | Verhalten |
|---|---|
| `settings.json`, `categories.json`, `tab-categories.json` beschädigt | `.bad`-Kopie, Defaults, Hinweis wie heute bei Settings. Start nie blockiert. |
| Unbekannte Kennung in `TileOrder`/`ToolbarOrder` (Kachel entfernt, altes Update) | Beim Laden entfernt, beim nächsten Speichern weg. |
| Kategorie gelöscht, Tab war manuell zugeordnet | Zuordnung entfernt, Tab „Automatisch“. Filter-Häkchen der Kategorie verschwindet. |
| Zwei Kategorien mit gleicher Id in der Datei | Erste gewinnt, zweite wird beim Laden verworfen (Log). |
| Ungültige Farbe (`ColorHex`) | Standardfarbe der Fallback-Kategorie, Log. |
| `pack\` oder `build\` nicht lesbar | Schritt „Bauen“ Unbekannt mit Grund im Tooltip; Klick weiterhin möglich (Bauen meldet den Fehler selbst). |
| Datei in `data` fehlt oder gehört nicht dem Tool | Schritt „Installieren“ Unbekannt mit Grund; Klick zeigt die heutige Meldung des Installers. |
| Projektliste nicht speicherbar (heutige Sperre) | Schritt „Installieren“ deaktiviert mit heutigem Grund. |
| Kette braucht länger als 2 s | Abbruch, Log, Schritte „Unbekannt“. |
| Fenster zu schmal | Kachelzeile scrollt waagerecht; Werkzeugleiste bricht um (oben/unten) bzw. scrollt (links/rechts). |
| Alle Tabs ausgeblendet außer dem aktiven | Aktiver bleibt sichtbar (Regel 6.8). |
| Filter blendet den Tab aus, den „Übernehmen“ aus der Kette aktivieren will | Der Tab wird aktiviert und damit sichtbar. |
| `InstalledFrom` fehlt (alte Projektliste) | Installieren „Unbekannt“ mit Tooltip „Einmal installieren, dann kennt das Tool den Stand.“ |

## 10. Tests

Libraries test-first, Fakes von Hand, Temp-Ordner je Test.

- `Settings.Tests`: Round-Trip aller neuen Felder; alte `settings.json` ohne neue Felder lädt mit Defaults;
  `Sanitize` entfernt unbekannte Kennungen und ergänzt fehlende; `ToolbarLayout.VisibleSequence` (Trennlinien am
  Anfang, Ende, doppelt); `ReorderList.MoveNextTo` (von vorn und hinten gezogen).
- `Categories.Tests` (Kopie der PMT-Tests, angepasst): `Normalize`, `Match` (Reihenfolge, Vanilla vor Pfad, `**`,
  Groß/Klein, Trennzeichen), Defaults, `CategoryStore` (fehlend, beschädigt, Round-Trip), `TabCategoryStore`
  (Sanitize bei gelöschter Kategorie).
- `Projects.Tests`: `BuildChain.Compute` für jede Zelle der Tabelle in 6.6 (Tabs offen/sauber, Pack fehlt, Datei neuer,
  Unterordner, `pack\` nicht lesbar, `InstalledFrom` null/gleich/ungleich, `data` fehlt); Migration 1 → 2 mit
  Regressionsdatei; `Install` liefert `InstalledFrom`.
- `Session.Tests`: neue Felder Round-Trip, alte Datei lädt mit leeren Listen.
- `App.Tests` (STA-Helper): `TileBar` baut Kacheln in `TileOrder`, Ausblenden erhält Position, Abzeichen; `ToolbarBuilder`
  baut `VisibleSequence`; Filter: aktiver Tab bleibt sichtbar; Kontextmenü speichert Zuordnung; Kette: Klick auf
  „Installieren“ bei „älter“ fragt nach; Menü hat keine Werkzeug-Einträge mehr; Locale-Test (`en`, `de` vollständig).
- Parity: `ToolbarCatalog`-Ids ↔ Befehle im `MainViewModel` (jede Id hat einen Befehl, jeder Leisten-Befehl eine Id).

## 11. Umsetzung in Plänen

1. **Leisten und Kachelzeile:** Settings-Felder, Kataloge, Kopie des PMT-Shell-Codes, Icons, Menü, Werkzeugleiste,
   Kachelzeile, Dokument-Leiste des Tabellen-Tabs (die alte Knopfzeile entfällt im selben Plan), Einstellungsseiten
   Layout/Kachelzeile/Werkzeugleiste. Danach nutzbar: alles mit einem Klick.
2. **Kette:** `BuildChain`, `InstalledFrom` + Migration, `InstallOnlyAsync`, Statusleiste, Aktualisierung. Danach
   nutzbar: Nutzer sieht, ob das Spiel seinen Stand hat.
3. **Kategorien, Filterleiste, Pfadleiste:** Library `Categories`, Stores, Reiterfarbe, Kontextmenü, Filterleiste,
   Pfadleiste, Einstellungsseite Kategorien.
4. **Tastenkürzel-Liste und Abschluss:** Einstellungsseite Tastenkürzel, Changelog, Testcheckliste.

Jeder Plan erst, wenn die Interfaces des vorigen existieren.

## 12. Annahmen, die der erste echte Lauf prüft

1. Der PMT-Shell-Code (`TileBar`, `ReorderDragHost`, `ReorderListBox`) läuft mit dem TWMT-Theme nach Abbildung der
   vier Token-Namen ohne weitere Anpassung.
2. Das Durchsuchen von `pack\` auf Schreibzeiten braucht bei 1000 Dateien unter 100 ms (Spike in Plan 2 messen, sonst
   Cache mit Fingerprint je Ordner).
3. Der `FileSystemWatcher` auf `pack\` feuert bei Speichern aus dem Raster und beim Übernehmen zuverlässig; falls nicht,
   reicht die Neuberechnung nach eigenen Operationen plus Fensteraktivierung.
4. Vanilla-Herkunft lässt sich aus `TabSource` ohne neuen Katalogzugriff bestimmen.
5. Die Kette bleibt verständlich, ohne dass ein Schritt die anderen nachholt (Nutzer erwartet A). Prüfen beim ersten
   Durchlauf „Text ändern → Kette abarbeiten → Spiel starten“.

## 13. Manuelle Testcheckliste (nach Plan 4)

1. Start ohne `settings.json`: Menü mit vier Punkten, Kachelzeile mit zwei Kacheln, Werkzeugleiste oben, Statusleiste
   ohne Kette (kein Projekt).
2. Projekt öffnen: Kette erscheint, Zustand plausibel. Objekt-Tab ändern → „Übernehmen“ gelb. Übernehmen → „Speichern“
   oder „Bauen“ gelb. Bauen → „Installieren (älter)“. Installieren → alles grün.
3. Datei unter `pack\` außerhalb des Tools ändern → „Gebaut (älter)“ binnen einer Sekunde.
4. Klick auf „Installieren“ bei „älter“ → Nachfrage; Abbrechen ändert nichts.
5. Kachel ziehen, Größe wechseln, Beschriftung aus, Kachel ausblenden und wieder einblenden (Position erhalten).
6. Werkzeugleiste links: nur Icons, Tooltips mit Kürzel; wieder oben: Beschriftung zurück. Trennlinie hinzufügen,
   Eintrag ausblenden, Standard wiederherstellen.
7. Tab-Reiter: Farbbalken je Kategorie; eigene Kategorie mit Regel `db/building*` anlegen → passende Tabs wechseln Farbe.
   Kategorie löschen → Tabs fallen zurück. Ganzer Reiter färben an/aus.
8. Filter: Vanilla ausblenden, aktiver Vanilla-Tab bleibt sichtbar, Tabwechsel blendet ihn aus; „3 ausgeblendet“ klicken.
9. Pfadleiste: Pfad kopieren, Ordner öffnen, bei Objekt-Tab Schlüssel kopieren.
10. Einstellungen Tastenkürzel: Liste vollständig gegen Menü und Tooltips.
11. Sprache auf Englisch: alle neuen Texte übersetzt, nichts leer.
12. App schließen und starten: Leisten, Filter und Zuordnungen wie verlassen.

## Änderungen beim Schreiben von Plan 1

- **Task 2, Laden fehlender Reihenfolgen:** Fehlt `tileOrder` oder `toolbarOrder` in `settings.json` (oder ist `null`), bleibt die
  Standardreihenfolge (`DefaultOrder`, bei der Werkzeugleiste mit den vier Trennlinien). Nur eine vorhandene Liste läuft durch den
  normalisierenden Setter. Grund: `ReorderList.Complete([], ToolbarCatalog.All, …)` liefert die Katalog-Kennungen ohne Trennlinien;
  ohne diese Unterscheidung hätte eine alte Datei die Trennlinien verloren. Eine vorhandene Liste ohne Trennlinien bekommt keine
  erfundenen (Review Focus 2); fehlende Kennungen werden in Katalogreihenfolge hinten angehängt.
- **Task 2, bestehender Wächtertest:** `SettingsFileMapTests.Sample` kennt jetzt auch Enums, `bool`, `int` und `IReadOnlyList<string>`,
  damit der Round-Trip-Wächter die neuen Felder prüft.
- **Task 5, Tooltip eines gesperrten Leisten-Knopfs:** Erste Zeile immer Name und Kürzel („Bauen (Ctrl+B)“), bei einem Sperrgrund
  folgt er als zweite Zeile. Grund: Links und rechts zeigt die Leiste nur Icons; ein Tooltip nur mit dem Grund würde den Knopf nicht
  benennen (6.3 „Tooltips nennen Name und Kürzel“). Der Plan sah den Grund statt des Namens vor.
- **Task 5, `ToolbarBinding`:** trägt den Sperrgrund als `Func<string?>? Reason` statt als Property-Pfad (`ToolTipPath`). Eine
  Tabelle im `MainViewModel`, kein Auflösen von Pfad-Strings. Das Fenster liest `MainViewModel.ToolbarToolTip(id)` bei jedem
  `PropertyChanged` des View-Models und bei Sprachwechsel neu.
- **Task 5, Trennlinien im Menü Projekt:** Die Trennlinie nach „Neue Loc-Datei“ ist nur mit den Datei-Operationen sichtbar, die vor
  „Bauen“ nur mit den Dokumenten, damit nie zwei Trennlinien aufeinander folgen.
- **Task 5, Rahmen der Werkzeugleiste:** PMT hat keine Fenster-Verdrahtung als Vorlage. Die Leiste hat eine 1-px-Linie
  (`Theme.Separator`) zur Arbeitsfläche hin: oben unten, unten oben, links rechts, rechts links.
- **Task 5, Wizards:** Der Composition Root setzt `MainViewModel.Wizards` vor dem Fenster. Das Fenster übernimmt den Wert im
  Konstruktor und folgt zusätzlich `PropertyChanged("Wizards")` (alte Befehle abmelden, Kachelzeile neu bauen).
- **Review Tasks 1–5, `TileItem.DefaultVisible`:** entfällt. Kein Code hat das Feld gelesen (jede Kachel ist sichtbar, bis der Nutzer sie
  ausblendet); eine Kachel, die ausgeblendet startet, bekommt das Feld erst mit dem Werkzeug, das es braucht.
- **Review Tasks 1–5, Kachel-Zuordnung:** Welche Befehle und Sperrgründe eine Kachel hat, liefert `MainViewModel.TileBindingFor(id)`
  (wie `ToolbarBindingFor`, gemeinsamer Record `EntryBinding`); das Fenster verdrahtet nur noch. Die Tooltips der Werkzeugleiste
  liest das Fenster nur bei den sechs Grund-Properties des View-Models neu, nicht bei jedem `PropertyChanged`.
- **Task 7, Ziehgriff der `ReorderListBox`:** ohne eigenen Tooltip. `ReorderTexts` trägt nur die drei Knopftexte (Nach oben, Nach unten,
  Entfernen); das Ziehen erklärt der Hinweis über der Liste.
- **Task 7, neue Trennlinie:** `sep:n` mit n = höchste vorhandene Nummer + 1 (Standard: `sep:5`), hinten angehängt. Eine Nummer, die durch
  Entfernen frei wurde, wird nicht wieder vergeben, außer sie war die höchste.
- **Task 7, Knöpfe der Seiten:** „Standard wiederherstellen“, „Trennlinie hinzufügen“ und das Entfernen einer Trennlinie sind Befehle bzw.
  Methoden des `SettingsViewModel` (`ResetTilesCommand`, `AddToolbarSeparatorCommand`, `ResetToolbarCommand`, `RemoveToolbarSeparator`);
  `BarSettingsPages` verdrahtet nur. „Standard wiederherstellen“ ändert keine Sichtbarkeit: Kacheln bekommen die Standardreihenfolge und
  verlieren „Nur Symbol“, die Werkzeugleiste bekommt die Standardreihenfolge mit den vier Trennlinien.
- **Task 7, Lebensdauer der Seiten:** Jedes Öffnen baut ein neues Fenster, die Settings leben weiter. Die Code-Seiten (`BarSettingsPage`)
  melden sich beim Schließen des Fensters von `GlobalSettings.Changed` und `AppTexts.PropertyChanged` ab. Ein Sprachwechsel baut die Seite
  neu (Hinweis, Knöpfe, Zeilen und Knopf-Tooltips).
- **Task 7, gemerkte Seite:** `SelectedCategory` liest und schreibt `GlobalSettings.SettingsCategory`. Ein Wert über der letzten Seite
  (heute 4, Werkzeugleiste) zeigt die erste Seite; die Obergrenze steigt mit den Seiten aus Plan 3 und 4. Das Häkchen „Sichtbar“ der
  Werkzeugleiste nutzt den Text `settings.tiles.visible`.
