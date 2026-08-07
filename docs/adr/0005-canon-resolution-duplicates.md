# ADR-0005: Kanon-Auflösung der architektonischen Duplikate

Date: 2026-07-15
Status: Accepted
Supersedes: —
Related: ADR-0002 (worker-platform = Kernel, worker-* = Bausteine)

## Kontext

Phase 1.4 (`docs/ULTRAPLAN.md`) verlangt, die in der Foundation
entstandenen architektonischen Duplikate aufzulösen. ADR-0002 hat festgelegt,
dass `worker-platform` der Kanon für laufzeit-übergreifende Konzepte ist
(Context-Propagation, CQRS-Mediator, Settings) und `worker-*`-Geschwister
entweder Dünnschicht-Reexports des Kanons oder ergänzende, eigenständige
Bausteine sind — nie inhaltliche Konkurrenten.

Verifizierter Stand (Import-Analyse über `packages/` und `apps/`):

| Konzept | Platform-Kanon | Geschwister | Importeure des Geschwisters |
|---|---|---|---|
| Correlation/Tenant-Context | `worker_platform.context` | `worker-correlation` | `worker-logging` (1) |
| CQRS-Mediator | `worker_platform.application.cqrs` | `worker-cqrs` | keine (0) |
| Health-Router | `worker_platform.presentation.health` (`create_health_router`, `ReadinessCheck`) | `worker-health` (`HealthStatus`, `HealthCheckResult`, `Database/Redis/RabbitMQ`-Checks, `run_health_checks`) | keine (0) |
| Settings | `worker_platform.PlatformSettings` | `worker-config` (`BaseSettings`-Reexport) | keine (0) |
| Tenant-Resolver | `worker_platform.presentation.middleware` (`TenantContextMiddleware`) | `worker-tenancy` (`TenantResolver`, `HeaderTenantResolver`, `ClaimTenantResolver`, `NoTenantResolver`, Context-Helper) | `worker-middleware` (2) |

## Entscheidung

1. **`worker-cqrs` wird gelöscht.** Byte-identisch zum Kanon, kein Importeur.
   Der Kanon `worker_platform.application.cqrs.Mediator` ist die alleinige
   Implementierung. Löschung vermeidet Wartungszweige.
2. **`worker-correlation` wird zur Dünnschicht-Reexport-Schicht** für
   `worker_platform.context`. Das bestehende `worker-logging` importiert
   weiterhin `get_correlation_id`/`get_tenant_id` — der Reexport hält die
   öffentliche Flanke stabil, ohne eine zweite Implementierung zu pflegen.
   Nebenbei wird der im Duplikat vorhandene `except <Name>, <Name>:`-Bug
   (fängt nur eine Exception und bindet die Instanz an den anderen Namen)
   eliminiert, weil nur noch der Kanon gilt.
3. **`worker-config` re-exportiert die `PlatformSettings`-Familie** des Kanons
   (statt `pydantic_settings.BaseSettings`). Services, die nur
   Settings-Primitives brauchen, können `worker_config.platform_settings`
   importieren; das Modell wird nicht konkurrierend neudefiniert.
4. **`worker-health` bleibt als ergänzender Baustein bestehen** — es ist
   **kein** Duplikat des Platform-Routers: Platform liefert den Health-Router
   (`/health/live`, `/health/ready`) und das `ReadinessCheck`-Protocol;
   `worker-health` liefert die konkreten Dependency-Checks
   (`DatabaseHealthCheck`, `RedisHealthCheck`, `RabbitMQHealthCheck`) und das
   `HealthCheckResult`/`HealthStatus`-Modell. Beide greifen komplementär
   ineinander, nicht konkurrierend. Kein Reexport nötig; dokumentiert, dass
   Platform der Router-Kanon bleibt.
5. **`worker-tenancy` bleibt als eigenständiger Baustein** (Tenant-Resolver +
   Context-Helper). Es konkurriert nicht mit `worker_platform.PlatformSettings`
   (Tenant ist kein Settings-Typ) und nicht mit der Tenant-Middleware, sondern
   liefert die Resolver-Implementierungen, die die Platform-Middleware nutzt.
   `NoTenantResolver` wurde ergänzt als Default-Resolver ohne Tenant.

### Nebenbefund: `except`-Syntax-Bug im Kanon

Beim Abgleich fiel auf, dass `worker_platform.context.normalize_correlation_id`
sowie das `worker-correlation`-Duplikat die Form
`except AttributeError, ValueError:` nutzten. Das ist gültige Python-3-Syntax,
fängt aber **nur** `AttributeError` und bindet die Instanz an den Namen
`ValueError` (Shadowing des Builtins). `UUID(<ungültiger String>)` wirft jedoch
`ValueError` — dieser wurde also **nicht** gefangen, und der Aufruf crashte statt
eine neue ID zurückzugeben. ruff/mypy meldeten nichts (gültige Syntax, beide
Namen existieren). Im Zuge von 1.4 wurde der Kanon auf
`except (AttributeError, ValueError):` korrigiert; ein Repo-weiter Check
bestätigte, dass dies die letzte Stelle war (`worker-correlation` ist nun
Reexport, das Duplikat existiert nicht mehr).

## Konsequenzen

- Eine Konzept-Implementierung pro Cross-cutting-Angelegenheit; ADR-0002
  wird konsequent durchgesetzt — Re-Duplikation ist verboten.
- Die öffentliche CLI/API-Flanke von `worker-correlation`/`worker-config`
  bleibt durch Reexporte stabil; Konsumenten müssen nicht angepasst werden.
  `worker-config` hängt nun direkt am Kanon (`worker-platform`).
- `worker-health` und `worker-tenancy` sind erste Beispiele für den Fall
  "ergänzender Baustein statt Reexport" — die Unterscheidung Duplikat-vs-
  Ergänzung wird künftigen 1.4ähnlichen Entscheidungen als Präzedenz dienen.
- Phase 1.4 DoD-Eintrag dieser ADR verlinkt.

## Offene Folge-Defizite (post-1.4)

- **Tenant-Context-Speicher-Dualismus.** `worker-tenancy` führt eine eigene
  `_tenant_id`-ContextVar (Typ `UUID | None`) **zusätzlich** zur
  `worker_platform.context._tenant_id` (Typ `str | None`) und einen separaten
  `tenant_context`-dict-Speicher, den die Plattform nicht hat. Das ist in der
  Tenant-Context-**Speicherung** ein echtes Duplikat, nicht nur in den
  Resolvern. Die Typen-Semantik (UUID vs str) konsistent zu vereinigen ist ein
  Identität/Domain-Thema und wird daher auf **Phase 2** ("Identity & Tenancy")
  verschoben, nicht in der Foundation aufgelöst. ADR-0005 stellt nur fest, dass
  worker-tenancy als *Baustein* bleibt; die ContextVar-Konsolidierung ist
  Phase-2-Arbeit.
- **`worker-cli`-Sub-Befehl-Templates.** (Aus Phase 1.3 übernommen.) Die
  Templates `cqrs`/`domain`/`infrastructure` sind leer; `command`/`query`/
  `entity`/`event`/`consumer`/`publisher` erzeugen heute nur ein leeres
  Modulgerüst. Vollständige Sub-Befehl-Templates sind ein Folge-Defizit.
