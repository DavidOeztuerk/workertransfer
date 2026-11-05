## Fehlalarm

Regel 2 (ADR-0011) lehnt lesende `sed`-Aufrufe ab, wenn der Ausdruck `.*/` enthält. Der Ausdruck wird als Pfad gelesen und gegen `.monarch/ledger/**` geprüft.

Verwandt mit #55, das `sed 's/.*/new/' f` wieder erlaubt hat. Dieser Fall ist noch offen: Das Muster steht hier in der Mitte des Ausdrucks oder endet auf `//`.

## Beide Fälle vom 05.11.2026 (WT, G-0001)

Die Kommandos schreiben nichts, sie lesen eine Datei oder eine Pipe:

```
sed 's/^  JwtSettings__Issuer: .*/  NOELIA_M2M_TOKEN_ENDPOINT: "http:\/\/x"/' m2m-render.yaml | bash scripts/k8s-render-check.sh
```
Meldung: Das Skript von 'sed' schreibt in den geschützten Pfad '.*/' (protect_paths: .monarch/ledger/**).

```
grep -E '^ +Failed WorkerTransfer' test-run.log | sed -E 's/^ +Failed //; s/\(.*//; s/ \[.*//' | cut -c1-110
```
Meldung: Das Skript von 'sed' schreibt in den geschützten Pfad '.*//' (protect_paths: .monarch/ledger/**).

## Erwartet

`sed` ohne `-i` und ohne `w`-Befehl schreibt nichts, und ein Ausdruck ist kein Pfad. Direkte Zugriffe auf geschützte Pfade bleiben blockiert.

Zu dem Fehlalarm gehört ein Regressionstest mit diesen beiden Kommandos.
