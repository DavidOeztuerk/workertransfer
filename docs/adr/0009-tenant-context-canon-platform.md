# ADR-0009: Tenant-Context-Kanon = worker_platform.context (str-Form); worker-tenancy als Reexport + scope-basierter ClaimTenantResolver

Date: 2026-07-20
Status: Accepted
Supersedes: —
Related: ADR-0002 (worker-platform = Kernel, worker-* = Bausteine), ADR-0005 (Kanon-Auflösung der Duplikate), ADR-0008 (Auth-Flow Passwort, nicht OIDC)

## Kontext

ADR-0005 hat die Tenant-Context-Dualismus ausdrücklich auf Phase 2 vertagt. Bei der Ablieferung von Sub-step 2.6 wurden zwei inkompatible Implementierungen desselben Konzepts vorgefunden:

- **Plattform-Kanon** (`worker_platform.context`): ein `tenant_context`-ContextVar der `str | None`-Form der Tenant-UUID, plus eine scope-basierte `TenantResolver`-Protocol-Signatur `resolve(scope: Scope) -> str | None`. Diese Implementierung steht auf dem Live-Request-Pfad — `create_api_app` montiert ihre eigene `TenantContextMiddleware` (`worker_platform.presentation.middleware`), die `tenant_context(self.resolver.resolve(scope))` setzt.
- **worker-tenancy** (alt): ein `_tenant_id: ContextVar[UUID | None]` plus ein `_tenant_context: ContextVar[dict | None]`, UUID-rückgebende `resolve(request: Request) -> UUID | None`-Signaturen, sowie die Resolvers `HeaderTenantResolver`, `SubdomainTenantResolver` (Stub), `ClaimTenantResolver` (UUID-rückgebend, `request.state` lesend) und `NoTenantResolver`.

Nur die Plattform steht produktiv im Request-Pfad. `worker-tenancy` wurde außer vom eigenen Smoke-Test von genau einem in-Repos-Bibliothekspaket konsumiert — `worker-middleware` —, das dieselben Middleware-Klassen (`TenantContextMiddleware`, `SecurityHeadersMiddleware`, `CompressionMiddleware`, `ExceptionHandlingMiddleware`) neu implementierte statt reexportierte, `BaseHTTPMiddleware` statt rohem ASGI nutzte und `worker_tenancy.set_tenant_id(UUID)` statt `worker_platform.context.tenter_context(str)` rief. `worker-middleware` hatte **keinen Produktions-Importeur** (nur sein eigener Smoke-Test) — exakt die Kriterien, unter denen ADR-0005 `worker-cqrs` (bytidentisch zum Kanon, kein Importeur) gelöscht hat. ADR-0005 hat `worker-middleware` allerdings als einzigem überlappenden Geschwister **nicht entschieden** (die Kanon-Tabelle listet bibligatisch nur die Consumer-(2))-Spur auf).

Die Tenant-Identität darf in der Produktion laut `docs/product-scope.md` niemals aus einem Browser-Header stammen; die authentifizierte Tenant-Quelle ist der JWT-Claim (gesetzt vom Auth-Middleware in `request.state.user`).

## Entscheidung

**Kanon = `worker_platform.context`** (str-Form der Tenant-UUID; kein Runtime-Bruch zur laufenden Plattform). `worker-tenancy` wird zu einer **Dünnschicht-Reexport-Schicht** der Plattform-TenantResolver-Protocol / `NoTenantResolver` / `DevelopmentHeaderTenantResolver` sowie `get_tenant_id` / `tenant_context`.

**Neu: scope-basierter `ClaimTenantResolver(claim_attr="tenant_id")`**, der `scope["state"]["user"]` liest (gesetzt vom identity-service Auth-Middleware) und die str-Form der Tenant-ID zurückgibt — die produktive Tenant-Quelle. Die UUID-kanonische Repräsentation bleibt der Value-Object `TenantId`; die ContextVar hält ihre str-Form (Null-Bruch für die laufende Plattform; Konsumenten, die eine `UUID` brauchen, parsen die str).

**Entfernt** (in Task 20): `HeaderTenantResolver`, der `SubdomainTenantResolver`-Stub (eine echte scope-basierte Subdomain-Auflösung ist ein Phase-4-Anliegen, dort scope-basiert wiederaufzunehmen), die UUID-typisierten Helper-Contextvars `set_tenant_id` / `get_tenant_context` / `set_tenant_context`, sowie die alt-UUID-typisierte `TenantResolver`-Protocol.

**`worker-middleware` gelöscht** — toter Phase-1-Duplikat der Plattform-Kanon (keine Produktions-Importeure; `BaseHTTPMiddleware` statt rohem ASGI; `set_tenant_id(UUID)` statt `tenant_context(str)`); dieselbe Behandlung, die ADR-0005 `worker-cqrs` angedeihen ließ (bytidend zum Kanon, kein Importeur).

**`worker-platform.create_api_app` erhält einen Compose-Hook** (`tenant_resolver`, `auth_middleware`, `auth_middleware_kwargs`, `routers`), damit ein Service seinen Claim-Resolver installiert, ohne dass der Kernel Business-Logik lernt (ADR-0002-Grenze gewahrt). `auth_middleware_kwargs` ist ein generisches `dict[str, Any]`, das der Kernel 1:1 an `app.add_middleware(auth_middleware, **kwargs)` weitergibt — nur dieses generische Dict überschreitet die Grenze, der Kernel kennt keine konkreten Middleware-Konstruktoren.

## Konsequenzen

- Eine ContextVar, eine Resolver-Signatur (`scope`), eine Tenant-Quelle in der Produktion (Claims). Dev/Test-Header-Unterstützung unverändert (`DevelopmentHeaderTenantResolver` ist environment-gated, `allow_development_tenant_header` default `false`).
- Ein scope-basierter Subdomain-Resolver wird, wenn benötigt, in Phase 4 hier scope-basiert wiederaufgenommen.
- Der Tenant-str-vs-UUID-Kosmetik ist explizit: Konsumenten, die eine `UUID` brauchen, parsen die str; die ContextVar hält `str` für Null-Bruch.
- Eine Middleware mit Service-spezifischen Konstruktor-Argumenten (z.B. `AuthMiddleware(tokens=...)`) wird über `auth_middleware_kwargs` installiert — das ist die einzige Stelle, an der Service-Konfiguration die Kernel-Grenze passiert, und sie ist absichtlich generisch.
- `worker-middleware` als Duplikat ist beseitigt; die Repository-Geschwister, die bisher symbolisch `worker-tenancy` konsumierten, importieren jetzt die Reexports.

## Verifikation

- `apps/identity-service/tests/integration/test_tenant_source.py` beweist: im Produktionsmodus (`WORKER_ENVIRONMENT=production`, `WORKER_ALLOW_DEVELOPMENT_TENANT_HEADER=false`) wird ein `X-Tenant-ID`-Spoof-Versuch ignoriert und `/me` liefert die Tenant-ID aus dem Claim (`== str(tenant)`, nicht der Header-Wert). Die Tenant-Identität kommt niemals aus einem Browser-Header in der Produktion.
- `packages/worker-tenancy/tests/test_claim_resolver.py::test_reexports_match_platform_canon_identity` beweist die Identitäts-Gleichheit der Reexports zum Plattform-Kanon (`worker_tenancy.get_tenant_id is worker_platform.context.get_tenant_id`; `tenant_context` analog).
- `packages/worker-platform/tests/test_app_compose_hook.py` beweist, dass der Compose-Hook `tenant_resolver` überschreibt, `routers` einklinkt und `auth_middleware_kwargs` an den Middleware-Konstruktor weiterreicht.
- `make check` grün (108 passed, 2 skipped nach Task 21).
