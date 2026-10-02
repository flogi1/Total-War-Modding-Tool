# Template-Checkliste für neue Projekte

Abgeleitet aus den Architekturentscheidungen im Paradox Modding Tool (PMT), Stand 2026-09-27.
Gedacht für Windows-Desktop-Programme (C#/.NET), die meisten Punkte gelten aber sprachunabhängig.

**So benutzen:** Datei ins neue Projekt kopieren (z. B. `docs/checkliste.md`) und abhaken.
Abschnitte mit *(bei Bedarf)* nur bearbeiten, wenn das Projekt die Funktion hat. Wenn ein Punkt
bewusst nicht umgesetzt wird, kurz mit Begründung notieren statt einfach leer zu lassen.

---

## Teil A — Ableitungen für neue Projekte

### A1. Aus PMT übernehmen (hat sich bewährt)

| Entscheidung | Warum |
|---|---|
| Fachlogik in UI-freien Libraries, UI als dünne Hülle | Logik vollständig per Unit-Test prüfbar; UI-Tests bleiben ein kurzer manueller Durchlauf |
| Eine App-Assembly als einziger Ort, der alles zusammensetzt | Klare Abhängigkeitsrichtung, Module bleiben austauschbar |
| Interfaces nur an der Außenwelt (Prozesse, HTTP, externe APIs) | Tests ohne Netz und ohne Fremdprogramme, ohne überall Abstraktionen zu erzwingen |
| Handgeschriebene Fakes statt Mocking-Library | Weniger Abhängigkeiten, Fakes sind lesbar und wiederverwendbar |
| Atomare Writes (temp + rename) und Fallback auf Defaults bei kaputten Dateien | Kein Datenverlust bei Absturz, kaputte Datei blockiert nie den Start |
| Stabile IDs statt Pfade als Identität | Umbenennen/Verschieben verliert keine Daten |
| App-Daten nie in Nutzerinhalten ablegen | Nutzerordner bleibt sauber (Upload, Versionskontrolle, Weitergabe) |
| Datengetriebene Profile statt Sonderfälle im Code | Neues Ziel = neue Datei, keine neue Version nötig |
| Schichten: ausgeliefert < global < Projekt, ausgelieferte Daten unveränderlich | Updates verbessern Defaults, ohne Nutzeranpassungen zu überschreiben |
| Read-only als Default, Schreiben nur mit Vorschau → Bestätigung → Snapshot → Undo | Nutzer vertraut dem Tool; Fehler sind reparierbar |
| Quelltext bleibt die Wahrheit, Editoren machen minimale Span-Edits | Kommentare, Formatierung und Diffs bleiben erhalten |
| Pure Compute getrennt von Side-Effect-Write | UI (Vorschau, Entscheidungen) kann sich dazwischen schieben; leicht testbar |
| Unsicherheit sichtbar machen (Flags, Tooltips, „stale“-Hinweise) | Kein stilles Raten; Nutzer weiß, wem er trauen kann |
| Auth an vorhandene Tools delegieren, keine Secrets in der exe | Keine Credentials im eigenen Code, kein extrahierbarer Schlüssel |
| Signiertes Update-Manifest, Health-Marker, automatischer Rollback | Sichere Updates ohne Installer, kaputtes Update repariert sich selbst |
| Portable self-contained ZIP statt Installer | Einfaches Update durch Ordnertausch, keine Admin-Rechte |
| Ein Settings-Modell mit `Changed`-Event, ein Einstellungsfenster, jede Einstellung an genau einem Ort | Kein Zustand in UI-Controls, Einstellungen sind auffindbar |
| UI-Texte als flache JSON-Dateien mit Fallback-Kette und Validator | Übersetzbar ohne Code, Community kann beitragen |
| Spec mit Non-Goals, gemessenem Ist-Zustand und verworfenen Alternativen | Scope bleibt klein, Entscheidungen sind später nachvollziehbar |
| Spikes messen, bevor man auf Annahmen baut | Mehrfach haben Messungen den Plan geändert (z. B. Round-Trip-Rate 168/661) |

### A2. Anders machen als in PMT (ab Tag 1)

| PMT-Stand | Für neue Projekte | Warum |
|---|---|---|
| Kein `Directory.Build.props`, jedes csproj wiederholt Einstellungen | `Directory.Build.props` mit `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `LangVersion`, gemeinsamer Version | Einheitliche Regeln, eine Stelle zum Ändern |
| Paketversionen pro Projekt gepinnt | Central Package Management (`Directory.Packages.props`) | Keine Versionsdrift zwischen Projekten |
| Kein globaler Exception-Handler, ~125 lokale `catch (Exception)` | Globaler Handler (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) mit Crash-Dialog und Log | Unbekannte Fehler werden sichtbar statt verschluckt |
| Kein Logging | Einfaches rotierendes File-Log in `%LocalAppData%`, Knopf „Log öffnen“ und Anhang für Bug-Reports | Fehler beim Nutzer nachvollziehbar |
| `test.bat` führt nur 18 von 31 Testprojekten aus | `dotnet test` über die ganze Solution, keine handgepflegte Liste | Neue Testprojekte werden nicht vergessen |
| Keine CI | CI für Build + Test bei jedem Push (Signieren bleibt lokal, Schlüssel offline) | Regressionen früh sehen, ohne den Signing-Key auf CI zu legen |
| Kein MVVM, `MainWindow` in ~30 partial files | ViewModels für jede nicht-triviale View (z. B. CommunityToolkit.Mvvm) | UI-Logik testbar, Fenster bleiben klein |
| Mehrere statische Singletons (`ThemeManager`, `UiText.Current`) | Services als Instanzen im Composition Root erzeugen und übergeben; DI-Container optional | Testbarkeit, keine versteckten Abhängigkeiten |
| Keine generische Schema-Version, Einzel-Migrationen im Code | `schemaVersion` in jeder gespeicherten Datei, Migrationskette `vN → vN+1` mit Tests | Formatänderungen planbar statt Sonderfall-Logik |
| Kein Clock-Interface | `TimeProvider` (.NET 8) injizieren, wo Zeit Logik beeinflusst | Zeitabhängige Tests (Update-Intervall, Timeouts) deterministisch |

---

## Teil B — Checkliste

### 0. Projektstart

- [ ] Spec schreiben: Ziele, **Non-Goals**, gemessener Ist-Zustand, Architektur mit **verworfenen Alternativen**, Fehlerfälle, Tests, Umsetzungsreihenfolge, „Annahmen, die der erste echte Lauf prüft“
- [ ] Spikes für riskante Annahmen (Performance, Dateigrößen, Round-Trip) durchführen und Ergebnis in die Spec schreiben
- [ ] Backlog-Datei anlegen; alles, was nicht jetzt gebaut wird, dort notieren
- [ ] Git-Repo, `.gitignore`, LICENSE, README
- [ ] Solution-Struktur: `src/`, `tests/`, `tools/`, `docs/`
- [ ] `Directory.Build.props` (Nullable, ImplicitUsings, TreatWarningsAsErrors, Version)
- [ ] `Directory.Packages.props` (Central Package Management)
- [ ] Skripte: `build`, `test` (ganze Solution), `run`
- [ ] CI: Build + Test bei jedem Push
- [ ] Changelog mit Abschnitt „Unreleased“ (Neu / Geändert / Behoben), aus Nutzersicht

### 1. Schichtung und Struktur

- [ ] Fachlogik in UI-freien Libraries (`net8.0`, nicht `-windows`)
- [ ] Nur die App-Assembly referenziert alle Module (Composition Root)
- [ ] Kleine Leaf-Libraries ohne Projektreferenzen (Storage, Localization, UndoRedo)
- [ ] Rückkanäle Modul → App über Host-Interfaces, nie direkte Referenz auf die App
- [ ] Interfaces für Prozesse, HTTP (`HttpClient` injizieren), externe APIs
- [ ] Dateisystemzugriff über übergebene Root-Pfade, damit Tests im Temp-Ordner laufen
- [ ] `TimeProvider` injizieren, wo Zeit Logik beeinflusst
- [ ] ViewModels für nicht-triviale Views
- [ ] Keine statischen Singletons für Services
- [ ] Code, den zwei Seiten teilen müssen (z. B. Signieren/Prüfen), in einer gemeinsamen Library
- [ ] *(bei Bedarf)* Eine exe mit mehreren Modi über Kommandozeilen-Flags (Update-Apply, Helper-Prozess)

### 2. Fehlerbehandlung und Logging

- [ ] Globaler Exception-Handler (UI-Thread, AppDomain, unbeobachtete Tasks)
- [ ] Crash-Dialog mit „Log öffnen“ und „Fehler melden“
- [ ] Rotierendes File-Log in `%LocalAppData%\<App>\logs`, begrenzte Größe
- [ ] Bug-Report-Link mit vorausgefülltem Issue, **ohne personenbezogene Daten**
- [ ] Fehlermeldungen in Klartext mit Handlungshinweis

### 3. Daten und Persistenz

- [ ] Alle Nutzerdaten in `%APPDATA%\<App>`, große/maschinengebundene Daten in `%LocalAppData%`
- [ ] **Nie** in Ordner schreiben, die der Nutzer verwaltet oder weitergibt
- [ ] Stabile IDs (GUID) für Entitäten, Pfade nur als Attribut
- [ ] Pfad-Zuordnung per längstem Präfix an Verzeichnisgrenze (`C:\a\mod` passt nicht auf `mod2`)
- [ ] Atomare Writes: temp-Datei + rename, Retry gegen Virenscanner-/Indexer-Locks
- [ ] `schemaVersion` in jeder Datei, Migrationskette mit Tests
- [ ] Neue Felder mit Defaults, die das alte Verhalten ergeben
- [ ] Echter Formatbruch → neuer Dateiname (alte Builds überschreiben sonst die neue Datei als „korrupt“)
- [ ] Kaputte Datei → nach `.bad` kopieren, mit Defaults weitermachen, Start nie blockieren
- [ ] Beim Laden still reparieren (`Sanitize`), beim Editieren mit lesbarer Meldung ablehnen
- [ ] Globale Einstellungen und Einstellungen pro Kontext in getrennten Dateien; Test, dass jede Property genau einer Seite zugeordnet ist
- [ ] Zeitgestempelte Backups vor jedem Überschreiben fremder oder wichtiger Daten
- [ ] *(bei Bedarf)* Mehrere Instanzen: Named Mutex beim Schreiben, 3-Wege-Merge (Disk / Baseline / Current) beim Speichern
- [ ] *(bei Bedarf)* Eine Instanz pro Kontext: exklusiv geöffnete `.lock`-Datei (OS gibt sie beim Crash frei)
- [ ] *(bei Bedarf)* Crash-Recovery: eine Datei pro ungespeichertem Dokument, plus mtime der Quelldatei
- [ ] *(bei Bedarf)* Single-Instance-Handoff über Named Pipe

### 4. Konfiguration datengetrieben *(bei Bedarf)*

- [ ] Varianten/Ziele als JSON-Profile; Code bietet ein festes Menü an Strategien, das Profil wählt daraus
- [ ] Zweites Testprofil, das beweist, dass kein Zielwissen im Code steckt
- [ ] Schichten: ausgeliefert < global < Kontext < Projekt; jede Schicht eine eigene Datei
- [ ] Ausgelieferte Daten unveränderlich; Anpassen = „Kopie erstellen“ (neue ID) + Original auf dieser Schicht deaktivieren
- [ ] Deaktivieren per ID (`disabledRuleIds`), nie löschen
- [ ] „Sicher“-Flag auf Regeln; unbestätigte Regeln als unsicher markieren, Befunde erben das Flag
- [ ] Nutzersprache statt Regex (`*`, `#`), intern kompiliert; Live-Vorschau und Trefferzähler im Editor
- [ ] Kataloge aus echten Daten lernen; neu lernen bei geändertem Fingerprint (Pfad + Größe + mtime)
- [ ] Externe Doku offline per Maintainer-Skript in versioniertes JSON mit Zeitstempel einbacken
- [ ] Anleitung „Neues Ziel hinzufügen“ als Schritt-für-Schritt-Dokument
- [ ] Unlesbare Profildateien überspringen, nicht abstürzen

### 5. Nicht-destruktiv arbeiten

- [ ] Default read-only; Schreibgrenze explizit festlegen (welche Ordner/Dateien darf das Tool ändern?)
- [ ] Originale und Fremdquellen nie schreiben
- [ ] Jede mutierende Aktion: Vorschau → Bestätigung → Snapshot → ein Undo-Schritt
- [ ] Schreiben: Snapshot → temp-Dateien → Swap; Fehlermeldung nennt den Snapshot
- [ ] Kein Teilzustand bei Fehler
- [ ] Irreversible Aktionen mit starker Bestätigung (z. B. Namen eintippen), reversible ohne Nachfrage
- [ ] Globale Undo-History (Command-Pattern), auch für Dateioperationen; `Execute` + `Record`, Composite-Actions, Obergrenze
- [ ] Temp-/Staging-Ordner immer in `finally` aufräumen

### 6. Text und Parser *(bei Bedarf: Tool bearbeitet strukturierte Textdateien)*

- [ ] Quelltext ist Single Source of Truth; strukturierte Editoren machen minimale Span-Replacements
- [ ] Nie ganze Dateien neu generieren
- [ ] Round-Trip-Rate an echten Dateien messen, bevor man einem Serializer vertraut
- [ ] Jeder Knoten kennt Full-, Header- und Body-Span
- [ ] Test-Invariante: Spans + Lücken ergeben die Datei byte-genau
- [ ] Parser tolerant: Diagnostics statt Exceptions, fehlende Klammern nur im Baum ergänzen
- [ ] Syntax und Semantik trennen (neutraler AST + Resolver)
- [ ] Große Dateien streamen statt vollem AST
- [ ] Editor re-parst mit Debounce (~300 ms); bei Syntaxfehler letzten gültigen Stand mit „stale“-Hinweis zeigen

### 7. Diff und Merge *(bei Bedarf)*

- [ ] 3-Wege-Merge gegen gemeinsame Basis
- [ ] Konflikte zeigen, nicht automatisch lösen; konservativer Default
- [ ] Compute (pur) und Write (Side Effects) getrennt
- [ ] Pipeline aus puren Stufen; Undo = Entscheidung ändern; manuelle Änderung = eigene Entscheidung
- [ ] Wiederholte Elemente nach Inhalt matchen, nicht nach Position; Gleichstand nie still paaren
- [ ] Abgestufte Ergebnisse (conflict / review / auto / decided / stale / not needed)
- [ ] Entscheidungen an Key + Checksum der Eingaben binden; geänderte Stellen werden „stale“ und zeigen den alten Wert
- [ ] Vergleichsbasis austauschbar (eine Funktion)
- [ ] Strukturell diffen, nicht textuell (Whitespace ignorieren)
- [ ] Laufvergleich mit normalisiertem Key (Zeilennummern, Zeitstempel entfernen)

### 8. Performance und Hintergrundarbeit

- [ ] Operationen: `Task.Run` + `IProgress<T>` + `CancellationToken`
- [ ] Jede vom Nutzer gestartete Operation läuft immer über die Fortschritts-Komponente; das Fenster erscheint erst nach Anzeigeverzögerung (~150 ms, zentrale Konstante), ist die Operation vorher fertig, wird es nie angezeigt; automatische Hintergrundläufe bleiben still
- [ ] Hauptfenster ab Start der Operation für Eingaben sperren (auch während der Verzögerung) und in jedem Fall wieder freigeben (Ende, Abbruch, Fehler)
- [ ] Eine wiederverwendbare Fortschritts-Komponente (z. B. `ProgressRunner.Run`), keine Einzellösungen
- [ ] Fortschrittsfenster modal (sperrt Hauptfenster bis Ende oder Abbruch); Werkzeugfenster bleiben nicht-modal
- [ ] Anzeige in Prozent mit Balken, wenn Gesamtmenge bekannt, sonst unbestimmt; dazu Zwischenstand als Text („Datei 120 / 4000")
- [ ] Keine Teilergebnisse während des Laufs, Ergebnisse erst am Ende
- [ ] Abbrechen-Knopf: Token setzen, bisherige Ergebnisse behalten, Fenster schließen, UI freigeben
- [ ] Operation startet sofort, nicht mit dem Fenster; keine Race Condition zwischen `ShowDialog()` und Fertigstellung (nie `Close()` vor `ShowDialog()`)
- [ ] Test: Operation endet vor der Verzögerung (Fenster nie erzeugt), danach (Fenster wird geschlossen), wirft Exception, wird abgebrochen
- [ ] Exceptions in der Komponente abfangen, Fenster schließen, Fehler melden; Fenster darf nie offen hängen bleiben
- [ ] Indizes im Hintergrund bauen, mit Fingerprint cachen; UI bleibt bedienbar und zeigt „lädt“
- [ ] Vor teurer Arbeit filtern (nur relevante Dateien parsen)
- [ ] `FileSystemWatcher` nur als Signal, Lesen per Timer (bündelt Bursts, übersteht Log-Rotation)
- [ ] Debounce über `DispatcherTimer`
- [ ] *(bei Bedarf)* Master-Schalter für schwere Subsysteme, ohne Neustart

### 9. Externe Integrationen *(bei Bedarf)*

- [ ] Dünne Adapter hinter Interfaces, Logik mit Fakes testbar
- [ ] Auth delegieren (`gh`, Git Credential Manager, Fremd-Client); Credentials nie selbst sehen oder speichern
- [ ] Kindprozesse: kein Fenster, stdin geschlossen, Timeout beendet den Prozessbaum, Cancel unterstützt
- [ ] Kein zentrales Secret in der exe; nutzereigene Tokens mit DPAPI speichern, nie loggen
- [ ] Installiertes Tool vor heruntergeladenem; Download nur nach Bestätigung und SHA-256-Prüfung
- [ ] Sync mit Feld-Ownership: nur Service-Felder überschreiben, remote zuerst, lokal erst nach Erfolg
- [ ] Fehlgeschlagener Abruf ändert nichts; verschwundene Einträge weich markieren statt löschen
- [ ] Teilfehler isolieren, Ergebnis pro Ziel melden
- [ ] Vollständige externe Ausgabe im Log/Fenster zeigen
- [ ] Prüfen getrennt vom Ausführen: Pre-Flight-Checkliste (rot blockiert, grau ist Hinweis)
- [ ] Keine unnötigen NuGet-Pakete; Fremd-DLLs nicht mitliefern, wenn der Nutzer sie hat

### 10. Self-Update *(bei Bedarf)*

- [ ] Portable, self-contained ZIP; Programmordner zur Laufzeit read-only
- [ ] Signiertes Manifest (ECDSA P-256) mit Version, Hash, Größe, Asset-Name, `minApplyProtocol`
- [ ] Prüfreihenfolge: Signatur → Version = Tag und > installiert → Asset-Name → Protokoll → Hash + Größe
- [ ] Download nach `.part` streamen, Hash dabei berechnen, erst nach Prüfung umbenennen
- [ ] Nichts aus dem Download ausführen, bevor alle Prüfungen bestanden sind
- [ ] Staging-Ordner → altes Install nach Backup → neue Version setzt Health-Marker vor `Show()`
- [ ] Ohne Marker nach Timeout (~90 s) automatischer Rollback
- [ ] Updater fasst nur Dateien an, die er selbst installiert hat (Install-Dateiliste)
- [ ] Apply-Kommandozeile ist ein Vertrag: nur Parameter hinzufügen, unbekannte ignorieren, Bruch nur über höhere Protokollnummer
- [ ] Update nur auf Nutzeraktion, abbrechbar, nie still; Check höchstens alle 24 h
- [ ] Kein Elevation-Pfad
- [ ] Lock gegen zwei gleichzeitige Updates
- [ ] Lokaler Test-Feed per Umgebungsvariable (nur Debug-Build), Schalter zum Testen des Rollbacks
- [ ] Self-Update aus, solange kein Public Key hinterlegt ist

### 11. Release

- [ ] Release-Tool: Logik in Core-Library, Fenster nur als Hülle mit Status und Live-Log pro Schritt
- [ ] Ablauf: Checks → Build → Test → Publish → Paket → Signieren → Zusammenfassung bestätigen → Commit → Push → Release
- [ ] Bei Fehler bis zum lokalen Commit zurückrollen; danach nur den fehlgeschlagenen Schritt wiederholen
- [ ] Signing-Key offline, passwortgeschützt, Passwort nur über Tastatur (nie Umgebungsvariable)
- [ ] Schlüsselverlust dokumentieren: beendet Auto-Updates
- [ ] *(bei Bedarf)* Private Historie, bereinigte öffentliche Historie (ohne `docs/`, noreply-Autor), vor Push prüfen
- [ ] Version an genau einer Stelle (`<Version>` in `Directory.Build.props`)
- [ ] Changelog in der App unter Hilfe → Changelog

### 12. UI

- [ ] Menü = Werkzeuge; alle Einstellungen in einem Einstellungsfenster (Kategorien links, Seite rechts)
- [ ] Einstellungen wirken sofort, kein OK/Abbrechen; jede Einstellung an genau einem Ort
- [ ] Ein Settings-Modell mit `Changed`-Event, nie Zustand aus UI-Controls lesen
- [ ] Beim Umbau: kein Zwischenstand darf eine Einstellung unerreichbar machen
- [ ] Toolbars aus einem Katalog, jedes Element ausblendbar, Separator nur zwischen sichtbaren Gruppen
- [ ] Theming über Tokens + `DynamicResource`, live umschaltbar; Option „System“ folgt Windows; dunkle Titelleiste
- [ ] Theme vor dem ersten Fenster anwenden; Splash bis `ContentRendered`
- [ ] Fensterpositionen gegen den virtuellen Bildschirm prüfen (abgesteckter Monitor)
- [ ] Nicht-modale Tool-Fenster
- [ ] Unsicherheit zeigen: deaktivierte Controls mit Tooltip „warum“, Hinweise bei veralteten Daten
- [ ] Herkunft zeigen: jeder aufgelöste Wert kennt Datei und Zeile
- [ ] Hotkey-Referenzliste mit Test, der die Handler im Code findet
- [ ] Keine Ellipsen in Labels; alle Texte über die Texttabelle
- [ ] Neustart = Relaunch nach Exit, nachdem Locks freigegeben sind

### 13. UI-Lokalisierung

- [ ] Eine flache JSON-Datei pro Sprache in `locales/`; Keys mit `_` sind Kommentare
- [ ] Aufruf `Get(key, fallback)`; Texte pro Modul in einer `*Texts`-Klasse
- [ ] Fallback-Kette: gewählte Sprache → en → Quellsprache → Inline-Text
- [ ] User-Ordner überschreibt Programm-Ordner und überlebt Updates; User-Kopie wird nie Fallback-Basis
- [ ] Validator/CLI: JSON-Fehler mit Zeile, fehlende/unbekannte Keys, Platzhalter-Abgleich, Überlänge, Maschinenübersetzungs-Flag
- [ ] Test streng nur für die Referenzsprache
- [ ] *(bei Bedarf)* Community: `TRANSLATING.md`, Issue-Template, Glossar, maschinell übersetzte Startdateien markieren

### 14. Tests

- [ ] Ein Testprojekt pro Modul (xUnit)
- [ ] Test-First für alle Libraries (RED → GREEN → Commit)
- [ ] Handgeschriebene Fakes (`FakeProcessRunner`, `FakeHttpHandler`, …)
- [ ] Temp-Verzeichnis pro Test für Dateisystem-Tests
- [ ] WPF-Komponenten über STA-Thread-Helper testen
- [ ] Tests auf echten Daten mit Invarianten statt fester Zahlen; überspringen, wenn Fixture fehlt
- [ ] Parity-Tests, wenn hartcodierte Regeln zu Daten werden
- [ ] Regressionstests für jede Migration
- [ ] Manuelle UI-Checkliste pro Feature als Abnahme

### 15. Entwicklungsprozess

- [ ] Brainstorm → Spec → Plan(s) → Task-Briefs → TDD → Review pro Task
- [ ] Große Features in sequentielle Pläne; nächster Plan erst, wenn echte Interfaces existieren
- [ ] Plan enthält: globale Constraints, Rulings (Abweichungen von der Spec, Spec im selben Commit anpassen), Review-Fokus
- [ ] Task-Brief: betroffene Dateien, genutzte und erzeugte Interfaces, TDD-Schritte, Commit-Message
- [ ] `progress.md` pro Feature: Status, Commit-Bereiche, Fix-Runden (max. 5), aufgeschobene Kleinigkeiten
- [ ] Worktree + Branch pro Feature; Baseline-Build und -Tests vor Task 1 festhalten
- [ ] Bestehende Test-Suite muss grün bleiben
- [ ] Lebende Specs: Abweichungen und Umentscheidungen in der Spec dokumentieren
- [ ] Bugs aus manuellen Tests als ungeplante Fixes mit Ursache festhalten
- [ ] Felder und Texte erst mit dem Plan einführen, der sie nutzt (YAGNI)
- [ ] Modellstärke pro Phase wählen (Wechsel mit `/model`; Namen bei neuen Modellen hier anpassen):

| Phase | Modellstärke | Warum |
|---|---|---|
| Brainstorming, Spec | stark | Fehler hier wirken auf jede Implementierung; wenig Text, geringe Kosten |
| Spec-Abnahme (gegen Non-Goals und offene Annahmen gegenlesen) | stark | Findet Lücken, bevor Code entsteht |
| Plan, Task-Briefs | stark oder mittel | Schnittstellen müssen stimmen |
| Umsetzung nach Task-Brief (TDD) | mittel | Aufgabe ist klein und klar; spart Kosten und Limit |
| Review pro Task, Abschlussreview | stark | Prüft Abweichungen von Spec und Brief |
| Fehlersuche bei unklarer Ursache | stark | Hypothesen bilden und Evidenz bewerten |

- [ ] Dem starken Modell beim Brainstorming die relevanten Dateien der Wissensbasis mitgeben; Annahmen ohne Beleg als „zu messen“ markieren

---

## Referenz: Wo in PMT nachschauen

Pfade relativ zu `C:\Users\flori\Documents\Claude Projekte\Paradox Modding Tool\Runde 10.1\`.

| Thema | Datei |
|---|---|
| Atomare Writes, Mutex | `src/ParadoxModTool.Storage/SharedFile.cs` |
| 3-Wege-Merge von Collections | `src/ParadoxModTool.Storage/CollectionMerge.cs` |
| Global vs. Kontext-Einstellungen | `src/ParadoxModTool.Session/SessionPartition.cs` |
| Instanz-Lock | `src/ParadoxModTool.Session/GameLock.cs` |
| Crash-Recovery | `src/ParadoxModTool.Session/RecoveryStore.cs` |
| Settings-Modell | `src/ParadoxModTool.Settings/AppSettings.cs` |
| Undo/Redo | `src/ParadoxModTool.UndoRedo/` |
| Theming | `src/ParadoxModTool.Theming/`, `src/ParadoxModTool.App/ThemeManager.cs` |
| Prozess-Adapter | `src/ParadoxModTool.GitIntegration/ProcessRunner.cs` |
| Self-Update | `src/ParadoxModTool.Updates/` |
| Release-Tool | `tools/ParadoxModTool.Release.Core/ReleaseRun.cs`, `PublicCommit.cs`, `Changelog.cs` |
| Signieren | `tools/ParadoxModTool.ReleaseSigner/` |
| Lokalisierung | `src/ParadoxModTool.Localization/`, `tools/ParadoxModTool.LocaleTool/`, `TRANSLATING.md` |
| Datei-Watcher | `src/ParadoxModTool.ErrorLog/LiveLogWatcher.cs` |
| Single-Instance-Handoff | `src/ParadoxModTool.LaunchControl/EditorHandOffPipe.cs` |
| STA-Test-Helper | `tests/ParadoxModTool.Editor.Tests/Sta.cs` |
| Neues Ziel hinzufügen | `docs/pmt-adding-a-game.md` |
| Specs mit Begründungen | `docs/superpowers/specs/` (v. a. self-update, layered-rules, counting-rules, compatch-merge, gui-editor, ui-reorganisation) |
