# Auftrag: Härtung

Die Migration ist durch. Dieser Auftrag löst sie ein: **Girder vollständig
einsetzen, Konfiguration über Umgebung, und alles, was von Hand gebaut wurde,
gegen das prüfen, was die Bibliothek liefert.**

Er ersetzt Phase D aus `MIGRATION-AUFTRAG.md`, soweit sie sich überschneidet.
D1 (Bremse) und D2 (Routenkarte) sind gebaut und bleiben — aber H1 prüft sie
gegen das nach, was Girder anbietet.

---

## Die Regel, die vorne steht

**Suche nach dem Zweck, nicht nach dem Bauteil.**

Zweimal ist genau das schiefgegangen: einmal bei der Korrelationsweitergabe
(gesucht wurde ein `DelegatingHandler`, Girder löst es im
`ServiceCommunicationManager`), einmal bei der Bremse (getestet wurde eine von
**drei** Ratenbegrenzungs-Middlewares). Girder hat für viele Belange mehrere
Wege, und sie antworten nicht gleich. **Ein Weg, der kaputt ist, beweist nichts
über die anderen.**

Bevor irgendetwas Querschnittliches gebaut wird: `grep -ril <zweck>` über
Girders `src/`, alle Treffer ansehen, dann entscheiden.

## Und eine Regel, die weggeht

Die ADRs halten Gründe fest, und Gründe sind wertvoll. Sie sind aber **kein
Veto**. Wenn ein ADR und eine bessere Lösung sich widersprechen, gewinnt die
bessere Lösung, und der ADR wird angepasst oder gelöscht. Nichts wird
unterlassen, weil ein Dokument von 2026 es untersagt.

Was bleibt, ist die Sache dahinter: Einwilligung muss sofort wirken, niemand
bekommt eine Punktzahl, Löschen heißt löschen. Diese Sätze gelten, weil sie
richtig sind — nicht, weil sie in `docs/adr/` stehen.

---

## H1 — Der Modulabgleich

**WorkerTransfer ruft 8 von 17 Girder-Modulen.** Das ist der Kern des Auftrags.

```
gerufen        AddHealthChecks · AddJwtAuthentication · AddObservability
               AddPasswordHashing · AddPrincipal · AddSecurityHeaders
               AddTokenSessions

nicht gerufen  AddAuditLogging · AddAuthorization · AddCaching
               AddCommunication · AddDistributedRateLimiting · AddEncryption
               AddInputSanitization · AddResilience · AddResourceAuthorization
               AddSecretManagement · AddSecurityMonitoring
```

**Die Vorgabe kehrt sich um: jedes Modul kommt rein, es sei denn, es gibt einen
gemessenen Grund dagegen.** Bisher war es andersherum, und das Ergebnis ist
diese Liste.

Je Modul drei Fragen, **gemessen und nicht überlegt**:

1. Was tut es wirklich? Quelltext lesen, nicht vom Namen schließen.
2. Haben wir es selbst gebaut? Wenn ja: ist unseres besser, oder nur unseres?
3. Wenn es draußen bleibt: **warum** — als ein Satz, der im Composition Root
   steht und den ein Fremder versteht.

Ergebnis ist eine Tabelle mit siebzehn Zeilen in `CLAUDE.md`. Keine Zeile bleibt
leer.

### Die vier, bei denen schon feststeht, dass etwas fehlt

**`AddAuthorization` kommt rein.** Es bringt den `PermissionPolicyProvider`, und
Girders eigene Doku sagt, was ohne ihn passiert: `[RequirePermission]` benennt
eine `Permission:`-Richtlinie, die sonst niemand beantwortet — das Gerüst lehnt
dann **jede** Anfrage an genau die Endpunkte ab, die das Attribut schützen
sollte. Heute fällt das nicht auf, weil niemand das Attribut benutzt und Rollen
von Hand geprüft werden. Genau das ist der Punkt: **wir haben ein
Berechtigungssystem und benutzen es nicht.** Nach dem Einbau werden die
handgeprüften Rollen darauf umgestellt.

**`AddInputSanitization` kommt rein** — in `src/` gibt es null
`AbstractValidator`, `ValidationBehavior` läuft also und lässt alles durch. Eine
Pipeline-Stufe, die immer besteht.

**`AddResilience` kommt rein** — dreizehn Aufrufe zwischen Diensten laufen ohne
Wiederholung und ohne Zeitlimit. Ein hängender Dienst hängt heute den Aufrufer
mit.

**`AddCommunication` wird geprüft.** `ServiceCommunicationManager` reicht die
Korrelationskennung an jeden ausgehenden Aufruf weiter (Zeile 386) — genau das
Stück, das hier fehlt. Das Modul erklärt aber zwei Anforderungen an, `IEventBus`
und einen verteilten Cache. Also messen: **lässt sich
`IServiceCommunicationManager` ohne `AddCommunication()` und ohne Broker
registrieren?** Wenn ja, umstellen. Wenn nein, ist es ein Girder-Ticket derselben
Form wie `AddCQRS`, das einen Cache verlangte, ohne zwischenzuspeichern.

### Und dieselben Fragen an unseren Eigenbau

`src/gateway/`: `Korrelation.cs` · `Gesundheit.cs` · `Bremse.cs` ·
`Navigation.cs`. Girder hat `UseCorrelationId()` und `AddHealthChecks()` — warum
steht daneben eigenes?

**`Bremse.cs` besonders.** Der Befund war richtig, aber unvollständig: getestet
wurde `DistributedRateLimitingMiddleware`. Girder hat noch
`Security/RateLimiting/RateLimitMiddleware.cs`, mit **eigener Verdrahtung** in
Zeile 632. Miss den. Bremst er, fällt `Bremse.cs` weg und das Ticket wird
präzisiert statt zurückgezogen — die eine Middleware bremst weiterhin nicht.

`src/shared/`: `Outbox` ist echte Lücke (Girder hat keine). `Wanderung`,
`ZugriffsCookie`, `Skills`, die drei `Contracts.*` sind Fachlichkeit und bleiben.
`ProblemDetailsMiddleware` gegen Girders Fehlerbehandlung prüfen.

---

## H2 — Konfiguration über die Umgebung

Heute steht `AddJwtAuthentication()` ohne Argument da und nimmt, was Girder aus
`IConfiguration` liest. Es gibt **kein `.env`**, **kein `.env.example`** und
**kein `DotNetEnv`** — ein neuer Entwickler kann nirgends nachsehen, was er
setzen muss.

Skillswap hat das gelöst, und der Weg wird übernommen:

- **`DotNetEnv` 3.1.1**, in `Program.cs` je Dienst: `Env.Load(envFile)` mit
  Rückfall auf `Env.Load(".env")`.
- **`.env.example` im Repo**, vollständig, mit einem Kommentar je Schlüssel.
  `.env` selbst ist ignoriert.
- **Die Rangfolge ist Umgebung vor `appsettings`**, wie Girder es schon tut:
  `JWT_SECRET` zuerst, dann `configuration["JwtSettings:Secret"]`, sonst ein
  Fehler beim Start, der den fehlenden Schlüssel **benennt**.
- **Später Infisical**, das die Umgebung füllt — es füttert `.env`, es ersetzt
  den Mechanismus nicht. Deshalb ändert sich der Code dafür nicht.

Und die Schlüssel selbst: nur identity-service bekommt den privaten Teil, alle
anderen den öffentlichen. Es muss ohne Codeänderung austauschbar sein, sonst ist
es keine Konfiguration.

**Kein Geheimnis im Repo.** Der Startfehler bei einem fehlenden Schlüssel ist
Absicht — ein eingebauter Vorgabewert ist ein Geheimnis, das in git liegt.

---

## H3 — Was aus D noch offen ist

- **`GET /notifications` gibt 405** und verrät damit einen Pfad, den der Dienst
  hinter einem 404 versteckt. Derselbe Aufzählungskanal, andere Tür.
- **Kaputter JSON-Rumpf gibt 500 statt 400.** `ProblemDetailsMiddleware`
  überschreibt den Code, den `BadHttpRequestException` selbst trägt. Unser Code,
  27 Zeilen, Patch liegt im Baum — mit Test und Gegenprobe behalten.
- **Validatoren** — fällt mit `AddInputSanitization` in H1 zusammen.

---

## H4 — Der Prüfer, von fremder Hand

In Phase C brach er ab, und die Bestandsaufnahme kam von derselben Hand, die die
Neufassung geschrieben hatte. Das zählt nicht.

Ein **frischer Agent**, der nichts davon geschrieben hat: zu jeder Zusage die
Fundstelle **und** der Test. Dazu neu: **je Girder-Modul die Zeile aus H1** — und
für jedes „bewusst nicht" die Prüfung, ob der Grund trägt.

---

## H5 — Externe Durchsicht

`/code-review ultra` über WorkerTransfer, Girder und Skillswap. Das startet ein
Mensch, nicht ein Agent. Ergebnisse werden hier eingearbeitet: Girder-Funde als
Tickets in `bugs/`, WorkerTransfer-Funde als Aufgaben.

---

## Abnahme

- Siebzehn Girder-Module, siebzehn begründete Zeilen in `CLAUDE.md`, keine leer
- `AddAuthorization` verdrahtet, handgeprüfte Rollen darauf umgestellt
- `.env.example` vollständig, ein neuer Entwickler kommt ohne Rückfrage hoch
- Kein Eigenbau, der etwas nachbaut, das Girder liefert — oder ein Satz, warum
  unserer besser ist
- `dotnet build` ohne Warnung, alle Tests grün, keiner übersprungen
- Der Prüfer war eine zweite Hand
