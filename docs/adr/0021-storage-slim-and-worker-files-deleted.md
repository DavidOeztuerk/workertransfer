# ADR-0021: `worker-files` gelöscht, `worker-storage` auf ein benutztes Backend zurückgeschnitten

Date: 2026-08-02
Status: Accepted
Related: ADR-0005 (`worker-cqrs` gelöscht), ADR-0014 (`worker-security`/`worker-exceptions` gelöscht), ADR-0004 (versionierte Verträge, kein Scraping), [ROADMAP](../ROADMAP.md) Sub-step 3.5

## Kontext

`worker-storage` und `worker-files` standen seit Phase 1 auf der Platte und
waren beide **aus dem uv-Workspace ausgeschlossen**. Der Grund stand in
`pyproject.toml`: ihre C-Erweiterungen ließen sich für Python 3.14 nicht bauen,
CI scheiterte daran, Pillow aus dem Quelltext zu übersetzen.

Zusammen waren es rund 400 Zeilen, die **fünf schwere Abhängigkeiten**
deklarierten:

| Abhängigkeit | wofür | Zustand |
|---|---|---|
| `boto3` | S3 | kein Konsument |
| `minio` | MinIO | kein Konsument, spricht ohnehin S3 |
| `azure-storage-blob` | Azure Blob | kein Konsument, keine Azure-Umgebung |
| `pillow` | Bildbearbeitung | kein Konsument, kein 3.14-Wheel |
| `python-magic` | Typerkennung | kein Konsument, braucht libmagic |

Kein einziger Dienst importierte eines der beiden Pakete. Der Smoke-Test von
`worker-files` übersprang sich selbst mit „system libmagic missing".

Sub-step 3.5 hieß in der ROADMAP „`worker-files`/`worker-storage` real machen
(Workspace-Re-Include)". Beim Hinsehen war klar, dass „re-include" die falsche
Handlung ist: die Pakete waren nicht ausgeschlossen, *weil* Python 3.14 neu ist,
sondern weil sie sich für jede denkbare Zukunft gleichzeitig gerüstet hatten.

## Entscheidung

**`worker-files` wird gelöscht.** Seine 199 Zeilen waren Typerkennung
(python-magic) und Bildbearbeitung (pillow) über einem Speicher, den es nicht
selbst besaß. Die Typerkennung ist als Signaturprüfung 20 Zeilen ohne
Systembibliothek; die Bildbearbeitung braucht niemand, weil die Plattform keine
Vorschaubilder erzeugt (Portfolio-Spec: das hieße, fremde URLs vom Server aus
aufzurufen).

**`worker-storage` wird neu geschrieben und enthält einen Port und ein
Backend**, das wirklich läuft:

- `Storage` — `put`/`get`/`delete`. `get` liefert `None` statt zu werfen: „gibt
  es nicht" ist beim Abrufen ein normaler Ausgang. `delete` schweigt über einen
  unbekannten Schlüssel, damit Aufräumpfade nicht einen Unterschied behandeln
  müssen, der sie nicht interessiert.
- `LocalStorage` — Dateisystem, mit `write + rename` statt direktem Schreiben:
  ein Absturz mittendrin hinterlässt sonst eine halbe Datei unter dem richtigen
  Namen, und die sieht für jeden Leser gültig aus.
- `sniff_content_type` — Typ aus den ersten Bytes, nicht aus dem, was der Client
  behauptet. Ein `Content-Type`-Header und eine Dateiendung sind beide frei
  wählbar; die Signatur nicht. Erlaubt sind PNG, JPEG und PDF.

**Einzige Abhängigkeit: `worker-core`** (für `DomainError`). Damit ist das Paket
wieder im Workspace, ohne Ausnahme in mypy oder CI.

**Kein S3-Backend, noch nicht.** Es zu bauen, bevor eine Umgebung es braucht,
wäre genau der Fehler, der zu diesem ADR geführt hat. `Storage` ist die Naht;
ein zweites Backend kostet dann eine Datei und eine Abhängigkeit, die dann auch
jemand benutzt.

## Konsequenzen

Der Ablageort ist in einer kleinen Installation ein Volume und kein
Objektspeicher. Für eine Installation, die über eine Maschine hinauswächst, ist
das zu wenig — dann kommt `S3Storage` dazu. `StoredObject` trägt deshalb nur
einen `key` und weder Pfad noch URL: beides bindet an ein Backend, und ein Pfad
in der Datenbank überlebt keinen Umzug.

Die Signaturliste ist kurz, und das ist Absicht: jeder zusätzliche Typ ist eine
Entscheidung, die jemand treffen und begründen muss. Ein Speicher, der alles
annimmt, wird zum Ausliefern von allem benutzt.

`worker-ai` bleibt ausgeschlossen — dort ist die fehlende 3.14-Unterstützung
echt und nicht selbstgemacht.

## Verworfene Alternativen

**Die Pakete wie geplant wieder einschließen.** Hätte `pillow` und
`python-magic` auf Python 3.14 gebraucht; beide sind für den tatsächlichen
Bedarf nicht nötig, und die Wartung eines Quelltext-Builds in CI wäre der Preis
für Code, den niemand aufruft.

**Ein Backend je Cloud, wie gehabt.** Drei SDKs, von denen zwei dasselbe
Protokoll sprechen (S3), und eines eine Cloud bedient, für die es keine
Umgebung gibt. Die Auswahl trifft man, wenn man deployt, nicht vorher.

**`worker-files` behalten und nur entschlacken.** Was übrig bliebe — Typprüfung
und Größenprüfung — sind zwei Funktionen. Ein eigenes Paket dafür ist eine
Grenze, die mehr kostet, als sie trennt.
