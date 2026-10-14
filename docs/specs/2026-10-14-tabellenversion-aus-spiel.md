# Spec: Tabellenversion aus den Spieldateien

Stand: 2026-10-14. Korrigiert eine Annahme aus:

- `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md` (Abschnitt Dateioperationen Punkt 6, Abschnitt
  Kopieren und Einfügen: „Eine neue Tabelle entsteht in der neuesten Version des Schemas“)
- `docs/specs/2026-10-09-teil-4-1-objekt-ansicht.md` (5.2: „pro Tabelle die neueste Definition (höchste Version)“,
  5.4: „auf die neueste Definition abgebildet“)
- `docs/specs/2026-10-10-teil-4-2a-block-bearbeiten.md` (5.4 Übernehmen, 5.8 Objekt kopieren und Assistenten,
  die beide auf der Definition des Referenzgraphen aufsetzen)

## 1. Zweck und Anlass

Ein Testmod aus dem Gebäude-Assistenten („Kaserne als neue Kette kopiert, Hellebardiere ergänzt“) ließ WH3 beim
Start abstürzen. Ursache: Das Tool schrieb `building_levels_tables` in Version 16, das Spiel nutzt Version 3.
WH3 meldet dann „The first invalid database record is wh_main_emp_barracks_1_copy in table building_levels_tables“
oder stürzt ohne Meldung ab.

Das Tool hält überall die **höchste Version im Schema** für die aktuelle Version einer Tabelle. Für WH3 ist das
falsch: Die Versionsnummern wurden im Lauf der Entwicklung zurückgesetzt, das RPFM-Schema führt alte Definitionen
mit höheren Nummern weiter (`building_levels_tables`: Schema 16, 15, 3, 2, 0; Spiel 3).

Ziel: Die aktuelle Version einer Tabelle ist die, die das Spiel in seinen eigenen Packs nutzt. Ein Pack aus dem
Tool stürzt nie mehr wegen einer falschen Tabellenversion ab.

## 2. Ziele und Non-Goals

### 2.1 Ziele

1. **Spielversionen lesen:** Das Tool liest pro Tabelle die Version aus den Vanilla-Packs des Spiels (Manifest)
   und cacht sie mit Fingerprint.
2. **Eine Regel für „aktuelle Definition“:** eine gemeinsame, pure Funktion entscheidet für jede Tabelle (Ziel 3).
   Alle Stellen, die heute „höchste Version“ wählen, nutzen sie.
3. **Regel:**
   - Das Spiel hat die Tabelle, das Schema kennt die Version: diese Definition.
   - Das Spiel hat die Tabelle, das Schema kennt die Version nicht: keine aktuelle Definition, Grund
     „Schema veraltet“.
   - Das Spiel hat die Tabelle nicht: die höchste Version im Schema (Verhalten wie heute).
4. **Objekt-Ansicht, Übernehmen, Kopieren, Assistenten** arbeiten in der aktuellen Definition. Vanilla-Zeilen
   tragen danach kein „older layout“ mehr.
5. **Neue Tabellen** (Menü „Neue Tabelle“ und neue Datei beim Übernehmen) entstehen in der aktuellen Definition.
   Tabellen ohne aktuelle Definition sind nicht anlegbar, mit Grund.
6. **Bau-Check:** Der Bau bricht ab, wenn eine Tabellendatei des Projekts eine andere Version hat als dieselbe
   Tabelle im Spiel. Die Meldung nennt Datei, Projektversion und Spielversion und sagt, was zu tun ist.
7. **Ablehnen beim Hereinholen:** Übernehmen (auch das Löschen fremder Zeilen mit Dateikopie), „Ins Projekt
   übernehmen“ und der Pack-Import lehnen eine Tabelle in anderer Version als im Spiel sofort mit lesbarer
   Meldung ab (5.7). Eine veraltete Tabelle kommt so gar nicht erst ins Projekt.

### 2.2 Non-Goals

- **Keine Migration** bestehender Projektdateien in falscher Version. Entscheidung des Nutzers (2026-10-14): Die
  betroffenen Testprojekte werden gelöscht. Der Bau-Check (Ziel 6) meldet solche Dateien.
- **Kein Umwandeln** einer Tabellendatei in eine andere Version (weder automatisch noch als Werkzeug). Notiert in
  `Backlog.txt`.
- **Keine Warnung über veraltete fremde Mods** in der Ladereihenfolge. Ihre Zeilen erscheinen in der
  Objekt-Ansicht wie heute als „older layout“. Notiert in `Backlog.txt`.
- **Kein Schema-Download oder -Update.** Ein veraltetes Schema wird gemeldet, nicht behoben.
- **Der normale Tabelleneditor** bleibt unverändert: Er zeigt und speichert jede Datei in ihrer eigenen Version.
- **Keine Referenzkorrektur beim Kopieren** (CAI-Stufenbereiche über zwei Ketten). Das läuft in einer eigenen
  Session und Spec.

### 2.3 Erfolgskriterien

- Der Gebäude-Assistent erzeugt für die Kaserne eine `building_levels_tables`-Datei in Version 3. Das gebaute Pack
  lädt im Spiel (Hauptmenü und Imperiums-Kampagne, Gebäude baubar).
- Der Echtdaten-Test `Every_table_of_the_load_order_merges_without_exception` findet ohne Mods null Zeilen mit
  „older layout“.
- Ein Projekt mit `building_levels_tables` in v16 lässt sich nicht bauen; die Meldung nennt v16 und v3.

## 3. Gemessener Ist-Zustand (Spike vom 2026-10-14, WH3, schreibt nichts)

- **Beweis im Spiel:** Das gebaute `test.pack` stürzte ab. Dasselbe Pack mit nur `building_levels_tables` neu in
  v3 kodiert (Werte der Vanilla-Zeile `wh_main_emp_barracks_1`, Schlüssel und Kette ersetzt) startet, die
  Kampagne lädt, die Kopie ist baubar. Die CAI-Tabellen des Packs sind dabei enthalten und nicht die Ursache.
- **Ausmaß:** In `db.pack` weicht bei 60 von 1600 Tabellen die Spielversion von der höchsten Schema-Version ab,
  darunter `main_units_tables` (Spiel 7, Schema max 36), `factions_tables` (6/36), `agent_subtypes_tables` (3/19),
  `effects_tables` (0/3), `ancillaries_tables` (0/18), `building_effects_junction_tables` (0/3),
  `building_levels_tables` (3/16).
- **Schema älter als Spiel:** 4 Tabellen haben eine Spielversion, die das mitgelieferte Schema nicht kennt:
  `autoresolver_unit_group_categories_enums_tables` v1, `initiative_sets_tables` v9,
  `ritual_calculation_overrides_tables` v0, `ritual_character_target_criterias_tables` v0.
- **Verteilung:** 271 Vanilla-Packs im Manifest, alle 1603 DB-Tabellen liegen nur in `db.pack`, je ein Eintrag.
  Keine Tabelle hat in zwei Vanilla-Packs verschiedene Versionen.
- **Dauer:** Alle 1603 Einträge lesen und vollständig entpacken (zstd, 99 MB) dauert 1,5 s bei warmem
  Datei-Cache. Der Kopf eines Eintrags (GUID, Versionsmarke) liegt in den ersten Bytes.
- **Code:** Nur zwei Stellen wählen die Version: `ReferenceGraph.PickLatest` (Objekte, Übernehmen, Kopieren,
  Assistenten über `Latest`, `KeyFields`, `Incoming`, `IsJunction`) und `NewFiles.CheckTable` (eigene Kopie
  derselben Logik). Das Dekodieren selbst liest jede Datei in ihrer Kopfversion und ist nicht betroffen.
  Einen Begriff „Spielversion“ gibt es im Code nicht.
- **Graph-Aufbau:** `ObjectStores.Create` baut den Graphen nur aus dem Schema. Der Katalog mit den Vanilla-Packs
  ist zu diesem Zeitpunkt vorhanden, wird aber nicht übergeben. Die Stores werden nur bei Schema- oder
  Installationswechsel verworfen, nicht bei einem Spiel-Patch.

## 4. Schreibgrenze

Unverändert gegenüber Teil 2 und 4.2a, plus:

- **Neu geschrieben:** die Cache-Datei der Spielversionen in `%LocalAppData%\TotalWarModTool\cache\` (5.2).
- Spiel-Packs werden nur gelesen.

## 5. Entwurf

### 5.1 Module

| Modul | Änderung |
|---|---|
| `TotalWarModTool.Tables` | Neu: `GameTableVersions` (Tabelle → Version, Fingerprints der Quellen) und die pure Regel `CurrentDefinition` (5.3). Neu: öffentliches Lesen nur des Tabellenkopfs (`DbTableDecoder.ReadHeader`) |
| `TotalWarModTool.Workspace` | Neu: `GameTableVersionsReader`: liest die Versionen aus den Vanilla-Packs des Katalogs, Cache mit Fingerprint (5.2) |
| `TotalWarModTool.Objects` | `ReferenceGraph.Build(schema, versions)`: „Latest“ heißt künftig „Current“ und folgt der Regel |
| `TotalWarModTool.Projects` | `NewFiles` (Anlegen, `Adopt`), `PackImporter` und `ProjectBuilder.ComputeBuild` bekommen die Versionen und prüfen sie (5.6, 5.7) |
| `TotalWarModTool.Editing` | `ProjectWritePlan` übergibt die Versionen an `NewFiles` und lehnt Dateien in anderer Version ab (5.7) |
| `TotalWarModTool.App` | Versionen im Workspace-Zustand, an alle Aufrufer weiterreichen; Stores verwerfen, wenn die Versionen sich ändern |

Die Regel liegt in `Tables`, weil `Objects`, `Projects` und `Editing` sie gleich verstehen müssen (CLAUDE.md:
gemeinsamer Code in gemeinsamer Library). `Tables` kennt das Schema, aber keine Packs; das Lesen der Packs liegt in
`Workspace`, das den Katalog schon hat.

### 5.2 Spielversionen lesen

- **Quellen:** die Packs des Katalogs mit `Origin = Vanilla` (Manifest, sonst Pack-Typ wie heute), in
  Katalogreihenfolge. Mods und Projekt zählen nie.
- **Lesen:** pro DB-Eintrag nur der Kopf (GUID-Marke, Versionsmarke). Bei komprimierten Einträgen wird nur so viel
  entpackt, wie der Kopf braucht. Ob das mit dem vorhandenen zstd-Leser geht, klärt der Plan per Messung; sonst
  wird der Eintrag ganz entpackt (gemessen 1,5 s für alles, siehe 3).
- **Eintrag ohne Versionsmarke:** Version 0 (wie der Decoder).
- **Mehrere Vanilla-Packs mit derselben Tabelle in verschiedenen Versionen:** das spätere Pack in Katalogreihenfolge
  gewinnt, ein Log-Eintrag nennt Tabelle, Packs und Versionen. Heute nicht vorhanden (3).
- **Unlesbarer Eintrag oder Pack:** Ein Log-Eintrag nennt Pack, Eintrag und Grund; die anderen werden weiter
  gelesen, damit das Log alle Probleme nennt. Das Ergebnis gilt dann aber als **unvollständig** und wird wie
  „nicht gelesen“ behandelt (5.5) und nicht in den Cache geschrieben. Grund: Eine fehlende Tabelle fiele sonst
  still auf „Spiel hat die Tabelle nicht“ (höchste Version) zurück, also genau auf den Absturzfall. Ein gesperrtes
  `db.pack` (z. B. während eines Steam-Updates) sperrt damit die Funktionen aus 5.5, bis der nächste Katalog-Scan
  es lesen kann. (Festlegung beim Schreiben der Spec.)
- **Ergebnis:** `GameTableVersions` mit Wörterbuch Tabelle → Version (Groß-/Kleinschreibung egal) und den
  Fingerprints (Pfad, Größe, mtime) aller gelesenen Vanilla-Packs.
- **Cache:** `%LocalAppData%\TotalWarModTool\cache\table_versions_<spiel>.json` mit `schemaVersion`, den
  Fingerprints und dem Wörterbuch. Stimmen alle Fingerprints mit dem aktuellen Katalog, wird der Cache genutzt,
  sonst neu gelesen und atomar geschrieben. Kaputte Datei: nach `.bad` kopieren, neu lesen.
- **Wann:** nach jedem Katalog-Scan, als stiller Hintergrundlauf (CLAUDE.md: automatische Hintergrundläufe
  bleiben still). Bis das Ergebnis da ist, gilt 5.5.

### 5.3 Die Regel

`CurrentDefinition(GameSchema schema, GameTableVersions versions, string table)` liefert genau eins von:

| Fall | Ergebnis |
|---|---|
| Spiel hat die Tabelle in Version v, `schema.Find(table, v)` gibt eine Definition | diese Definition |
| Spiel hat die Tabelle in Version v, das Schema kennt v nicht | keine Definition, Grund `SchemaLacksGameVersion` mit v |
| Spiel hat die Tabelle nicht, Schema hat Definitionen | höchste Version (bei Gleichstand die erste, wie heute) |
| Schema kennt die Tabelle nicht | keine Definition, Grund `NotInSchema` |

Die Funktion ist pur. `ReferenceGraph.PickLatest` und die Schleife in `NewFiles.CheckTable` entfallen.

### 5.4 Wirkung an den Verbrauchern

- **Referenzgraph:** `Build(schema, versions)` legt pro Tabelle die aktuelle Definition fest. Eine Tabelle ohne
  aktuelle Definition fehlt im Graphen wie eine Tabelle, die das Schema nicht kennt. `Latest` wird in `Current`
  umbenannt, damit niemand „höchste Version“ hineinliest.
- **Tabellen-Store, Objekt-Ansicht:** Zeilen werden auf die aktuelle Definition abgebildet (Mechanik wie heute,
  `RowLayout`). Vanilla-Zeilen sind damit identisch abgebildet. „older layout“ tragen nur noch Zeilen aus Mods
  oder Projektdateien in anderer Version.
- **Übernehmen (`ProjectWritePlan`):** unverändert in der Mechanik. Eine neue Projektdatei entsteht über `NewFiles`
  in der aktuellen Definition.
- **Kopieren und Assistenten:** keine eigene Änderung; sie lesen Vanilla-Zeilen und schreiben über
  `ProjectWritePlan`, also in der Spielversion.
- **Neue Tabelle (Menü):** `CreatableTables` listet nur Tabellen mit aktueller Definition ohne nicht unterstützten
  Feldtyp. `Table` lehnt eine Tabelle ohne aktuelle Definition mit Grund ab („Das Schema kennt Version v dieser
  Tabelle nicht, die das Spiel nutzt. Ein neueres Schema in den Einstellungen wählen.“).
- **Assistenten-Prüfung (`WizardNames.Check`) und Kategorie-Dateien:** prüfen Felder gegen die aktuelle Definition.
  Fehlt ein Feld dort, meldet die App das wie heute.

### 5.5 Ohne Spielversionen

Solange die Versionen nicht gelesen sind, das Lesen unvollständig war (5.2) oder kein Spiel gefunden ist:

- Objekt-Ansicht, Übernehmen, Kopieren, Assistenten, „Neue Tabelle“ und Bauen warten nicht still, sondern sind
  gesperrt. Deaktivierte Controls tragen einen Tooltip mit dem Grund: „Die Tabellenversionen des Spiels werden
  noch gelesen.“, „Die Tabellenversionen des Spiels konnten nicht vollständig gelesen werden. Siehe Log.“ bzw.
  „Kein Spiel gefunden. Bauen und neue Tabellen brauchen die Tabellenversionen des Spiels.“
- Bauen und „Neue Tabelle“ gingen bisher auch ohne gefundenes Spiel (nur mit Schema). Das entfällt bewusst: Ohne
  Spielversionen ist weder die richtige Version noch der Bau-Check möglich. (Festlegung beim Schreiben der Spec.)
- Die Sperre der Objekt-Ansicht ist ebenfalls eine Festlegung beim Schreiben der Spec: Ihr Graph braucht die
  aktuellen Definitionen.
- Es gibt keinen Rückfall auf „höchste Version“: Er würde genau die Packs erzeugen, die abstürzen.
- Der Tabelleneditor bleibt nutzbar.

### 5.6 Bau-Check

`ProjectBuilder.ComputeBuild` bekommt `GameTableVersions`. Für jede Tabellendatei des Projekts (TSV und binär):

- Hat das Spiel die Tabelle und weicht die Version der Datei ab: `BuildProblem` für die Datei. Text (Englisch,
  als Detail wie die anderen Bauprobleme): „the table file has version 16, the game uses version 3; the game
  crashes on this file. Delete the file or create the table again.“
- Hat das Spiel die Tabelle nicht: keine Prüfung.
- Ein Bauproblem verhindert das Schreiben wie heute (`BuildOutcome.HasProblems`), die App zeigt die Liste wie heute.

### 5.7 Ablehnen beim Hereinholen

Gilt nur für Tabellen, die das Spiel hat (5.3 Fall 1 und 2). „Andere Version“ heißt: Version der Datei ≠
Spielversion.

- **Übernehmen (`ProjectWritePlan`):**
  - Anhängen an oder Ändern in einer bestehenden Projektdatei, deren Version von der Spielversion abweicht:
    Fehler für die Zeile, der ganze Plan schreibt nichts (wie die bestehenden Planfehler). Meldung: Datei, beide
    Versionen, „Datei löschen oder die Tabelle neu anlegen“.
  - Löschen einer fremden Zeile mit Dateikopie (`PlannedFileKind.Copy`): Ist der fremde Eintrag in anderer Version,
    Fehler, der Plan schreibt nichts. Meldung nennt Pack, Eintrag, beide Versionen und dass die Mod veraltet ist.
  - Neue Dateien entstehen ohnehin in der Spielversion (5.4).
- **„Ins Projekt übernehmen“ (`NewFiles.Adopt`):** Ein Eintrag in anderer Version wird abgelehnt, mit Meldung wie
  oben. Sind mehrere Einträge gewählt und ist einer davon veraltet, schreibt die Aktion nichts und nennt alle
  veralteten Einträge (kein Teilzustand).
- **Pack-Import (`PackImporter`):** Enthält das Pack eine Tabelle in anderer Version, wird der ganze Import vor dem
  Schreiben abgelehnt. Die Meldung listet alle veralteten Einträge mit beiden Versionen.
- **Ohne Spielversionen (5.5):** Diese Aktionen sind gesperrt wie die anderen aus 5.5.
- Die Regel „Datei-Version ≠ Spielversion“ ist dieselbe Funktion wie beim Bau-Check (5.6), in `Tables`.

### 5.8 Neuer Zustand im Workspace

- Die Versionen gehören zum Workspace-Zustand neben Katalog und Schema.
- Ändern sich die Versionen (Spiel-Patch, anderes Installationsverzeichnis), werden die Objekt-Stores verworfen
  und beim nächsten Bedarf neu gebaut. Ein Schemawechsel verwirft sie wie heute.
- `CreatableTables` wird pro Schema und Versionen gecacht, nicht mehr nur pro Schema.

## 6. Verworfene Alternativen

- **Erste Definition der Schemaliste nehmen:** Die Liste ist absteigend nach Version sortiert, also dasselbe wie
  „höchste Version“. Falsch für die 60 Tabellen.
- **Liste der Ausnahmen pro Spiel im Profil:** bricht bei jedem Patch und bei einem vom Nutzer gewählten Schema.
- **Version erst beim Lesen einer Tabelle im Store bestimmen:** Der Graph braucht alle aktuellen Definitionen auf
  einmal (eingehende Referenzen über alle Tabellen), und Bauen und „Neue Tabelle“ laufen ohne Objekt-Stores.
- **Nur beim Bauen prüfen, sonst alles lassen:** Der Assistent würde weiter falsche Dateien erzeugen, die dann
  nicht baubar sind; die Objekt-Ansicht bliebe falsch.
- **Bei falscher Version im Bau nur warnen:** Entscheidung des Nutzers (2026-10-14): Abbruch. WH3 stürzt bei einer
  veralteten Tabelle immer ab.
- **Rückfall auf höchste Version, solange die Spielversionen fehlen:** erzeugt genau die abstürzenden Packs.

## 7. Fehlerfälle

| Fall | Verhalten |
|---|---|
| Manifest fehlt | Vanilla über Pack-Typ wie heute (`VanillaPackTypes`) |
| Ein Vanilla-Pack unlesbar oder gesperrt | Ergebnis unvollständig: 5.5, Log-Eintrag, kein Cache |
| Eintrag mit kaputtem Kopf | Ergebnis unvollständig: 5.5, Log-Eintrag, kein Cache |
| Kein Spiel gefunden | 5.5 mit eigenem Tooltip; Tabelleneditor nutzbar |
| Cache-Datei kaputt | `.bad`, neu lesen, kein Abbruch |
| Cache-Datei nicht schreibbar | Log-Eintrag, Ergebnis gilt für diese Sitzung |
| Schema kennt die Spielversion nicht | Tabelle ohne aktuelle Definition (5.3), nicht anlegbar, in der Objekt-Ansicht wie heute als nicht lesbar |
| Projektdatei in anderer Version als das Spiel | Bau bricht ab (5.6); Übernehmen in diese Datei abgelehnt (5.7); Objekt-Ansicht zeigt ihre Zeilen als „older layout“ |
| Fremde Zeile aus veralteter Mod löschen | Plan abgelehnt, Meldung nennt Mod und Versionen (5.7) |
| Import oder „Ins Projekt übernehmen“ mit veralteter Tabelle | ganze Aktion abgelehnt, Liste der veralteten Einträge (5.7) |
| Spiel-Patch während die App läuft | neuer Katalog-Scan liest die Versionen neu (Fingerprint), Stores werden verworfen |

## 8. Tests

Test-first in den Libraries, handgeschriebene Fakes, Temp-Ordner.

- **Tables:** `CurrentDefinition` für alle vier Fälle aus 5.3, auch Gleichstand bei der höchsten Version.
  `ReadHeader` mit und ohne GUID, mit und ohne Versionsmarke, kaputter Kopf.
- **Workspace:** `GameTableVersionsReader` mit erzeugten Packs im Temp-Ordner: komprimiert und unkomprimiert,
  zwei Packs mit verschiedenen Versionen (späteres gewinnt), unlesbares Pack und kaputter Eintragskopf
  (unvollständig, kein Cache geschrieben), Cache-Treffer, Cache veraltet (Fingerprint), Cache kaputt (`.bad`).
- **Objects:** `ReferenceGraphTests.Latest_picks_the_highest_version` wird zu Tests der Regel über den Graphen
  (Spielversion niedriger als Schema-Maximum gewinnt; Tabelle ohne Spielversion nimmt Maximum; Schema ohne
  Spielversion lässt die Tabelle aus). `TableStoreTests`: Vanilla in Spielversion ist nicht „older layout“, eine
  Mod-Datei in höherer Schema-Version schon.
- **Projects:** `NewFilesTests` mit Versionen: neue Tabelle in Spielversion, Tabelle mit unbekannter Spielversion
  nicht anlegbar. `ProjectBuilder`: Datei in falscher Version gibt ein Bauproblem mit beiden Versionen; Datei in
  Spielversion und Tabelle ohne Spielversion geben keins.
- **Editing:** `ProjectWritePlanTests.An_insert_without_a_project_file_creates_one_named_after_the_project`
  erwartet die Spielversion statt der höchsten. Neu: Anhängen an eine Projektdatei in anderer Version ist ein
  Fehler; Löschen einer fremden Zeile aus einem Eintrag in anderer Version ist ein Fehler; beides schreibt nichts.
  Die bestehenden Tests zu älteren Projektdateien (`An_override_row_is_mapped_onto_the_columns_of_an_older_file`
  u. a.) werden so umgestellt, dass die ältere Datei eine Tabelle betrifft, die das Spiel nicht hat, oder sie
  erwarten den neuen Fehler.
- **Projects (Hereinholen):** `NewFiles.Adopt` mit einem veralteten Eintrag unter mehreren schreibt nichts und
  nennt ihn; Pack-Import mit veralteter Tabelle schreibt nichts und listet sie.
- **Echtdaten (übersprungen ohne Fixture):** `Every_table_of_the_load_order_merges_without_exception` ohne Mods
  null „older layout“-Zeilen. Neuer Test: Gebäude-Assistent auf `wh_main_emp_barracks_1` erzeugt
  `building_levels_tables` in der Version aus `db.pack`. `ShippedCategoryTests` gegen die aktuelle Definition.
- **App:** manuelle Testcheckliste (Stores neu nach Patch-Simulation durch Ändern der Cache-Fingerprints, Tooltips
  in 5.5, Bauabbruch mit Meldung).

## 9. Pläne

Ein Plan reicht, in dieser Reihenfolge: Tables (Regel, Kopf) → Workspace (Lesen, Cache) → Objects (Graph) →
Projects und Editing (NewFiles, Bau-Check, WritePlan) → App (Zustand, Sperren, Weiterreichen) → Echtdaten-Lauf und
Spieltest. Die alten Specs (Kopf dieser Spec) bekommen im selben Commit wie die Umsetzung einen Verweis auf diese
Spec an den genannten Stellen.

## 10. Annahmen, die der erste echte Lauf prüft

1. Der Kopf eines zstd-Eintrags lässt sich ohne volles Entpacken lesen; sonst bleibt es bei etwa 1,5 s pro
   Katalog-Änderung, durch den Cache einmal pro Patch.
2. Mit der Spielversion als aktueller Definition passen die mitgelieferten Kategorie-Dateien und `wizards.json`
   weiter (Felder `chain`, `level` usw. gibt es in `building_levels_tables` v3; geprüft beim Spike für die
   Assistenten-Felder, nicht für alle Kategorie-Felder).
3. Vanilla hat in WH3 nach dem Fix null „older layout“-Zeilen.
4. Ein Pack aus dem Gebäude-Assistenten lädt im Spiel ohne manuelles Nacharbeiten.
5. Nach einem echten Spiel-Patch erkennt der Fingerprint die Änderung und die Versionen werden neu gelesen.

## 11. Entscheidungen des Nutzers (2026-10-14)

- Aktuelle Version einer Tabelle = Version in den Spieldateien statt höchster Schema-Version.
- Projektdateien in falscher Version werden nicht migriert; die Testprojekte werden gelöscht.
- Der Bau bricht bei einer Tabellendatei in anderer Version als im Spiel ab.
- Übernehmen, Löschen fremder Zeilen, „Ins Projekt übernehmen“ und Import lehnen veraltete Tabellen sofort mit
  Meldung ab, statt sie durchzulassen.
- Annahmen bestätigt: Versionen aus den Vanilla-Packs mit Fingerprint-Cache; Tabellen, die das Spiel nicht hat,
  nehmen die höchste Schema-Version; Spielversion unbekannt im Schema = nicht anlegbar mit Grund; Tabelleneditor
  unverändert.
