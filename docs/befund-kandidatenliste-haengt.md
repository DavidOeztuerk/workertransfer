# Befund — `/candidates` hängt im Ladezustand, und das ist keine Umstellung

Gefunden am 13.08.2026 beim Einlösen der Gateway-Beweise (`make validate-e2e`).
**Kein Fehler aus E3** — die Abfragelogik der Seite ist gegenüber dem Stand vor
E3a unverändert; der Diff berührt nur die Darstellung.

## Das Symptom

`notification-journey` fällt an dieser Zeile:

```ts
await expect(recruiter.locator("li").filter({ hasText: headline })).toBeVisible();
```

Das Beweisbild zeigt, was stattdessen auf der Seite stand — und nur das:

```
- status: Profile werden geladen…
```

30 Sekunden lang, und in einem Lauf mit dreifachem Budget 450 Sekunden lang.
`application-journey` prüft dieselbe Seite und war deshalb „flaky".

## Was die Trace-Datei zunächst zu widerlegen schien

```
resource-snapshot status=200 http://localhost:8003/profiles
```

Die Anfrage **gelingt**. Bei `retry: false` im QueryClient kann eine aufgelöste
Abfrage nicht „pending" bleiben — der Widerspruch löst sich in der Zeit auf: die
Antwort kam, nachdem die Behauptung längst aufgegeben hatte.

## Die Ursache, gemessen

`/candidates` fragt `GET /profiles` **ohne `limit`**, also mit
`DEFAULT_PAGE_SIZE = 20`. Für jede Zeile prüft `profile-service` die
Sichtbarkeit einzeln im Ledger (`handlers.py:176`) — parallel, aber einzeln:

```python
verdicts = await asyncio.gather(
    *(deps["consent"].may_see(p.subject_id, ...) for p in candidates)
)
```

Der Kommentar darüber kennt den Preis („eine Seite bedeutet `limit` Abfragen an
einen Service im selben Netz") und wählt bewusst parallel statt nacheinander.
Gemessen auf **ruhiger** Maschine, bei 130 freigegebenen Profilen:

| gemessen | Wert |
|---|---|
| eine einzelne `/consent/check` | 0,20 – 0,68 s |
| eine Seite `/profiles` (20 Prüfungen) | **1,7 – 8,8 s** |
| gelieferte Einträge | 16 |
| Zeilen in `consent_events` | 345 |

Die 0,2 s je Prüfung sind **nicht** Datenmenge — 345 Zeilen sind nichts. Es ist
Aufwand je Anfrage: HTTP, JWT-Prüfung, Verbindung aus dem Pool.

Unter der Last eines vollen Suite-Laufs (15 Container, Vite, Chromium) sind
daraus über 30 Sekunden geworden. Das ist die Grenze, ab der die Reise aufgibt.

## Warum es nicht nur ein Testproblem ist

Zwei Dinge, die eine Person auf dieser Seite trifft:

1. **Die Seite sagt „lädt" und hört nie auf.** Keiner der Clients hat ein
   Zeitlimit; `fetch` hat von sich aus keines. Für die Person ist das von
   Langsamkeit nicht zu unterscheiden — sie erfährt nie, dass etwas schiefging.
   Dieselbe Klasse Fehler wie im Testgerüst (`stack.ts`, behoben am 13.08.2026):
   eine Frist, die nichts durchsetzt, weil der Aufruf darunter unbegrenzt wartet.
2. **Es wird mit jedem freigegebenen Profil nicht schlimmer, aber mit jeder
   Seite gleich teuer.** 20 Prüfungen pro Seitenaufruf sind konstant — und
   konstant zu viel.

## Was NICHT die Lösung ist

**Zwischenspeichern.** ADR-0013 verbietet es ausdrücklich: ein Widerruf muss
beim nächsten Lesen wirken, deshalb wird der Ledger synchron gelesen. Ein Cache
hier ist keine Optimierung, sondern eine Regelverletzung.

## Was die Lösung wäre

**Eine Sammelprüfung.** `POST /consent/check` beantwortet heute eine Frage; ein
Endpunkt, der *n* Paare (`subject_id`, `capability`) in **einer** Anfrage
beantwortet, hält die Zusage vollständig — synchron, kein Cache, keine
Vorhaltung — und macht aus 20 Runden eine. Das ist eine Vertragsänderung
(`worker-contracts`) plus je eine Anpassung in consent-service und
profile-service.

Zusätzlich, unabhängig davon: **die Clients der Oberfläche brauchen ein
Zeitlimit.** Nicht, damit es schneller wird, sondern damit eine Seite sagen kann
„das hat nicht geklappt" statt endlos zu drehen.

Beides gehört **nicht** in einen Umstellungsschnitt von Prompt E — es sind
Änderungen an Vertrag und Verhalten. Eigener Zweig, eigener Beweis.
