# Auftrag: auf Girder 4.4.0 — und nachmessen, dass es stimmt

> **ABGESCHLOSSEN am 09.09.2026. Dieses Dokument ist Geschichte.**
> Der Sprung ist gefahren (`GirderVersion` steht auf `4.4.0`), und der
> eigentliche Auftrag — **nachmessen statt glauben** — auch. Alle fünf
> Messungen aus Abschnitt C sind gegen den laufenden Stapel gefahren und in
> `CLAUDE.md` eingearbeitet:
>
> | | gemessen |
> |---|---|
> | C1 Korrelationskennung auf stdout | ja, über einen echten Dienstsprung — `bugs/korrelationskennung-…md` ist damit geschlossen |
> | C2 Was geschwärzt wird | `[REDACTED]` kommt am laufenden Stapel **null**-mal vor; `MaskierungTests` liefert die andere Hälfte |
> | C3 Die Zusammensetzung | 19 Module **mit Namen**, 6 ausgelassen — und dass sie Namen nennt statt einer Zahl, war selbst eine Messung |
> | C4 Grenzköpfe auf der Abweisung | `X-RateLimit-*` und `Retry-After` stehen auf der 429 |
> | C5 Die Prüfspur | **nicht übernommen** — `VerweigerndePruefspur` weist Girders Senke ab, weil sie ADR-0012 nicht erfüllt |
>
> **Was offen blieb:** nichts. B2 endete mit einer Entscheidung *gegen* die
> Übernahme, und das ist ein Ergebnis, keine Lücke.
>
> **Wo das Ergebnis heute steht:** `Dienstgrundlage.cs`, `VerweigerndePruefspur`,
> `MaskierungTests` (in `WorkerTransfer.Ganzes.Tests`) und die Modultabelle in
> `CLAUDE.md`.

WorkerTransfer steht auf **4.2.3**. Es überspringt 4.3.0 mit Absicht: dort war
die Maskierung kaputt, und 4.4.0 behebt sie. Ziehe direkt auf **4.4.0**.

**Der Kern dieses Auftrags ist nicht der Versionssprung — der ist eine Zeile.
Der Kern ist die Messung.** In Girder wurden fünf Zusagen korrigiert, die vorher
behauptet und nicht gehalten wurden. Genau das darf hier nicht noch einmal
passieren: jede Aussage unten wird **gegen einen laufenden Dienst geprüft**, nicht
gegen den Quelltext und nicht gegen ein Commit.

---

## Was 4.2.3 → 4.4.0 mitbringt

### Aus 4.3.0

- **Die Korrelationskennung steht auf der Konsole.** Das Development-Template
  trägt `[{CorrelationId}]`. Das schließt
  `bugs/korrelationskennung-steht-nicht-auf-der-konsole.md`.
- **Maskierung im Serilog-Zug** (`DataMaskingEnricher`), standardmäßig
  registriert.
- **Revisionssichere Prüfspur**: `IAuditTrailService`, `ISovereignAuditSink`,
  `AuditEvent<T>` mit SHA-256-Verkettung.
- **`AddSovereignPlatform()`** — Egress-Grenze, Souveränitätsbericht, Prüfspur in
  einem Aufruf.

### Aus 4.4.0

| | 4.3.0 | 4.4.0 |
|---|---|---|
| Maskierung vergleicht | Teilzeichenkette, eigene Liste | **exakt**, gegen `SensitiveFieldNames` |
| `SecretName`, `TokenId` | `***` | sichtbar |
| `Username`, `Email`, `City` | sichtbar | **`[REDACTED]`** |
| Maske | `***` | `[REDACTED]` |
| Audit-Hash | Felder mit `|` verkettet, fälschbar | jedes Feld mit seiner Länge |
| Egress-Vorgabe | Loopback + RFC1918, nicht abschaltbar | `WithoutLoopback()`, `WithoutPrivateNetworks()` |
| `AddSovereignPlatform` | registriert am Katalog vorbei | `GirderModule.SovereignPlatform` |

Vollständig in Girders `MIGRATION.md`, Abschnitt „4.3.0 → 4.4.0".

---

## A — Der Sprung

`dotnet/Directory.Packages.props`: `GirderVersion` auf `4.4.0`.

Dann **bauen und testen in getrennten Aufrufen** — verkettet scheitern die
Testcontainers-Reihen und sehen dabei aus wie echte Testfehler.

`AddGirder` und `UseDefaults()` sind unverändert. Wenn `Dienstgrundlage.cs` ohne
Änderung übersetzt, ist das das erwartete Ergebnis, nicht der Beweis, dass alles
stimmt.

---

## B — Die Registrierung anpassen

### B1. Der Souveränitätsbaumeister

`AddSovereignPlatform` ist jetzt ein Modul und gehört in `Dienstgrundlage.cs`
neben die anderen Entscheidungen. Prüfe zuerst, was heute schon von Hand steht —
`AddGirderEgressPolicy`, `AddGirderSovereigntyReport` — und ersetze es, statt es
zu verdoppeln.

```csharp
.AddSovereignPlatform(souveraen => souveraen
    .Allow(/* die Hosts, die dieser Dienst wirklich ruft */)
    .DeclareDependency("Secrets", configuration["OpenBao:Address"])
    .DeclareDependency("Telemetry", configuration["Otlp:Endpoint"]))
```

**Entscheide bewusst über `WithoutPrivateNetworks()`.** Vorgabe ist: RFC1918
erlaubt, weil Datenbank, Cache und Broker dort liegen. Wenn dieser Dienst sie nur
über benannte Hosts erreicht, schließe die Bereiche — und schreib den Grund
daneben, so wie bei den `Without(...)`-Zeilen.

Wo ein Dienst absichtlich nach draußen ruft, wähl das Modul mit Grund ab:

```csharp
.Without(GirderModule.SovereignPlatform, "<der Grund, in einem Satz>")
```

### B2. Die Prüfspur — nur wenn sie ADR-0012 wirklich erfüllt

ADR-0012 verlangt: jeder sicherheitsrelevante Befehl schreibt seine Prüfzeile
**in derselben Transaktion** wie die Änderung, die sie festhält.

`IAuditTrailService` bringt die Hash-Verkettung mit, aber **Girders
In-Memory-Senke erfüllt ADR-0012 nicht** — sie schreibt in eine Liste im Prozess,
nicht in eure Transaktion. Wenn ihr die Prüfspur wollt, braucht sie eine eigene
`ISovereignAuditSink`, die über denselben `DbContext` schreibt:

```csharp
.AddSovereignPlatform(s => s.WithAuditSink<PostgresPruefspurSenke>())
```

Und dann gilt der Befund aus 3.0.1: der Sitzungsspeicher tritt einer offenen
Transaktion bei — eure Senke muss das auch, sonst committet die Prüfzeile
getrennt und kann ein Rollback überleben.

**Das ist eine Entscheidung, kein Automatismus.** Wenn `audit_events` heute schon
tut, was ADR-0012 verlangt, schreib auf, warum Girders Prüfspur *nicht*
übernommen wird — eine zweite Prüfspur neben einer funktionierenden ist schlimmer
als keine.

---

## C — Nachmessen. Das ist der eigentliche Auftrag.

Jede Messung gegen einen **laufenden Dienst**. Kein „steht im Code, also
stimmt's".

### C1. Die Korrelationskennung auf stdout

Dienst starten, eine Anfrage mit gesetztem Kopf, und stdout lesen:

```bash
curl -H 'X-Correlation-ID: mess-1' http://localhost:<port>/health
docker compose logs <dienst> | grep mess-1
```

**Erwartet:** die Zeile trägt `[mess-1]`. Findest du sie nicht, prüfe zuerst, ob
der Dienst wirklich in `Development` läuft — das Template gilt nur dort.

Danach: **einen HTTP-Sprung**. Ein Endpunkt, der einen zweiten Dienst ruft, und
die Kennung muss in **beiden** Logs stehen. Das ist die Messung, die zählt; die
erste zeigt nur, dass das Template stimmt.

Dann `bugs/korrelationskennung-steht-nicht-auf-der-konsole.md` abhaken — mit der
Fassung, und mit dem, was du gemessen hast.

### C2. Was jetzt geschwärzt wird — und was nicht

Das ist die Änderung mit der größten Wirkung auf eure Logs, und sie geht in
**beide** Richtungen.

**Was ab jetzt `[REDACTED]` ist:** jede Log-Eigenschaft, deren Name exakt auf
`SensitiveFieldNames` steht — `Username`, `Email`, `City`, `Name`, `Address`,
`Phone`, `BirthDate`, `Iban` und viele mehr.

```bash
# Wie viele Log-Vorlagen sind betroffen?
grep -rhoE '\{[A-Za-z]+\}' src/ --include="*.cs" | sort -u
```

Geh die Treffer durch und beantworte **eine** Frage je Fund: *läuft eine
Fehlersuche ins Leere, wenn hier `[REDACTED]` steht?* Wenn ja, gibt es zwei
ehrliche Wege — die Eigenschaft umbenennen (`Username` → `SubjectId`, was ohnehin
besser ist), oder den Namen aus `SensitiveFieldNames` nehmen. Das Zweite ist ein
Girder-Ticket, kein lokaler Griff.

**Was ab jetzt wieder sichtbar ist:** `SecretName`, `TokenId`, `TokenEndpoint`.
Wenn ihr in 4.3.0 kurz `***` gesehen habt — das war der Fehler, der behoben ist.

**Messung, nicht Codelesen:** ein Endpunkt, der beides protokolliert, einmal
aufrufen, Log ansehen. Erwartet:

```
Username: [REDACTED]     SecretName: jwt-signing-key
```

### C3. Die Zusammensetzung ausgeben lassen

`GirderComposition` liegt im Container. Lass jeden Dienst beim Start sagen, was
er fährt und was er warum nicht fährt:

```csharp
var zusammensetzung = app.Services.GetRequiredService<GirderComposition>();
foreach (var (modul, grund) in zusammensetzung.Excluded)
{
    app.Logger.LogInformation("Girder-Modul {Modul} nicht in Betrieb: {Grund}", modul, grund);
}
```

**Erwartet:** genau die `Without(...)`-Zeilen aus `Dienstgrundlage.cs`, mit ihren
Gründen. Steht dort etwas, das ihr nicht geschrieben habt, oder fehlt eine Zeile,
dann stimmt die Vorgabe nicht mit der Absicht überein — und genau das war der
Grund, warum es `GirderComposition` gibt.

### C4. Die Grenzköpfe auf der Abweisung

Korrektur zum ursprünglichen Auftrag: hier stand, `Bremse.cs` ersetze Girders
Bremse. Das stimmt seit `3d1566f` nicht mehr — der Eigenbau ist gelöscht, Girders
Modul aus `Girder.Http` bremst, konfiguriert in `ocelot.json`. Die Aufgabe wird
damit kleiner und eindeutiger: eine reine Messung, kein Angleichen.

4.2.2 hat die Grenzköpfe auf die **Abweisung** gebracht — vorher standen sie nur
auf der erlaubten Antwort, ausgerechnet nicht auf der einen, bei der ein Aufrufer
sie lesen will.

```bash
# über das Limit hinaus rufen und die Köpfe der 429 ansehen
curl -si http://localhost:<gateway>/... | grep -i 'x-ratelimit\|retry-after'
```

**Erwartet auf der 429:** `X-RateLimit-Limit`, `X-RateLimit-Remaining: 0`,
`Retry-After`.

### C5. Wenn die Prüfspur übernommen wird

Zwei Ereignisse aufzeichnen, dann aus dem Speicher zurücklesen und die Kette
prüfen: `ereignis2.PreviousHash == ereignis1.Hash`, und `VerifyHash()` auf
beiden. Danach ein Feld im Speicher von Hand ändern und nachsehen, dass
`VerifyHash()` **fällt**. Ohne diese zweite Hälfte hast du eine Kette gemessen,
aber keine Manipulationserkennung.

---

## Abnahme

- `GirderVersion` steht auf `4.4.0`, Build ohne Warnung, alle Tests grün, keiner
  übersprungen
- Die Korrelationskennung ist **über einen Dienstsprung hinweg** in beiden Logs
  gemessen — und das Ticket abgehakt
- Jede Log-Vorlage mit einem jetzt geschwärzten Namen ist angesehen und
  entschieden, nicht überflogen
- `SecretName` und `TokenId` stehen wieder lesbar im Log — gemessen
- Jeder Dienst gibt seine Zusammensetzung beim Start aus, und sie deckt sich mit
  `Dienstgrundlage.cs`
- Über `AddSovereignPlatform` und über die Prüfspur ist **entschieden**, mit
  Begründung im Quelltext — Übernahme oder Ablehnung, beides ist ein Ergebnis
- Was nicht stimmt, wird als Ticket nach `bugs/` geschrieben, nicht lokal
  umschifft

**Bauen und Testen in getrennten Aufrufen.**
