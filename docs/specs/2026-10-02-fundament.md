# Spec: Fundament (Packs und Tabellen lesen, App-Gerüst)

Stand: 2026-10-02. Status: vom Nutzer am 2026-10-02 freigegeben.
Pläne: `docs/plans/2026-10-02-fundament-plan-1-geruest.md` (Gerüst, umgesetzt),
`docs/plans/2026-10-02-fundament-plan-2-lese-libraries.md` (Lese-Libraries).

Diese Spec ist die erste von mehreren. Sie beschreibt das Fundament, auf dem alle späteren Teile
aufbauen. Abschnitt 1 hält das Gesamtbild und die Architekturentscheidungen fest, die für alle Teile
gelten. Ab Abschnitt 2 geht es nur noch um das Fundament.

Kennzeichnung in diesem Dokument:

- **gemessen** = am 2026-10-02 auf dem Rechner des Nutzers per Spike ermittelt.
- **zu messen** = Annahme ohne Beleg. Sie wird im genannten Plan geprüft, bevor darauf gebaut wird.

---

## 1. Gesamtbild

### 1.1 Zweck des Tools

Ein Desktop-Werkzeug für das Modding von Total-War-Spielen. Es ist ähnlich wie der Rusted Pack File
Manager (RPFM), arbeitet aber objektzentriert: Alle Tabellenzeilen, die zusammen ein Gebäude, eine
Einheit oder einen Trait ergeben, erscheinen in einer Ansicht. Dazu kommen Komfortfunktionen aus dem
Paradox Modding Tool (PMT): Session, Tabs, Suche, Diff und Merge, Versionen, Error-Log.

Erstes Zielspiel ist Total War: WARHAMMER III (WH3). Weitere Spiele kommen über Spielprofile dazu.
Das Tool wird von Anfang an für eine öffentliche Veröffentlichung gebaut.

### 1.2 Zerlegung in Teile

| Nr. | Teil | Status |
|---|---|---|
| 1 | Fundament: Spielprofil, Packs lesen, Tabellen dekodieren, App mit Tabs und Session (nur lesen) | diese Spec |
| 2 | Arbeitsordner, Import bestehender Packs, Tabellen bearbeiten, Pack bauen | Backlog |
| 3 | Error-Log, Spielstart | Backlog |
| 4 | Objekt-Ansicht als Codeblock, Katalog (Effekte usw.), Autovervollständigung | Backlog |
| 5 | Lua-Editor | Backlog |
| 6 | Vanilla-Tracking, Diff, Merge, Kompatibilitätsprüfung | Backlog |
| 7 | Versionsmanager, Snapshots | Backlog |
| 8 | Workshop-Anbindung, GitHub, Steam-Upload | Backlog |
| 9 | Bug-Reports aus Discord und Steam | Backlog |

Dazu kommen eigene Specs für Release-Tool und Self-Update (früh, weil öffentlich).

Begründung der Reihenfolge: Die Teile 1 bis 3 ergeben zusammen die tägliche Schleife „ändern, bauen,
starten, Log lesen“. Die Objekt-Ansicht ist Komfort auf dieser Schleife. Das Error-Log stand zuerst
auf Platz 7 und wurde am 2026-10-02 auf Wunsch des Nutzers vorgezogen.

### 1.3 Architekturentscheidungen für alle Teile

| Entscheidung | Begründung | Verworfen |
|---|---|---|
| Stack C# / .NET 8 / WPF (laut `CLAUDE.md`) | Das Konzept nennt C oder C++. `CLAUDE.md` ist neuer und legt C# fest. | C / C++ |
| Eigener C#-Reader für Packs und Tabellen. Das RPFM-Schema wird nur als Datendatei genutzt. | Volle Kontrolle über Speicher und Geschwindigkeit, kein Fremdprozess, keine Abhängigkeit von der installierten RPFM-Version. Machbarkeit gemessen (Abschnitt 3). | `rpfm_cli` als Kindprozess (Prozessstart pro Operation, Temp-Dateien, an RPFM-Version gebunden; hier meldet die CLI 4.7.3, das Changelog 5.0.6+). `rpfm_server` als Dauerprozess (Protokoll unbelegt, 47 MB Fremd-exe, enge Kopplung). |
| Eigene Mods liegen als Arbeitsordner mit Textdateien vor. Das Pack entsteht per „Bauen“. | Git, Diff, Branches und Zip-Snapshots arbeiten direkt auf Text. Die Schreibgrenze ist klar. | Pack-Datei als Wahrheit (kein Text-Diff, Tool schriebe direkt in den Spielordner). Pack plus Text-Spiegel (zwei Wahrheiten). |
| Auf Platte liegen Tabellen als Text (eine Zeile pro Eintrag). Die Objekt-Ansicht ist eine berechnete Ansicht darauf, zuerst als Codeblock. | Verlustfrei per Bauart, jede Tabelle sofort bearbeitbar, Tabellenansicht bleibt immer als Rückfallebene. Eine Maske kann später als zweite Ansicht dazukommen. | Codeblock-Dateien als Wahrheit (Abbildung pro Objektart nötig, hohes Round-Trip-Risiko; PMT-Erfahrung: 168 von 661). |
| Schema wird mitgeliefert. Ein einstellbarer Schema-Ordner (z. B. der von RPFM) überschreibt es. | Erststart funktioniert ohne RPFM und ohne Netz. RPFM-Nutzer sind nach einem Spiel-Patch sofort aktuell. | Nur mitliefern (nach jedem Patch App-Update nötig). Download schon im Fundament (HTTP, Bestätigung und Hash-Prüfung vergrößern die erste Spec; Download steht im Backlog). |
| Vanilla und fremde Mods sind immer read-only. | Regel „Originale und Fremdquellen werden nie geschrieben“. | – |
| PMT-Leaf-Libraries werden per Kopie übernommen, jeweils erst im Plan, der sie braucht, und dabei nach Checkliste A2 angepasst. | Die Libraries sind klein (gemessen: `Storage` 170 Zeilen, `Localization` 268, `UndoRedo` 308, `Theming` 366). Release-Zyklen beider Tools bleiben unabhängig. | Gemeinsame Bibliothek für PMT und dieses Tool. Neu schreiben ohne Vorlage. |

Die spätere Objekt-Ansicht (Teil 4) braucht vom Fundament nur zwei Dinge: Schlüsselspalten und
Referenzangaben aus dem Schema. Beides liest das Fundament in das Schema-Modell ein. Der
Referenzgraph selbst wird erst in Teil 4 gebaut.

---

## 2. Ziele und Non-Goals des Fundaments

### 2.1 Ziele

1. **Spiel erkennen.** Das Spielprofil findet die Installation über die Steam-Bibliotheken. Der
   Nutzer kann den Pfad in den Einstellungen korrigieren.
2. **Pack-Übersicht mit Herkunft.** Die App listet Vanilla-Packs, Mods im Datenordner des Spiels und
   abonnierte Workshop-Mods (Pack-Name und Workshop-ID).
3. **Pack öffnen.** Ein Pack zeigt seinen Dateibaum. Einträge öffnen sich in Tabs:
   - DB-Tabelle als Raster mit Spaltennamen aus dem Schema,
   - Loc-Datei als Raster,
   - Textdateien (`.lua`, `.xml`, `.txt`, `.json`, `.csv`, `.tsv`) als Text,
   - alle anderen Typen mit dem Hinweis „nicht unterstützt“, Typ, Größe und Kompressionsformat.
4. **Session.** Offene Tabs und das gewählte Spiel sind nach einem Neustart wieder da.
5. **Grundgerüst nach `CLAUDE.md`.** Fortschritts-Komponente, Log, Crash-Dialog, Einstellungsfenster
   (Sprache, Theme, Spielpfad, Schema-Ordner), UI-Texte in Englisch und Deutsch, Theme-Tokens.
6. **Unsicherheit sichtbar.** Die App zeigt, welches Schema benutzt wird (Quelle und Datum). Eine
   Tabelle, die mit dem Schema nicht lesbar ist, erscheint als „nicht dekodierbar“ mit Grund. Sie
   führt nie zu einem Absturz.

### 2.2 Non-Goals dieser Spec

„Non-Goal“ heißt hier: nicht Teil des Fundaments. Es heißt nicht, dass das Tool die Funktion nie
bekommt.

**Kommt in einem späteren Teil (siehe 1.2):**

- Arbeitsordner, Import bestehender Packs, Bearbeiten, Bauen von Packs → Teil 2. Das Fundament
  schreibt deshalb nichts außerhalb der eigenen App-Daten.
- Error-Log und Spielstart → Teil 3.
- Objekt-Ansicht, Referenzgraph, Katalog, Autovervollständigung → Teil 4.
- Lua-Komfort → Teil 5.
- Diff, Merge, Vanilla-Tracking → Teil 6.
- Workshop-Titel aus Steam → Teil 8.
- Self-Update und Release-Tool → eigene Specs.

**Zurückgestellt ohne festen Teil (Backlog):**

- Suche über Packs.
- Schema-Download.
- Profile für andere Spiele. Der Profil-Mechanismus existiert, ausgeliefert wird nur WH3.
- Vorschau für Bilder, Modelle und Audio.
- LZMA-komprimierte Einträge (gemessen: 11 relevante Einträge in 397 Workshop-Packs, 0 in Vanilla).
- Verschlüsselte Packs.
- Mehrere gleichzeitig laufende Instanzen der App.
- Sortieren und Filtern im Raster über das hinaus, was das Standard-Raster ohne eigenen Code bietet.

### 2.3 Erfolgskriterien

- Alle Tabellen aus `db.pack`, die `rpfm_cli` als TSV exportieren kann, dekodiert das Tool mit
  identischen Werten (Parity-Test, übersprungen ohne Fixture).
- Jedes der sechs `flogi*.pack` im Datenordner des Nutzers öffnet ohne Fehler.
- Kein Pack wird vollständig in den Speicher geladen. Gelesen werden Header, Index und einzeln
  geöffnete Einträge.
- Nach einem Neustart sind die Tabs wiederhergestellt.
- Das Hauptfenster ist nie ohne Fortschrittsanzeige länger als die Anzeigeverzögerung blockiert,
  außer durch reines Zeichnen des Rasters (siehe 3.5).

---

## 3. Gemessener Ist-Zustand

Alle Messungen stammen vom 2026-10-02. Rechner des Nutzers, Spiel und Workshop auf NVMe-SSD,
Wegwerf-Code mit .NET 10 SDK außerhalb des Projekts. Der Spike-Code wird nicht übernommen.

### 3.1 Umgebung

- WH3: `D:\SteamLibrary\steamapps\common\Total War WARHAMMER III`, Steam-App-ID 1142710 (aus dem
  Workshop-Ordnernamen). Assembly Kit vorhanden. `data\manifest.txt` existiert (Inhalt nicht
  geprüft).
- Steam-Pfad aus Registry `HKCU\Software\Valve\Steam\SteamPath`, Bibliotheken aus
  `steamapps\libraryfolders.vdf`. Beides hat auf diesem Rechner funktioniert.
- Ebenfalls installiert: Total War Rome II (37 Packs), Total War Attila (32 Packs).
- RPFM: Binär-Installation unter `D:\Modding\Warhammer III\RPFM`, kein Quellcode. Lizenz MIT.
  Schemas liegen unter `%APPDATA%\FrodoWazEre\rpfm\config\schemas`, Format RON, eine Datei pro Spiel
  (14 Spiele), eigene MIT-Lizenzdatei.
- .NET: installiert sind SDK 10.0.400 sowie die Runtimes 8.0.23 und 10.0.x (jeweils auch
  WindowsDesktop). Ein .NET-8-SDK ist nicht installiert.

### 3.2 Pack-Dateien (Spike S1)

| Messwert | Datenordner | Workshop |
|---|---|---|
| Packs | 268 (92,3 GB) | 397 (65,4 GB) |
| Kennung | alle `PFH5` | alle `PFH5` |
| Einträge | 746.692 | 363.578 |
| Größter Index | `audio_en.pack`, 184.662 Einträge | `sfo_grimhammer_3_main.pack`, 48.894 Einträge |
| Index aller Packs lesen, kalt | 2,4 s | 2,3 s |
| Index aller Packs lesen, warm | 0,16–0,31 s | 0,13–0,18 s |
| Speicher für alle Indexe (naiv, ein String pro Pfad) | 133 MB | 78 MB |
| Pack-Typ im Header | 1 Boot, 261 Release, 6 Mod | 395 Mod, 2 Movie |
| Packs mit Abhängigkeitsliste | 4 | 62 |
| Index mit Zeitstempeln | 2 | 0 |
| Verschlüsselter Index | 0 | 0 |
| Verschlüsselte Daten (laut Header-Bit) | 1 | 0 |
| Komprimierte Einträge | 329.639 in 218 Packs | 104.049 in 51 Packs |
| Lesefehler | 0 | 0 |

Format, wie es der Spike für alle 665 Packs erfolgreich gelesen hat:

- Header 28 Bytes: Kennung (4), Bitmaske (4), Anzahl Abhängigkeiten (4), Größe Abhängigkeitsliste (4),
  Anzahl Einträge (4), Größe Index (4), Zeitstempel (4).
- Bitmaske: die unteren 4 Bit sind der Pack-Typ (0 Boot, 1 Release, 2 Patch, 3 Mod, 4 Movie). Bit
  `0x40` bedeutet Zeitstempel im Index.
- Indexeintrag: Größe (u32), bei Bit `0x40` ein Zeitstempel (u32), ein Byte „komprimiert“, dann der
  Pfad nullterminiert mit Backslashes.
- Die Daten liegen direkt hinter dem Index, in Indexreihenfolge und ohne Auffüllung. Geprüft: Bei
  allen 665 Packs ist Datenanfang plus Summe der Größen gleich der Dateilänge.
- Komprimierter Eintrag: zuerst die unkomprimierte Größe (u32), danach der Datenstrom. Die Kennung
  an Position 4 unterscheidet zstd (`0xFD2FB528`) und LZ4-Frame (`0x184D2204`). Alles andere wurde als
  LZMA eingestuft (erstes Byte `0x5D`).

Die Bedeutung der Bits `0x10` (verschlüsselte Daten), `0x80` (verschlüsselter Index) und `0x100`
(erweiterter Header) stammt aus Wissen über RPFM, nicht aus dieser Messung. **Zu messen.**

Kompressionsformat der für das Fundament relevanten Typen (vollständig gezählt, nicht Stichprobe):

| Typ | Vanilla | Workshop |
|---|---|---|
| DB-Tabellen | 1602 zstd, 136 unkomprimiert | 29.618 unkomprimiert, 5309 zstd, 187 LZ4, 10 LZMA |
| `.loc` | 260 zstd, 9 unkomprimiert | 3252 unkomprimiert, 660 zstd, 19 LZ4, 1 LZMA |
| `.lua` | 901 zstd, 10 unkomprimiert | 2635 unkomprimiert, 456 zstd, 2 LZ4 |
| `.xml` | 9264 unkomprimiert, 2442 zstd, 1142 LZ4 | 1711 zstd, 1541 unkomprimiert, 15 LZ4 |
| `.txt` | 5087 zstd, 55 unkomprimiert, 3 LZ4 | 152 unkomprimiert, 17 zstd, 1 LZ4 |

Folgen:

- Alle 1602 Tabellen in `db.pack` sind zstd-komprimiert. Ohne zstd ist keine Vanilla-Tabelle lesbar.
- .NET 8 enthält weder zstd noch LZ4 noch LZMA.
- Größte relevante Einzeleinträge (unkomprimiert): `text/localisation__.loc` mit 67,4 MB, eine
  `.xml`-Datei mit 72,2 MB.

### 3.3 Schema (Spike S2)

- Ein eigener Parser für die benutzte RON-Teilmenge (rund 190 Zeilen) liest `schema_wh3.ron`
  (11,4 MB) vollständig. Zeit 70–210 ms für das Parsen, Speicher des Modells 3,4 MB.
- Inhalt: 1742 Tabellen, 2338 Definitionen (333 Tabellen mit mehreren Versionen), 15.337 Felder,
  5825 Referenzfelder.
- Feldtypen im WH3-Schema: `StringU8`, `OptionalStringU8`, `I32`, `F32`, `Boolean`, `I64`, `F64`,
  `ColourRGB`, `StringU16`, `I16`, `OptionalI32`. Keine verschachtelten Sequenzen.
- 8 Tabellen haben in ihrer neuesten Definition keine Schlüsselspalte.

### 3.4 Tabellen (Spike S3)

Eingabe waren die von `rpfm_cli` entpackten (und damit dekomprimierten) Rohdateien aus `db.pack`:
1602 Tabellen, 99,1 MB.

- 1599 von 1602 Tabellen dekodiert, bei jeder wurden alle Bytes verbraucht.
- 3 nicht dekodierbar: 2 Tabellen fehlen im Schema, bei 1 fehlt die Version. `rpfm_cli` scheitert
  an denselben 3. Das Schema hinkt dem Spiel also auch bei Vanilla hinterher.
- Parity gegen den TSV-Export von `rpfm_cli`: 1579 Tabellen zellgenau identisch (6,2 Mio. Zellen).
  Die übrigen 20 unterscheiden sich nur darin, dass RPFM die drei Farbspalten r, g, b zu einer
  Anzeigespalte zusammenzieht.
- Zeit für alle Tabellen: 0,4–0,55 s. Speicher mit naiven Zeilenobjekten: 373 MB.
- 963.940 Zeilen, 6,29 Mio. Zellen. Meiste Zeilen: `diplomacy_negotiation_string_options_tables`
  (71.848 × 4). Breiteste Tabelle: `unit_special_abilities_tables` (3173 × 71).
- Keine dekodierte Vanilla-Tabelle ohne Schlüsselspalte, keine doppelten Schlüssel.
- Mods: 29.752 unkomprimierte Tabellen aus 401 Mod-Packs dekodiert, 3 Fehler (Ursache nicht
  untersucht). 5506 komprimierte Mod-Tabellen wurden nicht geprüft.

Format, wie es der Spike gelesen hat:

- optional GUID-Marke `FD FE FC FF`, dann Länge (u16, in Zeichen) und UTF-16-Text. Vorhanden bei
  allen 1602 Vanilla-Tabellen.
- optional Versions-Marke `FC FD FE FF`, dann Version (i32). Vorhanden bei 569 von 1602. Ohne Marke
  gilt Version 0.
- 1 Byte, dann Zeilenanzahl (u32).
- Zeilen: Felder in der Reihenfolge der Schema-Definition.
- `Boolean` 1 Byte, `I16` / `I32` / `I64` / `F32` / `F64` in fester Breite, `ColourRGB` 4 Bytes,
  `StringU8` mit Länge (u16) und UTF-8, `StringU16` mit Länge (u16, in Zeichen) und UTF-16,
  `OptionalStringU8` mit einem Flag-Byte und nur bei gesetztem Flag einem `StringU8`.
- `OptionalI32` (genau 1 Feld im Schema, in `start_pos_characters_tables`) lässt sich nicht messen:
  Die Tabelle liegt nicht in `db.pack`. Der Feldtyp gilt deshalb als nicht unterstützt (siehe 3.8).

Das Loc-Format wurde im ersten Spike nicht gelesen. Die Messung steht in 3.8.

### 3.5 Raster (Spike S4)

Standard-`DataGrid` von WPF, Zeilen- und Spaltenvirtualisierung an, Bindung über Indexer:

| Tabelle | Anzeige | Scrollen | Sortieren (Standardweg) |
|---|---|---|---|
| 71.848 × 4 | 0,34 s | 45–82 ms | 0,81 s |
| 26.637 × 10 | 0,32 s | 90–132 ms | 0,55 s |
| 3173 × 71 | 1,23 s | 113–114 ms | 0,48 s |
| 2679 × 60 | 1,20 s | 129–138 ms | 0,27 s |

Die Anzeigezeit ist reine Arbeit im UI-Thread und läuft deshalb nicht über die
Fortschritts-Komponente. Das Standard-Raster reicht für die Nur-Lese-Ansicht. Breite Tabellen
brauchen rund 1,2 s. Bekannte WPF-Eigenheit: Unterstriche in Spaltenköpfen werden als
Tastenkürzel-Zeichen verschluckt, wenn man sie nicht ausschaltet.

### 3.6 Andere Spiele (Spike S5, nur Information)

- Rome II und Attila benutzen `PFH4`. Derselbe Index-Reader hat alle 69 Packs gelesen (ohne das
  Kompressions-Byte). Keine Kompression. Rome II hat auch Packs vom Typ Patch.
- Tabellen: Rome II 1239 von 1461 dekodiert (85 %), Attila 1681 von 2449 (69 %). Ältere Spiele
  brauchen zusätzliche Arbeit am Tabellen-Decoder und teils am Schema. Kein Teil des Fundaments.

### 3.7 Was RPFM schon kann

Laut Changelog der installierten Version: Lua-Diagnose, Delta-Merge für Tabellen, Split View, Global
Search, Übersetzer. Der Mehrwert dieses Tools liegt deshalb in Objekt-Ansicht, Session, Arbeitsordner
mit Git, Versionen und Vanilla-Tracking, nicht in der reinen Tabellenansicht.

### 3.8 Messungen vor Plan 2 (2026-10-02)

Wegwerf-Code auf `net8.0` (Runtime 8.0.23), Vergleichsreferenz `rpfm_cli`. Diese Messungen
beantworten die Annahmen 1 bis 12 aus Abschnitt 8.

**zstd**
- Paket `ZstdSharp.Port` 0.8.8: Lizenz MIT, rein verwaltet, keine weiteren Abhängigkeiten für
  `net8.0`, 386 KB.
- Alle 1602 Tabellen aus `db.pack` dekomprimieren bytegleich zur Rohausgabe von `rpfm_cli`.
- Zeit für alle 1602 (99,1 MB): 164–247 ms.
- Aufruf: Zielpuffer in der Größe aus dem Größen-Präfix, Dekompression des Rests ab Byte 4.

**LZ4**
- Ein eigener Decoder (rund 60 Zeilen) dekodiert alle 50.798 LZ4-Einträge der Vanilla-Packs und
  3732 im Workshop auf die im Präfix genannte Größe. Kein Fehlschlag.
- Drei Stichproben sind per SHA-256 identisch zur Ausgabe von `rpfm_cli`.
- Frame-Eigenschaften (alle 54.530 Einträge): Version 1, kein Block-Checksum, keine Inhaltsgröße,
  kein Dictionary. Blöcke hängen meist voneinander ab (50.832 Einträge), der Inhalts-Checksum ist
  meist vorhanden (50.798). Blockgrößen-Kennung 4, 5 oder 7.
- Folge: Der Decoder schreibt in einen einzigen Zielpuffer, damit Rückverweise über Blockgrenzen
  funktionieren. Header- und Inhalts-Checksum werden übersprungen, nicht geprüft. Geprüft wird die
  Größe.
- Der einfache Decoder schafft rund 30 MB/s. Für die relevanten Typen reicht das.

**Loc**
- Format: `FF FE`, `LOC`, ein Nullbyte, Version (i32, Wert 1), Zeilenanzahl (u32). Pro Zeile:
  Schlüssel und Text als `StringU16`, dann ein Flag-Byte.
- Alle 248 Loc-Dateien aus `local_en.pack` dekodieren vollständig und sind zellgenau identisch zum
  TSV-Export von `rpfm_cli` (266.427 Zeilen, 0,5 s).
- Größte Loc-Datei: `text/localisation__.loc` in den Sprachpacks, rund 212.300 Zeilen, 61 MB.
  Lesen und Dekomprimieren 85–153 ms, Dekodieren 77–93 ms.

**Tabellen in Mods, jetzt mit Dekompression**
- 35.252 von 35.261 Mod-Tabellen dekodieren (5505 von 5511 komprimierten, 29.747 von 29.750
  unkomprimierten). 10 LZMA-Tabellen übersprungen.
- Die 9 Fehlschläge betreffen 3 Tabellen (`culture_settlement_occupation_options_tables` v7,
  `transported_military_forces_tables` v1, `cdir_events_mission_option_junctions_tables` v2). Die
  Mods tragen dieselbe Versionsnummer wie das Schema, aber ein älteres Layout. Erwartetes Verhalten:
  „nicht dekodierbar“.

**Feldtypen**
- In echten Daten bestätigt: `Boolean`, `F32`, `F64`, `I16`, `I32`, `I64`, `ColourRGB`, `StringU8`,
  `StringU16`, `OptionalStringU8`.
- Nicht messbar: `OptionalI32`. Alle nicht bestätigten Typen ergeben den Status „Feldtyp nicht
  unterstützt“ statt geratener Werte.

**Pack-Header und Scan**
- Nur die 28 Header-Bytes aller 665 Packs lesen: warm 24 ms, kalt rund 4,4 s. Die Zeit steckt im
  Öffnen der Dateien, nicht in der gelesenen Menge. Der Scan beim Start gehört deshalb in den
  Hintergrund.
- Zeitstempel im Index (4 Bytes): `boot.pack` und `shaders.pack`. Beide Indexe lesen fehlerfrei.
- Bit `0x10` (verschlüsselte Daten laut RPFM-Wissen): nur `audio_base_m.pack`. Ob die Einträge
  wirklich unlesbar sind, ist nicht geprüft. Für die relevanten Typen ohne Bedeutung.

**`manifest.txt`**
- 503 Zeilen im Format `Pfad<TAB>Größe<TAB>Flag`, davon 271 Packs.
- Alle 262 Packs mit Vanilla-Typ stehen im Manifest. Kein Mod-Pack steht darin. 9 Manifest-Packs
  fehlen auf der Platte (nicht gekaufte DLC).
- Folge: Das Manifest ist die robustere Vanilla-Liste (siehe 4.3).

**RPFM-Schemas**
- Quelle: `https://github.com/Frodo45127/rpfm-schemas`, lokaler Stand Commit `5d841c5`,
  `schema_wh3.ron` zuletzt geändert in Commit `1d5ce11` vom 2026-09-24 („Updated wh3 schemas for
  patch 9.0“). Die lokale Datei ist unverändert gegenüber dem Repo.

---

## 4. Architektur des Fundaments

### 4.1 Module

Solution `TotalWarModTool` (Anzeigename „Total War Modding Tool“, Kürzel TWMT). Alle Libraries zielen auf `net8.0`, nur die App auf `net8.0-windows`.

| Modul | Aufgabe | Referenziert |
|---|---|---|
| `TotalWarModTool.Storage` | Atomar schreiben (temp + rename, Retry), kaputte Datei nach `.bad`, `schemaVersion` lesen | – |
| `TotalWarModTool.Localization` | `UiText` als Instanz, `Get(key, fallback)`, Sprachdateien aus `locales/` | – |
| `TotalWarModTool.Logging` | Rotierendes File-Log | – |
| `TotalWarModTool.Settings` | Globale Einstellungen mit `Changed`-Event, Einstellungen pro Spiel | `Storage` |
| `TotalWarModTool.Operations` | `OperationProgress` (Anzahl, Gesamt, aktuelles Element) als Nutzlast für `IProgress<T>`. Ergänzt mit Plan 1, damit Libraries Fortschritt melden können, ohne die App zu referenzieren. | – |
| `TotalWarModTool.GameProfiles` | Ausgelieferte JSON-Profile laden, Installation finden (`GameLocator`) | – |
| `TotalWarModTool.PackFiles` | Pack-Header und Index lesen, Eintrag lesen, Dekompression | – (NuGet `ZstdSharp.Port`) |
| `TotalWarModTool.Schema` | RON-Teilmenge parsen, Schema-Modell, Schema-Quelle wählen | – |
| `TotalWarModTool.Tables` | DB-Tabellen und Loc-Dateien dekodieren | `Schema` |
| `TotalWarModTool.Workspace` | `PackCatalog`: Pack-Quellen eines Spiels mit Herkunft | `GameProfiles`, `PackFiles`, `Operations` |
| `TotalWarModTool.Session` | Offene Tabs und gewähltes Spiel speichern und laden | `Storage` |
| `TotalWarModTool.App` | WPF, Composition Root, Views und ViewModels, `ProgressRunner`, Theme | alle |

Module referenzieren nie die App. Für das Fundament ist kein Host-Interface nötig.

Interfaces nur an der Außenwelt:

- `ISteamLocationSource`: liefert den Steam-Pfad aus der Registry. In Tests ein Fake.
- `TimeProvider` für die Log-Rotation und für die Anzeigeverzögerung des `ProgressRunner`.

Dateizugriff läuft immer über übergebene Wurzelpfade (Spielordner, Workshop-Ordner, App-Daten,
Programmordner).

### 4.2 Schnittstellen der Lese-Libraries

Die Namen sind verbindlich für den ersten Plan. Details der Signaturen legt der Plan fest.

- `PackFiles`
  - `PackReader.ReadHeader(path)` liefert `PackHeader` (Kennung, Pack-Typ, Flags). Liest nur 28 Bytes.
  - `PackReader.ReadIndex(path)` liefert `PackIndex` (Header, Abhängigkeiten, Einträge mit Pfad,
    Offset, gespeicherter Größe, Kompressions-Flag, Fingerprint der Datei).
  - `PackReader.ReadEntry(index, entry, cancellationToken)` liefert `EntryContent` mit den
    dekomprimierten Bytes oder einer Diagnose (nicht unterstütztes Format, verschlüsselt,
    Lesefehler).
  - Pfade im Index werden auf `/` normalisiert. Der Vergleich ist nicht case-sensitiv.
  - Die Formatvariante wählt der Reader anhand der Kennung. Im Fundament gibt es genau die Variante
    `PFH5`. Andere Kennungen ergeben die Diagnose „unbekannte Kennung“.
- `Schema`
  - `SchemaLoader.Load(path)` liefert `GameSchema`: Tabellenname → Definitionen pro Version → Felder
    (Name, Typ, Schlüssel-Flag, Referenzziel).
  - `SchemaSource.Resolve(shippedFolder, overrideFolder, fileName)` wählt die Datei und liefert
    Quelle und Dateidatum für die Anzeige.
- `Tables`
  - `DbTableDecoder.Decode(bytes, tableName, schema)` liefert `DecodedTable`: Status (`Ok`,
    `TableNotInSchema`, `VersionNotInSchema`, `Failed`), benutzte Definition, Zeilen, Klartextgrund.
  - `LocDecoder.Decode(bytes)` liefert Zeilen (Schlüssel, Text, Flag) oder eine Diagnose.
  - Decoder werfen bei kaputten Daten keine Exceptions nach außen. Sie liefern eine Diagnose.
- `Workspace`
  - `PackCatalog.Scan(profile, installPath, workshopPath, progress, cancellationToken)` liefert die
    Liste der Packs mit Herkunft (`Vanilla`, `DataFolderMod`, `Workshop`), Workshop-ID,
    Fingerprint und gegebenenfalls einer Diagnose.

Berechnung und Schreiben sind getrennt. Die Lese-Libraries schreiben nichts.

Festlegungen aus Plan 2:

- **Diagnosen statt Log.** Die Lese-Libraries referenzieren weder `Logging` noch `Localization`.
  Jedes Problem kommt als Ergebnis zurück: ein Enum-Wert plus ein technisches Detail auf Englisch.
  Die App loggt und übersetzt den Enum-Wert in einen UI-Text (Plan 3).
- **Keine Exceptions bei kaputten Daten.** Nach außen dringt nur `OperationCanceledException`.
- **Größengrenze.** `ReadEntry` liest keinen Eintrag, dessen unkomprimierte Größe über einer
  Obergrenze liegt (Standard 256 MB), und meldet das als Diagnose.
- **Fingerprint.** `ReadEntry` prüft vor dem Lesen, ob die Pack-Datei noch dem Fingerprint des
  Index entspricht. Wenn nicht: Diagnose „Pack geändert“.
- **Feldtypen.** Tabellen mit einem nicht bestätigten Feldtyp bekommen den zusätzlichen Status
  `UnsupportedFieldType`.
- **Farbwerte.** `ColourRGB` wird als Text `RRGGBB` geliefert.

### 4.3 Spielprofil

Eine JSON-Datei pro Spiel im Programmordner unter `profiles/`. Für das Fundament enthält das
WH3-Profil nur Felder, die benutzt werden:

- `schemaVersion`
- `id` (`warhammer_3`), `displayName`
- `steamAppId` (1142710), `installFolderName` (`Total War WARHAMMER III`), `dataFolder` (`data`)
- `packFormat` (`PFH5`)
- `schemaFile` (`schema_wh3.ron`)
- `vanillaPackTypes` (`boot`, `release`, `patch`)
- `vanillaManifestFile` (`manifest.txt`, optional)

Unlesbare Profildateien werden übersprungen. Der Lader gibt die übersprungenen Dateien mit Grund
zurück, die App loggt sie. Ein zweites, synthetisches Profil in den Tests belegt, dass kein
WH3-Wissen im Code steckt.

Herkunft eines Packs:

- liegt unter dem Workshop-Ordner des Spiels → `Workshop`, die Workshop-ID ist der Ordnername,
- liegt im Datenordner und gilt als Vanilla → `Vanilla`,
- liegt im Datenordner und gilt nicht als Vanilla → `DataFolderMod`.

Was als Vanilla gilt:

- Nennt das Profil ein Manifest und ist die Datei im Datenordner lesbar, entscheidet allein das
  Manifest: Vanilla ist, was dort als Pack gelistet ist. Gemessen stimmt das für alle 268 Packs im
  Datenordner (3.8). Ein Mod, der als Pack-Typ „Release“ gespeichert wurde, wird so trotzdem als Mod
  erkannt.
- Sonst entscheidet der Pack-Typ aus dem Header: Vanilla ist, was in `vanillaPackTypes` steht.

### 4.4 Schema-Quelle

- Ausgeliefert: `schemas/schema_wh3.ron` im Programmordner, daneben die MIT-Lizenzdatei der
  RPFM-Schemas und eine Datei mit dem Stand (Datum der Übernahme).
- Override: ein Ordner in den globalen Einstellungen (z. B. der Schema-Ordner von RPFM). Ist er
  gesetzt und enthält er die Schema-Datei des Spiels und ist diese lesbar, wird sie benutzt.
  Andernfalls gilt die ausgelieferte Datei, und die App zeigt einen Hinweis.
- Die App zeigt immer an, welche Quelle aktiv ist, mit Dateidatum.

Abweichung vom Brainstorming: Dort hieß es „die neuere Datei gewinnt“. Das Dateidatum der
ausgelieferten Datei ist aber das Datum des Entpackens und sagt nichts über den Stand. Deshalb gilt:
Ein gesetzter und lesbarer Override gewinnt immer.

Verworfen: ein RON-Paket von NuGet (eigener Parser ist klein und gemessen ausreichend), ein eigenes
umgewandeltes Schema-Format (dann könnte der RPFM-Ordner nicht direkt gelesen werden).

### 4.5 Dekompression

- zstd über das NuGet-Paket `ZstdSharp.Port` 0.8.8 (vom Nutzer am 2026-10-02 freigegeben, Lizenz
  MIT). Gemessen: alle 1602 Tabellen bytegleich zur Rohausgabe von `rpfm_cli` (3.8).
- LZ4-Frame mit einem eigenen Decoder in `PackFiles`. Gemessen: 54.530 Einträge dekodieren auf die
  genannte Größe, drei Stichproben bytegleich zu `rpfm_cli` (3.8). Der Decoder prüft die Größe,
  nicht die Checksummen des Frames.
- LZMA: Diagnose „Kompressionsformat LZMA wird noch nicht unterstützt“. Steht im Backlog.
- Die im Eintrag genannte unkomprimierte Größe wird nach dem Dekomprimieren geprüft. Bei Abweichung
  gibt es eine Diagnose statt eines Ergebnisses.

Verworfen: drei NuGet-Pakete (zwei Abhängigkeiten mehr für 1145 + 11 Einträge), alles selbst
schreiben (zstd-Decoder ist groß und fehleranfällig), auf zstd in einer späteren .NET-Version
warten (ungeprüft, und der Stack ist auf .NET 8 festgelegt).

### 4.6 App

- **Hauptfenster.** Links der Explorer: Packs gruppiert nach Herkunft, aufklappbar zum Dateibaum.
  Rechts die Tabs. Unten eine Statuszeile mit Spiel und Schema-Quelle.
- **Menü.** Enthält nur Werkzeuge: Pack-Liste aktualisieren, Einstellungen, Log-Ordner öffnen.
- **Einstellungsfenster.** Nicht-modal, Kategorien links, wirkt sofort: Sprache, Theme (Hell, Dunkel,
  System), Spielpfad, Schema-Ordner.
- **ViewModels** für Explorer, Tab-Bereich, jede Tab-Art und Einstellungen. Code-behind enthält nur
  View-Verdrahtung.
- **Tab-Arten.**
  - Tabelle: Raster, nur lesen. Spalten in der Reihenfolge der Schema-Definition. Farbspalten
    bleiben getrennt. Kopfzeile mit Tabellenname, Version, Zeilenanzahl. Unterstriche in
    Spaltenköpfen bleiben sichtbar.
  - Loc: Raster mit Schlüssel, Text, Flag.
  - Text: nur lesen. UTF-8, bei vorhandener BOM die dort angegebene Kodierung. Ab 5 MB zeigt der
    Tab nur die ersten 5 MB und einen Hinweis mit der vollen Größe (eine Konstante).
  - Nicht dekodierbar und nicht unterstützt: Grund im Klartext, Typ, Größen, Schema-Quelle.
- **`ProgressRunner`.** Eine Komponente nach den Regeln in `CLAUDE.md` (Verzögerung 150 ms als eine
  Konstante, modal, Abbrechen, kein Fenster bei schneller Operation, Fehler werden gemeldet). Ist die
  Gesamtmenge bekannt, zeigt das Fenster Prozent, Balken und Zählstand (z. B. „120 / 4000“), sonst
  einen unbestimmten Balken. Über sie laufen: Pack-Liste auf Nutzerwunsch aktualisieren, Pack
  öffnen, Eintrag öffnen. Der Scan beim Start läuft still im Hintergrund.
- **Theme** über Tokens und `DynamicResource`, angewendet vor dem ersten Fenster.
- **UI-Texte** nur über `UiText.Get(key, fallback)` mit englischem Fallback. `locales/en.json` und
  `locales/de.json`. Standardsprache ist Englisch.
- **Fehler.** Globale Handler für die drei Exception-Quellen aus `CLAUDE.md`. Der Crash-Dialog hat
  „Log öffnen“ und „Details kopieren“.

Abweichung von `CLAUDE.md`: Der Knopf „Fehler melden“ fehlt im Fundament, weil es noch kein
öffentliches Ziel dafür gibt. Er kommt mit der Release-Spec und steht im Backlog.

### 4.7 Datenfluss

1. **Start.** Theme anwenden. Globale Einstellungen laden. Profil des gewählten Spiels auflösen,
   Installation finden. Session laden. Fenster zeigen. Danach still im Hintergrund:
   `PackCatalog.Scan` (Ordner auflisten, pro Pack nur den Header lesen) und das Schema laden.
   Tabs aus der Session werden geöffnet, sobald beides fertig ist.
2. **Pack aufklappen.** `ProgressRunner` liest den Index. Der Baum erscheint.
3. **Eintrag öffnen.** `ProgressRunner` prüft den Fingerprint, liest die Bytes, dekomprimiert,
   dekodiert. Der Tab erscheint erst am Ende.
4. **Session.** Jede Änderung an den Tabs speichert verzögert (Debounce) und atomar.

Indexe werden nur für aufgeklappte Packs gehalten. Tabellen werden nur für offene Tabs dekodiert.
Es gibt im Fundament keinen Cache auf Platte. Begründung: Index lesen dauert warm höchstens rund
0,1 s pro Pack (langsamstes Pack: `audio_en.pack` mit 53–104 ms), das Schema 0,1–0,2 s. Alle Indexe zusammen kosten naiv über 200 MB, alle Tabellen 373 MB.

Verworfen: alle Indexe beim Start laden (Speicher, 4,7 s kalt), alle Tabellen beim Start dekodieren
(373 MB), Index-Cache auf Platte (gemessen nicht nötig).

### 4.8 Daten und Persistenz

Schreibgrenze des Fundaments: nur `%APPDATA%\TotalWarModTool` und `%LocalAppData%\TotalWarModTool`.
Nie Spielordner, Workshop-Ordner, RPFM-Ordner oder Programmordner.

| Datei | Ort | Inhalt |
|---|---|---|
| `settings.json` | `%APPDATA%\TotalWarModTool` | Sprache, Theme, gewähltes Spiel, Schema-Ordner, Fensterposition |
| `games\<profilId>\game-settings.json` | `%APPDATA%\TotalWarModTool` | manuell gesetzter Spielpfad |
| `games\<profilId>\session.json` | `%APPDATA%\TotalWarModTool` | offene Tabs (Pack-Pfad, Eintragspfad), aktiver Tab, aufgeklappte Packs |
| `logs\` | `%LocalAppData%\TotalWarModTool` | rotierendes Log |

- Jede Datei hat `schemaVersion` (Start bei 1), wird atomar geschrieben und bei Lesefehler nach
  `.bad` kopiert. Danach gelten die Defaults.
- Beim Laden wird still repariert: Tabs auf fehlende Packs oder Einträge fallen weg (Log-Eintrag),
  eine Fensterposition außerhalb des virtuellen Bildschirms wird verworfen.
- Ein Test stellt sicher, dass jede Einstellung genau einer der beiden Einstellungsdateien
  zugeordnet ist.
- Packs sind fremde Dateien, keine eigenen Entitäten. Deshalb referenziert die Session sie über
  den Pfad. Eigene Entitäten mit GUID entstehen erst mit dem Arbeitsordner (Teil 2).
- Das Log enthält keine personenbezogenen Daten. Pfade unterhalb des Benutzerprofils werden mit
  `%USERPROFILE%` abgekürzt.

---

## 5. Fehlerfälle

| Fall | Verhalten |
|---|---|
| Spiel nicht gefunden (kein Steam, anderer Ort) | Hinweis im Explorer mit Knopf zur Pfadwahl. Die App bleibt bedienbar. |
| Gewählter Spielpfad enthält keinen Datenordner | Die Einstellung lehnt den Pfad mit lesbarer Meldung ab. |
| Pack abgeschnitten, unbekannte Kennung, Index inkonsistent | Der Eintrag im Explorer ist markiert, der Grund steht im Tooltip. Die übrigen Packs funktionieren. |
| Pack mit verschlüsseltem Index oder verschlüsselten Daten | Markiert als „verschlüsselt, nicht unterstützt“. |
| Pack ist vom Spiel oder einem anderen Programm gesperrt | Lesender Zugriff mit Freigabe für andere. Bei Fehlschlag kurzer Retry, danach eine Klartextmeldung mit Handlungshinweis. |
| Pack wurde seit dem Lesen des Index geändert (Fingerprint) | Der Index wird neu gelesen. Offene Tabs dieses Packs zeigen den Hinweis „veraltet“. |
| Pack wurde gelöscht | Meldung beim Zugriff. Der Eintrag verschwindet bei der nächsten Aktualisierung der Liste. |
| Tabelle fehlt im Schema oder Version fehlt | Tab „nicht dekodierbar“ mit Tabellenname, Version und Schema-Quelle. |
| Tabelle lässt sich nicht vollständig lesen (Bytes übrig, Daten zu kurz) | Tab „nicht dekodierbar“ mit Grund. Es werden keine halb gelesenen Zeilen gezeigt. |
| Eintrag mit LZMA oder unbekannter Kompression | Tab „nicht unterstützt“ mit dem Format. |
| Dekomprimierte Größe weicht ab | Diagnose, kein Inhalt. |
| Schema-Datei im Override-Ordner fehlt oder ist kaputt | Ausgeliefertes Schema, sichtbarer Hinweis in Statuszeile und Einstellungen. |
| Ausgeliefertes Schema fehlt oder ist kaputt | Tabellen sind nicht dekodierbar, der Rest funktioniert. Meldung mit Hinweis auf den Schema-Ordner. |
| Textdatei größer als 5 MB | Die ersten 5 MB werden gezeigt, dazu ein Hinweis. |
| Einstellungen oder Session kaputt | Kopie nach `.bad`, Defaults, Log-Eintrag. Der Start wird nie blockiert. |
| `settings.json` stammt von einer neueren Version oder ist beim Start nicht lesbar (gesperrt) | Die App läuft mit den gelesenen Werten bzw. mit Standardwerten. In dieser Sitzung wird `settings.json` nicht geschrieben, damit nichts überschrieben wird. Das Log enthält eine Warnung. |
| Sprachdatei ist kein gültiges JSON oder nicht UTF-8 | Die App nutzt Englisch. Das Log enthält eine Warnung mit Sprache, Zeile und Position. |
| Session verweist auf fehlendes Pack oder fehlenden Eintrag | Der Tab wird still entfernt, Log-Eintrag. |
| Abbruch durch den Nutzer | Kein Tab, kein Teilzustand. Das Hauptfenster ist wieder frei. |
| Unerwartete Exception in einer Operation | `ProgressRunner` schließt das Fenster und meldet den Fehler. Log-Eintrag. |
| Unbehandelte Exception | Crash-Dialog, Log-Eintrag. |
| Fehler beim Start, bevor das Hauptfenster existiert | Log-Eintrag, Crash-Dialog mit dem Hinweis, dass die App beendet wird, danach Prozessende mit Exit-Code 1. |

---

## 6. Tests

Ein Testprojekt pro Modul (xUnit), handgeschriebene Fakes, Dateisystem-Tests im eigenen
Temp-Ordner. Libraries test-first.

- **`PackFiles`.** Ein Testhelfer baut kleine `PFH5`-Packs als Bytes: mit und ohne Zeitstempel, mit
  Abhängigkeiten, mit komprimierten Einträgen. Fälle: abgeschnitten, falsche Kennung, Index kürzer
  als angegeben, Datenende ungleich Dateilänge, verschlüsselte Flags, zstd, LZ4, LZMA-Diagnose,
  Größenabweichung nach dem Dekomprimieren, Abbruch.
- **`Schema`.** Kleine RON-Beispiele für jede benutzte Form (Struct, Tupel, Map, Liste, `Some`,
  `None`, Escapes). Kaputtes RON ergibt eine Diagnose mit Position.
- **`Tables`.** Synthetische Tabellen-Bytes für jeden Feldtyp, mit und ohne Marken. Fälle: Tabelle
  fehlt, Version fehlt, Bytes übrig, zu kurz, ungültiger Bool-Wert. Dasselbe für Loc.
- **`Workspace`.** Herkunft aus Ort und Pack-Typ, Workshop-ID aus dem Ordnernamen, kaputtes Pack in
  der Liste, synthetisches zweites Profil.
- **`GameProfiles`.** Auflösung über Fake-Registry und Temp-Bibliotheken, mehrere Bibliotheken,
  kein Steam, kaputte Profildatei wird übersprungen.
- **`Settings`, `Session`, `Storage`.** Atomar schreiben, `.bad`, Sanitize, `schemaVersion`, jede
  Einstellung genau einer Datei zugeordnet.
- **`Localization`.** `en` und `de` vollständig, Fallback-Kette.
- **`ProgressRunner`** über den STA-Helper: fertig vor der Verzögerung (Fenster nie erzeugt), fertig
  danach (Fenster wird geschlossen), Exception, Abbruch.
- **Tests auf echten Daten**, übersprungen ohne Fixture, mit Invarianten statt festen Zahlen:
  - jeder Index im Datenordner: Datenende gleich Dateilänge,
  - Parity: dekomprimierte Bytes gleich der Rohausgabe von `rpfm_cli`,
  - Parity: dekodierte Tabellen und Loc-Dateien gleich dem TSV-Export von `rpfm_cli` (Farbspalten
    werden vor dem Vergleich wie bei RPFM zusammengezogen),
  - jede Tabelle ist entweder `Ok` mit vollständig verbrauchten Bytes oder trägt eine Diagnose.
- **Manuelle UI-Checkliste** als Abnahme von Plan 3.

---

## 7. Umsetzungsreihenfolge

Drei Pläne nacheinander. Der nächste Plan wird erst geschrieben, wenn die Schnittstellen des
vorigen existieren.

1. **Gerüst.** Solution, `Directory.Build.props`, `Directory.Packages.props`, `build.bat`,
   `test.bat`, `run.bat`, Changelog. `Storage`, `Localization`, `Logging`, `Settings`. App-Start mit
   Theme, globalen Exception-Handlern, Crash-Dialog, Einstellungsfenster (Sprache, Theme) und
   `ProgressRunner`. Vorher wird als Baseline geprüft, ob `net8.0` mit dem installierten SDK 10 baut.
2. **Lese-Libraries.** `GameProfiles`, `PackFiles` (mit Dekompression), `Schema`, `Tables`,
   `Workspace`. Erste Aufgaben sind die offenen Messungen aus Abschnitt 8, die diese Libraries
   betreffen.
3. **Explorer und Tabs.** Explorer, Tab-Arten, `Session`, die übrigen Einstellungen (Spielpfad,
   Schema-Ordner), Statuszeile, manuelle Checkliste.

---

## 8. Annahmen, die der erste echte Lauf prüft

| Nr. | Annahme | Stand |
|---|---|---|
| 1 | Die Spike-Zeiten gelten ähnlich unter .NET 8. | **Bestätigt** (3.8, Messungen liefen auf `net8.0`). Build mit SDK 10.0.400 und Lauf auf Runtime 8.0.23 ebenfalls gemessen. |
| 2 | `ZstdSharp.Port` hat eine passende Lizenz und dekomprimiert alle 1602 Vanilla-Tabellen bytegleich zu `rpfm_cli`. | **Bestätigt** (3.8): MIT, 1602 von 1602 bytegleich, 164–247 ms für alle. |
| 3 | Der eigene LZ4-Decoder liest die LZ4-Einträge korrekt. | **Bestätigt** (3.8): 54.530 Einträge auf die genannte Größe, drei Stichproben bytegleich zu `rpfm_cli`. Ein Paritätstest über mehr Einträge kommt mit Plan 2. |
| 4 | Loc-Format: Kopf, Version, Zeilenanzahl, Zeilen aus zwei UTF-16-Texten und einem Flag. | **Bestätigt** (3.8): 248 von 248 Dateien zellgenau identisch. |
| 5 | Die größte Loc-Datei lässt sich in vertretbarer Zeit dekodieren und im Raster anzeigen. | Dekodieren **bestätigt** (3.8): rund 212.300 Zeilen in unter 0,1 s. Anzeige im Raster: **zu messen in Plan 3**. |
| 6 | `OptionalI32` ist ein Flag-Byte plus immer vorhandener Wert. | **Nicht messbar** (3.8). Der Typ gilt als nicht unterstützt. |
| 7 | Bedeutung der Header-Bits `0x10`, `0x80`, `0x100`. | **Teilweise** (3.8): `0x10` trägt nur `audio_base_m.pack`. Die Bedeutung stammt weiter aus RPFM-Wissen. Packs mit diesen Bits gelten als nicht unterstützt. |
| 8 | Der Zeitstempel im Index ist bei `PFH5` 4 Bytes breit. | **Bestätigt** (3.8): `boot.pack`, `shaders.pack`. |
| 9 | Komprimierte Mod-Tabellen dekodieren mit derselben Quote wie unkomprimierte. Ursache der Fehler. | **Bestätigt** (3.8): 5505 von 5511. Ursache: älteres Layout bei gleicher Versionsnummer. |
| 10 | Packs lassen sich lesen, während das Spiel läuft. | **Zu messen** in Plan 3 (manuelle Checkliste). |
| 11 | Header aller Packs lesen dauert kalt deutlich unter 4,7 s. | **Widerlegt** (3.8): kalt rund 4,4 s, warm 24 ms. Der Scan beim Start läuft im Hintergrund. |
| 12 | `manifest.txt` listet die Vanilla-Packs und ist robuster als der Pack-Typ. | **Bestätigt** (3.8). Regel in 4.3 angepasst. |
| 13 | Steam-Erkennung funktioniert auch bei anderer Installation. Der manuelle Pfad deckt alle übrigen Fälle ab. | **Zu messen**: Plan 3, Rückmeldung von Testern. |
| 14 | 1,2 s Anzeigezeit für breite Tabellen ist für den Nutzer annehmbar oder lässt sich mit einfachen Mitteln senken. | Plan 3 |
| 15 | Nach einem Spiel-Patch sind mehr Tabellen nicht dekodierbar als die heutigen 3. Die Anzeige „nicht dekodierbar“ reicht dem Nutzer als Verhalten. | laufender Betrieb |

Annahmen für spätere Teile, hier nur notiert: Eignung des Referenzgraphen für die Objekt-Ansicht,
verlustfreie Umwandlung Tabelle ↔ Text, Inhalte der Spiel-Logs, Startoptionen des Spiels,
Workshop-Upload, Tabellenformate älterer Spiele.

---

## 9. Entscheidungen des Nutzers (2026-10-02)

- **Name:** „Total War Modding Tool“, Kürzel „TWMT“. Der Name erscheint so im Fenstertitel und in
  allen Texten. Im Code, in Projektnamen und in den Datenordnern heißt die App `TotalWarModTool`
  (gleiche Bildung wie `ParadoxModTool` bei PMT).
- **Lizenz:** MIT. Die Datei `LICENSE` liegt im Repo-Wurzelordner.
- **Repo:** lokal unter `C:\Users\flori\Documents\Claude Projekte\Total War Modding Tool`. Es gibt
  vorerst kein Remote. CI (Build und Test bei jedem Push) und der Knopf „Fehler melden“ brauchen ein
  öffentliches Ziel und bleiben deshalb im Backlog, bis ein Remote existiert.

Hinweise auf fremde Lizenzen (RPFM-Schemas, `ZstdSharp.Port`) kommen mit dem Plan, der diese
Bestandteile einführt.
