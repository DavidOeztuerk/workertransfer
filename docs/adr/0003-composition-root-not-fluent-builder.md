# ADR-0003: Explicit Composition-Root per service instead of a fluent PlatformBuilder

- **Status:** Accepted
- **Date:** 2026-07-12
- **Relates:** ADR-0002 (kernel vs libraries)

## Context

`kon.txt` sketches a fluent builder for assembling a service:

```python
app = PlatformBuilder()
    .add_configuration()
    .add_logging()
    .add_database()
    .add_authentication()
    ...
    .build()
```

The current `worker-platform` instead exposes a plain factory,
`create_api_app(settings)`, together with an **explicit** CQRS `Mediator`
(register_handler, add_behavior — no reflection). That style is already
running and tested in `identity-service`.

A fluent builder looks ergonomic but introduces real risks at this scale:
- **Hidden ordering dependencies** — `.add_database()` before
  `.add_authentication()` vs the reverse can silently change middleware order,
  cache availability, or transaction participation, with no compile-time signal.
- **Implicit "what's on"** — calling ten `.add_*()` methods makes it unclear
  which cross-cutting behaviours are actually active for a given service,
  which in a consent/security-first product is dangerous (e.g. an oversight
  silently drops security headers or tenant enforcement).
- **Conflicts with the explicit Mediator** — the kernel deliberately avoids
  reflection-based registration; a fluent builder that "discovers" features
  would contradict that decision.

## Decision

Each service assembles itself through an **explicit Composition-Root**:
a `compose.py` (or `composition.py`) module in the service that:

1. Constructs settings (`IdentityServiceSettings`).
2. Builds infrastructure adapters explicitly (`SqlAlchemyUserRepository`,
   `RedisCache`, `JwtIssuer`, …) from the relevant `worker-*` libraries.
3. Wires them into the kernel via `worker_platform.presentation.app.
   create_api_app(settings, builder=<service-specific builder>)`.
4. Registers commands/queries and pipeline behaviours on the `Mediator`
   explicitly.

The kernel may grow small, well-named **registration helpers**
(`register_auth(app, deps)`, `register_rate_limit(app, deps)`) that the
Composition-Root calls in a deliberate order — but there is **no fluent
builder** and no "add everything" convenience. Order is explicit and visible
in every service's Composition-Root.

## Consequences

- A reader can see, in one file per service, exactly what is on, in what order
  middleware is applied, and which ports are provided. This directly serves the
  product's consent/security-by-design posture (product-scope.md): a missing
  security control is a visible absence, not a forgotten method call.
- The `worker` CLI (`worker new-service`) generates a Composition-Root scaffold
  rather than a fluent builder, so new services inherit the convention.
- If a future, genuinely repeated assembly pattern emerges across many services,
  it graduates to a kernel helper — but only after the duplication is proven,
  not pre-emptively (mirrors ADR-0001's evolutionary stance).
- The fluent `PlatformBuilder` in `kon.txt`/`IMPLEMENTATION_PLAN.md` is
  documented as **not adopted**, so the vision's intent (reuse without
  boilerplate) is satisfied by Composition-Root + registration helpers instead.

## Verification

`worker new-service <name>` produces a service with a `compose.py` that the
reader can audit. CI enforces that every service's middleware order matches
an expected in-order list (a test in the generated scaffold asserts the
outermost-to-innermost middleware sequence).
