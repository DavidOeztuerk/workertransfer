# ADR-0014: Kanon-Auflösung Runde 2 — worker-security/-exceptions gelöscht, worker-logging als Reexport

Date: 2026-07-31
Status: Accepted
Related: ADR-0002 (worker-platform = Kernel, worker-* = Bausteine), ADR-0005 (Kanon-Auflösung Runde 1)

## Kontext

ADR-0005 hat in Phase 1.4 vier überlappende Konzepte entschieden (Correlation,
CQRS, Health-Router, Settings). Die Kanon-Tabelle dort war jedoch nicht
vollständig: eine erneute Bestandsaufnahme in Phase 2.5 fand **vier weitere**
`worker-*`-Pakete, die dieselben Konzepte wie der Kernel implementieren. Sie
sind ADR-0005 durchgerutscht, weil die damalige Analyse nur die Pakete mit
Importeuren betrachtete.

Verifizierter Stand (Import-Analyse über `apps/` und `packages/`):

| Paket | Kernel-Gegenstück | Produktions-Importeure |
|---|---|---|
| `worker-security` | `worker_platform.presentation.middleware.SecurityHeadersMiddleware` | **0** |
| `worker-exceptions` | `worker_platform.presentation.errors` | **0** |
| `worker-logging` | `worker_platform.logging` | 1 (`worker-telemetry`) |
| `worker-health` | `worker_platform.presentation.health` | 0 |

Die Duplikate waren nicht harmlos, sondern **abweichend**:

- **`worker-security`** setzt dieselben vier Header, nutzt aber
  `BaseHTTPMiddleware` statt rohem ASGI. Der Kernel nutzt bewusst rohes ASGI,
  weil `BaseHTTPMiddleware` die ContextVar-Propagation bricht — genau der Grund,
  aus dem ADR-0009 `worker-middleware` gelöscht hat.
- **`worker-exceptions`** exportiert eine Funktion mit **identischem Namen**
  (`register_exception_handlers`), die eine **inkompatible** Problem-Shape
  erzeugt: kein `correlationId`, `extensions` statt `errors`, und sie schreibt
  `str(exc)` in `detail`. Der Kernel scrubbt den Text bewusst (Zeile
  „An unexpected error occurred."), weil eine Exception-Message PII oder interne
  Struktur preisgeben kann. Ein Service, der versehentlich das falsche
  `register_exception_handlers` importiert, verliert die Correlation-ID **und**
  leakt Exception-Text — ohne dass ein Test anschlägt.
- **`worker-logging`** hatte ein `configure_logging`, das bei **jedem** Aufruf
  einen neuen `StreamHandler` anhängt. Zweimal aufgerufen = jede Logzeile
  doppelt. Der Kernel ist über einen Handler-Sentinel idempotent.

## Entscheidung

1. **`worker-security` wird gelöscht.** Kein Importeur, strikt schwächeres
   Duplikat des Kernel-Middleware (falsche Basisklasse). Dieselbe Behandlung wie
   `worker-cqrs` (ADR-0005) und `worker-middleware` (ADR-0009).
2. **`worker-exceptions` wird gelöscht.** Kein Importeur; gleichnamige Funktion
   mit abweichendem, weniger sicherem Verhalten. Wenn eine wiederverwendbare
   Fehler-Taxonomie (`NotFoundError`, `ConflictError`, …) später gebraucht wird,
   gehört sie in den Kernel — **einen** Ort — und nicht in ein zweites Paket.
   Nebeneffekt: die einzige `DeprecationWarning` der Testsuite
   (`HTTP_422_UNPROCESSABLE_ENTITY`) verschwindet mit.
3. **`worker-logging` wird Dünnschicht-Reexport** von `worker_platform.logging`
   (Muster wie `worker-correlation`/`worker-config` in ADR-0005). Der einzige
   Konsument `worker-telemetry` läuft unverändert weiter. Ein Test prüft
   Objekt-Identität zum Kanon **und** die Idempotenz von `configure_logging`.
4. **`worker-health` bleibt** ergänzender Baustein — ADR-0005 §4 hat das bereits
   entschieden (Kernel liefert Router + `ReadinessCheck`-Protocol, `worker-health`
   die konkreten Dependency-Checks). Kein Duplikat, keine Änderung.

## Konsequenzen

- Von den ursprünglich acht überlappenden Konzepten sind jetzt alle acht
  entschieden. `packages/` schrumpft von 36 auf 34 Python-Pakete.
- Die gelöschten Pakete bleiben über die Git-Historie erreichbar; ein späterer
  Bedarf wird als Kernel-Erweiterung mit eigener ADR umgesetzt, nicht durch
  Wiederbelebung.
- `worker-auth` verlor seinen verwaisten `[tool.uv.sources]`-Eintrag auf
  `worker-security`; `tests/test_workspace_dependencies.py` würde einen solchen
  Verweis künftig sofort melden.
- Regel für die Zukunft (ADR-0002 verschärft): Ein neues `worker-*`-Paket, das
  einen Namen aus dem Kernel wiederverwendet, ist per se verdächtig. Entweder es
  reexportiert den Kanon, oder es heißt anders.

## Verifikation

- `packages/worker-logging/tests/test_smoke_worker_logging.py` beweist
  Objekt-Identität der Reexporte zum Kanon und dass ein doppelter
  `configure_logging`-Aufruf keinen zweiten Handler anhängt.
- `tests/test_workspace_dependencies.py` fängt verwaiste
  `[tool.uv.sources]`-Verweise auf nicht mehr existierende Pakete.
- `make check-py` grün nach der Löschung.
