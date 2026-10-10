# Spec: Teil 4.1 (Objekt-Ansicht, nur Lesen)

- **Datum:** 2026-10-09
- **Status:** Entwurf, wartet auf Freigabe durch den Nutzer
- **Baut auf:** `docs/specs/2026-10-02-fundament.md` (Teil 1) und
  `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md` (Teil 2, umgesetzt und gemergt)
- **Pläne:** zwei Pläne nacheinander, siehe Abschnitt 9

---

## 1. Zweck und Einordnung

Die Tabellen eines Total-War-Spiels zerreißen ein Objekt (Gebäude, Einheit, Trait, Ancillary,
Fraktion) in viele Dateien: Kosten in einer Tabelle, Effekte in einer zweiten, Voraussetzungen in
einer dritten, Namen und Beschreibungen in Loc-Dateien. Die Textansicht aus Teil 2 zeigt nur die
TSV-Form einer einzelnen Tabelle und hilft beim Verstehen nicht (Nutzerwunsch vom 2026-10-05).

Teil 4.1 baut die Objekt-Ansicht: ein Codeblock, der alles, was zu einer Zeile gehört, untereinander
zeigt, mit sichtbaren Verknüpfungen, Herkunft pro Zeile (Vanilla, Mod, Projekt) und den
Spieltexten. Der Block ist eine berechnete, unveränderliche Ansicht auf Tabellenzeilen. Bearbeiten im
Block ist Teil 4.2.

### 1.1 Zerlegung von Teil 4

Am 2026-10-09 hat der Nutzer die Zerlegung von Teil 4 in Unterteile beschlossen:

- **4.1 Objekt-Ansicht, nur Lesen** (diese Spec).
- **4.2 Objekt bearbeiten** (Codeblock als Eingabe, Rückschreiben in viele Tabellen als ein
  Undo-Schritt, Maske als zweite Ansicht, Assistenten wie „Neues Gebäude“).
- **4.3 Katalog und Autovervollständigung** (alle Einstellmöglichkeiten wie Effekte und Boni mit
  Suche und Kategorien, auch aus Mods; Vorschläge beim Tippen).
- Kategorie-Browser und voreingestellte Inspektoren über das Gebäude hinaus: Backlog unter Teil 4,
  Reihenfolge entscheidet der Nutzer nach 4.1.

## 2. Ziele und Non-Goals

### 2.1 Ziele

1. **Objekt aus jeder Zeile.** Rechtsklick auf eine Zeile einer DB-Tabelle mit Schlüsselfeld
   (Vanilla-Pack, Mod-Pack oder Projekt) öffnet „Als Objekt anzeigen“ in einem eigenen Tab.
2. **Ein Block, keine Bäume.** Der Tab zeigt die Wurzelzeile, ihre Spieltexte und einen Abschnitt
   pro Tabelle, die auf die Wurzel zeigt. Verbindungstabellen (Junctions) werden durchgegriffen:
   statt der Junction-Zeile steht das andere Ende.
3. **Wie das Spiel es sieht.** Die Zeilen kommen aus Vanilla, den aktiven Mods und dem aktiven
   Projekt in Ladereihenfolge. Spätere Packs überschreiben gleiche Schlüssel. Jede Zeile trägt ihre
   Herkunft, überschriebene Herkünfte bleiben sichtbar. Vanilla, Mod und Projekt sind farblich
   unterschieden.
4. **Verknüpfungen als Links.** Jeder Referenzwert ist ein Link. Klick öffnet das Ziel als Objekt im
   selben Tab mit Verlauf (Zurück, Vor). Hover zeigt den Spieltext des Ziels (z. B. den Namen der
   Technologie). „Zur Zeile springen“ öffnet die Tabelle im Raster-Tab und markiert die Zeile.
5. **Datengetrieben.** Verknüpfungen kommen aus dem RPFM-Schema, die Darstellung pro Objektart aus
   einer Kategorie-Datei im Spielprofil. Ohne Kategorie-Datei gibt es eine brauchbare
   Standard-Darstellung. In 4.1 existiert genau eine Kategorie-Datei (Gebäude) als Beweis.
6. **Nicht destruktiv, nicht blockierend.** Nur Lesen. Der Aufbau läuft über die
   Fortschritts-Komponente, ist abbrechbar und hält den Speicher unter einer Obergrenze.

### 2.2 Non-Goals

- Bearbeiten im Block, Maske, Assistenten „Neues Gebäude“, „Neuer Trait“ (4.2).
- Katalog der Effekte und Boni, Autovervollständigung (4.3).
- Kategorie-Browser (alle Gebäude, alle Lords) und weitere voreingestellte Inspektoren (Backlog).
- Eigene Mod-Auswahl in den Einstellungen; 4.1 liest die aktiven Mods aus `used_mods.txt`
  (Backlog).
- Transitiver Referenzgraph über mehr als eine Ebene, Referenz-Baumansicht zum Debuggen (Backlog).
- Prüfung von Referenzen und doppelten Schlüsseln beim Bearbeiten (Backlog, nutzt später den
  Referenzgraphen).
- Index oder Cache auf Platte (gemessen nicht nötig, Abschnitt 3).
- Entpacken der Vanilla-Tabellen als Text (verworfen, Abschnitt 6).

### 2.3 Erfolgskriterien

- Der Nutzer öffnet `wh_main_emp_barracks_2` aus `db.pack` als Objekt und sieht ohne weiteren
  Klick: Felder der Wurzel, Name und Kurzbeschreibung, Effekte, erlaubte Einheiten, Upgrade von und
  nach, Technologien, die das Gebäude brauchen.
- Ein Gebäude, dem `flogis_buildings_mod.pack` Effekte anhängt, zeigt diese Effekte mit der
  Herkunft des Mods in Mod-Farbe neben den Vanilla-Effekten.
- Eine Fraktion (`wh_main_emp_empire`, 32 Abschnitte, 374 Zeilen im Spike) erscheint in unter
  2 s und bleibt durch Zuklappen großer Abschnitte lesbar.
- Nach „Speichern“ einer Projekttabelle, die zum Objekt gehört, zeigt der Tab „veraltet“ und
  „Aktualisieren“ baut den Block neu.

## 3. Gemessener Ist-Zustand (Spike vom 2026-10-09)

Wegwerf-Konsolenapp gegen die Lese-Libraries (`Schema`, `Tables`, `PackFiles`, `Workspace`).
Datenbasis: WH3 mit 262 Vanilla-Packs, 20 Workshop-Packs (die ersten 20 von 396), 7 Mods im
Datenordner, Loc aus `local_en.pack`. Kalte Messungen nach frischem Prozessstart.

### 3.1 Schema und Referenzgraph

- 1742 Tabellen im Schema, 5825 Referenzfelder, 927 Tabellen werden referenziert.
- Umkehrabbildung (Zieltabelle → Quelltabelle, Feld) in 5 ms nach dem Laden des Schemas (202 ms).
- Auf `building_levels` zeigen 39 Tabellen über 81 Felder (manche Tabellen mit mehreren Feldern,
  z. B. `building_upgrades_junction.from` und `.to`).
- Junction-Regel „genau zwei Referenzfelder, höchstens vier Felder“: 532 von 1742 Tabellen. Bei den
  Gebäude-Tabellen trifft sie alle `*_junctions`-Tabellen und lässt `building_effects_junction`
  (6 Felder: Effekt, Scope, Wert) korrekt als Werte-Zeile stehen.
- Schema-Lücken: Stichprobe über `building_levels`, `character_traits`,
  `building_effects_junction`, `main_units`, `ancillaries` (Textfelder ohne Referenz, deren Werte zu
  mindestens 50 % Schlüssel einer anderen Tabelle sind) fand keine. Das Schema genügt für 4.1.

### 3.2 Zeitbedarf und Speicher (Option A, Dekodieren bei Bedarf)

| Messung | Wert |
|---|---|
| Katalog-Scan (665 Packs) | 3,4 s kalt (bekannt aus dem Fundament, läuft schon im Hintergrund) |
| Indexe der 289 Packs der Ladereihenfolge | 0,5 s |
| Objekt kalt: alle Quelltabellen aus allen Packs dekodieren und zusammenführen | Gebäude 262 ms, Einheit 323 ms, Fraktion 358 ms; davon Dekodieren 66 ms für rund 200 Einträge |
| Objekt warm (Tabellen im Speicher) | 88–90 ms, nur wegen linearer Suche im Spike |
| Speicher pro Tabellensatz einer Objektart | Gebäude 50 MB, Einheit 69 MB, Fraktion 48 MB, Ancillary 8 MB |
| Alle 1601 Tabellen aller 289 Packs zusammengeführt | 1,9 s, 1.055.425 Zeilen, 477 MB, Prozess 1,1 GB |
| `local_en.pack` komplett (248 Loc-Dateien) | 447 ms, 266.510 Schlüssel |

Folgerung: Dekodieren bei Bedarf reicht. Ein Index auf Platte ist nicht nötig. Alle Tabellen im
Speicher zu halten (477 MB) ist zu viel; der Cache braucht eine Obergrenze mit Verdrängung.

### 3.3 Blockgrößen

| Wurzel | Abschnitte | Zeilen | Abschnitte über 20 Zeilen |
|---|---|---|---|
| `building_levels` `wh_main_emp_barracks_1` | 7 | 24 | 0 |
| `building_levels` `wh_main_emp_barracks_2` | 8 | 15 | 0 |
| `character_traits` `wh_main_trait_all_personality_all_hates_dwarfs` | 1 | 1 | 0 |
| `main_units` `wh2_dlc13_emp_inf_swordsmen_ror_0` | 13 | 42 | 1 |
| `factions` `wh_main_emp_empire` | 32 | 374 | 5 (bis 158 Zeilen) |
| `ancillaries` `wh_main_anc_arcane_item_black_periapt` | 1 | 1 | 0 |

### 3.4 Überschreibung und Mods

- `wh_main_emp_empire` kommt aus einem Workshop-Pack und überschreibt `db.pack`; ein Abschnitt
  mischt `db.pack` mit zwei Mods des Nutzers. Überschreibung nach Ladereihenfolge funktioniert und
  ist pro Zeile nachvollziehbar.
- `flogis_buildings_mod.pack` enthält nur `building_effects_junction` und
  `effect_bundles_to_effects_junctions`: ein Mod, der bestehende Gebäude ergänzt, ohne eigene
  `building_levels`-Zeilen. Genau dieser Fall muss im Block als Mod-Zeilen neben Vanilla-Zeilen
  erscheinen.
- 104 Mod-Einträge haben ein älteres Tabellenlayout (weniger Felder) als Vanilla. Der Spike hat sie
  übersprungen; die Umsetzung muss über Feldnamen zusammenführen.
- 4 Einträge waren nicht dekodierbar (Tabelle oder Version nicht im Schema).

### 3.5 Loc und Text-Tabellen

- Loc-Schlüssel folgen `<tabelle>_<spalte>_<schlüssel>`. Gebäudename:
  `building_culture_variants_name_<level_name>` (192 von 300 Gebäuden), Kurzbeschreibung:
  `building_short_description_texts_short_description_<level_name>` (189 von 300). Einheit:
  `land_units_onscreen_name_<key>`. Fraktion: `factions_screen_name_<key>`. Ancillary:
  `ancillaries_onscreen_name_<key>`. Effektbündel: `effect_bundles_localised_title_<key>`.
- Für `character_traits` gab es keinen Loc-Treffer mit dem Trait-Schlüssel. Der Name hängt
  vermutlich an `character_trait_levels` (Annahme, Abschnitt 10).
- Text-Tabellen wie `building_short_description_texts` fehlen im Schema und in den Packs. Referenzen
  darauf (z. B. `building_culture_variants.short_description`) sind „hängend“ und lösen sich nur
  über Loc auf: `<zieltabelle>_<zielspalte>_<wert>`.
- Hängende Referenzen auf fehlende Enum-Tabellen sind normal (`effect_bundle_targets`,
  `movie_event_strings`, `pre_battle_speech_parameters_enum`). Sie dürfen keinen Fehler auslösen.

### 3.6 Aktive Mods

Das Spiel schreibt bei jedem Start `used_mods.txt` in den Spielordner (gemessen am 2026-10-09,
Stand 2026-10-07, 239 Bytes):

```
add_working_directory "D:\SteamLibrary\steamapps\workshop\content\1142710\3807929920";
add_working_directory "D:\SteamLibrary\steamapps\workshop\content\1142710\2859802328";
mod "end_times_mp_narrative_fix.pack";
mod "QueBatAutResv1.pack";
```

`data\language.txt` enthält den Sprachcode des Spiels (`DE`). Die Loc-Packs heißen `local_<suffix>.pack`
mit abweichenden Suffixen (`DE` → `local_ge.pack`); die Zuordnung ist Profildaten.

### 3.7 Echtdaten-Tests aus Plan 1 (2026-10-09)

Gemessen mit den Echtdaten-Tests der Objects-Bibliothek (Spiel-Loc `local_ge.pack`, 264 Quellen mit 2 Mods aus
`used_mods.txt`, 0 Warnungen der Ladereihenfolge, jeweils 0 Objektwarnungen):

| Objekt | Dauer | Texte | Abschnitte |
|---|---|---|---|
| `wh_main_emp_barracks_2` (Gebäude) | erster Aufbau 1217 ms, zweiter Aufbau mit demselben Store 66 ms | 13 | 8 |
| `wh_main_emp_empire` (Fraktion, frischer Store) | 1352 ms | 31 | 32 |
| `wh_main_trait_all_personality_all_hates_dwarfs` (Trait) | nicht gemessen | 2 | 1 |
| Alle 1742 Schematabellen zusammenführen | 4406 ms, 339 MB Cache | nicht zutreffend | nicht zutreffend |

Loc-Spalten: Rund 130 von 138 Loc-Spalten fehlen im Schema. Die Regel, die daraus folgt (Spalten aus den
Loc-Schlüsseln ableiten), steht unter 5.4 „Festlegungen aus Plan 1“.

## 4. Schreibgrenze

Teil 4.1 schreibt nichts. Die Schreibgrenze aus Teil 2 Abschnitt 4 bleibt unverändert. Insbesondere:
keine Datei im Spielordner, kein Index unter `%LocalAppData%`, kein Entpacken. Der Tabellen-Cache
lebt nur im Speicher.

Nebenfund des Spikes: `<Spielordner>\data\!twmt_spike.pack` (655 Bytes, 2026-10-03, enthält
`twmt_spike.loc`) ist ein Überbleibsel eines Teil-2-Spikes und wird nicht vom Tool gelöscht. Der
Nutzer entscheidet.

## 5. Entwurf

### 5.1 Module

| Modul | Aufgabe | Referenziert |
|---|---|---|
| `TotalWarModTool.Objects` (neu) | Referenzgraph, Tabellen-Store mit Ladereihenfolge, Objektmodell, Text-Rendering, Kategorie-Dateien | `Schema`, `Tables`, `PackFiles`, `Workspace`, `Projects`, `Operations` |
| `TotalWarModTool.GameProfiles` | Profil um Loc-Pack-Zuordnung und Objekt-Kategorien-Ordner erweitert | – |
| `TotalWarModTool.App` | Objekt-Tab, Kontextmenü, Links, Verlauf, Veraltet-Markierung | alle |

`Objects` ist UI-frei, liefert Diagnosen (Enum plus englisches `Detail`), keine UI-Texte, kein Log.
Alle Pfade kommen als Parameter (Root-Pfade), damit Tests in Temp-Ordnern laufen.

### 5.2 Referenzgraph

`ReferenceGraph.Build(GameSchema)`:
- pro Tabelle die neueste Definition (höchste Version) als Grundlage;
- `Incoming(table)`: Liste von `(SourceTable, FieldName, TargetColumn)` für jedes Feld mit
  `is_reference` auf diese Tabelle;
- `IsJunction(table)`: genau zwei Referenzfelder und höchstens vier Felder;
- `KeyFields(table)`: alle Felder mit `is_key`; eine Tabelle ohne Schlüsselfeld hat keine Objekte.

Der Graph ist pur und wird einmal pro Schema gebaut (5 ms gemessen).

### 5.3 Ladereihenfolge

`LoadOrder.Resolve(catalog, usedModsFile, project)` liefert eine Liste von `TableSource`
(Vanilla-Pack, Mod-Pack, Projekt) in dieser Reihenfolge:

1. Vanilla-Packs des Katalogs in Katalogreihenfolge (Manifest).
2. Mods aus `used_mods.txt` in Dateireihenfolge. Ein `mod "<name>"` wird in den
   `add_working_directory`-Ordnern und im Datenordner gesucht; das erste Vorkommen zählt. Fehlt
   das Pack, wird es ausgelassen und als Warnung gemeldet. Fehlt die Datei, gibt es keine Mods und
   eine Warnung. Eine unlesbare Datei zählt wie eine fehlende.
3. Das aktive Projekt: Tabellen unter `pack\db\<tabelle>\*.tsv` und Loc unter `pack\text\…\*.loc.tsv`,
   gelesen über `TableTsv` und `ProjectFileOpener`. Immer zuletzt.

Ist kein Projekt aktiv, fehlt Schritt 3. Ist die Wurzelzeile aus einem Pack, das nicht in der
Ladereihenfolge liegt (ein Workshop-Pack, das nicht aktiv ist), wird dieses Pack hinter die Mods
und vor das Projekt eingefügt und das im Kopf gesagt („Pack nicht aktiv, für diese Ansicht
hinzugefügt“).

Loc-Quelle: das Loc-Pack zum Sprachcode aus `data\language.txt` über die Zuordnung im Profil
(`locPacks`, z. B. `"DE": "local_ge.pack"`), Fallback `local_en.pack`. Loc-Dateien aus Mods und
Projekt kommen in Ladereihenfolge dazu und überschreiben.

### 5.4 Tabellen-Store

`TableStore` hält pro Tabellenname die zusammengeführte Sicht `MergedTable` über die
Ladereihenfolge:
- Zeilen in Reihenfolge des ersten Auftretens; eine spätere Zeile mit gleichem Schlüssel ersetzt
  die frühere und merkt sich deren Herkunft in `Overridden`. Tabellen ohne Schlüsselfeld werden nur
  angehängt.
- Zusammenführung über Feldnamen: Einträge mit älterer Version (andere Definition) werden auf die
  neueste Definition abgebildet; fehlende Felder bleiben leer, die Zeile trägt `OlderLayout`.
- Index pro Feld, das im Referenzgraphen als Quellfeld vorkommt: Wert → Zeilen. Damit ist der
  warme Aufbau eine Dictionary-Abfrage.
- Nicht dekodierbare Einträge (Pack gesperrt, Tabelle ohne Schema, Version unbekannt, TSV kaputt)
  werden als `SourceProblem` (Herkunft, Grund) gesammelt, nicht geworfen.
- Cache: Schlüssel ist der Tabellenname plus die Fingerprints aller Quellen der Ladereihenfolge
  (Pack: Pfad, Größe, mtime; Projektdatei: `ProjectFileWriter.Fingerprint`). Obergrenze
  `MaxCacheBytes` (eine Konstante, Start 200 MB, geschätzt aus Zeilen mal Zellen mal
  Konstante). Bei Überschreitung fliegen die am längsten nicht benutzten Tabellen. Das ist nie ein
  Fehler.
- Loc: `LocStore` hält Schlüssel → (Text, Herkunft) für die Loc-Quelle, einmal geladen
  (0,45 s gemessen), mit demselben Fingerprint-Prinzip.

#### Festlegungen aus Plan 1

- Hat sich der Typ eines Feldes zwischen Schema-Versionen geändert (gleicher Name), wird der Wert über seinen Text in den neuen Typ konvertiert (`CellText.Format`, dann `CellText.TryParse`); passt der Text nicht, gilt der Standardwert des Zieltyps. Die Zeile trägt in beiden Fällen `OlderLayout`.
- Nennt die Meta-Zeile einer Projekt-TSV einen anderen Tabellennamen als der Ordner `pack\db\<tabelle>\`, in dem sie liegt, ist das ein `SourceProblem` `TsvInvalid`.
- Loc-Spalten stammen aus den Loc-Schlüsseln selbst (Bereich mit Präfix `<tabelle>_`, die Schlüsselmenge der Tabelle als Trenner: der Rest nach dem Präfix wird von links an jedem `_` geteilt, der erste Schwanz, der ein `JoinedKey` einer Zeile der Tabelle ist, liefert die Spalte), nicht aus der Tabellendefinition, weil rund 130 von 138 Loc-Spalten im Schema fehlen (gemessen 2026-10-09). Die Texte einer Zeile (Wurzel, gefundene Referenzziele, Abschnittszeilen) werden mit dem `JoinedKey` der ganzen Zeile gesucht.
- Bei Text-Tabellen ohne Schema (Ziel einer Referenz, das das Schema nicht kennt) werden zuerst die genauen Schlüssel `<tabelle>_<spalte>_<wert>` für die Anzeigespalten (`ObjectBuilder.DisplayNameColumns`, in deren Reihenfolge) gesucht; ohne Treffer gilt der kürzeste Loc-Schlüssel mit Präfix `<tabelle>_` und Suffix `_<wert>` (bei gleicher Länge der ordinal erste), die Spalte ist der Teil dazwischen; ohne Treffer bleibt es `NotFound` (Wurzelfelder) beziehungsweise `Unchecked` (Abschnittszeilen).
- Abweichend von „Index pro Feld“ sucht Plan 1 Zeilen über ein Feld linear (`MergedTable.FindByField`); der warme Aufbau eines Gebäudes dauerte so 66 ms (Echtdaten-Lauf 2026-10-09, Abschnitt 3.7). Den Index pro Feld gibt es erst, wenn Plan 2 einen Bedarf misst.
- Cache-Schlüssel: Ein Pack trägt nur dann seinen Fingerprint (Größe, mtime) zum Schlüssel einer Tabelle bei, wenn es Einträge dieser Tabelle hat, sonst `-` (fehlendes Pack `missing`, fehlender Projektordner `none`); so macht ein geändertes Pack nur seine eigenen Tabellen ungültig. Ergebnisse mit vorübergehenden Problemen (gesperrtes Pack, nicht lesbarer Eintrag oder Projektordner) werden nicht im Cache gehalten, damit eine Sperre nicht hängen bleibt.
- Abschnitte im Modus `list` (Regel 4 in 5.6): Pro Zeile entfällt nur das Feld, das auf die Wurzel zeigt (`FieldName` des Abschnitts); Schlüsselfelder der Zeile bleiben sichtbar, weil sie die Zeile erst unterscheidbar machen.
- Abschnittskopf (Regel 4 in 5.6): Er zeigt den Titel-Schlüssel aus der Kategorie-Datei oder, ohne Titel, den Tabellennamen ohne `_tables`, nicht beides; dahinter Zeilenzahl und Herkünfte.

### 5.5 Objektmodell

`ObjectBuilder.Build(ObjectId root, LoadOrder order, CancellationToken)` liefert `ObjectView`:

- `ObjectId`: Tabellenname plus Schlüsselwerte in Schemareihenfolge (Composite erlaubt).
- `Root`: `Provenance` (Quelle, Art Vanilla / Mod / Project, `Overridden`-Liste, `OlderLayout`),
  Felder als `FieldValue` (Name, Wert als Text über `CellText.Format`, `Reference` oder null,
  `Resolution`: `Found`, `NotFound`, `LocOnly`, plus Loc-Text bei `LocOnly`).
- `Texts`: Loc-Einträge zur Wurzel: für die Wurzeltabelle und die Zieltabellen ihrer Referenzen
  alle Schlüssel `<tabelle>_<spalte>_<schlüssel>`, die im `LocStore` existieren. Wie die Spalten
  gefunden werden (aus den Loc-Schlüsseln, begrenzte Präfix-Bereiche) und dass auch die Texte der
  Abschnittszeilen dazugehören, steht in 5.4 „Festlegungen aus Plan 1“.
- `Sections`: eine pro `(SourceTable, FieldName)` aus `Incoming`, deren Feld auf ein Schlüsselfeld
  der Wurzel zeigt, mit mindestens einer Zeile. Jede Zeile hat `Provenance`, Felder und bei
  Junction-Tabellen `OtherEnd` (`ObjectId`, `Resolution`, Loc-Name). Zeilen mit gleichem Schlüssel
  aus derselben Quelle werden beide gezeigt und tragen `Duplicate`.
- `Warnings`: fehlende Mods, `SourceProblem`s, Tabellen ohne Schema, Wurzel nicht gefunden.
- `IsStale` gehört nicht ins Modell; das entscheidet die App über die Fingerprints.

Der Aufbau meldet Fortschritt pro Tabelle („Tabelle 12 / 39“) und bricht bei `CancellationToken`
ohne Teilergebnis ab.

### 5.6 Rendering

`ObjectText.Render(ObjectView, CategoryDefinition?, RenderOptions)` liefert `IReadOnlyList<TextLine>`;
eine Zeile ist eine Liste von `TextSpan(Text, Kind, Target?)` mit `Kind` aus Normal, TableName,
Key, FieldName, Value, Link, LocText, ProvenanceVanilla, ProvenanceMod, ProvenanceProject,
Overridden, Warning, Collapsed, Duplicate, OlderLayout. `Target` ist bei Link die `ObjectId`; bei
Collapsed tragen `Section` die Abschnittsnummer und `Count` die Zahl der verborgenen Zeilen. Das
Rendering ist deterministisch und hat keinen UI-Text: Abschnittstitel kommen als Loc-Schlüssel
(`Kind = Title`, Text = Schlüssel) heraus, die App übersetzt sie; die Texte von Collapsed („+N“),
Duplicate und OlderLayout sind nur technischer Ersatz, die App bildet ihren Text aus `Kind` und `Count`.

Regeln:
1. Kopf: Tabellenname, Schlüssel, Herkunft, dahinter überschriebene Herkünfte. Dann Warnungen.
2. Wurzelfelder als `name = wert`, Referenzen als Link, bei `LocOnly` der Loc-Text in
   Anführungszeichen dahinter, bei `NotFound` ohne Link mit `Kind = Warning`.
3. Loc-Block: eine Zeile pro Text; Loc-Zeilen nennen Tabelle und Spalte (`tabelle.spalte`), Text und
   Herkunft und verlinken das Objekt, dem der Text gehört (`LocEntry.Owner`), wenn es nicht die Wurzel ist.
4. Abschnitte: Titel, Tabellenname, Zeilenzahl, Herkünfte. Junction-Abschnitt: pro Zeile nur das
   andere Ende als Link plus Loc-Name. Sonst pro Zeile die Nicht-Schlüssel-Felder als
   `name = wert` in einer Zeile; die Felder, die auf die Wurzel zeigen, werden weggelassen.
5. Leere Abschnitte fehlen. Ein Abschnitt mit mehr als `CollapseThreshold` Zeilen (Konstante,
   Start 20) zeigt die ersten `CollapsedPreview` Zeilen (Konstante, Start 5) und eine
   `Collapsed`-Zeile „… N weitere“. `RenderOptions.ExpandedSections` klappt einzelne Abschnitte auf.
6. Ohne Kategorie-Datei: Abschnitte alphabetisch nach Tabellenname, Titel = Tabellenname ohne
   `_tables`.
7. Mit Kategorie-Datei: Reihenfolge und Titel aus der Datei, Modus `list` (wie 4) oder `sentence`
   mit Muster wie `requires technology {technology_key}`; Platzhalter sind Feldnamen, Referenzfelder
   werden Links. `hidden: true` versteckt einen Abschnitt. Tabellen, die die Datei nicht nennt,
   folgen danach alphabetisch.

### 5.7 Kategorie-Dateien

Ordner `profiles/<profil-id>/objects/`, eine JSON-Datei pro Wurzeltabelle, `schemaVersion: 1`:

```json
{
  "schemaVersion": 1,
  "rootTable": "building_levels_tables",
  "titleKey": "objects.category.building",
  "sections": [
    { "table": "building_effects_junction_tables", "titleKey": "objects.section.effects", "mode": "list" },
    { "table": "building_level_required_technology_junctions_tables", "titleKey": "objects.section.requirements",
      "mode": "sentence", "pattern": "requires technology {technology_key}" },
    { "table": "cai_construction_system_synergies_tables", "hidden": true }
  ]
}
```

Unlesbare oder ungültige Dateien werden übersprungen und als Warnung gemeldet; die Ansicht fällt
auf das Standard-Rendering zurück. In 4.1 wird genau eine Datei ausgeliefert (`building_levels`).
Titel-Schlüssel stehen in `locales/en.json` und `de.json`.

### 5.8 App

- Kontextmenü im Raster (Pack-Tabelle und Projekt-Tabelle): „Als Objekt anzeigen“, aktiv nur bei
  Tabellen mit Schlüsselfeld und markierter Zeile. Bei einer Projekttabelle mit ungespeicherter
  Änderung wird der gespeicherte Stand gezeigt und das im Kopf gesagt.
- `ObjectTabViewModel` (Tab-Art `Object`): Kopfzeile mit Zurück, Vor, Aktualisieren, „Zur Zeile
  springen“, Status (Ladereihenfolge: „Vanilla + 2 Mods + Projekt“, Warnungen als Tooltip).
  Inhalt: der Block als `RichTextBox` (nur Lesen) oder `FlowDocument` mit Hyperlinks; Farben aus
  Theme-Tokens pro `Kind`. Klick auf Link: Ziel im selben Tab, Verlauf wächst. Shift-Klick: neuer
  Tab. Hover: Tooltip mit Loc-Name des Ziels. Klick auf `Collapsed`: Abschnitt aufklappen, Block
  neu rendern (kein Neuaufbau).
- Aufbau über `ProgressRunner` („Objekt wird aufgebaut“), Fortschritt aus `ObjectBuilder`.
  Abbruch: Tab bleibt mit „abgebrochen“ und „Aktualisieren“.
- Veraltet: Der Tab merkt sich die Fingerprints seiner Quellen. Nach Speichern einer Projektdatei
  oder einem Katalog-Aktualisieren prüft die App die Fingerprints; bei Abweichung zeigt der Tab
  „veraltet“, kein automatischer Neuaufbau. Dafür merkt sich der Tab beim Aufbau
  `TableStore.SignatureOf` für jede Tabelle aus `ObjectView.Tables` und `LocStore.SignatureOf` und
  vergleicht sie später mit neu berechneten Werten (nur Dateistatus, kein Dekodieren).
- Session: Objekt-Tabs werden mit `ObjectId` und Verlauf gespeichert und beim Start lazy
  wiederhergestellt (Block erst beim Aktivieren).
- Der `TableStore` lebt einmal im Composition Root und wird bei Schema-Wechsel oder
  Spielordner-Wechsel verworfen.

#### Festlegungen aus Plan 2

- Die Identität eines Objekt-Tabs ist sein aktueller Eintrag (`ObjectRef`: Tabelle, Schlüsselwerte, Wurzel-Pack), nicht `EntryPath`. `EntryPath` hält die Id des Eintrags beim Anlegen des Tabs (`history[position]`) und wird nicht nachgeführt. Ein zweiter Tab desselben Objekts ist erlaubt (Umschalt-Klick); „Als Objekt anzeigen“ wählt dagegen einen vorhandenen Tab mit gleichem Eintrag aus.
- Der Verlauf ist auf 50 Einträge begrenzt. Zurück und Vor bauen über den Runner neu; es gibt keinen Ergebnis-Cache pro Verlaufseintrag (der warme Aufbau dauert 66 ms, das Fenster erscheint nicht). Eine Navigation setzt den Tab auf „nicht aufgebaut“: die alten Zeilen sind weg, Quell-Hinweise und die Veraltet-Markierung gelöscht. „Aktualisieren“ behält die Zeilen bis zum Ergebnis. Bei Abbruch oder Fehler bleiben die alten Zeilen mit einem Hinweis stehen.
- Die Signaturen (`TableStore.SignatureOf` je Tabelle aus `ObjectView.Tables`, dazu `LocStore.SignatureOf`) werden in derselben Runner-Operation direkt nach dem Aufbau erfasst. Die stille Prüfung vergleicht zuerst die Quellenliste der neu berechneten Ladereihenfolge (Art und Pfad), danach die Signaturen. Ein Projektwechsel markiert deshalb über die Quellenliste, ohne Sonderfall. Ein erfolgreicher Neuaufbau löscht die Markierung (Pack- und Schema-Markierung, nicht die Außen-Änderung).
- Ein Ergebnis wird nur übernommen, wenn der Tab noch denselben Eintrag zeigt. Der Store wird bei einem Wechsel von Schema oder Spielordner verworfen; jeder Objekt-Tab trägt dann die Schema-Markierung.
- Wurzel-Pack: Nur ein Pack, das im Katalog liegt und kein Vanilla-Pack ist, kommt als Wurzel-Pack in die Ladereihenfolge. Ein Pack außerhalb des Katalogs kommt nicht hinein.
- Session: Objekt-Tabs werden mit Verlauf und Position gespeichert (`SessionTab.Object`, die Schema-Version bleibt 1) und beim Start ohne Aufbau angelegt. Während des Wiederherstellens sind Lazy-Aufbauten unterdrückt, danach wird nur der aktive Tab gebaut. Die Dedupe-Regel der Session gilt nicht für Objekt-Tabs.
- Der Hover-Name kommt aus der `LocTable` des Aufbaus (`ObjectBuilder.DisplayName`), ohne Zugriff auf den Store; ohne Namen zeigt der Tooltip den Tabellennamen. „Ziel unbekannt“ gilt für jeden Warnungs-Wert außer der Warnzeile selbst.
- „Zur Zeile springen“ nutzt `RowProvenance.EntryPath` (Pack-Eintrag oder Projektdatei) und prüft, dass der danach gewählte Tab Pack und Eintrag der Quelle zeigt, bevor die Zeile gesucht wird. Zeigt er sie nicht (Öffnen abgelehnt oder fehlgeschlagen, etwa weil eine Operation läuft), setzt der Sprung keinen Hinweis, denn das Öffnen meldet sich selbst; „Zeile nicht gefunden“ gilt nur, wenn die geöffnete Tabelle den Schlüssel nicht enthält. Die Auswahl wartet im Tab (`RowsTabViewModel.PendingSelection`, zugleich `SelectedRows`), bis ein Raster den Tab zeigt: Das Raster entsteht erst nach dem Sprung, wenn das Tab-Steuerelement die Vorlage anwendet, und übernimmt die Auswahl beim Verbinden.
- Der Kontextmenü-Eintrag erscheint nur in Tabellen-Rastern, nicht bei Loc-Dateien. Gründe für die Sperre, in dieser Reihenfolge: keine Schlüsselspalte, keine Zeile markiert, kein Spielordner, kein Schema, eine Operation läuft.
- Kategorie-Datei Gebäude: 9 Regeln (Namen, Effekte, Einheiten `allows {unit}`, Ausbau zu und von, `requires technology {technology_key}`, `requires building {required}`, Rückbau, `cai_construction_system_synergies_tables` versteckt). Die Titel-Schlüssel sind zusätzlich Properties in `AppTexts`, damit der Locale-Test sie als benutzt sieht; ein neuer Titel-Schlüssel braucht daher eine Property.
- Lange Zeilen im Block brechen um, es gibt keinen horizontalen Scrollbalken. `Alt+Links` und `Alt+Rechts` wirken, solange der Fokus im Tab-Inhalt liegt.

## 6. Verworfene Alternativen

| Entscheidung | Grund | Verworfen |
|---|---|---|
| Dekodieren bei Bedarf mit Speicher-Cache (A) | Objekt kalt unter 0,4 s, warm nahe 0; kein Zustand auf Platte, keine Invalidierungsfehler | Globaler Schlüsselindex auf Platte (B): Aufbau 1,9 s plus Schreiben, Format mit Migration, Invalidierung bei jedem Pack-Wechsel; nicht nötig |
| Keine Entpack-Option für Vanilla | Lesen und Dekomprimieren aller Tabellen kostet 0,2 s; Text auf Platte wäre langsamer zu parsen und größer. „Überschreibbar machen“ deckt der Import ab | Vanilla als TSV unter `%LocalAppData%` entpacken (Nutzerfrage 2026-10-09) |
| Aktive Mods aus `used_mods.txt` | Ehrliche Quelle ohne Launcher-Anbindung, vom Spiel selbst geschrieben | Alle Packs im Datenordner und Workshop (396 Workshop-Packs, meist inaktiv); eigene Auswahl in den Einstellungen (Backlog) |
| Eine Ebene plus Junction-Durchgriff | Block bleibt lesbar (Gebäude 15–24 Zeilen), Links reichen zum Weitergehen | Transitiv bis Tiefe N (Fraktion explodiert), nur eine Ebene ohne Durchgriff (Junction-Zeilen ohne Information) |
| Darstellung als Block mit Spans | Deterministisch, testbar, kein Parser | Paradox-ähnliche Syntax mit Klammern (nur sinnvoll, wenn der Block Eingabe wird; 4.2 entscheidet) |
| Loc per Schlüsselkonstruktion | Konvention `<tabelle>_<spalte>_<schlüssel>` gemessen; Dictionary-Abfrage | Suffix-Suche über 266.000 Schlüssel pro Objekt |
| Junction-Regel aus dem Schema | Trifft bei Gebäuden alle Junctions ohne Datenpflege; Kategorie-Datei kann übersteuern | Handgepflegte Junction-Liste |

## 7. Fehlerfälle

| Fall | Verhalten |
|---|---|
| Wurzelzeile nicht mehr vorhanden (gelöscht, Pack weg) | Kopf „Objekt nicht gefunden“, Verlauf bleibt, Zurück funktioniert |
| Pack der Ladereihenfolge gesperrt oder unlesbar | Tabellen dieses Packs fehlen; Warnung nennt das Pack; Block wird gebaut |
| Tabelle ohne Schema oder Version unbekannt | Abschnitt fehlt; Warnung nennt Tabelle und Quelle |
| Projekt-TSV unlesbar | wie Pack; Warnung nennt die Datei |
| Mod-Eintrag mit älterem Layout | Zeile über Feldnamen abgebildet, fehlende Felder leer, Markierung „älteres Layout“ |
| Schlüssel doppelt in derselben Quelle | beide Zeilen, Markierung „doppelt“ |
| Referenz auf Tabelle ohne Zeilen und ohne Loc | Wert ohne Link, Tooltip „Ziel unbekannt“ |
| Referenz auf Text-Tabelle (nur Loc) | Loc-Text dahinter, kein Link |
| `used_mods.txt` fehlt oder unlesbar | Ladereihenfolge ohne Mods, Hinweis im Kopf |
| Pack aus `used_mods.txt` fehlt | ausgelassen, Kopf „2 von 3 Mods geladen“ mit Tooltip |
| Kategorie-Datei unlesbar | Standard-Rendering, Warnung im Kopf |
| Cache-Obergrenze erreicht | Verdrängung, nächster Aufbau dekodiert neu; nie ein Fehler |
| Abbruch im Fortschrittsfenster | Tab „abgebrochen“ mit „Aktualisieren“, kein Teilblock |
| Projekttabelle mit ungespeicherter Änderung | gespeicherter Stand, Hinweis im Kopf |
| Spielordner nicht gefunden | Kontextmenü-Eintrag deaktiviert mit Tooltip |

## 8. Tests

- `Objects` test-first mit Mini-Schema und Fake-Quellen (Temp-Ordner, handgeschriebene Packs über
  `PackWriter`, Projekt-TSV über `TableTsv`):
  - `ReferenceGraph`: eingehende Referenzen, Junction-Regel (2 Referenzen und 4 Felder ja, 3
    Referenzen nein, 5 Felder nein), Composite-Schlüssel, Tabelle ohne Schlüssel.
  - `LoadOrder`: Reihenfolge Vanilla, `used_mods.txt`, Projekt; fehlendes Pack; fehlende Datei;
    Wurzel-Pack außerhalb der Ladereihenfolge; Loc-Pack-Zuordnung mit Fallback.
  - `TableStore`: Überschreibung nach Reihenfolge mit `Overridden`; älteres Layout per Feldname;
    Tabelle ohne Schlüssel wird angehängt; Fingerprint-Änderung invalidiert; Obergrenze verdrängt
    die älteste Tabelle; `SourceProblem` statt Exception.
  - `ObjectBuilder`: Abschnitte nur für Felder auf Schlüsselfelder; Junction-Durchgriff mit
    `OtherEnd`; Loc-Konstruktion für Wurzel und Referenzziele; `LocOnly`; `NotFound`; `Duplicate`;
    Warnungen; Abbruch ohne Teilergebnis; Fortschritt pro Tabelle.
  - `ObjectText`: Zeilen deterministisch (Golden-Test gegen Erwartungsliste); Klappgrenze und
    Vorschau; `list` und `sentence`; `hidden`; Fallback ohne Datei; unlesbare Datei.
  - `CategoryDefinition`-Loader: gültig, fehlende Pflichtfelder, falscher Typ, `schemaVersion`.
- Echtdaten (übersprungen ohne `TWMT_WH3_DIR`): `wh_main_emp_barracks_2` hat Abschnitte
  `building_effects_junction`, `building_units_allowed`, `building_upgrades_junction` und einen
  Namen aus Loc; `wh_main_emp_empire` baut in unter 2 s; Loc-Name eines Traits ist auffindbar (prüft
  Annahme 4).
- App: Kontextmenü nur bei Schlüsselfeld; Tab öffnet, Link-Navigation mit Verlauf; veraltet nach
  Speichern; Session-Wiederherstellung; Locale-Test für neue Keys; Vorlagen instanziieren.
- Manuelle Checkliste beim Plan (Farben, Hover, Shift-Klick, Zuklappen, großes Objekt).

## 9. Pläne

1. **Plan 1: Library `Objects`.** Referenzgraph, Ladereihenfolge, Tabellen-Store mit Cache,
   Loc-Store, Objektmodell, Rendering, Kategorie-Datei-Loader, Profil-Erweiterung. Test-first, keine
   App-Änderung. Echtdaten-Tests.
2. **Plan 2: App.** Kontextmenü, Objekt-Tab, Links und Verlauf, Hover, Zuklappen, veraltet,
   Session, Texte in `en` und `de`, Theme-Tokens für Herkunft, Gebäude-Kategorie-Datei, manuelle
   Checkliste.

## 10. Annahmen, die der erste echte Lauf prüft

| Nr. | Annahme | Prüfung |
|---|---|---|
| 1 | Klappgrenze 20 und Vorschau 5 machen eine Fraktion lesbar. | Abnahme Plan 2 mit `wh_main_emp_empire`. |
| 2 | 200 MB Cache reichen, um zwischen Gebäude, Einheit und Fraktion zu springen, ohne neu zu dekodieren. | Abnahme Plan 2: drei Objektarten nacheinander, zweiter Durchgang unter 0,2 s. Hinweis aus Plan 1: Das Zusammenführen aller Tabellen braucht 339 MB; die Grenze von 200 MB erzwingt daher Verdrängung, wenn der Nutzer durch viele Objektarten läuft. Status offen für Plan 2. |
| 3 | `used_mods.txt` bildet die Launcher-Auswahl des Nutzers ab. | Nutzer vergleicht mit dem Launcher. |
| 4 | Der Name eines Traits liegt unter `character_trait_levels_onscreen_name_<level>`. | **Bestätigt** (Echtdaten-Test 2026-10-09): Der Name liegt unter `character_trait_levels_onscreen_name_<trait>_<stufe>`, erreichbar über die Abschnittszeilen und `TryDisplayName`. |
| 5 | Die Junction-Regel passt auch bei Einheiten und Traits. | Abnahme Plan 2; sonst Kategorie-Datei. |
| 6 | Zusammenführung über Feldnamen genügt für die 104 Mod-Einträge mit älterem Layout. | **Bestätigt** (Echtdaten-Test 2026-10-09 über alle Tabellen der Ladereihenfolge): 1742 Tabellen, 964.436 Zeilen, 94.453 Zeilen mit älterem Layout, 1 Eintrag `VersionNotInSchema`, keine Exception, 4,4 s, 339 MB ohne Obergrenze. |
| 7 | `FlowDocument` mit einigen hundert Zeilen und Hyperlinks bleibt flüssig. | Abnahme Plan 2 mit der Fraktion. |

Die Abnahme von Plan 2 läuft über die manuelle Checkliste in `docs/plans/2026-10-09-teil-4-1-plan-2-app.md`.

## 11. Entscheidungen des Nutzers (2026-10-09)

- Teil 4 wird zerlegt: 4.1 nur Lesen, 4.2 Bearbeiten, 4.3 Katalog und Autovervollständigung.
- Einstieg über das Kontextmenü einer Zeile. Kategorie-Browser, voreingestellte Inspektoren und
  Assistenten („Neues Gebäude“) sind gewünscht, kommen aber später (Backlog).
- Tiefe: eine Ebene plus Durchgriff durch Verbindungstabellen; Links zum Weitergehen. Eine
  Referenz-Baumansicht nur als Debug-Werkzeug, falls nötig (Backlog).
- Datenbasis wie das Spiel: Vanilla plus aktive Mods plus Projekt in Ladereihenfolge, Vanilla und
  Mod farblich unterschieden.
- Dekodieren bei Bedarf (C: erst A, B nur wenn gemessen nötig); Spike zeigt: A reicht.
- Keine Entpack-Option.
- Form: Block mit Feld = Wert, Abschnitte pro Tabelle, Voraussetzungen als Code-Zeilen
  (Satzmuster), Hover zeigt den Spieltext des Ziels.
