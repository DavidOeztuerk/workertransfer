# Der Umstieg auf Girder 4.1.0 — was geändert wurde und was hier davon abhing

Stand 03.09.2026. Vorher stand hier eine Liste dessen, was **zu tun** ist. Sie
ist abgearbeitet; was jetzt hier steht, ist, was gemessen wurde und was es
gekostet hat. Alle sieben Tickets aus [`bugs/`](../bugs/) sind behoben und die
Dateien deshalb gelöscht — so, wie [`bugs/README.md`](../bugs/README.md) es
verlangt: nachmessen, Umweg entfernen, tragende Begründung retten, dann löschen.

Die tragenden Begründungen liegen jetzt dort, wo die Entscheidung fällt:
`Dienstgrundlage.cs`, `Bremse.cs`, `Korrelation.cs`, `GirderAccessTokenIssuer.cs`
und `CLAUDE.md`.

---

## Was in Girder geändert wurde

| # | Ticket | Behoben durch |
|---|---|---|
| 1 | Die Vorgabekette startet nicht und liest die Auswahl nicht | Die Kette liest `GirderComposition`; ein Glied, dessen Modul abgewählt ist, überspringt sich. Das Tor steht **vor** `Requires<T>` |
| 2 | Die Eingabeprüfung weist gewöhnliche Wörter ab | Syntax statt Wörtern; `Referer` ist keine Eingabe; JSON-Rümpfe werden geprüft und die Prüfung ist abschaltbar |
| 3 | Die Korrelationskennung erreicht den nächsten Dienst nicht | Die Kennung steht auch im **Anfrage**kopf — die einzige Stelle, die ein Proxy weitergibt |
| 4 | `UseSharedInfrastructure` heißt nicht wie `AddGirder` | `UseGirder`, alter Name als `[Obsolete]`-Weiterleitung |
| 5 | Die Abweisung der Bremse ist kein Problemdokument | 429 ist `application/problem+json` und nennt die Korrelationskennung |
| 6 | Das Token trägt zwei konstante Lügen | `EmailVerified`/`AccountStatus` sind `bool?`/`string?` ohne Vorgabe |
| 7 | `CustomClaims` kann keine Liste ausdrücken | `UserClaims.CustomClaimArrays` |

Dazu, aus denselben Messungen und ohne eigenes Ticket:

- **`RateLimiting` stand in der Vorgabe und registrierte keinen Zähler.** Der
  eine Eintrag der Vorgabe, der nicht laufen konnte — und die eigentliche
  Ursache von Ticket 1. Es bringt jetzt einen prozessinternen Zähler mit,
  dieselbe Bauart wie das `AddDistributedMemoryCache()`, das `Caching` längst in
  der Vorgabe registriert; ein Anbieterpaket ersetzt ihn.
- **`Authorization` war ein Modul für zwei Dinge.** Der Richtlinienanbieter
  (beantwortet `[RequirePermission]`) und die Zwischenschicht, die *alles
  andere* abriegelt. Wer eine öffentliche Fläche hat, musste zwischen beidem
  wählen. Jetzt zwei Module, beide in der Vorgabe.
- **`EndpointSpecificLimits` kam mit sieben fremden Pfaden**, die Konfiguration
  nicht ersetzen, sondern nur ergänzen konnte. Jetzt leer.
- **`LdapInjection` stand im Enum und hatte nie ein Muster.** Der Test dafür
  bestand, weil die Befehls-Regel jede Klammer traf.
- **`ClientAddress.Of` normalisiert `::ffff:a.b.c.d`** — sonst bekommt derselbe
  Rechner zwei Töpfe und damit die doppelte Grenze.
- **`UseAuth()` benennt, was fehlt**, statt einen rohen DI-Fehler über
  `IAuthenticationSchemeProvider` zu melden.

3172 Girder-Tests grün, 0 rot, 0 übersprungen. Zwei Gegenproben, beide gefallen:
das Kettentor abgeschaltet (7 von 8 Reihen fallen), die Wort-Regel zurückgeholt
(9 von 25 fallen).

---

## Was hier daraus folgte

**Die Kettenkopie ist weg.** In `Dienstgrundlage.cs` standen dreizehn
abgeschriebene Glieder mit drei Auslassungen. Das war kein Entwurf, sondern
Zwang: ohne die Auslassung brach jeder Dienst beim Start ab. Jetzt steht dort

```csharp
app.UseGirder(environment, dienstname);
app.UseGirderPrincipal();
app.UseMiddleware<ProblemDetailsMiddleware>();
```

und jede Abwahl oben wirkt einmal statt zweimal.

**`UsePermissions()` ist jetzt eine Abwahl mit Begründung** statt einer
auskommentierten Zeile: `.Without(GirderModule.PermissionEnforcement, …)`. Der
Richtlinienanbieter bleibt — an ihm hängen die Firmenrechte in identity-service.

**Die JSON-Rumpfprüfung ist hier aus, und das ist gemessen.** Sie läuft vor
allem anderen und kann kein Feld benennen; damit wird aus einem 422, das sagt
*welches* Feld falsch ist, ein blankes 400. Gemessen an fünf Fällen in drei
Diensten (`javascript:` und `data:` als Portfolio-Link, dieselben zwei am
Arbeitgeberprofil, `../../etc/passwd` als GitHub-Anmeldename). Alle fünf werden
weiterhin abgewiesen — die Antwort sagt dem Menschen im Formular nur weniger.
Zu verschmerzen ist das hier und anderswo nicht: unsere Schreibfläche ist
durchgehend geprüfte Fachschicht mit Feldnamen, wir bauen kein SQL aus
Zeichenketten, und die Ausgabe entkommt React. Query-String und die zwei
Adressköpfe bleiben geprüft.

**`Bremse.cs` bleibt — aus einem neuen Grund.** Der alte („ihre Abweisung ist
kein Problemdokument") ist behoben und steht deshalb nicht mehr in der Datei.
Was bleibt, ist die *Gestalt*: Girders Zwischenschicht ist eine **globale**
Bremse mit Verfeinerung je Pfad — jeder Pfad ohne eigenen Eintrag bekommt die
Vorgabegrenze. Durch das Gateway läuft aber auch die ganze Oberfläche. Unsere
bremst **fünf benannte Pfade** und rührt nichts anderes an.

**`Korrelation.cs` bleibt — aus einem Grund, der nichts mit Girder zu tun hat.**
Der Bibliotheksfehler ist behoben; das Gateway ruft aber kein `AddGirder`. Um
zwanzig Zeilen zu sparen, müsste es sich das ganze Modulsystem einhängen.

**`TokenformTests` prüft jetzt die geschlossene Menge.** Jeder Test über einzelne
Namen prüft, woran jemand gedacht hat. Durchgerutscht ist der Anspruch, an den
niemand gedacht hat — genau so kamen drei ungenannte ins Token.

---

## Was hier NICHT falsch ist — geprüft, weil der Verdacht bestand

**Die Solution ist vollständig.** `WorkerTransfer.slnx` listet **77** Projekte,
auf der Platte liegen **77**. Dass VS Code `Dienstgrundlage.cs` als „außerhalb
der Solution" meldet, ist ein Werkzeugproblem: es gibt **nur** eine `.slnx` (das
neue Format) und keine `.sln`, und kein `.vscode/settings.json`. Ältere
Fassungen des C#-Dev-Kit lesen `.slnx` nicht und fallen auf einzelne `.csproj`
zurück — Dateien, die dabei nicht getroffen werden, melden genau diesen Satz.

Zwei Wege, einer reicht:
- C#-Erweiterung aktualisieren (`.slnx` wird seit 2025 unterstützt), oder
- `.vscode/settings.json` mit `{ "dotnet.defaultSolution": "WorkerTransfer.slnx" }`.

**Die `Without(...)` auf Module, die gar nicht in der Vorgabe stehen**
(`HttpResponseCaching`, `Communication`, `Encryption`, `ResourceAuthorization`)
sind **keine Abwahl**, sondern festgehaltene Entscheidungen — und Girders README
empfiehlt genau das:

> **No broker on this service.** `Communication` is not in the defaults, so this
> is only worth writing if someone might expect it.

Sie kosten nichts zur Laufzeit und beantworten die Frage „habt ihr das
übersehen?" im Quelltext statt im Gedächtnis.

**Was `AddWorkerTransferDefaults` registriert**, vollständig:

| | |
|---|---|
| `UseDefaults()` | Girders Vorgabemodule — Serilog, JSON, JWT-Prüfung, Sicherheitsköpfe, Gesundheit, Telemetrie, CORS, Swagger, Audit, Korrelationsweitergabe, Ratenzählung, Richtlinienanbieter … |
| `.Use(Principal)` | steht **nicht** in der Vorgabe. Baut `ICurrentPrincipal` aus dem geprüften Token — die Grundlage von `Capacity` (ADR-0017) |
| `.UseJwt(FromSharedSecret())` | heute ein gemeinsames HS256-Geheimnis für alle elf |
| 6 × `.Without(…)` | zwei echte Abwahlen (`RateLimiting` und `PermissionEnforcement`, beide Topologie), vier festgehaltene Entscheidungen |
| `Configure<InputSanitizationOptions>` | JSON-Rumpfprüfung aus, Begründung siehe oben |
| `AlsAussteller()` | **nur identity-service**: `PasswordHashing` + `TokenSessions` |

Mehr nicht. Datenbank, Repositories, Outbox und Zwischenspeicher entscheidet
jeder Dienst in seinem eigenen `Add<Dienst>Infrastructure()` (ADR-0003).
