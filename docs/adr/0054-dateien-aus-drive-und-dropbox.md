# ADR-0054 — Dateien aus Google Drive und Dropbox: nur die gewählte, im Browser geholt — OneDrive noch nicht

**Datum:** 18.10.2026 · **Status:** angenommen
**Berührt:** ADR-0035 (Unterlagen), ADR-0043 (Vorschläge aus Unterlagen),
`VISION-FRAGEN-2.md` N3

## Zusammenhang

Runde 2 will Dokumente aus Google Drive, OneDrive und Dropbox im MVP — „nur die
gewählte Datei, kein Ordnerzugriff“ (N3). Wer seine Zeugnisse in der Cloud hat,
soll sie nicht erst herunterladen müssen.

**Gemessen am 18.10.2026** an den Dokumentationen der Anbieter:

| Anbieter | Auswahl | Berechtigung | die Datei |
|---|---|---|---|
| Google Drive | Google Picker | `drive.file` — „nicht sensibel“, die App sieht nur, was die Person im Picker wählt | mit dem Token im Browser von der Drive-API |
| Dropbox | Chooser | **kein OAuth**, nur ein App-Key | ein direkter Link, vier Stunden gültig, mit CORS |
| OneDrive | File Picker v8 | `Files.Read` bzw. `OneDrive.ReadOnly` — **alle** Dateien der Person | mit dem Token über Graph/SharePoint |

## Entscheidung

**Google Drive und Dropbox kommen, OneDrive nicht.** Der OneDrive-Picker gibt
der App Lesezugriff auf alle Dateien; „nur die gewählte“ ist dort nicht zu
haben, und die Zusage aus N3 gilt. Wer OneDrive nutzt, lädt die Datei wie bisher
herunter und hoch. Sobald Microsoft einen Zugriff auf einzelne Dateien anbietet,
wird neu gemessen.

**Alles geschieht im Browser, der Server sieht keinen Anbieter.** Die Auswahl
läuft im Fenster des Anbieters, der Browser holt die Bytes und reicht sie über
den bestehenden Upload weiter (`POST /resumes/me/documents`) — dieselben
Grenzen, dieselbe Prüfung, dieselbe Löschung (ADR-0035). Kein Token erreicht
einen unserer Dienste, keiner wird gespeichert; das Google-Token lebt im
Speicher der Seite, bis die Datei geholt ist. Die Egress-Grenze der Dienste
bleibt unberührt.

**Die Skripte der Anbieter lädt erst der Klick.** Wer die Seite nur ansieht,
spricht weder mit Google noch mit Dropbox. Für eine spätere
Content-Security-Policy der Oberfläche (es gibt noch keine — vor dem Start)
stehen die Hosts hier: `apis.google.com`, `accounts.google.com`,
`www.googleapis.com`, `www.dropbox.com`, `*.dropboxusercontent.com`.

**Leer heißt aus.** Ohne Schlüssel in der Laufzeitkonfiguration der Oberfläche
(`window.__WT_CONFIG__`) erscheinen die Knöpfe nicht.

**Hochgeladen wird erst nach dem Blick der Person.** Gewählt ist nicht
gespeichert: die Datei steht im Formular wie eine von der Festplatte, und erst
„Hochladen“ legt sie ab — wie die Vorschläge aus Unterlagen, die erst „Speichern“
zur Nennung macht (ADR-0043).

## Was vor dem Bau gebraucht wird

- Google: ein Projekt mit Drive-API und Picker-API, eine OAuth-Client-ID (Web),
  ein API-Schlüssel, die Projektnummer als App-ID; `drive.file` braucht keine
  Prüfung durch Google.
- Dropbox: eine App mit Chooser-Domain; keine Freigabe nötig.
- Bis dahin sind die Knöpfe aus, und die Tests ersetzen die Anbieter.

## Was es kostet

- Zwei fremde Skripte, geladen erst beim Klick.
- Die Oberfläche bekommt zwei Konfigurationswerte je Anbieter.
- Die Nutzungsbedingungen der beiden Anbieter prüft der Jurist.
