# Übergabe: Korrelationskennung durch die Kette

**Auftrag:** Protokolle von WorkerTransfer analysieren, Schwerpunkt Korrelationskennung.
**Stand:** in Arbeit — Datei wird nach *jedem* gemessenen Sprung fortgeschrieben.
**Regel für Nachfolger:** Nichts hier ist Vermutung. Steht eine Zeile ohne Beleg, ist sie ungemessen und als solche markiert.

## Zustand der Maschine (fortlaufend gepflegt)

| Frage | Stand | Zeit |
|---|---|---|
| Container laufen? | **nein — noch nicht hochgefahren** | Start des Auftrags |
| Testkonten angelegt? | noch keine | — |
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

| # | von → nach | Endpunkt | Kennung angekommen? | Beleg |
|---|---|---|---|---|
| — | noch nichts gemessen | | | |

## Testkonten

_noch keine_

## Aufräumen

Am Ende: `docker compose down`. Temporäre Änderungen zurücknehmen und benennen.
