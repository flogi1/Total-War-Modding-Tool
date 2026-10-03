# Changelog

## Unreleased

### Neu

- Erste startbare Version: Hauptfenster mit hellem und dunklem Theme, Sprache Englisch oder Deutsch, Einstellungsfenster, Log-Ordner im Menü Werkzeuge. Bei einem unerwarteten Fehler erscheint ein Dialog mit Details zum Kopieren, die App läuft danach weiter.
- Das Spiel wird über Steam gefunden. Wird es nicht gefunden oder liegt es woanders, wählst du den Spielordner in den Einstellungen (Seite „Spiel“); „Automatisch erkennen“ stellt die Suche über Steam wieder her. Ein Ordner ohne Datenordner wird mit einer Meldung abgelehnt.
- Pack-Explorer: Die Packs erscheinen nach Herkunft gruppiert (Spiel, Mods im Datenordner, Workshop), mit der Workshop-ID in Klammern. Ein Pack lässt sich aufklappen und zeigt seine Ordner und Dateien. Verschlüsselte und beschädigte Packs sind markiert, der Tooltip nennt den Grund.
- Ein Doppelklick auf eine Datei öffnet sie in einem Tab: Tabellen und Loc-Dateien als Raster (nur lesen, Spalten sortierbar), Textdateien als Text, alles andere als Hinweis mit Typ, Größen und Kompression. Textdateien über 5 MB werden gekürzt gezeigt, mit Hinweis. Eine Datei, die sich nicht lesen oder dekodieren lässt, nennt den Grund im Klartext. Tabs schließen mit dem Knopf, der mittleren Maustaste oder Strg+W; derselbe Eintrag öffnet keinen zweiten Tab.
- Wird ein Pack außerhalb der App geändert, tragen die offenen Tabs dieses Packs die Markierung „veraltet“, sobald die App die Änderung bemerkt: beim Öffnen eines Eintrags, beim Aufklappen eines Packs oder beim Aktualisieren der Pack-Liste. F5 oder Werkzeuge, Pack-Liste aktualisieren liest die Packs neu. Ändert sich das Schema (anderer Schema-Ordner oder neu geladen), tragen auch offene Tabellen-Tabs die Markierung „veraltet“, mit eigenem Hinweis; Tab schließen und den Eintrag neu öffnen zeigt ihn mit dem neuen Schema.
- Offene Tabs, der aktive Tab und aufgeklappte Packs sind nach einem Neustart wieder da.
- Schema-Ordner: In den Einstellungen lässt sich ein Ordner mit RPFM-Schema-Dateien wählen, zum Beispiel der Schema-Ordner von RPFM. Eine brauchbare Datei daraus wird statt des mitgelieferten Schemas benutzt; ist keine brauchbar, steht das in den Einstellungen und in der Statuszeile. Die Statuszeile zeigt außerdem das Spiel, die Schema-Quelle mit Datum und einen Hinweis, wenn etwas nicht stimmt.

### Geändert

### Behoben