# Auftrag: WorkerTransfer auf Noelia 7.0.0 heben

> **Ausgeführt am 01./02.10.2026** auf `feature/noelia-dashboard`. Der Auftrag
> bleibt als Begründung stehen; was dabei gemessen wurde, steht in den
> Commit-Texten.
>
> - Version, Schema 3 im Journey-Test, `AGENTS.md`/`CLAUDE.md`: `2b76faa2`
> - KI-Deklaration je Dienst (`ArtificialIntelligence`, Pflichtparameter): `0de5c0f8`
> - Weiterleitungen (GitHub folgt im eigenen Ursprung, Anschreiben folgt nie): `fa3a7fea`
> - Dashboard-Texte: kein Test las Noelias geänderten Wortlaut, nichts zu ändern.
> - Control Plane: läuft seit NoeliaControlPlane PR #1 nur noch auf dem Host
>   (`make control-plane`), `5daa3114`.
>
> Unterwegs gefunden und behoben, unabhängig von 7.0.0: der Schlüsselring wurde
> nie zurückgelesen (`0b904720`), `github.com` fehlte der Egress-Grenze und dem
> Chart (`b678c824`), Marktstatus und Firmenprofil überschrieben eine Wahl mit
> dem spät geladenen Stand (`5e9fa208`), ein schweigender github-service ließ
> die Scout-Suche Timeouts stapeln (`098cdf0c`), zwei E2E-Reisen warteten nicht
> auf den Firmenwechsel (`102c598d`).

Stand 01.10.2026. Noelia 7.0.0 liegt auf nuget.org (alle 14 Pakete). Was sich
geändert hat und was zu tun ist, steht verbindlich in Noelias `MIGRATION.md`,
Abschnitt „Noelia 6.4.0 → 7.0.0 / Brechend auf einen Blick“
(https://github.com/DavidOeztuerk/noelia/blob/main/MIGRATION.md). Lies ihn
zuerst, dazu `CLAUDE.md`, `AGENTS.md` und `docs/AUFTRAG-NOELIA-UMSTIEG.md` in
diesem Repository.

## Ausgangslage

- Arbeitszweig: `feature/noelia-dashboard` (67 Commits vor `main`, 53 davon
  ungepusht). Darauf weiterarbeiten. Die vorhandene uncommittete Änderung in
  `web/e2e/assessment-journey.spec.ts` gehört zum Arbeitsstand: nicht
  verwerfen, nicht in eigene Commits mischen.
- Noelia-Version zentral in `Directory.Packages.props` (`NoeliaVersion` 6.4.0),
  zehn Pakete in 70 Projekten, kein `Noelia.Redis`.

## Bereits geprüft: NICHT betroffen

Nicht neu untersuchen, nur bei Widerspruch melden:

- **Outbox:** WorkerTransfer hat eine eigene (`src/shared/WorkerTransfer.Outbox`).
  Noelias `IOutboxReader`/`noelia_outbox`/`AttemptsBeforeAlarm` werden nicht
  benutzt, keine EF-Migration nötig.
- **Schlüsselring:** eigener Ring in der Datenbank (`ServiceDefaults/KeyRing.cs`),
  Noelias `UseDataProtection` wird nicht aufgerufen.
- **Redis-Audit, Checkpoint-Typen, Lua-Snapshot:** kein `Noelia.Redis`.
- **PrimaryHandler-Regel der Egress-Policy:** alle Clients nutzen den
  Standard-Handler (`AddHttpClient()` ohne `ConfigurePrimaryHttpMessageHandler`).
- **`scripts/evidence-check.sh`:** wird nur bei `Fail` rot und liest
  `securityChecks.results`. Das Feld ist unverändert, neue `Warning`s brechen es nicht.

## Was zu tun ist

### 1. Version heben

- `Directory.Packages.props`: `NoeliaVersion` → `7.0.0`.
- `CLAUDE.md` Zeile 7 („Noelia … 6.4.0“) auf 7.0.0.
- `AGENTS.md` Zeile 68 behauptet einen GitHub-Packages-Login für `Noelia.*`.
  Das stimmt seit dem Umstieg auf nuget.org nicht mehr (siehe `NuGet.Config`):
  korrigieren.

### 2. KI-Nutzung deklarieren (Befund R20 in Noelia)

Noelias `noelia.ai.inventory` erkennt Modelle nur an deklarierten Hostnamen.
Hier stehen die KI-Zugänge **pro Person in einer Tabelle** (`AiAccessV1`), auch
`openai_compatible` auf `localhost`. Bisher ergab das ein grünes Inventar und für
`noelia.ai.record-keeping` „keine Pflicht“. Mit 7.0.0 ist beides ohne Deklaration
`Warning` „Not determined“. Das ist richtig, aber für einen Dienst ohne KI irreführend.

- In `ServiceDefaults/ServiceFoundation.cs` im `AddSovereignPlatform`-Builder
  `DeclareArtificialIntelligence(bool)` setzen. Der Wert ist **pro Dienst** zu
  entscheiden, also einen Parameter in den gemeinsamen Aufruf aufnehmen.
  Kein Default, der still „nein“ sagt.
- Ermittle am Code, welche Dienste tatsächlich Modelle aufrufen oder
  KI-Zugänge lesen (u. a. identity für `AiAccessV1`; advisor, assessment,
  resume, scout prüfen). Diese bekommen `true` **ohne** Endpunkt, denn die
  Anbieter sind pro Person und zur Startzeit unbekannt. Alle anderen bekommen `false`.
- `wt.ki.anbieter` bleibt die vollständige Quelle. Kommentare, die Noelias
  bisheriges Verhalten beschreiben, an 7.0.0 anpassen: `Checks/ProviderCheck.cs`
  (~Z. 78 „reports no recognised endpoint“), `NoeliaJourneyTests.cs` (~Z. 236–245).
- Erwartet: KI-Dienste zeigen bei `inventory` `Warning` mit dem Wortlaut
  „declared … uses a model … not recognisable“, und `record-keeping` stellt die
  Sink-Frage. Dienste ohne KI zeigen `NotApplicable` als Deklaration, nicht als
  Beobachtung. Diese Ergebnisse mit einem Test festnageln, der die Wirkung prüft.

### 3. Weiterleitungen unter der Egress-Grenze (Befund R04 in Noelia)

`AddSovereignPlatform(… Allow(...))` ist eine durchsetzende Policy. Ab 7.0.0
folgt **kein** Client aus der `IHttpClientFactory` mehr automatisch einer
Weiterleitung (301/302/303/307/308). Die Antwort kommt beim Aufrufer an. Das gilt
für alle Factory-Clients des Dienstes, auch die internen
(`HttpErasureDelivery`, `HttpCompanyRoles`).

- Inventarisiere jeden ausgehenden HTTP-Aufruf (Hosts kommen aus
  `CalledHosts(configuration)`, u. a. `GitHub__BaseUrl`, `Draft__BaseUrl`,
  Jobquellen, Löschzustellung, Rollenabfrage) und prüfe, ob er heute auf
  einer Weiterleitung beruht. Typische Fälle:
  - GitHub-API 301 für umbenannte Repositories,
  - Archiv-/Zipball-Downloads → `codeload.github.com`,
  - Basis-URLs ohne abschließenden Slash.
- Für jeden echten Fall: Ziel-URL direkt ansprechen oder die Weiterleitung
  bewusst selbst behandeln. Das neue Ziel muss dann gegen dieselbe Grenze
  geprüft werden, also ein erlaubter Host sein. **AutoRedirect nicht wieder
  einschalten.** Pro Fall ein Test mit einem lokalen Listener, der umleitet.
- Interne Aufrufe: In `src/` gibt es kein `UseHttpsRedirection`. Bestätige, dass
  keine interne URL auf eine Umleitung angewiesen ist.

### 4. Geänderte Dashboard-Texte

Noelias Dashboard formuliert in 7.0.0 Audit und Egress ehrlicher:
- Write-Time-Prüfung und Verifikation sind getrennt.
- Vollständigkeit ist ohne Checkpoint `Unknown`/`Partial`.
- Egress heißt „HTTP guard configured“, „Traffic is not observed“.
- Ein leeres KI-Inventar ist kein grünes `pass` mehr.

.NET-Journey-Tests und `web/e2e`, die Seiteninhalte prüfen, laufen lassen. Nur
dort anpassen, wo Noelias Wortlaut sich geändert hat. Eine Aussage nicht
abschwächen, und keinen Checkpoint oder „Complete“ nachbauen.

### 5. Control Plane und Berichte

- `/noelia/report.json` kommt ab 7.0.0 in **Schema 3**: deklarierte
  Abhängigkeiten, HTTP-Policy und beobachtete Aufrufe stehen getrennt, und
  `observedCalls` ist `Unavailable`.
- `scripts/evidence.sh`/`evidence-check.sh` auf Felder prüfen, die sich
  geändert haben könnten (sie lesen `securityChecks`, sollte passen). Mit
  laufendem Stack bestätigen.
- `docker-compose.noelia.yml` startet die Control Plane. Diese muss Schema 3
  lesen können, das ist ab NoeliaControlPlane-`main` nach PR #1 der Fall. Das
  verwendete Image bzw. den Stand prüfen und notfalls neu bauen.
  `appsettings.WorkerTransfer.json` im CP-Repository **nicht öffnen und nicht
  ändern**.

## Abnahme

Nach den Befehlen in `CLAUDE.md`, `AGENTS.md` und dem `Makefile`, mindestens:
- vollständiger .NET-Build (Warnungen sind Fehler) und alle .NET-Tests,
  0 übersprungen,
- Web-Unit- und E2E-Tests,
- Stack hochfahren und `make evidence-check` gegen den laufenden Stack,
- `git diff --check`.

Nicht ausgeführte Prüfungen ausdrücklich als offen nennen.

## Grenzen

- Keine neuen Features. Nur was 7.0.0 verlangt, plus die zwei veralteten Doku-Stellen.
- Keine Secrets in Code, Logs, Tests oder Commits. Keine fremden Container,
  Volumes oder Stacks stoppen (auf der Maschine laufen u. a. Skillswap-k8s und
  andere Projekte).
- Commits im Stil dieses Repositories, thematisch getrennt (Version,
  KI-Deklaration, Weiterleitungen, Texte). Nicht pushen ohne Freigabe.

## Bericht am Ende

Betroffene Dienste je Punkt, gefundene Weiterleitungen und wie sie behandelt
sind, alte → neue Check-Ergebnisse je Dienst (`noelia.ai.*`), tatsächliche
Testergebnisse mit Zahlen, offene Punkte.
