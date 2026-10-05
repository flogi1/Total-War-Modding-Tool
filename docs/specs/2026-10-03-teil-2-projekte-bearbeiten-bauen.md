# Spec: Teil 2 (Mod-Projekte, Import, Tabellen bearbeiten, Pack bauen)

- **Datum:** 2026-10-03
- **Status:** Entwurf, wartet auf Freigabe durch den Nutzer
- **Baut auf:** `docs/specs/2026-10-02-fundament.md` (Teil 1, umgesetzt und abgenommen)
- **Pläne:** fünf Pläne nacheinander, siehe Abschnitt 9

---

## 1. Zweck und Einordnung

Teil 2 macht aus dem Nur-Lese-Werkzeug ein Werkzeug zum Modden. Der Nutzer legt Mod-Projekte an oder
importiert bestehende Packs, bearbeitet Tabellen im Raster und baut daraus ein Pack, das das Spiel
lädt.

Die Fundament-Spec hat in Abschnitt 1.3 festgelegt: Eigene Mods liegen als Arbeitsordner mit
Textdateien vor, das Pack entsteht per „Bauen“. Tabellen liegen als Text mit einer Zeile pro Eintrag
vor. Vanilla und fremde Mods bleiben read-only. Diese Spec setzt das um.

### 1.1 Geänderte Reihenfolge der Teile

Am 2026-10-03 hat der Nutzer entschieden: Nach Teil 2 folgt direkt die Objekt-Ansicht (bisher
Teil 4), danach Error-Log und Spielstart (bisher Teil 3). Grund: Der Nutzer will ein Gebäude oder
eine Einheit in einem Block bearbeiten statt in vielen Tabellen. Der Block ist eine berechnete
Ansicht auf die Tabellenzeilen und braucht das, was Teil 2 baut. Teil 2 bleibt deshalb schlank.

Das Textformat von Teil 2 ist so gewählt, dass die Objekt-Ansicht darauf abbildbar ist: eine Zeile
pro Eintrag, Spalten per Name, Schlüsselspalten aus dem Schema.

---

## 2. Ziele und Non-Goals

### 2.1 Ziele

1. **Mod-Projekt:** Ein Projekt ist ein vom Nutzer frei gewählter Ordner mit Projektdatei und
   stabiler GUID. Mehrere Projekte sind gleichzeitig sichtbar, genau eines ist aktiv.
2. **Import:** Ein bestehendes Pack wird zu einem neuen Projekt. Einzelne Einträge aus Vanilla oder
   fremden Mods lassen sich in das aktive Projekt übernehmen.
3. **Bearbeiten:** Tabellen und Loc-Dateien des Projekts sind im Raster bearbeitbar, mit Prüfung pro
   Feldtyp, Undo und Redo und ausdrücklichem Speichern.
4. **Textansicht:** Jede Tabelle aus jeder Quelle lässt sich als Text (TSV) anzeigen und kopieren.
5. **Bauen und Installieren:** Aus dem Projekt entsteht ein Pack im Projektordner. Auf Wunsch kopiert
   das Tool es in den `data`-Ordner des Spiels.
6. **Verlustfrei:** Import und Neubau eines Packs ergeben denselben Inhalt.

### 2.2 Non-Goals

- Objekt-Ansicht, Katalog, Autovervollständigung (nächster Teil).
- Prüfung von Referenzen und doppelten Schlüsseln (kommt mit der Objekt-Ansicht).
- Bearbeiten von Textdateien (Lua, XML). Sie bleiben Nur-Lese-Text bis zum Lua-Editor.
- Editierbare Textansicht der Tabellen.
- Erneuter Import eines geänderten Packs über ein bestehendes Projekt (braucht den 3-Wege-Abgleich
  aus dem Teil Diff und Merge).
- Tabelle auf eine neuere Schema-Version anheben.
- Kompression beim Schreiben, verschlüsselte Packs, andere Pack-Formate als PFH5.
- Mod im Launcher aktivieren, Spiel starten.
- Sicherung ungespeicherter Änderungen für die Wiederherstellung nach einem Absturz.
- Git-Funktionen im Tool. Der Projektordner ist nur so gebaut, dass Git darauf gut arbeitet.
- Erkennen einer TSV-Datei, die aus einem RPFM-Export stammt (gerundete Kommazahlen).

### 2.3 Erfolgskriterien

- Ein Workshop-Pack importieren, ohne Änderung bauen: Jeder Eintrag des neuen Packs hat denselben
  Inhalt wie im Original. Bei Tabellen darf nur die GUID im Kopf abweichen.
- Eine Zelle ändern, speichern, bauen, installieren: Das Spiel zeigt den geänderten Wert.
- `rpfm_cli` liest das gebaute Pack und die TSV-Dateien des Projekts ohne Fehler.
- Kein Schreibzugriff außerhalb der Schreibgrenze (Abschnitt 4).

---

## 3. Gemessener Ist-Zustand (Spikes vom 2026-10-03)

Wegwerf-Code außerhalb des Repos. Gelesen wurden der Spielordner (268 Packs) und der Workshop-Ordner
(397 Packs, 70 GB). Schema: `schemas/schema_wh3.ron`.

### 3.1 Round-Trip Tabellen (S1)

Binär → Text → binär, verglichen Byte für Byte.

| Quelle | Tabellen dekodiert | byte-identisch |
|---|---|---|
| Vanilla und `data` | 1.735 | 1.735 |
| Workshop | 35.168 | 35.168 |

Bedingungen dafür:

- Kommazahlen in der kürzesten Schreibweise, die denselben Wert zurückgibt (`R`-Format).
- Die GUID aus dem Tabellenkopf bleibt erhalten. Sie steht nicht im Text.
- Der Versionsmarker im Kopf fehlt genau dann, wenn die Version 0 ist (gemessen in allen 36.903
  Tabellen, keine Ausnahme). Das Byte vor der Zeilenzahl ist immer 1.
- Optionale Texte: „vorhanden, aber leer“ kommt nicht vor. Leerer Text heißt „nicht vorhanden“.
- Kein Text enthält ungültiges UTF-8 oder UTF-16.

Kopf: 36.897 Tabellen tragen eine GUID, 7 davon eine leere.

Zeit für alle Vanilla-Tabellen (99,5 MB binär, 967.461 Zeilen): Text schreiben 0,7 s, Text lesen
0,9 s, kodieren 0,5 s.

### 3.2 Round-Trip Loc (S6)

4.206 Loc-Dateien (269 Vanilla, 3.937 Workshop), 3,2 Mio. Zeilen: alle byte-identisch.

### 3.3 TSV-Dialekt von RPFM (S2)

Geprüft mit `rpfm_cli` 4.7.3 gegen die 1.599 dekodierbaren Tabellen von `db.pack`.

- Aufbau: Zeile 1 Spaltennamen, Zeile 2 `#<tabelle>;<version>;<pfad im pack>` gefolgt von Tabs,
  danach eine Zeile pro Eintrag. Dateiname `<name>.tsv`, bei Loc `<name>.loc.tsv`.
- Spalten werden per Name zugeordnet, die Reihenfolge ist frei.
- Ein Tab im Text steht als `\\t`, ein Zeilenumbruch als `\\n` (zwei Backslashes und ein Buchstabe).
  Ein einzelner Backslash bleibt, wie er ist.
- Drei Zahlenfelder `…_r`, `…_g`, `…_b` (oder `…_red`, `…_green`, `…_blue`, oder ohne Präfix)
  erscheinen als eine Spalte `<präfix>_hex` bzw. `unnamed colour group_N` mit dem Wert `RRGGBB`.
  Einzelne r/g/b-Spalten ignoriert der Import von RPFM.
- **RPFMs eigener Export ist verlustbehaftet:** Kommazahlen haben genau 4 Nachkommastellen. RPFMs
  eigener Export und Re-Import verändert 80 der 1.599 Tabellen. In Vanilla betrifft das 5.061 Zellen
  in 76 Tabellen, in den Workshop-Tabellen 39 Zellen in 1 Tabelle.
- **Unser TSV in diesem Dialekt, aber mit vollen Kommazahlen:** `rpfm_cli` importiert 1.598 von 1.599
  Tabellen inhaltsgleich. RPFM liest also volle Genauigkeit.
- Zwei Grenzen des Dialekts:
  - Ein Farbkanal außerhalb 0 bis 255: RPFM behält nur das unterste Byte. Gemessen 1 Vanilla-Tabelle
    (`campaign_tree_ids_tables`, 4 Zellen), 0 Workshop-Tabellen.
  - Ein Text, der wörtlich `\\t` oder `\\n` enthält, ist vom Escape nicht zu unterscheiden. Gemessen
    0 Fälle in allen Packs.

### 3.4 Pack schreiben (S3, S4)

- Kopf der Mod-Packs: 394 von 396 lesbaren Workshop-Packs haben die Bitmaske `0x3` (Typ Mod, keine
  Zeitstempel im Index), 2 haben `0x4` (Movie). Pfade im Index nutzen `\`.
- Alle Vanilla-Packs sind nach Pfad sortiert (ordinal). 53 Workshop-Packs sind es nicht, das Spiel
  verträgt also unsortierte Packs.
- 29 % der Workshop-Einträge sind komprimiert.
- Neu geschrieben (unkomprimiert, sortiert, Bitmaske `0x3`):
  - 2,97 GB, 13.422 Einträge: lesen 5,1 s, schreiben 1,4 s. Alle Einträge inhaltsgleich.
  - 20,9 MB, 666 Einträge: `rpfm_cli` packt Original und Neubau aus, alle 665 Dateien hash-gleich.
- **Im Spiel (S4, vom Nutzer geprüft):** Ein Pack mit einer Tabelle (eine Zeile, über TSV-Text
  geändert, neue GUID) und einer Loc-Datei lädt. Geänderter Wert und geänderter Name erscheinen.

### 3.5 Raster-Editing (S5)

Standard-`DataGrid`, editierbar, Zeilen mit Indexer und Änderungsmeldung, `ObservableCollection`.

| Tabelle | Anzeige | Zelle ändern | Zeile einfügen | 1.000 Zeilen einfügen |
|---|---|---|---|---|
| 71.848 × 4 | 277 ms | 14 ms | 1–3 ms | 115 ms |
| 26.637 × 10 | 300 ms | 17 ms | 1–4 ms | 114 ms |
| 3.173 × 71 | 1.190 ms | 17 ms | 3–6 ms | 205 ms |
| 400.000 × 6 | 135 ms | 30 ms | 0–1 ms | 85 ms |

Das Standard-Raster reicht. Speicher bei 400.000 × 6: 180 MB.

---

## 4. Schreibgrenze

Das Tool schreibt in Teil 2 zusätzlich zu `%APPDATA%\TotalWarModTool` und
`%LocalAppData%\TotalWarModTool`:

1. **In den Projektordner** eines bekannten Projekts: die Projektdatei, Dateien unter `pack\` und das
   Pack unter `build\`. Nichts sonst, außer den eigenen temp-Dateien während eines Schreibvorgangs
   (`twmt-project.json.<8 Hex>.tmp`, `build\<Pack-Name>.<8 Hex>.tmp`). Ein Import schreibt zuerst in
   einen Staging-Ordner `<Zielordner>.import-<8 Hex>` direkt neben dem Zielordner, also im
   Elternordner, den der Nutzer gewählt hat, und benennt ihn am Ende um. Ist `pack\` oder `build\`
   selbst eine Verknüpfung (Junction, symbolischer Link), schreibt, löscht und liest das Tool dort
   nichts (Festlegungen aus Plan 3, Punkt 19).
2. **In den `data`-Ordner des Spiels:** genau eine Datei pro Projekt, das installierte Pack, und nur
   unter den Bedingungen aus Abschnitt 5.7. Dazu gehört während des Installierens die temp-Datei
   `<Pack-Name>.twmt-tmp` im selben Ordner.

Vanilla-Packs, Workshop-Packs und fremde Packs in `data` werden nie geschrieben, umbenannt oder
gelöscht. Ein Projektordner wird nie gelöscht, auch nicht beim Entfernen aus der Projektliste.

---

## 5. Entwurf

### 5.1 Projekt auf der Platte

```
<Projekt>\
  twmt-project.json
  pack\
    db\main_units_tables\mein_mod.tsv
    text\db\mein_mod.loc.tsv
    script\campaign\mod\mein_skript.lua
  build\mein_mod.pack
```

- `pack\` spiegelt die Pfade im Pack.
- `pack\db\<tabelle>\<name>.tsv` ist eine Tabelle und wird zum Pack-Eintrag `db/<tabelle>/<name>`.
- `<pfad>.loc.tsv` ist eine Loc-Datei und wird zum Eintrag `<pfad>.loc`.
- Jede andere Datei geht unverändert ins Pack. Das gilt auch für eine Tabelle, die binär geblieben
  ist (`pack\db\<tabelle>\<name>` ohne Endung), und für eine `.tsv` an anderer Stelle.
- `twmt-project.json`: `schemaVersion`, `id` (GUID), `name`, `game` (Profil-ID), `packName`
  (Dateiname des Packs), `dependencies` (Pack-Namen für den Kopf des Packs). Weitere Felder kommen
  erst mit dem Plan, der sie braucht.
- Die Projektdatei liegt im Projekt und wandert mit Git. Die GUID ist die Identität, der Pfad ein
  Attribut.

### 5.2 Projektliste

`%APPDATA%\TotalWarModTool\games\<id>\projects.json`: bekannte Projekte (GUID, Pfad), das aktive
Projekt und pro Projekt der Fingerprint des installierten Packs.

- Projekt hinzufügen: neu anlegen, Pack importieren, bestehenden Projektordner öffnen.
- Projekt entfernen streicht nur den Eintrag. Solange für das Projekt ein installiertes Pack
  verzeichnet ist, ist Entfernen gesperrt: zuerst deinstallieren (Festlegungen aus Plan 3, Punkt 21).
- Ordner fehlt oder Projektdatei kaputt: Das Projekt bleibt in der Liste und zeigt das Problem. Es
  blockiert weder den Start noch die anderen Projekte.
- Zwei Einträge mit derselben GUID (kopierter Ordner): Der zweite wird beim Hinzufügen mit Meldung
  abgelehnt.

### 5.3 TSV-Dialekt

RPFM-Dialekt wie in 3.3 gemessen, mit diesen Festlegungen:

- Kodierung UTF-8 ohne BOM. Geschrieben wird `\n` als Zeilenende. Gelesen werden `\n` und `\r\n`
  (Git kann Zeilenenden umstellen).
- Kommazahlen in der kürzesten exakten Schreibweise, invariante Kultur.
- Wahrheitswerte `true` und `false`. Farbfelder (`ColourRGB`) als Hex: geschrieben mit mindestens
  6 Ziffern in Großbuchstaben (`RRGGBB`; 7 oder 8 Ziffern bei Werten über `FFFFFF`), gelesen mit 1 bis
  8 Ziffern.
- r/g/b-Dreiergruppen als eine Hex-Spalte, Regel wie bei RPFM.
- Geschrieben werden die Spalten in Schema-Reihenfolge, Farbgruppen am Ende. Gelesen wird per Name in
  jeder Reihenfolge.
- Die GUID steht nicht in der Datei. Beim Kodieren entsteht eine neue (wie bei RPFM, in S4 im Spiel
  geprüft).
- Eine Tabelle, die der Dialekt nicht verlustfrei trägt, wird nie als TSV geschrieben (siehe 5.5).

Lesen ist tolerant in der Form und streng im Inhalt: Eine Datei, die nicht zum Schema passt, ergibt
eine Diagnose mit Zeile und Grund, keine Ausnahme und keine stille Reparatur. Fälle: Metazeile fehlt
oder ist unlesbar, Tabelle oder Version nicht im Schema, Feldtyp nicht unterstützt, Spalte fehlt,
Spalte unbekannt, Spalte doppelt, Zeile mit falscher Zellenzahl, Wert passt nicht zum Typ.

Dokumentierter Hinweis für Nutzer: Ein TSV-Export aus RPFM rundet Kommazahlen auf 4 Stellen. Wer eine
Projektdatei damit überschreibt, baut die gerundeten Werte.

### 5.4 Module

| Modul | Stand | Aufgabe | Referenzen |
|---|---|---|---|
| `Tables` | erweitert | `DbTableEncoder`, `LocEncoder`, `TableTsv` (lesen und schreiben), `CellText` (die Zellregeln des Dialekts, gemeinsam für `TableTsv` und das Dokument). Dekodieren und Kodieren liegen in derselben Library. | `Schema` |
| `PackFiles` | erweitert | `PackWriter`: schreibt PFH5 als Stream. | – |
| `Projects` | neu | Projektdatei, Projektliste, Import, `ComputeBuild` und `WriteBuild`, Installieren, `ProjectFileWriter` (eine Datei unter `pack\` sicher schreiben, `Probe` und `Fingerprint` zum Prüfen). | `Storage`, `PackFiles`, `Schema`, `Tables`, `Operations` |
| `UndoRedo` | neu (Kopie aus PMT, Plan 4) | Globale Command-History (`Execute`, `Record`). | – |
| `Editing` | neu (Plan 4) | Tabellen-Dokument (`RowsDocument`): Zellen, Zeilen, Prüfung, Geändert-Zustand, Befehle; Zwischenablage-Text (`ClipboardRows`). | `Schema`, `Tables`, `UndoRedo` |
| `Workspace` | erweitert | Projektbaum als Quelle neben den Packs. | `Projects` |
| `App` | erweitert | Gruppe „Projekte“ im Explorer, Befehle, Dialoge, editierbares Raster, Textansicht. | alle |

Keine neuen NuGet-Pakete.

Der Dekoder liefert heute nicht alles, was der Encoder braucht (optionale Texte und Farben sind
schon Text, das reicht laut 3.1). Der Encoder nimmt das Modell des Dekoders (`DecodedTable`,
`LocRow`) und schreibt Version, Marker und GUID nach den Regeln aus 3.1.

#### Festlegungen aus Plan 1

1. Die Encoder nehmen das Modell des Dekoders. Die GUID liest `DbTableDecoder.ReadGuid`, der Import
   prüft damit byte-genau.
2. `TableTsv.Write` verweigert auch Texte mit `\r`.
3. `TableTsv.Read*` meldet nur das erste Problem.
4. `PackWriter.Write` schreibt genau den übergebenen Pfad. Temp-Datei und Umbenennen macht `Projects`.
   `PackWriter.Write` darf nie auf das lebende Pack zeigen, weil ein fehlgeschlagener Schreibvorgang
   seine Zieldatei löscht.
5. Die Encoder lehnen einen Text mit einem einzelnen Surrogat-Zeichen mit `ArgumentException` ab.
   `TableTsv.Write` schreibt eine solche Tabelle nicht, und `TableTsv.Read` meldet `InvalidValue`.
6. `PackWriter.Validate` behandelt zwei Pfade, die sich nur in Groß-/Kleinschreibung oder im Trenner
   unterscheiden, als Duplikat.
7. `PackWriter.Validate` lehnt Pfadsegmente `.` und `..`, einzelne Surrogat-Zeichen in Pfaden und
   Abhängigkeiten und Abhängigkeitsnamen über 65.535 Bytes ab.
8. `TableTsv.Read*` meldet `InvalidValue` für eine Kommazahl, die den Wertebereich sprengt (aus
   `1e39` wird nie still `Infinity`; nur die Texte `NaN`, `Infinity` und `-Infinity` ergeben diese
   Werte), und für ein `\r` mitten in einer Textzelle. Ein `\r` oder ein einzelnes Surrogat-Zeichen in
   der Metazeile ist `InvalidMetaLine`. Damit lässt sich alles, was `Read*` liefert, ohne Ausnahme
   wieder schreiben und kodieren. Eine sehr kleine Kommazahl wird wie jede Dezimalzahl auf den nächsten
   darstellbaren Wert gerundet, notfalls auf 0.
9. `TableTsv.Write` schreibt Farbfelder normalisiert (Großbuchstaben, mindestens 6 Ziffern). Es
   verweigert außerdem: `NaN` mit abweichendem Bitmuster, einen Farbkanal `-0`, einen Farbtext, der
   kein Hex ist, eine Definition ohne Spalten, einen Feldnamen mit Tab oder Zeilenumbruch und einen
   Spaltennamen, der doppelt vorkäme (ein echtes Feld `col_hex` neben `col_r`, `col_g`, `col_b`).
   Solche Tabellen bleiben beim Import binär.
10. Bekannte Grenze: In einer Tabelle mit nur einer Spalte ist eine leere letzte Zeile nicht von einem
    fehlenden Zeilenende zu unterscheiden. Entfernt ein Editor das letzte Zeilenende, geht diese eine
    leere Zeile verloren.
11. `PackWriter.Write` versucht das Löschen einer unvollständigen Zieldatei dreimal. Bleibt sie liegen,
    nennt `Detail` den Pfad. Nach einem Abbruch oder einer unerwarteten Ausnahme muss der Aufrufer
    selbst prüfen, ob die Datei weg ist.
12. Für Plan 2: Dateien werden streng als UTF-8 gelesen (ungültige Bytes sind ein Fehler, kein
    Ersatzzeichen). Tabellenname und Pfad der Metazeile werden mit der Lage der Datei im Projekt
    verglichen. Pfade aus fremden Packs werden vor `TableTsv.Write` auf Tab und Zeilenumbruch
    geprüft. Der Fortschritt von `PackWriter` trägt nur die Zahl der geschriebenen Einträge.

#### Festlegungen aus Plan 2

1. Der Papierkorb kommt mit Plan 5, weil erst das Löschen von Dateien ihn braucht.
2. `Projects` referenziert `GameProfiles` nicht. Der `data`-Ordner und die Vanilla-Packnamen kommen vom
   Aufrufer.
3. Der `packName` eines Imports wird vom Aufrufer vorgegeben (Vorschlag: Dateiname der Quelle) und gegen
   die Vanilla-Namen geprüft.
4. Die Lage einer Datei im Projekt bestimmt ihren Pack-Pfad. Der Tabellenname der Metazeile muss zum
   Ordner passen, der Pfad der Metazeile wird nicht geprüft.
5. Eine Projektdatei mit höherer `schemaVersion` wird nicht geladen.
6. Einträge über 1 GiB werden beim Import ausgelassen und gemeldet.
7. Jede Datei unter `pack\` geht ins Pack, auch Dateien wie `.gitkeep`.
8. Installieren gibt den neuen Fingerprint zurück; die App speichert ihn in der Projektliste. Scheitert
   dieses Speichern, gilt die Datei beim nächsten Mal als fremd.
9. Die Regeln 3 und 4 des Imports (Duplikat, Datei und Ordner gleichen Namens) vergleichen nur mit
   übernommenen Einträgen. Ein ausgelassener Eintrag belegt keinen Namen.
10. Kollidieren zwei Einträge erst nach der Umwandlung auf der Platte (die Tabelle `db/t/x` wird zur
    Datei `x.tsv`, ein anderer Eintrag braucht einen Ordner dieses Namens), wird der spätere
    ausgelassen und gemeldet. Die Vorschau kann deshalb etwas weniger ausgelassene Einträge nennen als
    der Import.
11. Leere oder ungültige Abhängigkeitsnamen eines fremden Packs werden beim Import verworfen und im
    Bericht genannt (`DroppedDependencies`).
12. Der Elternordner des Zielordners muss existieren. Ein relativer Zielordner ist ein Argumentfehler.
13. Lässt sich der Staging-Ordner nach einem Fehler nicht entfernen, nennt die Meldung ihn. Nach einem
    Abbruch muss der Aufrufer das selbst prüfen.
14. Ein Eintrag wird nur dann als Text gespeichert, wenn sich sein Dateiname im Projekt eindeutig auf
    seinen Pack-Pfad zurückführen lässt und höchstens 255 Zeichen hat; sonst bleibt er binär.
    Reservierte Gerätenamen umfassen auch `COM¹` bis `COM³`, `LPT¹` bis `LPT³`, `CONIN$` und `CONOUT$`.
15. `ProjectFile.Load` lehnt eine Projektdatei über 1 MB ab. `ProjectListEdits.SetInstalled` nimmt nur
    Fingerprints an, die das Speichern überstehen (gültiger Pack-Name, Länge nicht negativ, Zeit in UTC).
16. `PackInstaller` ist der einzige Code des Tools, der in den Spielordner schreibt. Er legt in `data`
    nur `<packName>` und `<packName>.twmt-tmp` an, ersetzt oder löscht sie, und nur nachdem
    `CheckOwnership` `None` ergab. Ein gleichnamiger Ordner gilt als fremd, ein relativer `data`-Ordner
    als nicht vorhanden. Die Vanilla-Prüfung vergleicht selbst ohne Rücksicht auf Groß-/Kleinschreibung.
    Eine übrig gebliebene `.twmt-tmp`-Datei eines abgestürzten Laufs wird beim nächsten Installieren
    überschrieben. Der Besitz wird einmal entschieden: Beim ersten Installieren wird ohne Überschreiben
    verschoben (taucht die Datei inzwischen auf, ist das Ergebnis `ForeignFile`), vor dem Ersetzen eines
    eigenen Packs wird erneut geprüft. Das verbleibende Fenster zwischen Prüfung und Ersetzen lässt sich
    unter Windows nicht schließen. Eine temp-Datei, die sich nach einem Fehler nicht entfernen lässt,
    nennt die Meldung; nach einem Abbruch muss der Aufrufer selbst prüfen, ob sie weg ist.
17. `ComputeBuild` meldet zwei Dateien mit gleichem Pack-Pfad selbst und nennt beide. Ein Ordner unter
    `pack\`, der eine Verknüpfung ist (Junction, symbolischer Link), wird nicht verfolgt und als Problem
    gemeldet. Ein nicht lesbarer Ordner wird mit seinem Namen gemeldet. Alte temp-Dateien eines
    abgebrochenen Baus räumt der nächste Bau weg. Für `pack\` und `build\` selbst siehe Festlegungen
    aus Plan 3, Punkt 19.
18. Ein Pack-Pfad über 259 Zeichen gilt als unsicher und wird beim Import ausgelassen. Er passt unter
    keinen Zielordner, und sehr tiefe Pfade eines fremden Packs könnten sonst den Speicher füllen
    (gemessen: 1,7 GB für einen Pfad mit 30.000 Segmenten).
19. Der Import legt Ordner in der Schreibweise des ersten Eintrags an. Liegen in einem Pack `Keep/a`
    und `keep/b`, stehen beide im Projekt unter `Keep\`, und das gebaute Pack trägt `Keep/a` und
    `Keep/b`. Inhalt und Zuordnung bleiben gleich, weil das Spiel Pfade ohne Rücksicht auf
    Groß-/Kleinschreibung liest.
20. Ein Pack-Name, der die Regeln nicht erfüllt, ist für `ComputeBuild` und `WriteBuild` ein
    Argumentfehler. `Install` meldet ein gebautes Pack, das sich nicht lesen lässt, als fehlend, und
    behandelt ein gebautes Pack, das selbst die Zieldatei ist, als Argumentfehler. Wurde das Pack installiert, sein Zustand
    danach aber nicht lesbar, meldet `Install` das eigens (`InstalledStateUnknown`); die Datei gilt
    dann beim nächsten Mal als fremd.
21. `twmt-project.json` darf mit BOM beginnen. Der Vanilla-Vergleich ignoriert Groß-/Kleinschreibung
    immer selbst, gleich welches Set der Aufrufer übergibt.
22. Für Plan 3: Meldungen zu Bau-Problemen, ausgelassenen Einträgen und binär gebliebenen Tabellen
    sind englischer Klartext ohne Art-Kennung (die App zeigt sie so an). `ProjectListStore.Save` lässt
    Dateifehler durch; eine Liste mit Status `Unreadable` oder `NewerVersion` darf nicht überschrieben
    werden. Wird der Pack-Name eines installierten Projekts geändert, deinstalliert die App zuerst
    unter dem alten Namen. Nach einem abgebrochenen Import sucht die App nach
    `<Zielordner>.import-*`. `Install` meldet keinen Fortschritt. Pfade ausgelassener Einträge
    stammen roh aus dem fremden Pack und können Steuerzeichen enthalten; die App maskiert sie vor
    der Anzeige.

#### Festlegungen aus Plan 3

1. `ProjectsController` und `ProjectsViewModel` liegen in der App. Der Controller liefert fertige
   Meldungstexte, das ViewModel fragt den Nutzer nur über `IProjectDialogs`.
2. Welche Dateien zu einem Projekt gehören, entscheidet `ProjectFiles.List` für Bau und Explorer
   gemeinsam. Versteckte Dateien, Systemdateien und Sperr- oder Sicherungsdateien von Editoren
   (`.~lock.*`, `~$*`, `*.bak`, `*.tmp`, `*~`) gehen nie ins Pack; der Bau nennt sie.
3. Tabs kennen eine allgemeine Quelle (`TabSource`). Ein Projekt-Tab hat den Schlüssel `project:<GUID>`
   und den relativen Dateipfad; so steht er auch in der Session, ohne neues Feld.
4. Die Gruppe „Projekte“ ist sichtbar, sobald ein Spielprofil existiert, auch wenn das Spiel nicht
   gefunden ist. Die Meldung des Explorers steht dann über dem Baum.
5. Die Vanilla-Packnamen für die Namensprüfung stammen aus dem Katalog (Herkunft Spiel). Ist das Spiel
   nicht gefunden, prüft erst das Installieren.
6. Wird der Pack-Name eines installierten Projekts geändert, deinstalliert „Bauen und installieren“
   zuerst das alte Pack.
7. Offene Projekt-Tabs zeigen den Stand beim Öffnen. Die Markierung für außen geänderte Dateien kam
   mit Plan 4 (siehe dort, Punkt 7 und 22).
8. Die Textansicht entsteht beim ersten Umschalten, höchstens 5 Mio. Zeichen.
9. `ProgressRunner` bricht die Arbeit ab, wenn das Fortschrittsfenster nicht geöffnet werden kann.
10. Beim Umbenennen des Pack-Namens läuft „Bauen und installieren“ in dieser Reihenfolge: Zuerst wird
    geprüft, ob der neue Name frei ist (`CheckOwnership`); eine fremde Datei unter dem neuen Namen oder
    ein anderer Grund gegen den Namen beendet den Lauf, bevor etwas entfernt wird. Danach wird das alte
    Pack entfernt. Ein Abbruch ist nur bis zu diesem Schritt spurlos möglich: Wird danach abgebrochen,
    endet der Lauf mit einem Ergebnis, das sagt, dass das alte Pack weg und das neue nicht installiert
    ist, damit der alte Eintrag nicht in der Liste bleibt. Eine zurückgebliebene temp-Datei wird in der
    Meldung genannt.
11. Ein `OperationOutcome` behält den Rückgabewert einer fertigen Arbeit auch dann, wenn das
    Fortschrittsfenster nicht geöffnet werden konnte (Status `Failed`, Wert gesetzt). Wer wissen muss,
    was die Arbeit getan hat, liest den Wert, nicht nur den Status.
12. `TooLarge` meldet `ProjectFileOpener` nur für Dateien, die wirklich gelesen werden. Eine Datei, die
    als „sonstige“ gezeigt wird, und eine Tabelle ohne Schema liefern nur ihre Größe, auch wenn sie
    über 256 MB groß sind.
13. Die Textansicht wird im UI-Thread gebaut, und die Grenze von 5 Mio. Zeichen greift erst nach dem
    Aufbau des Textes (Kürzung an einem Zeilenende). Das ist für große Tabellen nicht ideal (Backlog).
14. Der Explorer führt pro Projektknoten eine Listen-Generation. Ein stilles Neulisten ersetzt nie das
    Ergebnis einer neueren Auflistung und lässt Knoten in Ruhe, die zugeklappt, entfernt oder gerade
    im Aufklappen sind. Ein Projekt, das unbrauchbar wird, wird geschlossen.
15. Am Projektknoten gehen „Als aktiv setzen“ und „Aus der Liste entfernen“ auch während des stillen
    Ladens beim Start; sie brauchen keinen freien Runner. Während einer Operation über den Runner stellt
    sich die Frage nicht, weil das Hauptfenster dann gesperrt ist. Die übrigen Einträge sind deaktiviert
    und nennen den Grund, solange die App beschäftigt ist. „Aus der Liste entfernen“ hat zusätzlich einen
    eigenen Grund (Punkt 21).
16. Session: Der Projektteil der Wiederherstellung braucht wie der Pack-Teil einen Katalog. Wurde das
    Spiel nicht gefunden, wird nichts wiederhergestellt und die gespeicherte Session nicht
    überschrieben. Ein Projekt, das beim Start nicht benutzbar ist, verliert mit dem nächsten Speichern
    seine Tabs, wie ein fehlendes Pack. Ein wiederhergestellter Tab behält die gespeicherte Schreibweise
    seines Pfads. Gespeicherte Pfade, die absolut sind oder `..` enthalten, werden verworfen, ohne das
    Dateisystem zu berühren. Ein unerwarteter Fehler bei einer Projektdatei kostet nur diesen Eintrag.
17. Meldungen aus fremden Daten (Pfade, Gründe) maskieren Steuerzeichen, Bidi-Steuerzeichen sowie Zeilen-
    und Absatztrenner als `\uXXXX` (`DisplayText.Safe`). Ein Abschnitt eines Berichts zeigt höchstens
    1000 Zeilen; der Rest steht als eine Zeile mit der Anzahl. Projektnamen in Fortschrittstiteln und
    Log-Zeilen sowie technische Details der Libraries in Meldungen werden ebenso maskiert.
18. Dialog „Neues Projekt“ und Import: Der Pack-Name folgt dem Projektnamen, bis der Nutzer ihn selbst
    ändert. Eine abgelehnte Eingabe wird erneut abgefragt, mit der letzten Eingabe als Vorgabe.
19. Ist `pack\` oder `build\` eines Projekts selbst eine Verknüpfung (Junction, symbolischer Link), wird
    dort nichts geschrieben, gelöscht oder gelesen. `WriteBuild` meldet einen verknüpften `build\`-Ordner
    als Schreibfehler mit seinem Pfad, bevor es schreibt oder alte temp-Dateien aufräumt.
    `ProjectFiles.List` meldet einen verknüpften `pack\`-Ordner als ein Problem mit leerem relativen
    Pfad und listet nichts darunter; der Bau stoppt damit, der Explorer zeigt den Knoten `pack` mit dem
    Problem. `ProjectFileOpener` lehnt eine Datei, deren Weg von `pack\` bis zur Datei (einschließlich
    `pack\` selbst und der Datei) durch eine Verknüpfung führt, als nicht lesbar ab; das gilt auch für
    die Wiederherstellung der Session.
20. Darf die Projektliste in dieser Sitzung nicht gespeichert werden (Status `Unreadable` oder
    `NewerVersion`, beschädigt ohne `.bad`-Kopie), sind „Bauen und installieren“ und „Deinstallieren“
    deaktiviert und nennen den Grund; der Controller lehnt beides auch selbst ab. „Bauen“ bleibt möglich,
    weil es die Liste nicht ändert. Scheitert das Speichern erst, nachdem ein Pack installiert oder
    entfernt wurde (zum Beispiel ein Dateifehler), öffnet die App eine Meldung, die sagt, was im
    `data`-Ordner passiert ist und dass die Liste es nicht festhält, mit dem technischen Grund. Die
    Statuszeile trägt zusätzlich den Hinweis, dass die Liste nicht gespeichert wurde.
21. „Aus der Liste entfernen“ ist deaktiviert und wird abgelehnt, solange für das Projekt ein
    installiertes Pack verzeichnet ist; der Grund sagt „zuerst deinstallieren“. „Deinstallieren“ streicht
    den Eintrag auch dann, wenn die Datei in `data` schon von Hand gelöscht wurde. Folge: Ist das Spiel
    nicht gefunden (oder darf die Liste nicht gespeichert werden), lässt sich ein installiertes Projekt
    nicht entfernen. Der Nutzer stellt dann zuerst den Spielordner wieder her, deinstalliert und entfernt
    das Projekt danach. Darf die Liste nicht gespeichert werden, hilft erst eine neuere App-Version oder
    eine reparierte `projects.json`. Wurde das Pack in `data` seit dem Installieren verändert, lehnt
    „Deinstallieren“ ab und behält den Eintrag; der Nutzer löscht die Datei dann von Hand und
    deinstalliert erneut. In beiden Fällen nennt „Aus der Liste entfernen“ nur „zuerst deinstallieren“.
22. „Pack importieren“ im Menü und „Als Projekt importieren“ am Pack warten wie „Bauen“ auf das stille
    Laden beim Start und auf eine laufende Operation; der Tooltip nennt dann „beschäftigt“.
23. Schema fehlt (der Workspace hat geladen, aber kein Schema): „Bauen“ und „Bauen und installieren“
    sind deaktiviert, der Tooltip nennt den Grund, und der Controller lehnt den Bau ab, auch für ein
    Projekt ohne Tabellen. Vor dem ersten Laden gilt das Schema nicht als fehlend; die App ist dann
    ohnehin beschäftigt.
24. Verlässt ein Projekt die Liste, schließt die App seine offenen Tabs.

#### Festlegungen aus Plan 4

Plan 4 baut das Bearbeiten und Speichern von Tabellen und Loc-Dateien eines Projekts. Papierkorb,
Dateioperationen und „Ins Projekt übernehmen“ aus 5.6 und 5.8 sind Plan 5.

1. Die Zellregeln des Dialekts liegen einmal vor (`CellText` in `Tables`). `TableTsv` und das
   Dokument nutzen dieselben Regeln, damit alles, was das Dokument annimmt, sich als TSV schreiben und
   als derselbe Wert wieder lesen lässt.
2. Ein Dokument (`RowsDocument` in `Editing`) trägt Tabellen und Loc-Dateien. Eine Loc-Datei hat drei feste
   Spalten: `key`, `text` und `tooltip`.
3. „Geändert“ ist an den zuletzt angewandten Schritt gebunden, nicht an einen Zähler. Nimmt der Nutzer
   nach dem Speichern einen Schritt zurück, ist der Tab geändert; wiederholt er ihn, ist der Tab es
   nicht mehr.
4. Jedes Schließen eines bearbeitbaren Tabs entfernt dessen Schritte (Rückgängig und Wiederholen) aus der
   History, auch nach dem Speichern und auch, wenn ein Projekt die Liste verlässt. 5.8 nennt das nur für
   „Verwerfen“; ein geschlossenes Dokument gibt es nicht mehr.
5. Die Zwischenablage trägt Spaltennamen in der ersten Zeile und danach eine Zeile pro Eintrag, mit Tab
   getrennt und wie im Dialekt maskiert. Das ist das Format der App, nicht der RPFM-Dialekt: keine
   Metazeile, und Farbkanäle stehen einzeln (keine Hex-Spalte). Einfügen ordnet per Spaltenname zu.
6. Speichern schreibt nur Dateien unter `pack\` eines bekannten Projekts, nie durch eine Verknüpfung, und
   legt keine Ordner an. Fehlt der Ordner der Datei, scheitert das Speichern mit Meldung.
7. Änderungen von außen werden geprüft, wenn das Hauptfenster aktiv wird (nach einer Verzögerung von
   300 ms, eine Konstante), nach `F5` und vor dem Speichern. Die App lädt nie von selbst neu.
8. „Aus der Liste entfernen“ ist gesperrt, solange das Projekt ungespeicherte Änderungen hat; der
   Tooltip nennt den Grund (nach „unbekannt“ und „installiert“). „Bauen“ und „Bauen und installieren“
   fragen „alle speichern“ nur für Tabs des eigenen Projekts, gleich ob der Bau über das Menü, die
   Tastenkürzel oder den Explorer startet.
9. Im bearbeitbaren Raster ist Sortieren aus. Die Zeilen der Ansicht und des Dokuments müssen dieselben
   Indizes haben. Nur-Lese-Raster sortieren weiter.
10. Rückgängig in einer Zelle im Editiermodus ist das der Textbox. Der globale Schritt entsteht erst
    mit dem Bestätigen der Zelle.
11. Ein Zeilenumbruch `\r` in einer Zelle wird bei der Eingabe abgelehnt, weil der Dialekt dafür kein
    Escape hat. `CellText.TryParse` lehnt außerdem ein NUL, ein einzelnes Surrogat-Zeichen und die beiden
    wörtlichen Escape-Folgen (`\\t`, `\\n`) ab. Das Lesen vorhandener Dateien bleibt bei seiner
    bisherigen Toleranz: ein NUL in einer Datei wird weiter gelesen.
12. Die Regel für Farbkanäle (ganze Zahl von 0 bis 255, auch für Kommafelder) ist eine gemeinsame
    Prüfung (`CellText.IsColourChannelValue`). Das Dokument fragt über
    `RowsDocument.IsColourChannelColumn`, welche Spalten Farbkanäle sind.
13. Wirft ein Handler von `RowsDocument.Changed` während eines Schritts, nimmt das Dokument den Schritt
    zurück und reicht die Ausnahme weiter; der Schritt gilt dann als nicht ausgeführt. Ein Schritt kann
    keine Zeile einfügen, die schon im Dokument liegt (`InsertRows` lehnt dieselbe Zeile zweimal ab).
    Kommazahlen müssen genau das sein, was die Textform trägt (kein `NaN` mit abweichendem Bitmuster).
14. Zwischenablage, Grenzen: Kopieren lehnt Zeilen ab, die das Format nicht tragen kann (Meldung mit Zeile
    und Spalte), und kopiert höchstens 100.000 Zeilen. Eine Tabelle mit zwei Spalten gleichen Namens lässt
    sich weder kopieren noch einfügen. Ein BOM am Anfang wird ignoriert. Zellen in Anführungszeichen, wie
    Tabellenprogramme sie schreiben, werden wörtlich übernommen. Namen verworfener Spalten erscheinen in
    der Meldung gekürzt. Die Zwischenablage kennt die Farbkanal-Spalten nicht: Ein Packwert außerhalb
    von 0 bis 255 wird kopiert, beim Einfügen aber abgelehnt. Kopieren läuft nicht über die
    Fortschritts-Komponente (im Speicher, höchstens 100.000 Zeilen). Beim Einfügen nennt eine Meldung
    fehlende und verworfene Spalten; die nächste Änderung des Dokuments löscht sie.
15. `ProjectFileWriter`: Die strengen Pfadregeln liegen in `PackFolderPaths.ResolveForWrite`. Die
    temp-Datei wird nie über eine vorhandene Datei geschrieben. Ein Ordner auf dem Weg, dessen Attribute
    sich nicht lesen lassen, gilt für Schreiben als Verknüpfung. Ein Text, der kein gültiges UTF-16 ist,
    wird abgelehnt. `Probe` unterscheidet „vorhanden“, „fehlt“ und „nicht prüfbar“.
16. Bearbeitbarer Tab: Kommt eine Änderung, während die Textansicht zeigt (das geht nur über Rückgängig
    und Wiederholen), schaltet der Tab auf das Raster zurück; der Text entsteht beim nächsten Umschalten
    neu. Ändert ein Schritt mehr als 2.000 Zeilen, wird das Raster in einem Durchgang neu aufgebaut
    statt Zeile für Zeile.
17. Der `DocumentsController` besitzt die Tab-Liste; `MainViewModel` übernimmt sie von dort. Wiederhergestellte
    Projekt-Tabs der Session sind bearbeitbar.
18. Speichern: Nur das Bauen des Textes und das Schreiben laufen im Hintergrund, über die
    Fortschritts-Komponente; „Alle speichern“ ist ein Lauf. Ein Abbruch zwischen zwei Dateien lässt die
    schon geschriebenen Dateien gespeichert. Unmittelbar vor jedem Schreiben wird die Datei erneut gegen
    den Stand geprüft, den der Nutzer gesehen hat oder zu dem er gefragt wurde; hat sie sich inzwischen
    geändert, wird sie nicht geschrieben, der Tab bleibt geändert und die Meldung nennt die Datei.
    „Nicht prüfbar“ ist ein eigener Fall und gilt nie als gelöscht: Ohne bekannten Stand wird dann nicht
    geschrieben. Eine Meldung nennt höchstens 10 Dateien. Speichern und Alle speichern (Menü „Projekt“,
    `Strg+S`, `Strg+Umschalt+S`) sind gesperrt, solange eine Operation läuft.
19. Eine Zelle, die noch im Editiermodus ist, wird vor „Speichern“, „Alle speichern“, dem Schließen eines
    Tabs, dem Schließen der App, dem Bauen, Rückgängig und Wiederholen, dem Verlassen des Tabs, dem
    Umschalten auf die Textansicht und dem Neu laden bestätigt (zu den letzten drei die Punkte 24 und 26),
    ebenso vor dem Öffnen des Kontextmenüs. Ein ungültiger Wert hält die Aktion mit einer Meldung an. Eine
    getippte Zelle in einem sonst unveränderten Tab gilt als „etwas zu speichern“. Eine geöffnete, aber
    nicht geänderte Zelle gilt nicht als Änderung: nichts zu speichern, kein Schritt.
20. `Strg+Z` und `Strg+Y` in einer Zelle im Editiermodus wirken nur in der Zelle und erreichen nie die
    globale History. Außerhalb davon sind Rückgängig und Wiederholen gesperrt, solange eine Operation
    läuft. Scheitert ein Schritt beim Zurücknehmen, fällt er aus der History, und eine Meldung nennt
    den Grund.
21. Schließen: Ein geänderter Tab fragt asynchron; eine zweite Schließanfrage, solange die Frage offen
    ist, wird ignoriert. Beim Schließen der App fragt die App einmal für alle geänderten Tabs. Die Frage
    zeigt höchstens 10 Dateien.
22. Änderungen von außen, Einzelheiten: Pro offenem Projekt-Tab (bearbeitbar und nur lesbar) steht ein
    Fingerprint, genommen beim Anlegen des Tabs. Weicht die Dateigröße beim Öffnen von der Größe des
    Eintrags ab, ist der Tab sofort als geändert markiert und trägt keinen Fingerprint. Die Prüfung
    liest nichts, sie fragt nur Größe und Zeit ab; sie läuft nicht, solange eine Operation läuft. Eine
    Datei, die sich nicht prüfen lässt, bekommt keine Markierung. Die Markierung „gelöscht“ oder
    „geändert“ hat Vorrang vor den Markierungen für Pack und Schema. Der Knopf „Neu laden“ erscheint nur
    bei einem bearbeitbaren, ungeänderten Tab mit Markierung von außen; Nur-Lese-Projekt-Tabs bekommen
    nur die Markierung. Neu laden entfernt die Schritte des alten Dokuments aus der History und lässt
    für eine gelöschte Datei an derselben Stelle den Hinweis-Tab erscheinen. Bei der Rückfrage
    „Überschreiben, Neu laden, Abbrechen“ ist Abbrechen die Vorgabe, `Enter` überschreibt nie.
23. Die Zeilenkopfzelle zeigt die Zeilennummer (ab 1). Nach einem Zeilenbefehl (Einfügen, Duplizieren,
    Einfügen aus der Zwischenablage, Löschen) sind die betroffenen Zeilen markiert, bei mehr als 2.000
    Zeilen nur die erste. Ein anderer Tab beginnt ohne Markierung.
24. Ein getippter Wert geht beim Verlassen eines Tabs nie still verloren. Das gilt für den Klick auf
    einen anderen Tab, für die Tasten (`Strg+Tab`, auch mit Umschalt, `Strg+Bild auf` und `Strg+Bild ab`;
    Pfeiltasten, `Pos1` und `Ende`, solange die Kopfzeile eines Tabs den Fokus hat) und für den Wechsel
    durch die App selbst (Rückgängig und Wiederholen zeigen den Tab der Änderung, ein geöffneter Eintrag
    wird ausgewählt). Ein gültiger Wert wird vorher geschrieben, als ein Schritt der History. Ein ungültiger
    Wert hält den Nutzer im Tab: die Zelle bleibt im Editiermodus, und die Meldung zur Zelle steht unter
    der Kopfzeile; bei Rückgängig, Wiederholen und dem Wechsel zu einem anderen Tab kommt ein Dialog
    „Ungültiger Wert in einer Zelle“ dazu, beim Umschalten auf die Textansicht nur die Zeile unter der
    Kopfzeile, und die Ansicht springt zurück auf das Raster.
25. Raster, Einzelheiten: Eine Zelle mit Kästchen (Wahrheitswert) beendet ihr Bearbeiten gleich nach dem
    Umschalten. Ein Rechtsklick bestätigt zuerst eine Zelle im Editiermodus (ein ungültiger Wert lässt den
    Klick wirkungslos) und markiert dann die Zeile unter der Maus, wenn sie nicht markiert ist; ein
    Rechtsklick auf die Zelle im Editiermodus selbst gehört deren Textfeld mit seinem eigenen Menü.
26. Entfernt die App das Raster selbst (ein Tab wird ohne Rückfrage geschlossen oder durch einen anderen
    ersetzt) oder ändert sie die Zeilen unter der Zelle, kann die Zelle nicht mehr gefragt werden: Ein
    gültiger Text, dessen Zeile noch da ist, wird geschrieben; ein ungültiger Text oder eine Zeile, die
    weg ist, verwirft ihn, und ein Hinweis im Tab nennt das. „Neu laden“ über den Knopf bestätigt eine
    getippte Zelle zuerst: ein gültiger Wert macht den Tab geändert, damit wird nicht neu geladen (die
    Rückfrage beim nächsten Speichern ist der Weg); ein ungültiger Wert hält das Neu laden mit der Meldung
    an. Tippt der Nutzer, während die Datei gelesen wird, bleibt der Tab unverändert stehen. Wählt der
    Nutzer in der Rückfrage beim Speichern „Neu laden“, ist das gewollt: eine noch offene Zelle wird vor
    dem Ersetzen des Dokuments ohne Hinweis verworfen, damit sie nicht in das neue Dokument schreibt.
27. Einfügen läuft im UI-Thread und ist auf 20.000.000 Zeichen und 100.000 Zeilen begrenzt (die Zeilenzahl
    des Kopierens). Beides wird geprüft, bevor der Text gelesen wird; die Zeilen werden dafür nur gezählt.
    Ein größerer Text wird mit einer Meldung abgelehnt und ändert nichts.
28. Werkzeugleiste: Unter dem Hauptmenü steht, nur wenn die App Dokumente hat, eine Leiste mit
    Textknöpfen in dieser Reihenfolge: Speichern, Alle speichern | Rückgängig, Wiederholen | Zeile
    einfügen, Zeilen duplizieren, Zeilen löschen | Kopieren, Einfügen. Speichern und Rückgängig
    nutzen die Befehle des Menüs. Die Zeilenknöpfe wirken auf den gewählten Tab (`RowCommandsViewModel`):
    in einem bearbeitbaren Tab alle fünf, in einer Nur-Lese-Tabelle nur Kopieren, sonst keiner. Ein
    gesperrter Zeilenknopf nennt den Grund in seinem Tooltip („Wähle mindestens eine Zeile“, „Diese
    Tabelle ist nur lesbar“ oder „Öffne zuerst eine Tabelle“); ein freigegebener nennt die Aktion mit ihrer
    Taste. Die Knöpfe nehmen den Fokus nicht an, damit das Raster seine Auswahl und seine Tasten
    behält; deshalb bestätigt ein Zeilenknopf eine noch offene Zelle selbst, bevor er den Befehl
    ausführt (wie das Kontextmenü, Punkt 19), und ein ungültiger Wert hält den Befehl an. Die Leiste
    bricht in eine zweite Zeile um, wenn das Fenster schmal ist.

### 5.5 Import

Ablauf: Quelle wählen (Pack aus dem Explorer oder freie `.pack`-Datei), Zielordner wählen (leer oder
neu), Vorschau (Zahl der Tabellen, Loc-Dateien, sonstigen Dateien, Gesamtgröße), Bestätigen.

- Läuft über die Fortschritts-Komponente, abbrechbar.
- Geschrieben wird in einen Staging-Ordner neben dem Ziel, am Ende Umbenennen. Abbruch oder Fehler
  löscht das Staging (`finally`). Es gibt kein Teilprojekt.
- Pro Eintrag:
  - Tabelle dekodierbar: als TSV. Vorher prüft der Import, dass TSV → binär den Originalbytes
    gleicht (ohne GUID). Sonst bleibt der Eintrag binär.
  - Loc: als `.loc.tsv`, gleiche Prüfung.
  - Tabelle nicht dekodierbar (nicht im Schema, Version fehlt, Feldtyp nicht unterstützt, Fehler):
    binär.
  - Alles andere: Bytes unverändert, komprimierte Einträge entpackt.
  - Nicht lesbar (LZMA, verschlüsselt, beschädigt): Der Eintrag fehlt im Projekt, der Bericht nennt
    ihn. Der Import bricht deshalb nicht ab.
- Pfade, die unter Windows nicht gehen oder gefährlich sind: Eintrag auslassen und melden. Dazu
  zählen `..`, absolute Pfade, reservierte Namen, verbotene Zeichen, zu lange Pfade und zwei Pfade,
  die sich nur in Groß-/Kleinschreibung unterscheiden (der erste gewinnt).
- Ein Eintrag, dessen Name mit einer Projektregel kollidiert (im Pack heißt er schon
  `db/<tabelle>/<name>.tsv` oder `….loc.tsv`), wird ausgelassen und gemeldet. Im Projekt wäre er
  nicht von einer Tabelle in Textform zu unterscheiden.
- Die Abhängigkeiten des Packs gehen in die Projektdatei. `packName` gibt der Aufrufer vor (Vorschlag:
  Dateiname der Quelle); er wird gegen die Vanilla-Namen geprüft.
- Am Ende steht der Import-Bericht: binär gebliebene Tabellen mit Grund, ausgelassene Einträge.

### 5.6 „Ins Projekt übernehmen“

Kommt mit Plan 5 (Abschnitt 9). Ein Eintrag aus Vanilla oder fremder Mod wird in das aktive Projekt kopiert, mit denselben Regeln pro
Eintrag wie beim Import.

- Tabellen und Loc: Ein Dialog fragt nach dem neuen Dateinamen. Vorschlag ist der Projektname.
- Gleicher Name wie in Vanilla ist erlaubt, braucht aber eine ausdrückliche Bestätigung mit dem
  Hinweis, dass die Datei dann die ganze Vanilla-Tabelle ersetzt.
- Ziel existiert: Abbruch mit Meldung.
- Ein Undo-Schritt (die Datei wandert in den Papierkorb des Tools).

### 5.7 Bauen und Installieren

**Bauen**

- `ComputeBuild` ist pur: Es geht `pack\` durch und liefert die Eintragsliste und alle Probleme.
  `WriteBuild` schreibt.
- Gebaut wird der Stand auf der Platte. Gibt es ungespeicherte Änderungen im Projekt, fragt die App
  vorher: alle speichern oder abbrechen.
- Jedes Problem stoppt den Bau vor dem Schreiben. Die Meldung nennt Datei und, wo möglich, Zeile:
  TSV nicht lesbar (Diagnosen aus 5.3), zwei Dateien mit demselben Pack-Pfad, Pfad über 65.535 Bytes,
  Datei oder Index über der 32-Bit-Grenze des Formats, Datei nicht lesbar.
- Geschrieben wird `build\<packName>.<8 Hex>.tmp`, dann Umbenennen. Das alte Pack bleibt bis zum
  erfolgreichen Ende erhalten.
- Format: PFH5, Bitmaske `0x3`, Abhängigkeiten aus der Projektdatei, Einträge ordinal nach Pfad
  sortiert, unkomprimiert, Pfade mit `\`.
- Keine Vorschau und keine Nachfrage: Bauen trifft nur `build\` und ist wiederholbar.
- Über die Fortschritts-Komponente, abbrechbar. Abbruch löscht die temp-Datei.

**Installieren**

- „Bauen und installieren“ kopiert `build\<packName>` nach `data\<packName>` (temp-Datei im selben
  Ordner, dann Umbenennen).
- Erlaubt nur, wenn in `data` keine Datei dieses Namens liegt, oder wenn die dortige Datei vom Tool
  für dieses Projekt installiert wurde (Fingerprint aus der Projektliste stimmt).
- Sonst Abbruch mit Meldung, die den Grund nennt. In Teil 2 gibt es kein „trotzdem überschreiben“.
- Ein `packName`, der im Vanilla-Manifest steht oder einem Vanilla-Pack in `data` gleicht, ist immer
  gesperrt. Das prüft schon das Anlegen und Umbenennen des Projekts. Die Library prüft es beim
  Import und beim Installieren; beim Anlegen und Umbenennen prüft die App mit
  `PackNameRules.Check(name, vanillaNames)` (Plan 3).
- „Deinstallieren“ löscht die Datei aus `data`, unter derselben Bedingung.
- Das Spiel sperrt die Datei: Meldung mit Hinweis, kein Teilzustand.
- Kein Spielordner bekannt: Der Befehl ist deaktiviert, der Tooltip nennt den Grund. Ebenso
  „Bauen und installieren“ und „Deinstallieren“, solange die Projektliste in dieser Sitzung nicht
  gespeichert werden darf (Festlegungen aus Plan 3, Punkt 20).

### 5.8 Bearbeiten

**Dokument (`Editing`)**

- Das Öffnen einer Projekt-TSV lädt sie in ein Dokument mit typisierten Zellen.
- Befehle: Zelle setzen, Zeilen einfügen, Zeilen löschen, Zeilen duplizieren. Jeder Befehl ist ein
  Undo-Schritt. Ein Einfügen vieler Zeilen ist ein Schritt.
- Prüfung bei der Eingabe pro Feldtyp: ganze Zahl im Wertebereich, Kommazahl, Wahrheitswert, Farbe
  als Hex, Farbkanal 0 bis 255. Ungültig: Die Zelle bleibt im Editiermodus, die Meldung nennt den
  erwarteten Typ.
- Ein Text, der wörtlich `\\t` oder `\\n` enthält, wird mit Meldung abgelehnt (Grenze des Dialekts).
- Eine TSV mit Diagnose (5.3) öffnet als Hinweis-Tab mit Zeile und Grund und ist nicht bearbeitbar.
- Binär gebliebene Tabellen öffnen wie im Fundament (nur lesen, oder Hinweis).

**Kopieren und Einfügen**

- Kopieren aus jedem Raster legt TSV mit Spaltennamen in die Zwischenablage (Format der App, siehe
  Festlegungen aus Plan 4, Punkt 5 und 14).
- Einfügen in ein Projekt-Raster ordnet per Spaltenname zu. Fehlende Spalten bekommen den
  Standardwert des Typs, überzählige entfallen. Eine kurze Meldung nennt beides.
- Zeilen mit ungültigen Werten: Das ganze Einfügen wird mit Meldung abgelehnt.
- Eine neue Tabelle entsteht in der neuesten Version des Schemas. Importierte behalten ihre Version.

**Undo**

- Eine globale History für die App. `Strg+Z` nimmt den letzten Schritt zurück und wechselt zum
  betroffenen Tab. `Strg+Y` stellt wieder her.
- Die History lebt bis zum Schließen der App, auch über das Speichern hinweg.
- Dateioperationen stehen in derselben History: Tabelle anlegen, Datei löschen, umbenennen, „Ins
  Projekt übernehmen“.
- Jedes Schließen eines Tabs entfernt dessen Schritte aus der History (Festlegungen aus Plan 4, Punkt 4).

**Speichern**

- `Strg+S` speichert den Tab, „Alle speichern“ alle. Atomar: temp-Datei, Umbenennen, Retry.
- Geänderte Tabs tragen eine Markierung.
- Kein Snapshot beim normalen Speichern. Undo und Git decken das ab.
- Tab oder App schließen mit ungespeicherten Änderungen: Speichern, Verwerfen, Abbrechen.

**Dateioperationen im Projekt** (Plan 5)

- Neue Tabelle (Tabelle aus dem Schema wählen, Dateiname), neue Loc-Datei, Datei umbenennen, Datei
  löschen.
- Löschen verschiebt die Datei nach `%LocalAppData%\TotalWarModTool\trash\<Projekt-GUID>\`. Undo holt
  sie zurück. Beim Start kürzt das Tool den Papierkorb auf die letzten 30 Tage.
- Löschen ist damit reversibel und fragt nicht nach.

**Änderungen von außen**

- Fingerprint (Pfad, Größe, mtime) pro offener Projektdatei. Geprüft wird, wenn die App den Fokus
  bekommt, und vor dem Speichern.
- Datei außen geändert, Tab ohne eigene Änderungen: Markierung „veraltet“, Neuladen per Klick.
- Datei außen geändert, Tab mit eigenen Änderungen: Speichern fragt: Überschreiben, Neu laden,
  Abbrechen. Nie still überschreiben.
- Der Projektbaum im Explorer folgt „Aktualisieren“ (`F5`), wie die Packs.

### 5.9 App

- Explorer: Gruppe „Projekte“ über den Packs. Jedes Projekt ist ein Knoten mit dem Baum von `pack\`.
  Das aktive Projekt ist hervorgehoben.
- Kontextmenü am Projekt: Als aktiv setzen, Bauen, Bauen und installieren, Deinstallieren, Aus der
  Liste entfernen. Am Pack: Als Projekt importieren. Am Eintrag: Ins Projekt übernehmen (Plan 5).
- Menü: Neues Projekt, Projekt öffnen, Pack importieren, Bauen (aktives Projekt). Speichern, Alle
  speichern stehen seit Plan 4 im Menü „Projekt“, Rückgängig und Wiederholen im Menü „Bearbeiten“
  (zwischen „Werkzeuge“ und „Projekt“). Zeilenbefehle und Kopieren und Einfügen liegen im
  Kontextmenü des bearbeitbaren Rasters und auf Tastenkürzeln (`Einfg`, `Entf`, `Strg+D`, `Strg+C`,
  `Strg+V`).
- Tabellen-Tab: Umschalter „Raster“ und „Text“. Die Textansicht zeigt die TSV-Form, nur lesen,
  markieren, kopieren. Sie gilt für alle Quellen.
- Session: Tabs von Projektdateien werden wie Pack-Tabs wiederhergestellt (Projekt-GUID und Pfad).
- Alle Texte über `AppTexts`, in `en` und `de`.
- Aus dem Backlog vorgezogen, weil jetzt Schreiboperationen laufen: `ProgressRunner` bricht die
  Arbeit ab, wenn das Fortschrittsfenster nicht geöffnet werden kann.

---

## 6. Verworfene Alternativen

| Entscheidung | Verworfen | Grund |
|---|---|---|
| Projektordner frei wählbar | Stammordner mit Unterordnern; fester Ordner unter `%APPDATA%` | Git und Backups brauchen freie Lage. |
| Bauen in den Projektordner, Installieren getrennt | Direkt nach `data` bauen | Ein abgebrochener Bau träfe den Spielordner. Kein Exemplar im Projekt. |
| | Nur Projektordner, Kopieren von Hand | Jeder Testlauf braucht einen Handgriff. |
| TSV im RPFM-Dialekt | Eigenes TSV | Kein Austausch mit RPFM, keine Gegenprobe mit fremdem Werkzeug. |
| | `name = wert` pro Feld | Sehr große Dateien, weicht von „eine Zeile pro Eintrag“ ab. |
| Kommazahlen exakt statt 4 Stellen | Genau wie RPFM runden | Gemessen verlustbehaftet (80 Vanilla-Tabellen). |
| GUID beim Bauen neu | GUID in der Metazeile oder in einer Begleitdatei | Zusatz in der Metazeile gefährdet RPFM-Kompatibilität. Begleitdateien sind eine zweite Wahrheit. Das Spiel braucht die alte GUID nicht (S4). |
| Nicht verlustfreie Tabelle bleibt binär | Eigene Erweiterung des Dialekts | Betrifft gemessen 1 Vanilla-Tabelle. |
| Bearbeiten im Raster | Editierbare Textansicht | Braucht Editor-Komponente und Abgleich zweier Ansichten. Kommt mit Objekt-Ansicht und Lua-Editor. |
| Ausdrückliches Speichern | Automatisches Speichern | Jeder Tippfehler stünde sofort in Datei und Git-Status. |
| Mehrere Projekte, eines aktiv | Genau ein Projekt offen; kein aktives Projekt | Konzept 1c verlangt mehrere Mods. Ohne aktives Projekt fragt jede Aktion nach dem Ziel. |
| Unkomprimiert schreiben | Kompression | Nicht nötig für die Schleife, eigenes Risiko. Backlog. |
| Papierkorb des Tools | Windows-Papierkorb | Undo muss die Datei sicher zurückholen können. |

---

## 7. Fehlerfälle

| Fall | Verhalten |
|---|---|
| Projektordner fehlt oder ist nicht lesbar | Projekt bleibt in der Liste mit Problemtext. Befehle des Projekts sind deaktiviert. |
| `twmt-project.json` kaputt | Wie oben. Die Datei wird nicht überschrieben und nicht nach `.bad` kopiert (sie gehört dem Nutzer und liegt in Git). |
| `projects.json` kaputt | Nach `.bad` kopieren, mit leerer Liste weiter. |
| Zielordner des Imports nicht leer | Ablehnen mit Meldung. |
| Import abgebrochen oder Fehler | Staging löschen, kein Projekt, Meldung. |
| Platte voll beim Import, Bauen, Speichern | Temp-Datei löschen, alter Stand bleibt, Meldung. |
| TSV passt nicht zum Schema | Hinweis-Tab mit Zeile und Grund. Bauen stoppt mit derselben Diagnose. |
| Schema fehlt | Projekt-Tabellen sind nicht bearbeitbar, Bauen ist deaktiviert, Tooltip nennt den Grund. |
| Datei beim Speichern gesperrt | Retry, dann Meldung. Der Tab bleibt „geändert“. |
| Datei außen gelöscht, Tab offen | Markierung „veraltet“. Speichern legt sie nach Rückfrage neu an. |
| Pack in `data` gehört nicht dem Tool | Installieren und Deinstallieren brechen mit Meldung ab. |
| `packName` gleicht einem Vanilla-Pack | Beim Anlegen, Umbenennen und Installieren abgelehnt. |
| Spiel sperrt das Pack in `data` | Meldung, alte Datei bleibt. |
| Undo einer Dateioperation scheitert (Papierkorb-Datei fehlt) | Meldung, der Schritt bleibt in der History nicht erhalten, kein Teilzustand. |
| Zwei Dateien im Projekt ergeben denselben Pack-Pfad | Bauen stoppt und nennt beide. |

---

## 8. Tests

- Libraries test-first. Ein Testprojekt pro neuem Modul (`Projects`, `Editing`, `UndoRedo`).
- `TableTsv`: jeder Feldtyp, Escapes, Farbgruppen, Spaltenreihenfolge, `\r\n`, jede Diagnose.
- Encoder: Round-Trip gegen den Dekoder für jeden Feldtyp, Kopf mit und ohne Versionsmarker.
- `PackWriter`: lesbar mit `PackReader`, Sortierung, Abhängigkeiten, Grenzen (Pfadlänge, 32 Bit),
  Abbruch hinterlässt keine Datei.
- `Projects`: Import in Temp-Ordner (Staging, Abbruch, gefährliche Pfade, Namenskollisionen), Bauen
  (jedes Problem), Installieren (jede Bedingung) mit einem Temp-Ordner als `data`.
- `Editing`: jeder Befehl mit Undo und Redo, Prüfung pro Typ, Einfügen per Spaltenname.
- Echtdaten-Tests, übersprungen ohne Fixture, Invarianten statt fester Zahlen:
  - Round-Trip aller Tabellen und Loc-Dateien aus `db.pack` und `local_en.pack`.
  - Import und Neubau eines Workshop-Packs: inhaltsgleich.
  - `rpfm_cli` importiert unser TSV inhaltsgleich und packt unser Pack aus.
- App: ViewModels mit Fakes, Raster und Dialoge über den STA-Helper.
- Pro Plan eine manuelle Checkliste für den Nutzer.
- Die bestehende Suite (665 Tests) bleibt grün.

---

## 9. Pläne

1. **Schreib-Libraries:** `TableTsv`, `DbTableEncoder`, `LocEncoder`, `PackWriter`.
2. **`Projects`:** Projektdatei, Projektliste, Import, Bauen, Installieren. Ohne UI.
3. **App, erster Teil:** Projekte im Explorer, Import, Bauen, Installieren, Textansicht, Session.
   Danach ist nutzbar: Mod importieren, außen bearbeiten, bauen, installieren.
4. **Bearbeiten:** `UndoRedo`, `Editing`, editierbares Raster, Speichern, Änderungen von außen.
   Danach ist nutzbar: importieren, im Raster ändern, speichern, bauen, installieren.
5. **Dateioperationen:** Papierkorb, neue Tabelle und Loc-Datei, umbenennen, löschen, „Ins Projekt
   übernehmen“. Baut auf History, Dokument und Speichern aus Plan 4 auf.

Jeder Plan wird erst geschrieben, wenn die Schnittstellen des vorigen existieren.

---

## 10. Annahmen, die der erste echte Lauf prüft

1. Ein großes importiertes Mod (tausende Dateien) bleibt im Explorer und in Git handhabbar.
2. Das Spiel lädt ein gebautes Pack mit vielen Tabellen, Loc-Dateien und sonstigen Dateien, nicht nur
   das kleine Testpack aus S4.
3. Die Fokus-Prüfung auf Änderungen von außen stört bei vielen offenen Tabs nicht.
4. Das editierbare Raster bleibt mit dem App-Theme und Spaltenvirtualisierung so schnell wie im
   Spike.
5. Der Fingerprint reicht, um das eigene installierte Pack sicher zu erkennen (Steam oder ein
   Mod-Manager fassen die Datei nicht an).
6. Eine globale Undo-History über mehrere Tabs ist im Alltag verständlich.
7. Git mit `autocrlf` verändert die TSV-Dateien nicht so, dass das Lesen scheitert.

---

## 11. Entscheidungen des Nutzers (2026-10-03)

- Projektordner frei wählbar pro Projekt.
- Teil 2 schlank, danach Objekt-Ansicht, dann Error-Log und Spielstart.
- Bauen in den Projektordner, Installieren als eigener Schritt mit Eigentumsprüfung.
- TSV im RPFM-Dialekt. Hinweis zum RPFM-Export nur in der Doku, Erkennung im Backlog.
- Bearbeiten im Raster, Textansicht nur lesen, auch für fremde Mods zum Kopieren.
- Ausdrückliches Speichern.
- Import ganzer Packs und einzelner Einträge. Erneuter Import über ein Projekt später.
- Mehrere Projekte sichtbar, eines aktiv.
