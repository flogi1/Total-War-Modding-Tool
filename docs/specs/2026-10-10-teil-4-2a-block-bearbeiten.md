# Spec: Teil 4.2a (Objekt im Block bearbeiten, Objekt kopieren, Assistenten)

Stand: 2026-10-10. Baut auf:

- `docs/specs/2026-10-09-teil-4-1-objekt-ansicht.md` (Teil 4.1, umgesetzt und gemergt: Objekt-Ansicht,
  nur Lesen)
- `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md` (Teil 2: Projekte, Schreibgrenze,
  TSV-Dialekt, Dateioperationen, Undo)
- `docs/specs/2026-10-02-fundament.md` (Fundament)

## 1. Zweck und Einordnung

Teil 4.1 zeigt ein Objekt (Gebäude, Trait, Einheit) als Block: Wurzelzeile, Spieltexte, ein
Abschnitt pro Tabelle, die auf das Objekt zeigt, mit Herkunft pro Zeile. Der Block ist bisher eine
berechnete, unveränderliche Ansicht.

Teil 4.2a macht den Block zur Eingabe. Der Nutzer ändert Werte, fügt Zeilen hinzu, löscht Zeilen
und ändert Spieltexte direkt im Text, wie in einem Texteditor. „Übernehmen“ parst den Block, bildet
die Differenz zum aktuellen Stand und schreibt sie in das Projekt: als Überschreibzeilen für
Vanilla- und Mod-Zeilen, als Änderung für Projektzeilen, als Dateikopie für das Löschen fremder
Zeilen. Alles, was ein Übernehmen schreibt, ist ein Undo-Schritt. Dazu kommen „Objekt kopieren“
und darauf aufbauend die Assistenten „Neues Gebäude“ und „Neuer Trait“.

Der eigentliche Wunsch des Nutzers (2026-10-10) ist eine Übersicht, was man an einem Objekt alles
ändern und was man ihm anhängen könnte, und das Finden nach Wirkung statt nach Schlüssel. Das
liefert Teil 4.2b (Katalog, Suche, Vorschläge beim Tippen, Tooltips). 4.2a liefert die Mechanik,
auf der 4.2b aufsetzt: Ohne Rückschreiben nützt kein Katalog.

### 1.1 Zerlegung von Teil 4.2

Am 2026-10-10 hat der Nutzer die Zerlegung beschlossen:

- **4.2a Block bearbeiten** (diese Spec): Block als Text, Rückparsen, Überschreibzeilen ins
  Projekt, Löschen fremder Zeilen per Dateikopie, Verbund-Undo über Tabellen und Loc, „Objekt
  kopieren“, Assistenten „Neues Gebäude“ und „Neuer Trait“ über Vorlagen.
- **4.2b Katalog, Suche, Vorschläge**: Suche im Arbeitsbereich über Tabellen und Spieltexte der
  Ladereihenfolge, Effekte-Katalog als Seitenleiste mit „Einfügen“ am Cursor, Vorschläge beim
  Tippen mit Spieltext, Tooltips mit Erklärungen aus der Kategorie-Datei. Eigene Spec, baut auf
  4.2a.
- **4.3** bleibt der Katalog als eigener Bereich ohne Objektbezug (Backlog).

Die Maske als zweite Ansicht auf das Objektmodell entfällt (Nutzer 2026-10-10: tippen im Block
reicht).

## 2. Ziele und Non-Goals

### 2.1 Ziele

1. **Tippen im Block.** Der Objekt-Tab ist ein Texteditor mit Färbung. Herkunft, Spieltexte hinter
   Werten, Zeilenzahlen und Warnungen sind Anhängsel, die der Nutzer sieht, aber nicht editiert.
   Die Übersicht des Blocks aus 4.1 bleibt: ein Feld pro Zeile, keine Wertekolonnen nebeneinander.
2. **Übernehmen als ein Schritt.** Strg+Enter oder der Knopf „Übernehmen“ parst den ganzen Block,
   vergleicht mit dem Stand des letzten Aufbaus und schreibt die Differenz sofort auf die Platte,
   alle betroffenen Dateien zusammen (temp-Dateien, Tausch). Der Schritt steht in der globalen
   History; Strg+Z stellt die vorherigen Dateiinhalte wieder her. Keine Rückfrage, weil umkehrbar.
   Eine Ergebniszeile sagt, was wohin geschrieben wurde.
3. **Vanilla und Mods bleiben unberührt.** Eine geänderte Vanilla- oder Mod-Zeile wird als
   Überschreibzeile mit gleichem Schlüssel in das Projekt geschrieben. Zieldatei: die Projektdatei,
   die diese Zeile schon enthält; sonst die Projektkopie der Quelldatei, wenn es sie gibt; sonst die
   einzige Projektdatei der Tabelle; sonst eine neu angelegte; bei mehreren Projektdateien fragt das
   Tool (Nutzer 2026-10-10).
4. **Löschen überall.** Eine Projektzeile wird gelöscht. Eine Vanilla- oder Mod-Zeile wird gelöscht,
   indem das Tool die Quelldatei unter demselben Pack-Pfad in das Projekt kopiert und die Zeile dort
   entfernt. Das fragt einmal pro Datei mit Zeilenzahl und Warnung (Nutzer 2026-10-10).
5. **Spieltexte im Block.** Loc-Zeilen sind editierbar; eine Änderung landet in einer Loc-Datei des
   Projekts mit demselben Schlüssel.
6. **Objekt kopieren und Assistenten.** „Kopieren als“ legt ein Objekt unter neuem Schlüssel an: die
   Wurzel, alle Abschnittszeilen und alle Spieltexte der Vorlage, umbenannt, als Projektzeilen.
   „Neues Gebäude“ fragt Volk, Vorlage, Schlüssel und Kette (neu oder bestehende plus Stufe) und
   kopiert; „Neuer Trait“ fragt Vorlage und Schlüssel. Beides sind dünne Hüllen um „Kopieren als“,
   ohne Spielregeln im Code über das Nötigste hinaus.
7. **Fehler sichtbar, nie still.** Parse-Fehler und Typfehler sperren Übernehmen und markieren die
   Zeile. Unbekannte Referenzen sind Warnungen, kein Fehler (der Nutzer legt das Ziel vielleicht als
   nächstes an).

### 2.2 Non-Goals

- Katalog, Suche im Arbeitsbereich, Vorschläge beim Tippen, Tooltips mit Erklärungen (4.2b).
- Maske als zweite Ansicht (verworfen, 1.1).
- Umbenennen des Wurzelschlüssels im Block. Der Weg ist „Kopieren als“ und danach die alte Zeile
  löschen (Backlog: Umbenennen mit Nachführen aller Referenzen).
- Neutralisieren statt Löschen als Automatik (Regel pro Tabelle, Abschnitt 6).
- Bearbeiten in Raster-Tabs ändert sich nicht. Die Dokumente aus Teil 2 bleiben, wie sie sind.
- Ein Undo-Schritt über das Schließen der App hinaus (History lebt bis zum Schließen, wie Teil 2).
- Zusammenführen bei gleichzeitigem Bearbeiten derselben Datei im Raster und im Block: Übernehmen
  sperrt, solange ein Raster-Tab ungespeicherte Änderungen an einer betroffenen Datei hat.
- Prüfung von Referenzen gegen das Spiel über die Ladereihenfolge hinaus; doppelte Schlüssel in
  anderen Mods.
- Weitere Assistenten (Einheit, Fähigkeit, Fraktion): nach dem Muster „Kopieren als“ später
  (Backlog).

### 2.3 Erfolgskriterien

- Der Nutzer öffnet `wh_main_emp_barracks_2` als Objekt, ändert `create_time` von 3 auf 2, drückt
  Strg+Enter. Danach: eine neue Datei `db\building_levels_tables\<projekt>.tsv` mit einer Zeile (oder
  eine Zeile mehr in der vorhandenen), der Block zeigt die Zeile mit Herkunft Projekt und die
  überschriebene Herkunft Vanilla, die Ergebniszeile nennt die Datei. Strg+Z entfernt die Zeile
  wieder, der Block zeigt wieder Vanilla.
- Der Nutzer fügt im Abschnitt „Effekte“ einen Block aus drei Zeilen ein (`effect = …`,
  `effect_scope = …`, `value = …`), übernimmt, und `building_effects_junction_tables` im Projekt hat
  eine neue Zeile mit `building = wh_main_emp_barracks_2`.
- Der Nutzer löscht die Zeile `allows wh_main_emp_inf_swordsmen`, bestätigt die Dateikopie, und das
  Projekt enthält `db\building_units_allowed_tables\data__.tsv` ohne diese Zeile; der Block zeigt die
  übrigen Einheiten mit Herkunft Projekt.
- Der Nutzer ändert den Namen des Gebäudes in der Loc-Zeile, übernimmt, und eine Loc-Datei des
  Projekts enthält `building_culture_variants_name_…` mit dem neuen Text; der Block zeigt den Text
  mit Herkunft Projekt.
- „Neues Gebäude“: Volk Imperium, Vorlage `wh_main_emp_barracks_3`, Schlüssel
  `flo_emp_barracks_4`, bestehende Kette als Stufe 4. Danach existiert das Gebäude mit
  Kultur-Variante, Effekten, Einheiten, `upgrades from wh_main_emp_barracks_3` und Spieltexten als
  Projektzeilen, und der Block öffnet sich darauf. Ein gebautes Pack zeigt das Gebäude im Spiel
  (prüft Annahme 1).
- Ein Objekt-Tab mit nicht übernommenem Text trägt eine Markierung; Schließen, Zurück, Vor und
  Aktualisieren fragen „Übernehmen, Verwerfen, Abbrechen“.

## 3. Gemessener Ist-Zustand (Spike vom 2026-10-10)

Gemessen mit `schema_wh3.ron`, Vanilla und den zwei aktiven Mods des Nutzers (264 Quellen),
Wegwerf-Konsole über die Bibliotheken `Objects`, `Schema`, `Workspace`, `GameProfiles`.

### 3.1 Feldbeschreibungen im Schema

Das RPFM-Schema trägt pro Feld eine `description`. Gefüllt sind 2920 von 16.010 Feldern über alle
Versionen (18 %). In der neuesten Version der relevanten Tabellen:

| Tabelle | Felder | beschrieben |
|---|---|---|
| `building_levels_tables` | 27 | 0 |
| `building_chains_tables` | 14 | 1 |
| `building_culture_variants_tables` | 11 | 0 |
| `building_effects_junction_tables` | 6 | 0 |
| `building_units_allowed_tables` | 7 | 1 |
| `effects_tables` | 7 | 3 |
| `effect_bundles_tables` | 11 | 3 |
| `character_traits_tables` | 8 | 1 |
| `character_trait_levels_tables` | 8 | 0 |

Dazu hat das Schema einen `patches`-Abschnitt (155 Tabellen) mit `lookup_hardcoded`-Erklärungen für
Aufzählungswerte, vor allem bei den `cai_*`-Tabellen. Folgerung für 4.2b: Erklärungen kommen aus der
Kategorie-Datei, nicht aus dem Schema; `SchemaLoader` liest `description` heute nicht und muss es
für 4.2a nicht.

### 3.2 Effekte (für 4.2b, hier nur festgehalten)

`effects_tables`: 17.734 Zeilen, alle Vanilla (die aktiven Mods bringen keine Effekte mit), Felder
`effect` (Schlüssel), `icon`, `priority`, `icon_negative`, `category` (nur `campaign`, `battle`,
`both`), `is_positive_value_good`. 16.436 Effekte (93 %) haben einen Spieltext unter
`effects_description_<effect>`, zum Beispiel `wh2_dlc09_effect_economy_gdp_mod_culture_tmb` =
„Einkommen aus Einkommensgebäuden: %+n%“. 644 verschiedene Icons. `building_effects_junction_tables`
hat 23.681 Zeilen mit `effect_scope` aus `campaign_effect_scopes` und `value`, `value_damaged`,
`value_ruined`. Laden der Effekte-Tabelle kalt 1,0 s, Loc (212.535 Schlüssel) 0,7 s.

### 3.3 Vorlage kopieren

`wh_main_emp_barracks_2` hängt im Schema an 65 Tabellen, tatsächlich an 8 mit 15 Zeilen:
`building_culture_variants` 1, `building_effects_junction` 1, `building_set_to_building_junctions` 1,
`building_units_allowed` 4, `building_upgrades_junction` 1 von und 1 nach, `technologies` 3,
`technology_required_building_levels_junctions` 3. Ausgehende Referenzen der Wurzel: `chain`
(`wh_main_EMPIRE_barracks`) und `building_instance_key`; die übrigen fünf sind leer.

Die Kette `wh_main_EMPIRE_barracks` hängt an 6 Tabellen: `building_chain_availability_sets` 2,
`building_chain_set_items` 1, `building_levels` 3 (die Stufen), `building_set_to_building_junctions`
1, `cai_construction_system_building_values` 1, `cai_construction_system_synergies` 16.

Die Kultur-Variante der Kaserne trägt `culture = wh_main_emp_empire`; `cultures_tables` hat 28
Kulturen, `cultures_subcultures_tables` 33 Subkulturen. Das Volk für den Assistenten ist die
Subkultur, denn Gebäude-Kultur-Varianten und Ketten-Verfügbarkeit arbeiten mit ihr (Festlegung,
prüft Annahme 3).

Traits: 790 in `character_traits_tables`; Struktur Trait, Stufen (`character_trait_levels`, 4 beim
Beispiel), Effekte pro Stufe (`trait_level_effects`). Der Name liegt unter
`character_trait_levels_onscreen_name_<trait>_<stufe>` (4.1, Annahme 4 bestätigt).

Folgerung: „Kopieren als“ findet alle Zeilen über den Referenzgraphen (eingehende Referenzen eine
Ebene) und die Loc-Schlüssel über das Objektmodell aus 4.1. Keine Tabellenliste im Code.

### 3.4 Bearbeiten heute (Teil 2)

Projekt-Tabellen und Loc-Dateien liegen als TSV-Text unter `pack\`. Ein Undo-Schritt ist genau ein
`DocumentEdit` (eine Zelle, Zeilen einfügen oder löschen in einem Dokument) oder ein `FileStep`
(eine Dateioperation). Es gibt keinen Verbund-Schritt. Speichern, Geändert-Markierung und
Außen-Änderung hängen am Tab (`DocumentsController`). Atomares Schreiben:
`ProjectFileWriter.WriteText` (temp-Datei neben dem Ziel, `AtomicFile.Replace`, Pfadprüfung über
`PackFolderPaths.ResolveForWrite`). Neue Dateien: `NewFiles.Table(schema, table, name)` liefert eine
leere TSV in der neuesten Schemaversion, `ProjectFileOps.Create` schreibt sie ohne Überschreiben.
Der Objektaufbau (`SourceReader`) liest Projektzeilen von der Platte, nie aus offenen Dokumenten.

Folgerung: Übernehmen arbeitet auf Dateien, nicht auf Dokumenten (Abschnitt 5.5), und geht an den
Raster-Dokumenten vorbei. Deren Außen-Änderungs-Mechanik fängt das auf.

## 4. Schreibgrenze

Teil 4.2a schreibt in den Projektordner des aktiven Projekts unter `pack\`, innerhalb der Grenze aus
Teil 2 Abschnitt 4, Punkt 1. Neu gegenüber Teil 2:

- Übernehmen legt Dateien an (`db\<tabelle>\<name>.tsv`, `<name>.loc.tsv`), ändert sie und legt
  Ordner unter `pack\` an. Es überschreibt nie eine Datei, die es nicht selbst zuvor gelesen und per
  Fingerprint geprüft hat (Abschnitt 5.5).
- Undo eines Übernehmens schreibt die vorherigen Inhalte zurück; eine vom Übernehmen angelegte Datei
  wandert beim Undo in den Papierkorb des Tools (Teil 2 Abschnitt 4, Punkt 3), wie bei „Neue
  Tabelle“.
- Die Dateikopie beim Löschen fremder Zeilen ist eine neue Projektdatei unter dem Pack-Pfad der
  Quelle. Vanilla-, Workshop- und fremde Packs werden nie geschrieben.

Nichts sonst. Kein Schreiben außerhalb des aktiven Projekts, nichts im Spielordner, nichts unter
`%LocalAppData%` außer Papierkorb und Log.

## 5. Entwurf

### 5.1 Module

| Modul | Aufgabe | Neu oder geändert |
|---|---|---|
| `Objects` | Blocktext-Grammatik: Rendern des editierbaren Textes mit Anhängseln (`ObjectText`), Parser `ObjectTextParser`, Differenz `ObjectDiff`, Umbenennen für „Kopieren als“ (`ObjectCopy`) | geändert und neu |
| `Editing` | Schreiben der Differenz in Projektdateien: `ProjectWritePlan` (rein, berechnet Zieldateien und neue Dateiinhalte), `ProjectWriter` (Side Effects: liest, prüft Fingerprints, schreibt atomar) und `FileContentsStep` (`IUndoableAction`, hält pro Datei den vorherigen und den neuen Inhalt; liegt hier, weil `UndoRedo` keine Referenzen hat und der Schritt Dateien und Papierkorb braucht) | neu |
| `UndoRedo` | unverändert | — |
| `Projects` | unverändert; `NewFiles`, `ProjectLayout`, `ProjectFileWriter`, `ProjectFileOps` werden genutzt | — |
| `App` | Objekt-Tab mit AvalonEdit, Übernehmen, Ergebniszeile, Rückfragen, „Kopieren als“, Assistenten, Texte, Theme-Tokens für die Färbung | geändert |

`Objects` referenziert weiterhin `Schema`, `PackFiles`, `Tables`, `GameProfiles`, `Workspace`; die
Typprüfung in `ObjectDiff` nutzt die Formatregeln aus `Tables` (`CellText`), nicht `RowsDocument`.
`Editing` bekommt eine Referenz auf `Objects` (für `ObjectDiff`) und `Projects` (für Pfade und
Schreiben). Die App bleibt einziger Composition Root. AvalonEdit ist ein neues NuGet-Paket
(`AvalonEdit`, MIT; Nutzer 2026-10-10), nur in der App referenziert, Version in
`Directory.Packages.props`.

### 5.2 Blocktext: Text und Anhängsel

Der Objekt-Tab zeigt einen Text, der „Blocktext“. Er besteht aus dem **Text** (editierbar, wird
geparst) und **Anhängseln** (nicht editierbar, nicht Teil des Textes, vom Editor als virtuelle
Inline-Elemente hinter der Zeile gezeichnet). `ObjectText.Render` liefert ab 4.2a pro Zeile beides:
die Spans des Textes und die Spans der Anhängsel (`TextLine.Spans` und `TextLine.Trailing`).
Anhängsel sind: Herkunft und überschriebene Herkünfte, `duplicate`, `older layout`, Spieltexte
hinter Referenzwerten, Zeilenzahl und Herkünfte im Abschnittskopf, der übersetzte Abschnittstitel,
die `Collapsed`-Zeile, Warnzeilen des Aufbaus.

Der Text folgt dieser Grammatik (Einrückung zwei Leerzeichen pro Stufe; der Parser akzeptiert jede
Zahl Leerzeichen und Tabs, zählt Stufen aber nach zwei Leerzeichen):

```
building_levels wh_main_emp_barracks_2            ← Kopf: Tabelle ohne _tables, Schlüssel (nur Lesen)
  level_name = wh_main_emp_barracks_2             ← Wurzelfelder, alle, auch leere
  chain = wh_main_EMPIRE_barracks
  create_time = 3
  onscreen_name = "Kaserne"                       ← Loc-Zeile der Wurzel: spalte = "text" (nur Trait, Einheit; Gebäude haben keine)

building_culture_variants                         ← Abschnitt: Tabelle ohne _tables, optional .feld
  culture = wh_main_emp_empire  subculture =  faction =   ← Zeile als Block: Schlüsselfelder in der Kopfzeile
    icon = empire_barracks
    name = "Kaserne"                              ← Loc-Zeile der Abschnittszeile: spalte = "text"
    short_description = "Hier werden Truppen ausgebildet."

building_effects_junction
  effect = wh_main_effect_economy_gdp_mod_all
    effect_scope = building_to_province_own
    value = 10

building_units_allowed
  allows wh_main_emp_inf_swordsmen                ← Satzzeile (Modus sentence der Kategorie-Datei)
  allows wh_main_emp_inf_spearmen
    xp = 2                                        ← optionales Feld unter einer Satzzeile

building_upgrades_junction.from
  upgrades to wh_main_emp_barracks_3

technology_required_building_levels_junctions     ← Junction ohne Regel: nur das andere Ende
  wh_main_tech_emp_barracks_2
```

Regeln:

1. **Kopfzeile** `tabelle schlüssel` (zusammengesetzte Schlüssel mit ` | `). Der Editor macht sie
   nur lesbar (Abschnitt 5.7). Der Parser prüft, dass sie mit der Wurzel des Aufbaus übereinstimmt.
2. **Feldzeile** `name = wert` auf Stufe 1 (Wurzel) oder Stufe 2 (Feld einer Abschnittszeile).
   `wert` reicht vom ersten Zeichen nach `= ` bis zum Zeilenende, Leerzeichen am Ende werden
   abgeschnitten (kein Feldwert im Spiel endet auf Leerzeichen; prüft Annahme 4). Ein leerer Wert ist
   `name = ` oder `name =`. Werte enthalten nie Tab oder Zeilenumbruch (Grenze des TSV-Dialekts aus
   Teil 2; ein wörtliches `\t` oder `\n` wird mit Meldung abgelehnt).
3. **Loc-Zeile** `spalte = "text"` unter ihrem Besitzer: auf Stufe 1 für Spieltexte der Wurzel
   (`LocEntry.Owner` gleich Wurzel), auf Stufe 2 unter der Abschnittszeile, der der Text gehört
   (`Owner` gleich `RowId` der Zeile). Der Loc-Schlüssel ist `<tabelle>_<spalte>_<schlüssel des
   Besitzers>` wie in 4.1. Der Text steht in Anführungszeichen; ein Anführungszeichen im Text ist
   `\"`, ein Zeilenumbruch `\n` (im Text, nicht im Block; der Block zeigt Spieltexte einzeilig, wie
   4.1). Der Parser erkennt die Loc-Zeile daran, dass der Name kein Feld der Tabelle ist und der
   Wert in Anführungszeichen steht; ein unbekannter Name ohne Anführungszeichen ist ein Fehler. So
   kann der Nutzer auch einen Spieltext anlegen, den es noch nicht gibt (etwa `short_description`).
   Spieltexte von Referenzzielen (der Name der Kette hinter `chain = …`) bleiben Anhängsel und sind
   nicht editierbar; sie gehören dem Ziel. Der Loc-Block aus 4.1 (`tabelle.spalte` auf Stufe 1
   für alle Texte) entfällt damit; jeder Text steht bei seinem Besitzer. Ein Gebäude hat in WH3
   keine eigenen Spieltexte, seine Namen hängen an der Kultur-Variante (3.3).
4. **Abschnittskopf** `tabelle` oder `tabelle.feld` auf Stufe 0 nach einer Leerzeile. `.feld` steht,
   wenn dieselbe Tabelle mit zwei Feldern auf die Wurzel zeigt (`building_upgrades_junction.from`
   und `.to`); sonst ist es erlaubt, aber nicht nötig. Der übersetzte Titel, die Zeilenzahl und die
   Herkünfte sind Anhängsel. Ein Abschnitt, den der Aufbau nicht kennt, darf im Text stehen, wenn
   die Tabelle im Schema existiert und per Referenz auf die Wurzel zeigt: so entsteht der erste
   Effekt eines Gebäudes, das noch keinen hat. Sonst Fehler „unbekannter Abschnitt“.
5. **Listenzeile** (Modus `list` und Standard): Kopfzeile auf Stufe 1 mit den Schlüsselfeldern als
   `name = wert`, mehrere durch zwei Leerzeichen getrennt, in Schemareihenfolge; darunter Feldzeilen
   auf Stufe 2. Das Feld, das auf die Wurzel zeigt, fehlt und wird beim Schreiben gesetzt. Hat die
   Tabelle keine Schlüsselfelder außer diesem, ist die Kopfzeile das erste Feld in Schemareihenfolge
   (4.1 zeigte das erste nicht leere; ab 4.2a das erste, damit der Text eindeutig bleibt).
6. **Satzzeile** (Modus `sentence`): das Muster der Kategorie-Datei mit eingesetzten Werten, zum
   Beispiel `allows wh_main_emp_inf_swordsmen`. Der Parser nutzt dasselbe Muster rückwärts:
   Literale müssen wörtlich vorkommen, Platzhalter nehmen den Text dazwischen. Zwei Platzhalter ohne
   Literal dazwischen sind in der Kategorie-Datei ungültig (der Loader lehnt das ab). Darunter
   dürfen Feldzeilen auf Stufe 2 stehen für Felder, die das Muster nicht nennt.
7. **Junction-Zeile** (Junction ohne Regel): nur der Wert des anderen Endes auf Stufe 1; darunter
   dürfen Feldzeilen für die übrigen Felder der Junction stehen.
8. **Felder, die im Text fehlen**: Bei einer Zeile, die es schon gibt (gleicher Schlüssel), behält
   das Feld seinen Wert. Bei einer neuen Zeile bekommt es den Standardwert des Typs (leer, 0, false).
   Das gilt für die Felder unter Satz- und Junction-Zeilen genauso. Wurzelfelder stehen immer alle
   im Text; fehlt eines, ist das eine Warnung „Feld fehlt, Wert bleibt“ und kein Fehler.
9. **Leerzeilen** trennen Blöcke und Abschnitte und sind sonst bedeutungslos. Zeilen, die nur aus
   Leerzeichen bestehen, sind Leerzeilen.
10. **Kommentare** gibt es nicht. Alles, was keine der Formen oben hat, ist ein Fehler mit
    Zeilennummer und Grund.
11. Groß- und Kleinschreibung: Tabellen- und Feldnamen wie im Schema, Vergleich ohne Rücksicht auf
    Schreibweise (wie `MergedTable.FieldIndex`); Werte wörtlich.

Der Text, den `Render` liefert, muss `Parse` ohne Fehler und ohne Differenz passieren
(Round-Trip-Invariante, Test in Abschnitt 8). Die Zeilen der `Collapsed`-Darstellung: Im Editor ist
ein Abschnitt entweder ganz da oder zugeklappt. Zugeklappt heißt ab 4.2a **gefaltet** (AvalonEdit
Folding): Der Text enthält alle Zeilen, der Editor zeigt nur die Vorschau und ein `+N`. Damit sind
`Collapsed`-Zeilen kein Teil des Textes mehr; `RenderOptions.ExpandedSections` steuert den Faltzustand.

### 5.3 Parser und Modell

`ObjectTextParser.Parse(string text, ObjectView baseline, CategoryDefinition? category, ReferenceGraph graph)`
liefert `ParsedObject`:

- `Root`: Liste `(FieldName, Text)` der Wurzelfelder im Text und `Texts`: Liste `(Column, Text)`
  der Loc-Zeilen der Wurzel.
- `Sections`: pro Abschnitt `(SourceTable, FieldName)` und Zeilen
  `ParsedRow(Fields: Liste (FieldName, Text), Texts: Liste (Column, Text), LineNumber)`.
- `Diagnostics`: Liste `(LineNumber, Severity, Message)`; Fehler sperren Übernehmen, Warnungen nicht.

Der Parser ist tolerant: Er sammelt alle Fehler statt beim ersten abzubrechen. Fehler: unbekannte
Zeilenform, unbekanntes Feld in einer Tabelle, unbekannter Abschnitt, Feldzeile ohne vorherige
Zeile, Kopfzeile verändert, Satzzeile passt nicht zum Muster, zweimal dasselbe Feld in einem Block,
`\t` oder `\n` im Wert. Warnungen: Wurzelfeld fehlt (Wert bleibt), Loc-Zeile mit leerem Text.

`ObjectDiff.Compute(ObjectView baseline, ParsedObject parsed, GameSchema schema)` liefert
`ObjectChanges`:

- `RowChanges`: pro Zeile `Set` (geänderte Felder, Quelle und `EntryPath` der alten Zeile),
  `Insert` (alle Felder, Tabelle, Feldname auf die Wurzel) oder `Delete` (Quelle, `EntryPath`,
  Schlüssel). Identität einer Zeile ist ihr Schlüssel (Schlüsselfelder der Tabelle, das Feld auf die
  Wurzel eingeschlossen). Ohne Schlüsselfelder ist die Identität der ganze Feldsatz: gleiche
  Felder = unverändert, sonst `Delete` und `Insert`. Zeilen, die im Text zweimal mit demselben
  Schlüssel stehen, sind ein Fehler, mit einer Ausnahme: Trägt der Aufbau selbst doppelte
  Schlüssel (`Duplicate`, zwei Zeilen mit gleichem Schlüssel aus derselben Quelle), ist die
  Identität `(Schlüssel, Ordnungszahl)` in Reihenfolge des Textes, solange der Text nicht mehr
  Zeilen dieses Schlüssels hat als der Aufbau; eine mehr ist ein Fehler. So hält die
  Round-Trip-Invariante auch für doppelte Schlüssel, und das Schreiben trifft die n-te Zeile.
  Eine geänderte Schlüsselzelle einer vorhandenen Zeile ist `Delete` plus `Insert` (bei fremden
  Zeilen also eine Dateikopie, 5.5); ein geändertes Schlüsselfeld der Wurzel ist ein Fehler
  (Non-Goal Umbenennen).
- `TextChanges`: pro Loc-Zeile `Set` (Schlüssel, Text, Quelle und `EntryPath` der alten) oder
  `Insert`; der Schlüssel entsteht aus Tabelle, Spalte und dem Schlüssel des Besitzers (Wurzel oder
  Abschnittszeile). Eine Loc-Zeile, die im Text fehlt, bleibt (wie Felder, Regel 8); Loc-Texte
  werden in 4.2a nie gelöscht, nur geleert (Backlog: Löschen per Dateikopie wie Tabellen).
- Versteckte Abschnitte (`hidden` in der Kategorie-Datei) stehen nicht im Text und nicht in der
  Differenz; ihre Zeilen bleiben unberührt.
- Typprüfung pro Feld über die Prüfregeln aus Teil 2 (`CellCheck`); ein Typfehler ist ein Fehler mit
  Zeilennummer. Referenzprüfung über den Graphen und den Stand des Aufbaus: Ziel nicht vorhanden
  ist eine Warnung.
- Ein Objekt, dessen Wurzel es nicht gibt (`Root == null`), kann im Block angelegt werden: Der Text
  hält die Wurzelfelder, `Compute` liefert `Insert` der Wurzel. So funktioniert „Kopieren als“ intern.

Beides ist rein und ohne Dateizugriff, test-first.

#### Festlegungen aus Plan 1

Was die Umsetzung von Plan 1 gegenüber den Skizzen oben festlegt oder anders macht (Stand: nach
jeder Task im selben Commit ergänzt):

- `ObjectText.Render` aus 4.1 bleibt unverändert, die App nutzt es bis Plan 3. Neu daneben:
  `ObjectText.RenderBlock(view, category)` liefert `BlockText` (`Lines` mit `BlockLineKind`, Text-Spans
  und Anhängsel-Spans pro Zeile, `Sections` mit Zeilenbereichen pro Abschnitt und Zeile, `Text` als
  editierbarer Text). Der Text enthält alle Zeilen; `Collapsed`-Zeilen gibt es dort nicht.
- Satz- und Junction-Zeilen haben eine Kopfzeile (Satz bzw. nur das andere Ende). Felder, die das
  Muster nicht nennt (und bei Junction alle außer dem anderen Ende und dem Feld auf die Wurzel),
  stehen mit nicht leerem Wert als Feldzeilen auf Stufe 2 darunter, ebenso Spieltexte der Zeile.
  Sonst ginge der Wert in Tabellen ohne Schlüssel beim Round-Trip verloren (der Feldsatz bestimmt dort
  die Identität). Die Skizze oben (`xp = 2` unter der Satzzeile) zeigt dasselbe.
- Die Kopfzeile einer Listenzeile ohne Schlüsselfeld ist das erste Feld in Schemareihenfolge außer dem
  Feld auf die Wurzel, auch wenn es leer ist (`a = `).
- Spieltexte von Referenzzielen sind nur Anhängsel (hinter dem Wert), nie Zeilen. Warnungen des
  Aufbaus sind Zeilen der Art `Annotation` ohne Text (im Text leere Zeilen).
- Ein Abschnittskopf trägt `.feld`, wenn `ObjectView.Sections` dieselbe Tabelle mehr als einmal
  enthält, auch mit leeren Abschnitten, nicht nur wenn der Text sie zeigt.
- Diagnosen haben `Kind` und `Detail` (kurzes technisches Englisch) statt einer Meldung; die App
  übersetzt nach `Kind`. Der Parser kennt die Arten `UnknownLineForm`, `HeadChanged`, `UnknownField`,
  `UnknownSection`, `HiddenSection`, `FieldWithoutRow`, `SentenceMismatch`, `FieldRepeated`,
  `ControlCharacter`, `UnterminatedText` (Fehler) sowie `RootFieldMissing`, `EmptyText` (Warnungen).
- Parser: Ein Abschnittskopf, der nicht aufgelöst werden kann (unbekannt, versteckt, Feld nötig),
  überspringt seine Zeilen ohne Folgefehler. Ein zweiter Kopf derselben `(Tabelle, Feld)` ist
  `UnknownSection` („section repeated“); seine Zeilen werden trotzdem gelesen.
- Parser: Ein Wert mit den zwei Zeichen `\t` oder `\n` ist `ControlCharacter` (Regel 2). Echte Werte
  mit diesen Zeichenfolgen würden dadurch beim Round-Trip abgelehnt; Task 6 misst, ob es sie gibt.
- Parser: Satzzeilen werden gegen den Text ohne Entfernen der Leerzeichen am Ende gelesen, damit
  `allows ` (Platzhalter leer) zum Muster passt; Platzhalterwerte werden getrimmt.
- Der Kategorie-Loader lehnt ein Muster mit zwei Platzhaltern ohne Text dazwischen ab (Detail
  „pattern has two placeholders without text between them.“).
- Differenz: `ObjectDiff.Compute(baseline, parsed, category, graph, referenceExists)` nimmt wie `Parse`
  die Kategorie und den `ReferenceGraph` (die Skizze oben nennt `GameSchema`). Die Kategorie ist nötig,
  damit die Zeilen versteckter Abschnitte, die nicht im Text stehen, nie als `Delete` erscheinen; sie
  ist deshalb kein optionaler Parameter. `referenceExists` null prüft keine Referenzen.
- Differenz: Die Typprüfung nutzt `CellText.TryParse` aus `Tables` (`CellCheck` liegt in `Editing`, das
  `Objects` nicht referenziert). Die Zusatzregel für Farbkanäle gilt dadurch hier nicht; Plan 2 prüft
  vor dem Schreiben mit `CellCheck`, falls das Schreiben sie nicht ohnehin erzwingt.
- Differenz: `TextChange` trägt keinen `EntryPath` der alten Zeile, weil `LocEntry` keinen kennt. Plan 2
  ermittelt die Datei über den Loc-Schlüssel und die Ladereihenfolge (Spec 5.4).
- Differenz: Identität einer Abschnittszeile sind die Schlüsselfelder ohne das Feld auf die Wurzel.
  Hat die Tabelle Schlüsselfelder, aber nur dieses, bilden alle Zeilen eine Gruppe, die nach
  Reihenfolge zugeordnet wird (eine mehr als der Aufbau: `DuplicateCountExceeded`). Tabellen ganz ohne
  Schlüsselfelder werden über den ganzen Feldsatz zugeordnet.
- Differenz: Fehlt ein Wurzelschlüsselfeld im Text einer Wurzel, die es noch nicht gibt, nimmt `Compute`
  den Wert aus der `ObjectId`; ein abweichender Wert ist `RootKeyChanged`.
- Differenz: `RowChange.Line` ist die Zeile des Zeilenkopfs, 1 für die Wurzel und 0 für Zeilen eines
  Abschnitts, den der Text nicht hat (`Delete`).
- Kopie: `ObjectCopy.Rename(source, newId, build, categoryOf, graph)` nimmt zusätzlich die Kategorie pro
  Tabelle (`categoryOf`) und den Graphen. Versteckte Abschnitte liefern den Hinweis `HiddenSection`
  (Tabelle, Zeilenzahl) als Daten; die App zeigt ihn nicht an (Regel 1 oben: kein Hinweis).
- Kopie: Zeilen einer Tabelle ohne eigenen Schlüssel, die nicht Junction ist (sie existieren nur
  als Zeilen der Wurzel), werden wie Regel 2 kopiert. Die Regeln 2 bis 4 sagen dazu nichts.
- Kopie: Die Regeln entscheiden pro Zeile über die Schlüsselfelder der Zeilen im Aufbau (`FieldValue.IsKey`).
  Regel 3 gilt nur bei einer Wurzel mit einem Schlüsselwert und genau einem Schlüsselfeld der Zeile;
  ein Unterobjekt bekommt den Schlüssel `neuerWurzelschlüssel + Rest`, und sein Feld auf die Wurzel
  zeigt auf den neuen Schlüssel. Hinweise der Unterobjekte stehen im Ergebnis des Objekts.
- Dateiersetzung: Eine Datei einer späteren Quelle mit demselben Pack-Pfad (ohne Rücksicht auf
  Schreibweise; Projektdateien über `ProjectLayout` auf ihren Pack-Pfad abgebildet, `Data__.tsv` gleich
  `db/x_tables/data__`) ersetzt die frühere Datei ganz. `TableStore` liest die ersetzte Datei nicht,
  `LocStore` ebenso für Loc-Dateien. Die Zeilen anderer Dateien derselben Tabelle bleiben und werden wie
  bisher zeilenweise überschrieben. Eine ersetzte Datei gilt nicht als überschrieben: ihre Quelle steht
  nicht in `RowProvenance.Overridden`.
- `RowProvenance.OverriddenEntries` ist eine init-Eigenschaft (Standard leer), kein Konstruktorparameter:
  bestehende Aufrufe bauen unverändert, `with { OverriddenEntries = … }` setzt sie. Sie hält zu jeder
  Quelle in `Overridden` den Eintragspfad (bei Quellen mit mehreren Dateien den der ersten Zeile).

### 5.4 Übernehmen: Zielregeln

`ProjectWritePlan.Compute(ObjectChanges, ProjectSnapshot, GameSchema, string projectName)` ordnet jede
Änderung einer Projektdatei zu und berechnet den neuen Inhalt pro Datei. `ProjectSnapshot` ist der
gelesene Stand der betroffenen Projektdateien (Pfad, Fingerprint, Zeilen) und die Liste aller
Projektdateien pro Tabelle; `ProjectWriter` liest ihn vor dem Rechnen.

Eine **Kopie** ist eine Projektdatei, deren Pack-Pfad gleich dem Pfad eines Eintrags in einem
Vanilla- oder Mod-Pack der Ladereihenfolge ist (sie ersetzt ihn im Spiel, 5.5). Kopien zählen
nicht als „Projektdatei der Tabelle“ in den Regeln 2 bis 4; neue Zeilen und Überschreibzeilen gehen
nie in eine Kopie, nur Änderungen und Löschungen ihrer eigenen Zeilen (Regel 1). So bleibt die
eigene Datei des Projekts klein und die Rückfrage selten.

Zieldatei einer Tabellenänderung, in dieser Reihenfolge:

1. Die Zeile stammt aus dem Projekt (`Source.Kind == Project`): ihre Datei (`EntryPath`), Kopie
   oder nicht.
2. Sonst genau eine Projektdatei der Tabelle (ohne Kopien): diese.
3. Sonst keine: eine neue Datei `db\<tabelle>\<name>.tsv` in der neuesten Schemaversion. `<name>` ist
   der Projektname, bereinigt nach `NewFiles.NameProblem`; ist er leer oder ungültig, `twmt`. Gibt es
   die Datei schon (unerwartet), nimmt das Tool sie.
4. Sonst mehrere: Rückfrage mit der Dateiliste, eine Auswahl pro Tabelle und Übernehmen. Abbrechen
   bricht das ganze Übernehmen ab, nichts wird geschrieben.

Eine `Set`-Änderung an einer Vanilla- oder Mod-Zeile wird zur Überschreibzeile: die ganze Zeile
(alle Felder der Quelle, auf die Feldliste der Zieldatei abgebildet wie beim Zusammenführen in
`TableStore`, fehlende Felder mit Standardwert), die geänderten Felder ersetzt, angehängt an die
Zieldatei. Eine `Set`-Änderung an einer Projektzeile ersetzt die Felder in ihrer Zeile. Ein
`Insert` hängt an die Zieldatei an (Regeln 2 bis 4). Hat die Zieldatei den Schlüssel schon (Projekt
dupliziert), ist das ein Fehler vor dem Schreiben: „Schlüssel X steht schon in Datei Y“.

Zieldatei einer Loc-Änderung: 1. die Projekt-Loc-Datei, die den Schlüssel enthält; 2. sonst die
einzige Loc-Datei des Projekts (ohne Kopien); 3. sonst eine neue `<name>.loc.tsv` wie oben; 4. sonst
mehrere: Rückfrage wie oben, eine Auswahl für alle Loc-Änderungen des Übernehmens.

Die Regeln 2 bis 4 (Tabellen und Loc) wenden die Nutzer-Entscheidung an: ohne Rückfrage, außer bei
mehreren Dateien.

Der Plan ist rein: Eingabe sind Snapshot und Änderungen, Ausgabe pro Datei `(RelativeFile,
PreviousFingerprint oder null bei neu, NewText)` plus die Liste der Rückfragen, die vor dem Rechnen
beantwortet sein müssen (das Tool rechnet zweimal: erst die Fragen sammeln, dann mit Antworten).

### 5.5 Löschen fremder Zeilen: Dateikopie

Das Spiel kennt kein „Zeile weg“. Es überlagert Tabellenzeilen per Schlüssel über die
Ladereihenfolge, und eine Pack-Datei mit demselben Pfad ersetzt die frühere Datei ganz. Deshalb:

`Delete` heißt: Der Schlüssel ist danach in der Ladereihenfolge weg, nicht nur in der obersten
Schicht. Die Zeile im Block trägt ihre Quelle und die überschriebenen Quellen (`Overridden`); das
Tool entfernt die Zeile in jeder dieser Schichten:

- `Delete` einer Projektzeile: Zeile aus ihrer Datei entfernen. Überschreibt sie eine Vanilla- oder
  Mod-Zeile, folgt für diese der nächste Punkt, sonst käme die untere Zeile nach dem Neuaufbau
  zurück.
- `Delete` einer Vanilla- oder Mod-Zeile: Das Tool kopiert die Quelldatei (`EntryPath`, zum Beispiel
  `db/building_units_allowed_tables/data__`) als Projektdatei unter demselben Pack-Pfad
  (`db\building_units_allowed_tables\data__.tsv`, über `NewFiles.Adopt` mit Schema, also in der
  Version der Quelle) und entfernt die Zeile dort. Gibt es die Kopie schon, wird nur die Zeile
  entfernt. Vor dem ersten Anlegen pro Datei fragt das Tool: Dateiname, Zeilenzahl, Warnung
  („Die Kopie ersetzt die Datei des Spiels ganz. Andere Mods, die dieselbe Datei ersetzen, kommen
  in Konflikt. Nach einem Spiel-Update kann die Kopie veraltet sein.“), mit „Kopieren und löschen“
  und „Abbrechen“. Braucht ein Übernehmen mehrere Kopien (Überschreibkette über zwei Packs, oder
  Löschungen in mehreren Tabellen), nennt ein Dialog alle Kopien mit den betroffenen Zeilen.
  Abbrechen bricht das ganze Übernehmen ab. Die Rückfrage ist die Ausnahme von „umkehrbar fragt
  nicht“, weil die Kopie groß ist und Folgen über das Projekt hinaus hat. Eine geänderte
  Schlüsselzelle einer fremden Zeile löst dieselbe Rückfrage aus (5.3), der Dialog nennt die
  gelöschte Zeile; wer nur eine Zeile hinzufügen will, lässt die alte stehen.
- Loc: nicht in 4.2a (5.3, `TextChanges`).
- Veraltete Kopie: Der Objektaufbau liest die Kopie als Projektdatei; die ersetzte Quelldatei
  erscheint nicht mehr (die Zeilen der Kopie tragen `Overridden` für die Quelle nur, wenn der
  Schlüssel dort steht, wie heute). Eine Prüfung „Kopie veraltet gegenüber Vanilla“ kommt nicht in
  4.2a (Backlog; braucht einen Fingerprint der Quelle in der Projektdatei oder der Projektdatei).

Wichtig für den Aufbau: `SourceReader` und `TableStore` müssen eine Projektdatei mit demselben
Pack-Pfad wie eine Vanilla-Datei so behandeln wie das Spiel: die Vanilla-Datei fällt weg. Heute
werden alle Quellen zusammengeführt, die Vanilla-Zeilen blieben also sichtbar. Das ist eine
Änderung in `Objects` (Abschnitt 5.1), getestet mit Mini-Daten. Dasselbe gilt für Mod-Dateien, die
Vanilla-Dateien ersetzen: Das war in 4.1 schon falsch dargestellt (Zeilen erschienen als
überschrieben statt als ersetzt) und wird hier mit behoben.

### 5.6 Schreiben und Undo

`ProjectWriter.Apply(plan, projectFolder, ct)`:

1. Prüft für jede Datei des Plans den Fingerprint gegen den Snapshot (`ProjectFileWriter.Probe`).
   Abweichung: Abbruch mit „Datei X hat sich geändert, Objekt aktualisieren“, nichts geschrieben.
2. Schreibt alle neuen Inhalte als temp-Dateien neben die Ziele, dann tauscht sie der Reihe nach
   (`AtomicFile.Replace`, Retry wie Teil 2). Scheitert ein Tausch, werden die schon getauschten
   Dateien aus den gehaltenen vorherigen Inhalten zurückgeschrieben; gelingt auch das nicht, nennt
   die Meldung die Dateien und ihren Zustand. Es gibt keinen Snapshot-Ordner: Die vorherigen Inhalte
   leben im Undo-Schritt.
3. Liefert `FileContentsStep` (UndoRedo): pro Datei `(RelativeFile, Previous: Text oder null, Next:
   Text, FingerprintAfter)`. `Undo` prüft den Fingerprint jeder Datei gegen `FingerprintAfter`
   (Abweichung: `StepDeferredException`-Verhalten wie `FileStep`, Meldung „Datei X wurde seither
   geändert“), schreibt `Previous` zurück oder verschiebt eine Datei mit `Previous == null` in den
   Papierkorb und entfernt leer gewordene Ordner, die der Schritt angelegt hat. `Redo` schreibt
   `Next` zurück oder holt die Datei aus dem Papierkorb. Der Schritt wird mit `Record` eingetragen,
   denn das Schreiben ist schon passiert.

Die App ruft das über den Runner („Änderungen werden übernommen“); der Lauf ist nicht abbrechbar
zwischen Tausch und Rückschreiben (kurz; der Abbrechen-Knopf wirkt nur vor dem ersten Tausch).

Speichergröße der History: Eine Vanilla-Kopie von `building_effects_junction_tables` hat 23.681
Zeilen (rund 2 MB Text). Der Schritt hält `Previous` und `Next`; bei 50 solchen Schritten rund
200 MB. Obergrenze: Die History begrenzt Schritte nicht; wird das zum Problem, kommt `Previous` in
den Papierkorb statt in den Speicher (Backlog, Annahme 5).

### 5.7 Editor

Der Objekt-Tab nutzt AvalonEdit:

- **Färbung** über einen eigenen `DocumentColorizingTransformer` aus den Spans von `Render`
  (`SpanKind` auf Theme-Tokens wie in 4.1), nach jeder Eingabe neu für die geänderten Zeilen. Der
  Parser läuft bei Eingabe nach einer Entprellung (`DispatcherTimer`, 300 ms) im UI-Thread über den
  Text; Fehler und Warnungen erscheinen als Unterstreichung mit Tooltip. Das Parsen eines Blocks
  mit einigen hundert Zeilen ist schnell genug für den UI-Thread (Annahme 2).
- **Anhängsel** als `VisualLineElementGenerator`: nicht editierbar, nicht selektierbar, am Zeilenende
  hinter einem Abstand. Sie stammen aus dem letzten Aufbau; für Zeilen, die der Nutzer geändert
  hat, werden sie ausgeblendet (die Herkunft stimmt dann nicht mehr), und erscheinen nach dem
  Übernehmen neu.
- **Nur-Lesen-Segmente** (`TextSegmentReadOnlySectionProvider`): die Kopfzeile.
- **Falten** für Abschnitte über der Klappgrenze (5.2) mit `+N` als Faltungstext.
- **Links** bleiben: Strg+Klick auf einen Referenzwert öffnet das Ziel wie in 4.1 (Klick allein
  setzt den Cursor). Hover zeigt den Spieltext des Ziels wie in 4.1.
- **Tastatur**: Strg+Enter übernimmt; Strg+Z und Strg+Y gehen an die globale History der App, nicht
  an AvalonEdits eigene (die ist deaktiviert, sonst gäbe es zwei Undo-Stapel; Text-Undo im Editor
  kommt nicht in 4.2a, Backlog). Alt+Links und Alt+Rechts wie in 4.1.
- Ein Objekt-Tab, dessen Text vom letzten Aufbau abweicht, ist „ungeübernommen“: Markierung im
  Tab-Titel wie geänderte Raster-Tabs. Zurück, Vor, Aktualisieren, Link-Navigation, Schließen und
  App-Schließen fragen „Übernehmen, Verwerfen, Abbrechen“. Die stille Veraltet-Prüfung aus 4.1 läuft
  weiter; ein veralteter Tab sperrt Übernehmen („Objekt veraltet, erst aktualisieren“; Aktualisieren
  fragt dann wie oben).

### 5.8 Objekt kopieren und Assistenten

`ObjectCopy.Rename(ObjectView source, ObjectId newId, Func<ObjectId, ObjectView> build)` liefert
eine Liste von `ParsedObject` (das Objekt und seine kopierten Unterobjekte) plus Hinweise. Das
Objekt wird unter neuem Schlüssel beschrieben: Wurzelfelder mit neuem Schlüsselwert, die kopierten
Abschnittszeilen (das Feld auf die Wurzel bekommt beim Schreiben den neuen Schlüssel), alle
Loc-Zeilen der Wurzel und der kopierten Zeilen mit dem alten Schlüssel im Loc-Schlüssel durch den
neuen ersetzt. Übernehmen eines `ParsedObject` gegen eine leere Baseline (`Root == null`) schreibt
alles als `Insert`. Zeilen und Texte landen nach den Regeln aus 5.4 im Projekt, ohne Dateikopie
(nichts wird gelöscht).

Kopierregel für Abschnittszeilen, eine pro Zeile, in dieser Reihenfolge:

1. Abschnitt versteckt (`hidden`): nicht kopiert, kein Hinweis.
2. Junction-Tabelle, oder der Schlüssel der Zeile enthält das Feld auf die Wurzel
   (`building_culture_variants`, `building_effects_junction`): kopiert, der Wurzelwert ersetzt.
3. Eigener Schlüssel, dessen Wert mit dem alten Wurzelschlüssel plus `_` beginnt
   (`character_trait_levels.key = <trait>_1`): kopiert, das Präfix ersetzt. Die Zeile ist selbst ein
   Objekt; `ObjectCopy` baut es über `build` auf und wendet dieselbe Regel auf seine Abschnitte an
   (`trait_level_effects` hängt an der Stufe). Tiefe höchstens 2 (Wurzel, Unterobjekt), danach
   Hinweis.
4. Sonst (eigener Schlüssel, zeigt nur per Fremdschlüssel: `technologies.building_level`): nicht
   kopiert, Hinweis im Ergebnis („3 Zeilen in `technologies` zeigen auf die Vorlage und wurden nicht
   kopiert“).

Die Regel ist allgemein; die Assistenten tragen keine Ausnahme. Prüft Annahme 3.

„Kopieren als“ steht im Objekt-Tab (Knopf und Kontextmenü), fragt den neuen Schlüssel (Prüfung: nicht
leer, keine Leerzeichen und Tabs, nicht in der Ladereihenfolge vorhanden) und öffnet danach das
neue Objekt im selben Tab (Verlauf wächst).

**Neues Gebäude** (Menü Werkzeuge, nur mit aktivem Projekt und Spielordner), Schritte in einem
Dialog:

1. Volk: Subkultur aus `cultures_subcultures_tables` mit Spieltext (`cultures_subcultures_name_…`),
   vorbelegt mit der Subkultur des zuletzt gewählten Objekts, wenn es ein Gebäude war.
2. Vorlage: Gebäude des Volks (Stufen aus `building_levels`, deren Kultur-Variante die Subkultur
   oder ihre Kultur trägt), mit Spieltext, Suchfeld über Schlüssel und Namen.
3. Schlüssel: Eingabe mit Vorschlag `<vorlage>_copy`, Prüfung wie bei „Kopieren als“.
4. Kette: „Neue Kette“ (Standard) oder „Stufe in bestehender Kette“. Neue Kette: Die Kette der
   Vorlage wird ebenfalls kopiert (`ObjectCopy` auf `building_chains` mit Schlüssel
   `<neuer_schlüssel>_chain`, die Stufe zeigt darauf; `upgrades from`/`upgrades to` der Vorlage
   entfallen, `level` wird 0). Bestehende Kette: Die Kette der Vorlage bleibt, `level` wird höchste
   Stufe plus 1, und eine Zeile `upgrades from <höchste Stufe>` wird angelegt.
5. Ergebnis: ein Übernehmen (ein Undo-Schritt) und der Block des neuen Gebäudes. Spieltexte tragen
   den Namen der Vorlage mit Zusatz „(Kopie)“ (nur `name`, englisch wie der Vorlagentext; der Nutzer
   ändert ihn im Block).

**Neuer Trait**: Vorlage (Trait mit Spieltext aus `character_trait_levels_onscreen_name_…`,
Suchfeld) und Schlüssel; kopiert über die Kopierregel 3 Trait, Stufen und Effekte der Stufen in
einem Übernehmen (ein Undo-Schritt). Prüft Annahme 3 für Traits.

Beide Assistenten sind Hüllen um `ObjectCopy` und Übernehmen; ihre einzige Spielregel ist die
Ketten- und Stufenlogik oben. Die Felder und Tabellen kommen aus Schema und Daten.

### 5.9 App

- Objekt-Tab: Kopfzeile wie 4.1 plus „Übernehmen“ (aktiv nur bei ungeübernommenem Text ohne
  Fehler, nicht veraltet, kein Aufbau, keine Operation; Tooltip nennt den Grund) und „Kopieren als“.
  Darunter die Ergebniszeile des letzten Übernehmens („3 Zeilen in 2 Dateien geändert, 1 Datei
  angelegt: `db\building_levels_tables\flo.tsv`“, mit den Hinweisen aus 5.8) bis zur nächsten
  Navigation. Nach dem Übernehmen baut der Tab automatisch neu (Runner), denn die Platte hat sich
  geändert; der Faltzustand bleibt.
- Rückfragen (Zieldatei bei mehreren, Dateikopie beim Löschen) sind modale Dialoge vor dem
  Schreiben, nach dem Parsen, mit den Texten aus `AppTexts`.
- Raster-Tabs: Übernehmen prüft vorher, ob ein Raster-Tab eine betroffene Datei mit ungespeicherten
  Änderungen hält; dann Sperre mit Hinweis „`<datei>` hat ungespeicherte Änderungen im Raster, erst
  speichern“. Nach dem Übernehmen melden die Raster-Tabs die Außen-Änderung über die vorhandene
  Mechanik (Fokus-Prüfung aus Teil 2 läuft zusätzlich sofort nach dem Übernehmen, damit der Nutzer
  nicht erst das Fenster wechseln muss).
- Undo eines Übernehmens wählt den Objekt-Tab, von dem es kam, wenn er noch offen ist, und baut ihn
  neu. Schließen des Objekt-Tabs entfernt seine Schritte **nicht** aus der History (anders als
  Raster-Tabs): Die Dateien bleiben geändert, Undo muss möglich bleiben. Der Schritt trägt dafür eine
  Beschreibung („Übernehmen: Gebäude `flo_emp_barracks_4`“).
- Session: Ein ungeübernommener Text geht beim Schließen der App nach Rückfrage verloren
  (Verwerfen) oder wird übernommen; die Session speichert keinen Text.
- Theme-Tokens: keine neuen Farben nötig außer `ObjectTrailing` (Anhängsel, gedämpft) und
  `ObjectError` (Unterstreichung). Texte in `en.json` und `de.json` über `AppTexts`.
- Logging: jedes Übernehmen mit Objekt, Dateien und Zeilenzahlen; jeder abgelehnte Schritt mit
  Grund. Keine Werte.

## 6. Verworfene Alternativen

| Entscheidung | Grund | Verworfen |
|---|---|---|
| Übernehmen schreibt sofort auf die Platte, ein Undo-Schritt mit vorherigen Inhalten | Der Aufbau liest Projektzeilen von der Platte; die Außen-Änderungs-Mechanik der Raster-Tabs fängt die Änderung auf; keine Dokument-Registry nötig (Nutzer 2026-10-10) | Zwei Stufen „Übernehmen in den Speicher, Strg+S auf die Platte“: hätte einen Lesepfad des Aufbaus über ungespeicherte Dokumente, eine Registry ohne Tabs und Dateien, die erst beim Speichern entstehen, gebraucht |
| Text und Anhängsel getrennt (virtuelle Elemente) | Parser ohne Trennzeichen, keine Mehrdeutigkeit bei Werten mit Leerzeichen, der Block sieht aus wie in 4.1 | Anhängsel als Text hinter einem Trennzeichen (`│`): Werte dürften das Zeichen nicht enthalten, Loc-Texte schon gar nicht; Kommentarsyntax (`//`): unleserlich |
| AvalonEdit | Färbung, Nur-Lesen-Segmente, virtuelle Inline-Elemente, Vorschlagsfenster (4.2b), Falten vorhanden; MIT (Nutzer 2026-10-10) | `RichTextBox` mit eigener Färbung: Nachziehen bei jeder Eingabe, keine Vorschlagsliste, keine festen Anhängsel; Eigenbau eines Editors |
| Löschen fremder Zeilen per Dateikopie | Einziger allgemeiner Weg im Spiel; Tool kennt den Pfad der Quelle; Konflikte und Veralten werden benannt | Neutralisieren (Wert 0, Bedingung nie): nur bei manchen Tabellen, Regel pro Tabelle; Löschen nur bei Projektzeilen: Nutzer will entfernen können |
| Kopieren über den Referenzgraphen mit Regel „Schlüssel enthält Wurzelfeld oder Junction“ | Keine Tabellenliste im Code; Hinweis für nicht kopierte Zeilen | Feste Rezeptliste pro Assistent: pflegeintensiv, nur für Gebäude richtig; alles kopieren: zöge Technologien und AI-Tabellen mit |
| Assistenten als dünne Hülle um „Kopieren als“ | Eine Mechanik, zwei Fragen (Volk, Kette) als Spielregel | Formulare mit allen Feldern: die Maske, die der Nutzer nicht will |
| Editor-Undo deaktiviert, nur globale History | Ein Undo-Stapel in der App; Text-Undo ist ein eigenes Thema | Zwei Stapel mit Fokusregel: verwirrend, Backlog |
| Veraltete Dateikopie nicht geprüft | Braucht einen gespeicherten Fingerprint der Quelle; erst nach dem ersten Spiel-Update relevant | Prüfung in 4.2a: Backlog |

## 7. Fehlerfälle

| Fall | Verhalten |
|---|---|
| Parse-Fehler im Text | Zeile unterstrichen, Tooltip mit Grund, Übernehmen gesperrt mit Tooltip „N Fehler“ |
| Typfehler (Text in Zahlenfeld, Farbe ungültig) | wie Parse-Fehler, Meldung nennt den erwarteten Typ wie im Raster |
| Referenzziel unbekannt | Warnung (gelb), Übernehmen erlaubt, Ergebniszeile zählt Warnungen |
| Schlüssel im Text doppelt | Fehler an der zweiten Zeile; Ausnahme: der Aufbau hatte den Schlüssel schon doppelt (`Duplicate`), dann Identität nach Reihenfolge (5.3) |
| Projektzeile überschreibt Vanilla-Zeile und wird gelöscht | Zeile aus der Projektdatei entfernt und Dateikopie für die Vanilla-Zeile mit Rückfrage (5.5) |
| Schlüssel schon in der Zieldatei (Projekt dupliziert) | Fehler vor dem Schreiben, Meldung nennt Datei und Schlüssel; nichts geschrieben |
| Kopfzeile verändert (Nur-Lesen umgangen, etwa durch Einfügen) | Fehler „Kopfzeile verändert, Objekt aktualisieren“ |
| Objekt veraltet (Signaturen abweichend) | Übernehmen gesperrt, Tooltip; Aktualisieren fragt „Übernehmen, Verwerfen, Abbrechen“ (Übernehmen ist dann gesperrt, bleibt Verwerfen oder Abbrechen) |
| Projektdatei hat sich zwischen Aufbau und Übernehmen geändert | Abbruch vor dem Schreiben: „Datei X hat sich geändert, Objekt aktualisieren“ |
| Raster-Tab mit ungespeicherten Änderungen an einer Zieldatei | Sperre mit Hinweis, kein Schreiben |
| Mehrere Projektdateien für eine Tabelle | Rückfrage mit Liste; Abbrechen = nichts geschrieben |
| Löschen einer fremden Zeile | Rückfrage mit Dateiname, Zeilenzahl, Warnung; Abbrechen = nichts geschrieben |
| Quelldatei der fremden Zeile nicht lesbar (Pack gesperrt, Schema fehlt für die Version) | Fehler vor dem Schreiben mit Pack und Datei; nichts geschrieben |
| Tausch einer Datei scheitert (Virenscanner, Sperre) | Retry wie Teil 2; dann Rückschreiben der schon getauschten Dateien; Meldung nennt jede Datei und ihren Zustand |
| Undo, Datei seither geändert | Schritt bleibt, Meldung „Datei X wurde seither geändert“ (wie `FileStep`) |
| Undo, Datei seither gelöscht | Schritt bleibt, Meldung nennt die Datei |
| Kein aktives Projekt | Übernehmen, „Kopieren als“ und Assistenten deaktiviert mit Tooltip |
| Objekt aus einem Pack außerhalb der Ladereihenfolge (Wurzel-Pack nicht im Katalog) | Übernehmen deaktiviert: „Pack nicht in der Ladereihenfolge“ |
| Neuer Schlüssel beim Kopieren schon vorhanden | Dialog lehnt ab mit Herkunft des vorhandenen |
| Vorlage ohne Kette (Gebäude mit leerem `chain`) | „Neue Kette“ legt keine an, Hinweis im Ergebnis |
| Abbruch im Runner vor dem ersten Tausch | nichts geschrieben, Text bleibt ungeübernommen |
| Loc-Datei des Projekts mit Diagnose (unlesbar) | wie Projektdatei geändert: Fehler vor dem Schreiben |

## 8. Tests

- `Objects` test-first mit Mini-Schema und Fake-Quellen wie in 4.1:
  - `ObjectText`: Text und Anhängsel getrennt; Golden-Test des Textes für Gebäude-Mini-Objekt;
    Round-Trip `Parse(Render(view)) == keine Differenz, keine Fehler` für jedes Test-Objekt
    (Invariante); Satzmuster rückwärts; Junction-Zeile; keyless Tabelle.
  - `ObjectTextParser`: jede Fehlerart aus 5.3 mit Zeilennummer; Toleranz (alle Fehler gesammelt);
    Einrückung mit Tabs; fehlende Wurzelfelder als Warnung; Loc-Zeile mit `\"` und `\n`; neuer
    Abschnitt, den der Aufbau nicht kennt.
  - `ObjectDiff`: Set, Insert, Delete; Identität mit und ohne Schlüssel; doppelte Schlüssel des
    Aufbaus nach Reihenfolge, eine Zeile mehr ist ein Fehler; Felder und Loc-Zeilen, die fehlen,
    bleiben; Loc-Zeile einer Abschnittszeile mit Schlüssel aus dem Besitzer; geänderte
    Schlüsselzelle = Delete plus Insert; Wurzelschlüssel geändert = Fehler; Typfehler;
    Referenzwarnung; Baseline ohne Wurzel; versteckte Abschnitte nicht in der Differenz.
  - `ObjectCopy`: jede Kopierregel aus 5.8 (versteckt, Junction und Wurzelfeld im Schlüssel,
    Präfix-Schlüssel mit Unterobjekt und Tiefe 2, Fremdschlüssel mit Hinweis); Loc-Schlüssel der
    Wurzel und der kopierten Zeilen umbenannt.
  - `TableStore`/`SourceReader`: Projektdatei mit demselben Pack-Pfad ersetzt die Vanilla-Datei;
    Mod-Datei ersetzt Vanilla-Datei; `Overridden` nur bei gleichem Schlüssel aus anderer Datei.
  - `CategoryDefinition`-Loader: zwei Platzhalter ohne Literal werden abgelehnt.
- `Editing` test-first im Temp-Ordner:
  - `ProjectWritePlan`: jede Zielregel aus 5.4 (1 bis 4) für Tabellen und Loc, Kopien ausgeschlossen; Überschreibzeile mit
    Abbildung auf die Feldliste der Zieldatei; Dateikopie mit Zeile entfernt; Fehler bei Schlüssel in
    Zieldatei; Rückfragen gesammelt.
  - `ProjectWriter`: Fingerprint-Abweichung bricht ab; mehrere Dateien getauscht; Tausch scheitert
    nach der ersten Datei (Fake `AtomicFile`-Fehler): Rückschreiben; Schritt mit `Previous == null`
    für neue Dateien.
- `UndoRedo`: `FileContentsStep` Undo/Redo mit Temp-Dateien; Fingerprint-Abweichung lässt den
  Schritt stehen; neue Datei wandert in den Papierkorb und zurück.
- Echtdaten (übersprungen ohne `TWMT_WH3_DIR`): Round-Trip für `wh_main_emp_barracks_2`,
  `wh_main_emp_empire` und einen Trait; `ObjectCopy` der Kaserne kopiert die Junction-Zeilen und die
  Kultur-Variante, nicht die 3 Zeilen aus `technologies` (eigener Schlüssel), und nennt diese im
  Hinweis; der Test prüft die Regel, nicht feste Zahlen.
- App: Übernehmen-Sperren in der Reihenfolge aus 5.9; Rückfragen; Ergebniszeile; Undo wählt den Tab;
  Schließen fragt; Raster-Tab-Sperre; Assistenten-Dialoge mit Fake-Daten; Locale-Test.
- Manuelle Checkliste beim App-Plan: Färbung beim Tippen, Anhängsel, Falten, Strg+Klick, Dateikopie
  mit Warnung, Spiel zeigt das neue Gebäude (Annahme 1).

## 9. Pläne

1. **Plan 1: `Objects` Text, Parser, Differenz, Kopie.** Text und Anhängsel trennen, Grammatik,
   `ObjectTextParser`, `ObjectDiff`, `ObjectCopy`, Dateiersetzung in `TableStore`, Loader-Regel für
   Muster. Test-first, keine App-Änderung. Echtdaten-Round-Trip.
2. **Plan 2: `Editing` und `UndoRedo` Schreiben.** `ProjectWritePlan`, `ProjectWriter`,
   `FileContentsStep`, Dateikopie, Rückfragen als Daten. Test-first, keine App-Änderung.
3. **Plan 3: App Editor und Übernehmen.** AvalonEdit, Färbung, Anhängsel, Falten, Fehleranzeige,
   Übernehmen über den Runner, Rückfragen, Ergebniszeile, Sperren, Undo-Verhalten, Raster-Kopplung,
   Texte, manuelle Checkliste.
4. **Plan 4: App Kopieren und Assistenten.** „Kopieren als“, „Neues Gebäude“, „Neuer Trait“, Texte,
   manuelle Checkliste mit Spielstart.

## 10. Annahmen, die der erste echte Lauf prüft

| Nr. | Annahme | Prüfung |
|---|---|---|
| 1 | Eine Projektdatei unter dem Pack-Pfad einer Vanilla-Datei ersetzt diese im Spiel ganz; ein Gebäude aus „Neues Gebäude“ erscheint im Spiel. | Abnahme Plan 3 und 4: Pack bauen, installieren, Spiel starten, gelöschte Einheit fehlt, neues Gebäude baubar. |
| 2 | Parsen und Färben eines Blocks mit einigen hundert Zeilen bleibt im UI-Thread unter 50 ms. | Plan 1 misst `Parse` auf der Fraktion; Abnahme Plan 3 beim Tippen in `wh_main_emp_empire`. |
| 3 | Die Kopierregel (Junction oder Schlüssel enthält Wurzelfeld) trifft bei Gebäude und Trait alle nötigen Zeilen und keine falschen. | Echtdaten-Test Plan 1 und Abnahme Plan 4. Subkultur als Volk: Abnahme Plan 4. |
| 4 | Kein Feldwert endet auf Leerzeichen, kein Loc-Text braucht mehr als `\"` und `\n`. | Plan 1 Echtdaten-Round-Trip über alle Tabellen und Loc der Ladereihenfolge (wie 4.1 Annahme 6). |
| 5 | Die History mit vorherigen Dateiinhalten bleibt unter 200 MB in einer Sitzung. | Abnahme Plan 3: zehn Dateikopien großer Tabellen, Speicher beobachten. |
| 6 | AvalonEdit baut mit `TreatWarningsAsErrors`, Central Package Management und `net8.0-windows` ohne Sonderbehandlung. | Plan 3, erste Task. |

## 11. Entscheidungen des Nutzers (2026-10-10)

- Hauptwunsch: Übersicht über alle Optionen (Felder mit erlaubten Werten; was man anhängen kann),
  Finden nach Wirkung, nicht nach Schlüssel. Der Katalog soll zum Handeln führen („diesen Effekt an
  dieses Gebäude“), nicht nur zum Nachschlagen. Das ist 4.2b.
- Ändern durch Tippen im Block wie in einem Texteditor, keine Maske.
- Überschreibzeilen ohne Rückfrage ins Projekt; fragen nur bei mehreren Projektdateien.
- Maske entfällt; Assistenten „Neues Gebäude“ und „Neuer Trait“ bleiben, mit Vorauswahl des Volks.
  Beim Gebäude ist die neue Kette der häufigste Fall, eine Stufe in einer bestehenden Kette muss
  einfach möglich sein.
- Übersicht im Block hat Vorrang (keine Wertekolonnen nebeneinander); Tooltips mit Erklärung an
  nicht offensichtlichen Elementen (4.2b).
- Effekt aus dem Katalog auf beiden Wegen ins Gebäude: Seitenleiste mit Einfügen und Vorschläge
  beim Tippen (4.2b).
- Teilung in 4.2a und 4.2b.
- Übernehmen in einer Stufe, sofort auf die Platte, ein Undo-Schritt; Raster-Tab mit ungespeicherten
  Änderungen sperrt.
- Löschen fremder Zeilen per Dateikopie mit einmaliger Rückfrage pro Datei.
- AvalonEdit als neues NuGet-Paket.
