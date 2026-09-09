# Übergabe: Korrelationskennung durch die Kette

**Auftrag:** Protokolle von WorkerTransfer analysieren, Schwerpunkt Korrelationskennung.
**Stand:** in Arbeit — Datei wird nach *jedem* gemessenen Sprung fortgeschrieben.
**Regel für Nachfolger:** Nichts hier ist Vermutung. Steht eine Zeile ohne Beleg, ist sie ungemessen und als solche markiert.

## Zustand der Maschine (fortlaufend gepflegt)

| Frage | Stand | Zeit |
|---|---|---|
| Container laufen? | **JA** — `docker compose up -d --wait`, 15 Container `healthy` | 02.09. 04:05 |
| Testkonten angelegt? | zwei, siehe unten | 02.09. 04:08 |
| Produktivcode geändert? | **nein** (und soll so bleiben) | — |

## Vorhaben

1. Stapel hochfahren (`docker compose up -d --wait`).
2. Alle ausgehenden Dienst-zu-Dienst-Aufrufe finden (`grep -rn "AddHttpClient\|IHttpClientFactory" src/`), je Sprung einzeln messen — mit eigener `X-Correlation-ID` und ohne.
3. Je Fund prüfen, ob *unser* Code den Kopf selbst setzt. Nur wenn nicht, hat Girders `CorrelationIdHandler` getragen.
4. Brüche suchen: alles ohne `HttpContext` (Outbox-Versandhintergrunddienst, Löschkaskade).
5. Kennung in der Antwort prüfen über mehrere Statuscodes (401/403/404/422/500/429).
6. Protokollhygiene: vollständige Reise fahren, danach alle Protokolle nach den eingegebenen Werten greppen.
7. Brauchbarkeit: eine Kennung, alle Zeilen über alle Dienste — Kommandozeile plus Trefferzahl.

## Sprungtabelle (wird fortgeschrieben)

### Der Mechanismus — erst klären, sonst misst man das Falsche

Kein einziger unserer sechzehn Aufrufplätze setzt `X-Correlation-ID` selbst
(`grep -n "Headers" <Aufrufstelle>` über alle: nur `Authorization`,
`X-Notify-Secret`, `X-Erasure-Secret`, `x-api-key`). Getragen hat also Girder.

**Und zwar `CorrelationIdHandler`, nicht `ServiceCommunicationManager`.**
CLAUDE.md sagt, `ServiceCommunicationManager` sei „das einzige Bauteil in Girder,
das die Korrelationskennung weiterreicht" — das galt für Girder 3. Wir sind auf
**4.0.2** (`Directory.Packages.props:12`), und dort steht
`GirderModuleCatalogue.cs:81`:
`Entry(GirderModule.CorrelationPropagation, girder => girder.Services.AddCorrelationIdPropagation())`
— **über** der Vorgabelinie, und `Dienstgrundlage.cs` wählt es nicht ab.
`AddCorrelationIdPropagation` hängt den Handler über `ConfigureHttpClientDefaults`
an **jeden** Klienten der Fabrik.

Der Handler liest `CorrelationId.Current`, und das ist
`Activity.Current?.GetBaggageItem("CorrelationId")`
(`Girder.Abstractions/Observability/CorrelationId.cs:29`).
**Daraus folgt der Bruch:** ohne `Activity` (Hintergrunddienst) ist das `null`,
und der Handler setzt nichts.

| # | von → nach | Endpunkt | Kennung angekommen? | Beleg |
|---|---|---|---|---|
| A | profile → consent | `POST /consent/check-batch` (ausgelöst durch `GET /profiles/{sub}` als Firma) | **ja** | consent-Datei: `[INF] LoggingBehavior: Starting request SammelpruefungAbfrage [...] with correlation SPRUNG-A1-PROFILE-CONSENT` + `CorrelationId: SPRUNG-A1-PROFILE-CONSENT` |
| B | resume → consent | `POST /consent/check` (ausgelöst durch `POST /resumes/{sub}/requests`) | **ja** | consent: `Starting request EinwilligungPruefenAbfrage [...] with correlation SPRUNG-B2-RESUME-CONSENT` |
| C | portfolio → consent | `POST /consent/check` (ausgelöst durch `GET /portfolios/{sub}`) | **ja** | consent: `Starting request EinwilligungPruefenAbfrage [...] with correlation SPRUNG-C1-PORTFOLIO-CONSENT` |
| D | transfer → consent | `POST /consent/check` (ausgelöst durch `POST /market/{sub}/requests`) | **ja** | consent: `Starting request EinwilligungPruefenAbfrage [...] with correlation SPRUNG-D2-TRANSFER-CONSENT` |
| **X1** | **resume → notification (über Outbox)** | `POST /internal/notifications` | **NEIN — reisst** | notification: `HTTP POST /internal/notifications responded 422` mit `CorrelationId: 0HNO8KTF2FI43:00000001` — das ist die **eigene** `TraceIdentifier` von notification-service, nicht `SPRUNG-B2-RESUME-CONSENT` |

### Nebenbefund beim Messen von X1

Die Outbox-Zustellung nach notification-service antwortet **422**, dauerhaft:
`resume.outbox` hat Zeilen mit `attempts = 10` (die Aufgabegrenze) und
`delivered_at IS NULL`. Gleiches Bild in `transfer.outbox`. Das ist kein
Korrelationsbefund, faellt aber beim Messen an und gehoert in den Bericht.


## Testkonten (in `identity`, überleben `compose down`, weil Volume)

| Rolle | Adresse | Passwort | sub / tenant |
|---|---|---|---|
| Person | `zylinderkopf@example.org` | `Trompetenbaum-99-Xq` | sub `94724c46-cd48-4321-a184-1c4e0b2e9c8a` |
| Firma (admin) | `krummhorn@ventilbau-krummhorn.de` | `Trompetenbaum-99-Xq` | sub `4d13f584-b7c8-4e4d-aff1-e1532807d546`, tenant `0d26b55e-5595-4ef3-a711-bc0da6a1fa16`, Firma „Krummhorn Ventilbau GmbH" |

Cookie-Gläser liegen in `/tmp/korr-person.jar` und `/tmp/korr-firma.jar`, die reinen Token in
`/tmp/korr-person.tok` / `/tmp/korr-firma.tok` (Firma **mit** `tenant`-Anspruch).

Absichtlich auffällige Werte für die Protokollhygiene (Punkt 4):
`zylinderkopf`, `Trompetenbaum-99-Xq`, `Quirinus Federleicht`, `Krummhorn Ventilbau GmbH`,
Biografie `Termin bei Dr. Weber am Donnerstag`, Widerrufsgrund `Grund Nashornkaefer`.

## WO die Kennung überhaupt steht — das muss man wissen, sonst misst man ins Leere

`docker compose logs` zeigt sie **nicht**. Girders `LoggingConfiguration` schreibt in
Development eine Konsolenvorlage **ohne** `CorrelationId`
(`Girder.Infrastructure/Logging/LoggingConfiguration.cs:104`), und Compose setzt
`ASPNETCORE_ENVIRONMENT: Development` (`docker-compose.yml:81`).

Die Kennung steht nur in der **Datei-Senke im Container**:
`/app/<dienstverzeichnis>/logs/<dienst>-JJJJMMTT.log`, Vorlage mit
`CorrelationId:`-Zeile (`LoggingConfiguration.cs:125`).

Hilfsskript (temporär, im Scratchpad, nicht im Repo):
`scratchpad/logsuche.sh <muster>` greppt alle elf Dienste über
`docker compose exec <d>-service grep -rhF -- '<muster>' /app/*/logs/*.log`.

## Aufräumen

Am Ende: `docker compose down`. Temporäre Änderungen zurücknehmen und benennen.
