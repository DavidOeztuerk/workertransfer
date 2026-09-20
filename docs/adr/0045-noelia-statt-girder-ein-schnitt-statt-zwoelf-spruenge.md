# ADR-0045 — Noelia 6.4.0 statt Girder 4.4.0: ein Schnitt statt zwölf Sprünge

**Datum:** 20.09.2026 · **Status:** angenommen
**Ersetzt:** nichts · **Berührt:** ADR-0027 (siehe *Was sich an ADR-0027 ändert*)

## Zusammenhang

Girder und Noelia sind **dieselbe Codelinie**. `MIGRATION.md:781` im
Noelia-Baum trägt die Überschrift *„4.4.3 code line → Noelia 5.0.0"*; alles
darüber ist Noelia-intern, alles darunter Girder-intern. Es gibt genau **eine**
Tür zwischen den beiden Identitäten, und sie steht bei `4.4.3 → 5.0.0`.

`docs/AUFTRAG-NOELIA-UMSTIEG.md` sah deshalb zwölf dokumentierte Sprünge vor,
mit `make build` und `./scripts/test-dotnet.sh` zwischen jedem — und davor eine
**Phase 0** in `~/Projects/Demo`, die beweist, dass ein gespeicherter Widerruf,
eine bestehende Sitzung und ein alter Passwort-Hash den Identitätswechsel
überleben.

Dieser Aufwand hat genau einen Grund: **5.0 ist eine Datenwanderung, kein
Umbenennen.** Die authentifizierten Domänenzeichenfolgen ändern sich, weshalb
das Kopieren von Geheimtextbytes keine Wanderung ist; der Passwort-Pfeffer muss
nach `noelia.passwords.primary`, bevor ein Dienst das erste Mal auflöst; und die
schärfste Kante ist der Einwilligungsledger — *„An empty new revocation prefix
must never be mistaken for ‚nothing was revoked'."*

## Entscheidung

### 1. Der Schnitt, und die einzige Bedingung, unter der er richtig ist

**Diese Plattform ist nicht in Betrieb.** Es gibt keine echten Konten, keine
gespeicherten Widerrufe, keine Sitzungen und keinen Passwort-Pfeffer, den
jemand verlieren könnte. Damit hat die Datenwanderung **keinen Gegenstand** —
und elf Zwischenversionen kosten nur Zeit.

Entschieden wurde deshalb: **direkt von Girder 4.4.0 auf Noelia 6.4.0**,
Datenträger geleert, keine Kompatibilitätsschicht, kein `[Obsolete]`, kein
zweiter Pfad daneben.

> **Diese Entscheidung hat ein Verfallsdatum.** Sie gilt, solange kein echtes
> Konto in diesem System liegt. Am Tag, an dem das erste liegt, ist der Weg
> zurück in die zwölf Sprünge nicht mehr offen — dann gilt wieder, was
> `MIGRATION.md` §*„Before the first Noelia process starts"* sagt, und
> `docs/AUFTRAG-NOELIA-UMSTIEG.md` bleibt genau dafür im Baum stehen.

### 2. Zwei Identitäten im selben Baum gibt es nicht

Kein `Girder`-Rest „bis später". Zwei Identitäten sind zwei Präfixe in denselben
Speichern, und der zweite ist der, den niemand pflegt. 363 Quelldateien, 71
Projektdateien und die Paketliste wurden in **einem** Commit umbenannt, damit
der nächste Leser ihn überspringen kann.

**Historische Versionsnennungen bleiben Girder.** *„Girder 4.3.0 hatte einen
Substring-Fehler in seiner Maskierung"* ist eine Tatsache über eine
veröffentlichte Version; sie umzubenennen hieße, Geschichte zu fälschen — und
das ist dieselbe Regel, aus der `docs/KI-EINSATZ-PRUEFUNG.md` nicht
umgeschrieben wurde, sondern einen Verweis bekam.

### 3. Was der Umstieg wirklich gekostet hat

Die Vorhersage des Auftrags — *„Das ist zeichengleich Noelias API"* — hat
gehalten. Über zwölf Versionen hinweg nannte der Übersetzer **drei**
Änderungen, jede in `MIGRATION.md` dokumentiert:

| Was | Seit | Fundstelle |
|---|---|---|
| `ISovereignAuditSink` zog aus `Noelia.Infrastructure.Audit` in `Noelia.Abstractions.Audit` | 5.2.0 | `:482` |
| `ITokenSessionService`, `SignInResult`, `RefreshResult` nach `Noelia.Abstractions.Security.Sessions` | 5.0.0 | `:843` |
| `SessionObservations` ist der fünfte Konstruktorparameter von `TokenSessionService` | 5.1.0 | `:451` |

Die ersten beiden sind Namensräume — ein Port lag im Motor, wo ihn kein
Anbieterpaket umsetzen konnte. Die dritte ist die interessante: die Menge der
beobachteten Subjekte war ein Instanzfeld an einem `AddScoped`-Dienst, also je
Anfrage ein neues leeres Wörterbuch, weshalb die Sitzungsübersicht dauerhaft
*„0 active sessions observed by this instance"* meldete. **Registriert,
vorhanden, ohne Wirkung, und niemand merkt es** — dieselbe Fehlerklasse, für
die ADR-0044 in diesem Repositorium gebaut wurde. Nur eine Testreihe hier baut
den Dienst selbst und musste das Singleton nachziehen.

Dazu zwei mechanische Dinge: `Microsoft.Extensions.*` und
`Microsoft.EntityFrameworkCore.Relational` von 10.0.11 auf 10.0.12, weil Noelia
6.4.0 daran hängt und das transitive Pinning aus einer Herabstufung zu Recht
einen Fehler macht — **nicht** das Pinning abgeschaltet. Und die
Sitzungstabelle heißt `noelia_refresh_tokens`, als Wanderung mit `RenameTable`
(`MIGRATION.md` Punkt 6).

### 4. Was sich an ADR-0027 ändert

ADR-0027 (Löschung mit Nachweis) verlangt, dass eine Löschung den Nachweis
hinterlässt, den sie verspricht. `MIGRATION.md` Punkt 4 sagt für den
Identitätswechsel: die alte Prüfkette mit 4.4.3 **archivieren und prüfen**,
dann eine neue beginnen — die Ableitungsdomäne und die Ereignisidentität haben
sich absichtlich geändert.

**Hier gibt es keine alte Kette.** Die Datenträger waren leer und sind es beim
Umschalten wieder gewesen; es existiert kein vor dem Umstieg ausgestellter
Löschnachweis, der auf eine verschwundene Kette zeigen könnte. ADR-0027 gilt
damit **unverändert weiter** und beginnt seine Kette unter Noelia neu.

> **Was ab jetzt gilt, und es ist der Satz, den man später braucht:** wird
> dieser Baum je wieder auf eine neue Identität umgestellt, während echte
> Löschnachweise ausgestellt sind, dann ist „die alte Kette bleibt archiviert
> und prüfbar" keine Option mehr, sondern die Bedingung. Ein Löschnachweis, der
> auf eine Kette zeigt, die es nicht mehr gibt, ist keiner — und ADR-0027 wäre
> dem Buchstaben nach erfüllt und dem Sinn nach gebrochen.

Unberührt bleiben: die dreizehn Empfänger, die Reihenfolge
(Quittung → Schlussmail → eigene Zeilen), `Aufbewahrung.*` als Konstante, und
`wt.loeschung.nachweis` aus ADR-0044.

### 5. Was von ADR-0044 bleibt

`WorkerTransfer.Nachweis` ist am 20.09.2026 gebaut worden, und Noelia 6.4.0
liefert einen Teil davon selbst: `ISecurityCheck` samt wertfreiem Läufer mit
Zeitgrenze, `Noelia.Dashboard`, `OperatorReport`, `RegulatoryReference`.

**PR #87 wurde trotzdem zusammengeführt und nicht verworfen.** Seine vier
eigenen Prüfungen — `wt.ki.naht`, `wt.ki.keine-zahl`, `wt.einwilligung.wirkt`,
`wt.loeschung.nachweis` — haben in Noelia keine Entsprechung und behalten
Inhalt, Tests und Gegenversuche. Und die drei Funde, die er hervorgebracht hat,
sind bezahlt: die Teilzeichenketten-Fehlalarme (`Capability`, `Benefits`,
`Availability`), die Zwei-Tabellen-Regel der Prüfspur, und der Gegenversuch,
der nicht fiel.

Das Zusammenlegen auf Noelias Oberfläche ist **noch nicht ausgeführt** — siehe
*Was offenbleibt*.

## Folgen

Gemessen am 20.09.2026 nach dem Schnitt:

- `dotnet build` — **0 Warnungen**
- `./scripts/test-dotnet.sh` — **1381 Tests grün, 0 rot, 0 übersprungen**,
  dieselbe Zahl wie vor dem Umstieg
- **17 Dienste gesund** auf geleerten Datenträgern
- `POST /auth/register` 201 · `GET /jobs` 200 · `GET /consent/me` 401
- `make nachweis-pruefen` — 14 von 14 Diensten, 83 Befunde, keine Zusage
  unbelegt
- `noelia analyze .` — **keine Befunde**; es sieht die neun Pakete,
  `AddNoelia(...)`, `UseDefaults()` und die Egress-Erklärung

`make analyze` steht in `make validate`. Fehlt das Werkzeug, wird der Schritt
**benannt** und nicht stillschweigend übersprungen — dieselbe Behandlung, die
`validate.sh` dem Nachweis und den E2E-Reisen gibt.

## Was offenbleibt

- **Das Zusammenlegen auf Noelias Oberfläche** (Phasen 2, 3 und 5 des
  Auftrags): `ISecurityCheck` statt eigenem Läufer, `Noelia.Dashboard` statt
  der sieben `/nachweis/…`-Adressen, `OperatorReport` statt `bericht.json`,
  `RegulatoryReference` statt `Rechtsbezug`, und die Control Plane über
  vierzehn Dienste. **Was dabei gelöscht wird, wird gelöscht** — zwei Wege zu
  einer Aussage laufen auseinander, und beim ersten Mal merkt es niemand.
- **Die KI-Achse** (`DependencyKind.ArtificialIntelligence`, `noelia.ai.*`).
  Hier kann WorkerTransfer mehr als Noelia: Noelia erkennt Modell-Endpunkte am
  Hostnamen und nennt sein Verzeichnis deshalb eine Untergrenze, während
  `KiZugangV1` die `base_url` **hält**. Der Weg dahin ist `DeclaredDependency`
  aus den eingetragenen Zugängen — **aggregiert und gezählt, nie pro Person**
  (ADR-0026 gilt auch für Noelias Werkzeuge).
- **Die Lizenzfrage der Control Plane.** Vierzehn Dienste liegen über der
  freien Stufe. Das ist eine Preisfrage und keine technische.
