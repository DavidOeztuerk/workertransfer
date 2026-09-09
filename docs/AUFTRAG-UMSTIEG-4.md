# Auftrag: auf Girder 4.0.0

Girder hat die fehlende Mitte bekommen. Dieser Auftrag zieht WorkerTransfer
nach — und hebt dabei mehrere Entscheidungen auf, die nur galten, weil Girder
sie erzwang.

**Erst prüfen: ist 4.0.0 überhaupt veröffentlicht?** Beim Schreiben war es das
nicht — PR #3 auf `feature/girder-baumeister` war offen, `main` stand auf 3.0.1,
und der einzige Tag war `v3.0.0`. Ohne veröffentlichtes Paket bricht der erste
`restore`. Nichts anfangen, bevor `dotnet restore` mit 4.0.0 durchläuft.

---

## Was 4.0.0 ändert

**Ein Katalog aus 24 Modulen, davon 18 in `UseDefaults()`:**

```
in der Vorgabe   Logging · HttpContextAccess · JsonOptions · Jwt
                 SecurityMonitoring · Resilience · SecretManagement · Audit
                 InputSanitization · RateLimiting · HealthChecks · Caching
                 Observability · SecurityHeaders · Authorization
                 CorrelationPropagation · ApiDocumentation · Cors

auf Zuruf        Communication · Encryption · HttpResponseCaching
                 PasswordHashing · TokenSessions · Principal
```

Die letzten sechs sind draußen, weil sie eine Entscheidung verlangen, die Girder
nicht treffen darf — welcher Hasher, welcher Sitzungsspeicher, welcher
Cache-Anbieter.

**`UseDefaults()` ist ein Aufruf, kein Automatismus.** Wer ihn weglässt, bekommt
nichts — dieselbe Form wie EF ohne Anbieter, aus demselben Grund. Die Reihenfolge
der Registrierung gehört Girder, nicht dem Aufrufer: zwei vertauschte Zeilen
dürfen nicht ändern, was ein Dienst tut.

**`Without(modul, grund)` verlangt die Begründung und übersetzt ohne sie nicht.**

---

## H1 — Der neue Composition Root

`Dienstgrundlage.cs` wird umgeschrieben. Die Form:

```csharp
services.AddGirder(configuration, environment, dienstname, girder => girder
    .UseDefaults()
    .Use(GirderModule.Principal)
    .Without(GirderModule.HttpResponseCaching,
        "ETag ist ein Cache beim Client; fast alles hier steht hinter dem "
        + "Einwilligungstor, und ein 304 zeigte ein widerrufenes Profil weiter")
    // … je Abweichung eine Zeile mit ihrem Grund
);
```

identity-service bekommt zusätzlich `PasswordHashing` und `TokenSessions`.

**Fünf Dinge, die dabei von selbst zurückkommen** — sie fehlten still, weil das
alte Lambda die Vorgabe ersetzte statt sie zu ergänzen: **Serilog, Swagger, CORS,
JSON-Feineinstellungen, `HttpContextAccessor`**. Prüfe nach dem Umbau, dass
Serilog wirklich schreibt und Swagger antwortet; das sind die zwei, die man
sofort sieht.

---

## H2 — Vier Entscheidungen, die neu gemessen werden müssen

Sie stammen aus 3.0.1 und galten, weil Girder kaputt oder unbrauchbar war. Das
kann jetzt anders sein. **Jede wird gemessen, keine wird angenommen.**

**`Bremse.cs`.** Sie wurde gebaut, weil Girders Ratenbegrenzung nicht bremste
*und* `X-Forwarded-For` bedingungslos glaubte. Beides ist behoben, und `Bremse.cs`
ist die einzige Datei in `src/`, die einen Girder-Fehler ersetzt. Miss
`GirderModule.RateLimiting` gegen dieselben Proben, gegen die `Bremse.cs` geprüft
ist — vor allem: **glaubt sie einem selbst gesetzten Herkunftskopf?** Bremst sie
und tut sie es nicht, fällt `Bremse.cs` weg. Sonst bleibt sie, mit einem
Kommentar, warum.

**Die Korrelationsweitergabe.** `CorrelationPropagation` ist ein Modul und in der
Vorgabe. Damit ist unser Eigenbau überflüssig — **wenn** er einen HTTP-Sprung mit
blankem `HttpClient` überlebt. Genau das messen: eine Anfrage mit
`X-Correlation-ID` an einen Endpunkt, der einen zweiten Dienst fragt, dann in
**beiden** Logs suchen.

**`Communication`.** Es blieb draußen, weil `ServiceCommunicationManager` jedes
Nicht-2xx auf `null` abbildete — und in diesem System *ist* der Statuscode die
Aussage. Der Befund gilt als behoben. Prüf es nach: ein `404` und ein `503`
müssen beim Aufrufer als `404` und `503` ankommen, und ein `404` darf **nicht**
dreimal nachgefragt werden. Erst dann darf es rein.

**`Caching`.** Meine frühere Anweisung „aus wegen ADR-0013" ist überholt: das
Modul ist aufgeteilt. `Caching` stellt Zwischenspeicher *bereit*, es speichert
nichts von selbst — die Regel ist „keine Einwilligungsentscheidung
zwischenspeichern", und die ist eine Frage je Abfrage, nicht je Modul. Keine
Einwilligungsabfrage implementiert `ICacheableQuery`. **`Caching` bleibt an**;
draußen bleibt `HttpResponseCaching`.

---

## H3 — Konfiguration über die Umgebung

Unverändert offen und unabhängig von 4.0.0:

- **`DotNetEnv` 3.1.1**, `Env.Load` je Dienst, wie in Skillswap.
- **`.env.example` vollständig**, ein Kommentar je Schlüssel; `.env` ignoriert.
- **Umgebung vor `appsettings`.** Fehlt ein Schlüssel, bricht der Start ab und
  **benennt ihn**. Kein eingebauter Vorgabewert — das wäre ein Geheimnis in git.
- Der private Schlüssel geht nur an identity-service.

---

## H4 — Was aus D noch offen ist

- **`GET /notifications` gibt 405** und verrät einen Pfad, den der Dienst hinter
  einem 404 versteckt.
- **Validatoren.** `AddCQRS` hängt `ValidationBehavior` längst ein und ruft
  `AddValidatorsFromAssemblies` — es fehlte nie die Verdrahtung, sondern der
  Inhalt. `AddInputSanitization` löst das **nicht**: das ist XSS-Abwehr am
  HTTP-Rand. Schreib die Validatoren.
- **Das Einwilligungstor von portfolio-service** hat als einziges kein Zeitlimit.
- **Sieben der fünfzehn ausgehenden Aufrufe** laufen in die Vorgabe von
  `HttpClient`: hundert Sekunden.

---

## H5 — Der Prüfer, von fremder Hand

Ein frischer Agent, der nichts davon geschrieben hat. Zu jeder Zusage die
Fundstelle **und** der Test. Dazu je Modul die Zeile: benutzt, oder bewusst nicht
mit tragendem Grund. Vierundzwanzig Zeilen, keine leer.

---

## Abnahme

- Jeder Dienst steht auf `AddGirder(...)` mit `UseDefaults()`; jede Abweichung
  trägt ihren Grund im Code
- Serilog schreibt, Swagger antwortet, CORS ist gesetzt
- Von den vier neu gemessenen Entscheidungen ist jede belegt — mit Messung, nicht
  mit Annahme
- `.env.example` reicht, damit ein Fremder ohne Rückfrage hochkommt
- `dotnet build` ohne Warnung, alle Tests grün, keiner übersprungen
