# ADR-0002: worker-platform is the runtime kernel; worker-* are composable libraries

- **Status:** Accepted
- **Date:** 2026-07-12
- **Supersedes:** none
- **Relates:** ADR-0003 (Composition-Root), ADR-0001 (evolutionary services)

## Context

The repository today has overlapping implementations of the same cross-cutting
concept in two places:

- Correlation IDs: `worker_platform.context` *and* `worker-correlation`
- CQRS dispatch: `worker_platform.application.cqrs.Mediator` *and* `worker-cqrs`
- Health probes: `worker_platform.presentation.health` *and* `worker-health`
- Settings base types: `worker-platform.PlatformSettings` *and* `worker-config`
  *and* `worker-tenancy`

Only `worker-platform` is wired into a running service today
(`identity-service`), is tested, and ships the FastAPI factory
`create_api_app()`. The `worker-*` siblings are single-file modules with
genuine but unverified implementations.

`kon.txt` and `IMPLEMENTATION_PLAN.md`描绘 a future fluent
`PlatformBuilder().add_*().build()` that pulls in ~30 packages. Without a
canonical kernel, this would multiply rather than consolidate duplication.

## Decision

`worker-platform` is the **runtime kernel**: it owns the service factory,
the HTTP middleware, the canonical settings base, the CQRS mediator, the
health router, and the error/problem mapping. Services depend on it directly.

The `worker-*` packages are **isolated, composable libraries** (auth, cache,
database, ai, github, …). The kernel does **not** depend on them; instead the
**service Composition-Root** (see ADR-0003) wires them in.

For each overlapping concept there is exactly **one canonical home**:

| Concept | Canonical home | Sibling |
|---|---|---|
| Correlation / tenant contextvars | `worker_platform.context` | `worker-correlation` becomes a re-export or is removed |
| CQRS mediator + pipeline | `worker_platform.application.cqrs` | `worker-cqrs` becomes a re-export or is removed |
| Health router + probes | `worker_platform.presentation.health` | `worker-health` is repositioned as a *probe library* (dependency checks), not a router |
| Settings | `worker_platform.PlatformSettings` (base) | `worker-config` = env/flags/secrets layer; `worker-tenancy` = tenant-resolution layer; both **extend**, not replace, the platform base |

Re-exporting (a thin module that imports the canonical symbols) is acceptable
short-term to avoid churn for any consumer; removal is the long-term goal.

## Consequences

- A new service imports `worker-platform` for the HTTP shell and selects
  `worker-*` libraries for its infrastructure. No second implementation of
  the same cross-cutting concern is permitted.
- Adding a new cross-cutting concern requires choosing, up front, whether it
  lives in the kernel (runtime-wired, always-on) or in a `worker-*` library
  (opt-in). This decision is recorded in an ADR.
- `worker-platform` must stay small and transport-fastapi-specific; it must not
  grow business or domain logic, and must not gain heavy dependencies
  (langchain, chromadb, qdrant, weasyprint…) — those stay in `worker-*`.
- The fluent `PlatformBuilder` from `kon.txt` is **not** adopted; see ADR-0003.

## Verification (Phase 1.4)

During Phase 1.4 the duplicates are resolved concretely: each sibling mapping
above becomes a re-export or deletion, and `mypy`/`ruff`/`pytest` stay green.
