# ADR-0008: Auth flow — password-flow now, OIDC-provider as upgrade path

- **Status:** Accepted
- **Date:** 2026-07-19
- **Relates:** ADR-0002 (worker-platform kernel, worker-* libraries), ADR-0006 (bcrypt-direct hashing), ADR-0007 (HS256 + PyJWT signing), [Phase 2 design spec](../superpowers/specs/2026-07-16-phase-2-identity-tenancy-design.md) §2, [product-scope.md](../product-scope.md)

## Context

ULTRAPLAN §Phase 2 names an "OIDC/OAuth2-Einstieg". Strictly reading, that could mean
one of two very different things, and the Phase-2 slice needs to commit to one:

- **OIDC-as-provider** — identity-service would expose Authorization-Code/OAuth2
  endpoints (`/authorize`, `/callback`, grant-type mapping, `state` handling, frontend
  redirect, PKCE). That is roughly 3× the slice scope of a password flow and, more
  importantly, has no internal consumer needing it yet in Phase 2.
- **OIDC-as-consumer / external IdP** — delegate all identity to an external provider
  (Auth0, Okta, etc.). The repo would then hold no `User` aggregate; identity lives
  outside the monorepo.

Phase 3 builds the **Consent-Ledger** against a candidate-owned profile. For consent to
bind to a user the identity **must live in this repo** — an external IdP would mean no
`user.id` / `user.tenant_id` here, undermining the domain-first goal and making the
consent binding (the central product promise) indirect and brittle. So the
OIDC-consumer reading is out for Phase 2 regardless of the OIDC-provider scope question.

On the provider side, the Authorization-Code flow's cost (endpoints + redirect plumbing)
buys nothing this slice: the only token consumer in Phase 2 is `apps/identity-service`
itself plus the Phase-3 consent service reading the JWT claim, all inside one trust
domain (ADR-0007). A self-hosted password flow covers register/login/refresh/revoke at a
fraction of that surface and keeps the `User` aggregate local — which the consent
binding needs.

## Decision

Phase 2 implements a **self-hosted password flow**:

- `apps/identity-service` owns the `User` aggregate (`identity_service.domain.user`),
  with `AccountStatus` lifecycle and `UserRegistered`/`UserLoggedIn` domain events
  (Task 9).
- `POST /auth/register` + `POST /auth/login` issue **HS256 access + refresh JWTs**
  (ADR-0007). Passwords are hashed with bcrypt-direct (ADR-0006).
- Refresh uses a **server-side `sessions` jti ledger**: a `refresh_jti` row is
  persisted per active session; refresh rotates the jti (old jti revoked, new one
  issued) so a stolen refresh token is single-use. `/auth/logout` revokes the active
  session's jti.
- **No external IdP, no OIDC-provider endpoints** in Phase 2. The ULTRAPLAN
  "OIDC/OAuth2-Einstieg" checkbox is intentionally deferred, not silently dropped.

## Consequences

- **Consent binding is preserved.** Consent in Phase 3 binds to `User.tenant_id` +
  `User.id` — both already claim-authenticated after Phase 2 (the tenant comes from the
  JWT claim, never a browser header in prod — see `docs/product-scope.md` and
  ADR-0004). No identity-lives-elsewhere indirection.
- **Audit is PII-free by construction** (Task 8 / ADR-0012): the password flow emits
  `AuditAction` events with an allowlisted metadata set, never the password or email.
- **Smaller security surface for the slice.** One trust domain, one shared HS256
  secret (rotated via ops runbook — ADR-0007), no redirect/MFA/ PKCE plumbing to get
  wrong in Phase 2.

## Upgrade path documented (not built in Phase 2)

Two independent seams exist, both deferred to Phase 6 (developer-intelligence /
multi-service) / Phase 10 (hardening):

- **OIDC-as-provider:** if/when multiple agents or services need a real
  Authorization-Code flow, `authlib` (already a transitive dep via `worker-auth`)
  supplies the OIDC-provider endpoints. The existing `TokenService` port
  (`identity_service.domain.services.TokenService`) and the `User` aggregate are the
  seam — the JWT issuance changes, the domain does not. No refactor of `User`,
  `AccountStatus`, or the audit model is required.
- **External IdP / federated login:** can be added later as an *alternative*
  authentication path (a `PasswordHashing`-adjacent port for federated identity). It
  coexists with the password flow rather than replacing it; the local `User` aggregate
  and consent binding stay.

The decision is therefore reversible in both directions without re-doing Phase 2.

## Verification

- `POST /auth/register` persists a `User` (ACTIVE) + `AuditAction.REGISTER` and returns
  201; a duplicate email in the same tenant returns 409.
- `POST /auth/login` returns `{access_token, refresh_token}` (HS256, JWT claims
  include `sub`/`tenant_id`/`roles`); failure persists `AuditAction.LOGIN_FAILURE`
  and returns 401 (`InvalidCredentials`).
- `GET /me` echoes `tenant_id` from the JWT claim, not a header (Sub-steps 2.5/2.6).
- `make check` + `pnpm check`/`pnpm test` green after Sub-steps 2.4–2.7.
