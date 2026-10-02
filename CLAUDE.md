# CLAUDE.md

<!-- Vorlage aus den PMT-Architekturentscheidungen (Stand 2026-09-27).
     Beim Projektstart: Platzhalter <...> ausfüllen, nicht zutreffende optionale Abschnitte löschen.
     Ausführliche Begründungen und Checkliste: Projekt-Checkliste.md (nach docs/checkliste.md kopieren). -->

## Projekt

- **Name:** Total War Modding Tool
- **Zweck:** Leichteres Modding von Total-War-Spielen.
- **Wissensbasis:** `Wissensbasis/` liefert bei Bedarf Kontext zum Projekt (Spielwissen, Formate, Quellen). Nur die für die Aufgabe relevanten Dateien lesen, nicht die ganze Basis laden. Die Struktur legt der Nutzer fest.
- **Stack:** C# / .NET 8, WPF (`net8.0-windows`) für UI, `net8.0` für alle Libraries, xUnit
- **Sprache Code:** Code, Bezeichner und Code-Kommentare Englisch. Specs, Pläne und Backlog Deutsch (der Nutzer erfasst Nuancen auf Deutsch am besten). Chat-Antworten an den Nutzer auf Deutsch.
- **UI-Sprachen:** Hauptsprachen Deutsch und Englisch, weitere Sprachen müssen jederzeit ergänzbar sein (nur neue Sprachdatei, kein Code). **Standardsprache für alle Nutzer ist Englisch**, auch wenn das System auf einer anderen Sprache läuft und keine passende Sprachdatei existiert.

## Befehle

```bash
build.bat          # dotnet build der ganzen Solution
test.bat           # dotnet test der ganzen Solution, keine handgepflegte Projektliste
run.bat            # App starten
```

## Struktur

```
src/<App>.App/        WPF-App, einziger Composition Root
src/<App>.<Modul>/    UI-freie Libraries (net8.0)
tests/<App>.<Modul>.Tests/
tools/                Release-Tool, Hilfs-CLIs
docs/specs/, docs/plans/, docs/checkliste.md
Wissensbasis/         Projektwissen (Formate, Quellen)
Backlog.txt
```

## Arbeitsweise

- Vor neuen Features: Spec in `docs/specs/` mit Zielen, **Non-Goals**, gemessenem Ist-Zustand, verworfenen Alternativen, Fehlerfällen, Tests und „Annahmen, die der erste echte Lauf prüft“.
- Riskante Annahmen (Performance, Dateigrößen, Round-Trip) erst per Spike messen, Ergebnis in die Spec schreiben.
- Große Features in sequentielle Pläne aufteilen. Den nächsten Plan erst schreiben, wenn die Interfaces des vorigen existieren.
- Weicht die Umsetzung von der Spec ab: Spec im selben Commit anpassen.
- Libraries test-first: failing Test → RED prüfen → implementieren → GREEN → Commit.
- Die bestehende Test-Suite muss grün bleiben. Vor der ersten Änderung Baseline-Build und -Tests festhalten.
- UI-Änderungen: dem Nutzer eine konkrete manuelle Testcheckliste liefern.
- Alles, was nicht jetzt gebaut wird, in `Backlog.txt` notieren statt vorsorglich einzubauen (YAGNI). Felder und Texte erst mit dem Plan einführen, der sie nutzt.
- Am Ende jeder Phase (Brainstorming, Spec, Plan, Umsetzung, Review) kurz sagen, dass die Phase fertig ist, und welche Modellstärke die nächste Phase braucht: stark für Brainstorming, Spec und Review, mittel für Umsetzung nach Task-Brief. Der Nutzer wechselt das Modell selbst. Tabelle siehe `docs/checkliste.md` Abschnitt 15.
- Changelog-Abschnitt „Unreleased“ (Neu / Geändert / Behoben) bei jeder nutzersichtbaren Änderung pflegen, aus Nutzersicht formuliert.

## Architektur-Regeln

- Fachlogik gehört in UI-freie Libraries. Die UI ist eine dünne Hülle.
- Nur die App-Assembly referenziert alle Module. Module referenzieren nie die App; Rückkanäle laufen über Host-Interfaces (`I<Modul>Host`).
- Nicht-triviale Views bekommen ViewModels. Keine Logik in Code-behind außer reiner View-Verdrahtung.
- Keine statischen Singletons für Services. Services im Composition Root erzeugen und übergeben.
- Interfaces nur an der Außenwelt: Prozesse (`IProcessRunner`), HTTP (`HttpClient` injizieren), externe APIs, Zeit (`TimeProvider`). Nicht alles vorsorglich abstrahieren.
- Dateisystemzugriff über übergebene Root-Pfade, damit Tests in einem Temp-Ordner laufen.
- Berechnung (pur) und Schreiben (Side Effects) trennen, z. B. `Compute…` und `Write…`.
- Code, den zwei Seiten gleich verstehen müssen (Signieren/Prüfen, Serialisieren/Lesen), liegt in einer gemeinsamen Library.
- Build-Einstellungen nur in `Directory.Build.props` (Nullable, ImplicitUsings, TreatWarningsAsErrors, Version). Paketversionen nur in `Directory.Packages.props`.
- Neue NuGet-Pakete nur nach Rückfrage.

## Daten und Persistenz

- Nutzerdaten in `%APPDATA%\<App>`, große oder maschinengebundene Daten in `%LocalAppData%\<App>`.
- Nie in Ordner schreiben, die der Nutzer verwaltet oder weitergibt.
- Entitäten bekommen stabile IDs (GUID). Pfade sind Attribute, nicht Identität.
- Pfad-Zuordnung per längstem Präfix an Verzeichnisgrenze.
- Immer atomar schreiben: temp-Datei + rename, mit Retry gegen Virenscanner-Locks. Nie direkt `File.WriteAllText` auf Zieldateien.
- Jede gespeicherte Datei hat `schemaVersion`. Migrationen als Kette `vN → vN+1`, jede mit Regressionstest.
- Neue Felder bekommen Defaults, die das alte Verhalten ergeben. Echter Formatbruch = neuer Dateiname.
- Kaputte Datei: nach `.bad` kopieren, mit Defaults weitermachen. Eine kaputte Datei darf nie den Start blockieren.
- Beim Laden still reparieren (`Sanitize`), beim Editieren mit lesbarer Meldung ablehnen.
- Globale Einstellungen und Einstellungen pro Kontext in getrennten Dateien.

## Nicht-destruktiv arbeiten

- Default ist read-only. Die Schreibgrenze (welche Dateien das Tool ändern darf) steht in der Spec.
- Originale und Fremdquellen werden nie geschrieben.
- Jede mutierende Aktion: Vorschau → Bestätigung → Snapshot/Backup → ein Undo-Schritt. Bei Fehler kein Teilzustand.
- Mehrere Dateien schreiben: Snapshot → temp-Dateien → Swap. Die Fehlermeldung nennt den Snapshot.
- Irreversible Aktionen brauchen starke Bestätigung (z. B. Namen eintippen); reversible Aktionen keine Nachfrage.
- Undo über eine globale Command-History (`Execute` für neue Aktionen, `Record` für bereits erledigte), auch für Dateioperationen.
- Temp- und Staging-Ordner immer in `finally` aufräumen.

## Fehlerbehandlung und Logging

- Globale Handler für `DispatcherUnhandledException`, `AppDomain.UnhandledException` und `TaskScheduler.UnobservedTaskException` mit Crash-Dialog („Log öffnen“, „Fehler melden“).
- Rotierendes File-Log in `%LocalAppData%\<App>\logs`. Nie Tokens, Passwörter oder personenbezogene Daten loggen.
- Kein pauschales `catch (Exception)` ohne Log-Eintrag.
- Fehlermeldungen im Klartext mit Handlungshinweis.
- Unsicherheit sichtbar machen statt still raten: „unsicher“-Flags, deaktivierte Controls mit Tooltip „warum“, Hinweis bei veralteten Daten.

## Hintergrundarbeit

- Operationen laufen mit `Task.Run` + `IProgress<T>` + `CancellationToken`.
- Jede vom Nutzer gestartete Operation (Suche, Scan, Import, Export) läuft immer über die Fortschritts-Komponente, egal wie lang sie dauert. Das Fenster erscheint erst nach einer Anzeigeverzögerung von ~150 ms (eine Konstante, zentral änderbar). Ist die Operation vorher fertig, wird es nie angezeigt, damit nichts aufblitzt. Automatische Hintergrundläufe bleiben still.
- Ab dem Start der Operation sind Eingaben im Hauptfenster gesperrt, auch während der Verzögerung. Das Hauptfenster wird in jedem Fall wieder freigegeben (Ende, Abbruch, Fehler).
- Eine einzige wiederverwendbare Fortschritts-Komponente, keine Einzellösungen pro Feature. Aufruf z. B. `ProgressRunner.Run(title, (progress, ct) => …)`.
- Das Fortschrittsfenster ist **modal** und sperrt das Hauptfenster, bis die Operation fertig oder abgebrochen ist. Werkzeugfenster bleiben nicht-modal.
- Anzeige: Prozent und Balken, wenn die Gesamtmenge bekannt ist, sonst unbestimmter Balken. Dazu ein Zwischenstand als Text (z. B. „Datei 120 / 4000"). `IProgress<T>` trägt dafür Anzahl, Gesamt und aktuelles Element.
- Keine Teilergebnisse während des Laufs. Ergebnisse erscheinen erst am Ende.
- Abbrechen-Knopf ist Pflicht. Abbrechen setzt das `CancellationToken`, bereits ermittelte Ergebnisse bleiben erhalten, danach wird das Fenster geschlossen und die UI freigegeben.
- Die Operation startet sofort, nicht erst mit dem Fenster. Wird sie während der Verzögerung fertig, wird das Fenster nie erzeugt. Wird sie danach fertig, schließt die Komponente das Fenster selbst. Es darf keine Race Condition zwischen `ShowDialog()` und Fertigstellung geben (nie `Close()` vor `ShowDialog()`).
- Die Komponente fängt Exceptions ab, schließt das Fenster und meldet den Fehler. Das Fenster darf nie offen hängen bleiben.
- Caches und Indizes mit Fingerprint (Pfad + Größe + mtime); bei Änderung neu berechnen. Die UI bleibt währenddessen bedienbar.
- `FileSystemWatcher` nur als Signal nutzen; gelesen wird per Timer.
- Debounce mit `DispatcherTimer`.

## UI

- Menü enthält Werkzeuge. Alle Einstellungen gehören in ein Einstellungsfenster, jede an genau einem Ort, und wirken sofort (kein OK/Abbrechen).
- Ein Settings-Modell mit `Changed`-Event. Nie Zustand aus UI-Controls (`IsChecked`) lesen.
- Farben nur über Theme-Tokens und `DynamicResource`, nie hartcodiert. Das Theme vor dem ersten Fenster anwenden.
- Tool-Fenster sind nicht-modal.
- Gespeicherte Fensterpositionen gegen den virtuellen Bildschirm prüfen.
- Keine Ellipsen in Labels.

## UI-Texte und Lokalisierung

- Alle UI-Texte über `UiText.Get(key, fallback)`. Keine Literale in XAML oder Code. Der Inline-Fallback ist **Englisch**.
- Eine flache JSON-Datei pro Sprache in `locales/` (`en.json`, `de.json`, …). Keys pro Modul in einer `*Texts`-Klasse.
- Referenzsprache ist `en`. Standardsprache bei Erststart und bei unbekannter Systemsprache ist `en`.
- Fallback-Kette: gewählte Sprache → en → Inline-Fallback (Englisch).
- Neuer Key: immer in `en` und `de` eintragen. Der Locale-Test muss grün bleiben (`en` vollständig, `de` vollständig).
- Weitere Sprachen: nur neue Datei in `locales/`, kein Code. Unvollständige Sprachen fallen pro Key auf `en` zurück.
- Spielspezifische Begriffe (Einheiten, Fraktionen, Tabellennamen) sind keine UI-Texte, sondern Daten und kommen aus dem jeweiligen Spielprofil.

## Tests

- Ein Testprojekt pro Modul (xUnit). Kein Mocking-Framework, stattdessen handgeschriebene Fakes (`Fake<Name>`).
- Dateisystem-Tests im eigenen Temp-Verzeichnis, danach aufräumen.
- WPF-Komponenten über einen STA-Thread-Helper testen.
- Tests auf echten Daten prüfen Invarianten statt fester Zahlen und werden übersprungen, wenn die Fixture fehlt.
- Werden hartcodierte Regeln zu Daten, prüfen Parity-Tests das identische Verhalten.

## Optional: nur wenn das Projekt es hat

### Datengetriebene Profile / Regeln
- Neues Ziel = neue JSON-Datei, kein Code. Code bietet ein festes Menü an Strategien, das Profil wählt daraus.
- Schichten: ausgeliefert < global < Kontext < Projekt; spezifischste gewinnt. Ausgelieferte Daten sind unveränderlich; Anpassen = Kopie mit neuer ID + Original deaktivieren (`disabledRuleIds`).
- Unbestätigte Regeln tragen `certain: false`, Befunde erben das Flag.
- Unlesbare Profildateien überspringen, nicht abstürzen.

### Strukturierte Textdateien bearbeiten
- Der Quelltext ist die Wahrheit. Editoren machen minimale Span-Replacements; ganze Dateien nie neu generieren.
- Jeder AST-Knoten kennt seinen Source-Span. Test-Invariante: Spans + Lücken ergeben die Datei byte-genau.
- Der Parser ist tolerant: Diagnostics statt Exceptions. Syntax (neutraler AST) und Semantik (Resolver) getrennt.
- Große Dateien streamen statt vollem AST.

### Diff und Merge
- 3-Wege-Merge gegen gemeinsame Basis. Konflikte zeigen, nicht automatisch lösen.
- Wiederholte Elemente nach Inhalt matchen, nicht nach Position. Gleichstand nie still paaren.
- Entscheidungen an Key + Checksum binden; geänderte Eingaben machen sie „stale“.

### Externe Dienste
- Dünne Adapter hinter Interfaces. Die Logik ist ohne Netz testbar.
- Auth an vorhandene Tools delegieren (`gh`, Git Credential Manager). Nie Credentials selbst speichern; nutzereigene Tokens nur mit DPAPI.
- Kindprozesse: kein Fenster, stdin geschlossen, Timeout beendet den Prozessbaum.
- Downloads nur nach Bestätigung und SHA-256-Prüfung.
- Teilfehler isolieren, Ergebnis pro Ziel melden. Ein fehlgeschlagener Abruf ändert keinen lokalen Zustand.

### Self-Update und Release
- Portable self-contained ZIP, kein Installer. Der Programmordner ist zur Laufzeit read-only.
- Signiertes Manifest (ECDSA P-256). Prüfreihenfolge: Signatur → Version = Tag und > installiert → Hash + Größe. Vorher nichts aus dem Download ausführen.
- Health-Marker der neuen Version; fehlt er nach Timeout, erfolgt automatischer Rollback.
- Update nur auf Nutzeraktion, nie still, keine Elevation.
- Die Apply-Kommandozeile ist ein Vertrag: nur Parameter hinzufügen.
- Der Signing-Key bleibt offline und kommt nie auf CI. CI macht nur Build + Test.
- Release-Logik in einer Core-Library; das Release-Fenster ist nur Hülle.

## Nicht tun

- Keine Dateien außerhalb der festgelegten Schreibgrenze ändern.
- Keine Secrets, Tokens oder Passwörter im Code, in Logs oder in Umgebungsvariablen für Signing.
- Keine stillen Updates, keine Telemetrie ohne explizite Entscheidung in einer Spec.
- Keine Features „auf Vorrat“. Non-Goals der Spec respektieren.
- Keine ganzen Dateien neu schreiben, wenn eine gezielte Änderung reicht.
