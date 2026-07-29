# ADR-0007: JWT signing — HS256 + PyJWT (RS256/Vault as upgrade path)

- **Status:** Accepted
- **Date:** 2026-07-18
- **Relates:** ADR-0002 (worker-platform kernel, worker-* libraries), ADR-0006 (password hashing), [Phase 2 design spec](../superpowers/specs/2026-07-16-phase-2-identity-tenancy-design.md) §2

## Context

`worker-auth.TokenManager` defaulted to `RS256` and issued/verified via
`python-jose`:

- `python-jose` is effectively unmaintained (last release 2022) and carries unpatched
  CVEs; there is no Python-3.14-facing maintenance branch.
- `RS256` requires an RSA keypair at call time. The old `create_access_token` would fail
  at call-time without a real key, so the worker-auth smoke test never exercised token
  issuance in Phase 1.
- Phase 2's tokens are consumed **only by worker-internal services in the same trust
  domain** (`apps/identity-service` issues; other `worker-*` services may verify). There
  is no external validator across a trust boundary; the asymmetry HS256 sacrifices
  (separate signing key vs. verification key) is not yet needed.

## Decision

**HS256 + PyJWT** with a shared secret.

- `TokenManager(secret, *, algorithm="HS256", access_token_expire_minutes=15,
  refresh_token_expire_minutes=1440)` lives in `packages/worker-auth/src/worker_auth/jwt.py`.
- Tokens carry `sub`, `tenant_id` (UUID, serialised as str in the claim), `roles`,
  `permissions`, `exp`, `iat`, `type` (`"access"` | `"refresh"`), `jti`.
- `verify_token(token, *, expected_type)` validates the `type` claim and wraps PyJWT
  exceptions: `jwt.ExpiredSignatureError` → `ExpiredToken`;
  `jwt.InvalidTokenError` (bad signature, malformed, wrong type) → `InvalidToken`
  (`ExpiredToken` subclasses `InvalidToken`).
- `python-jose` is dropped from `worker-auth`'s `dependencies`; `pyjwt` (already
  declared) is retained; the orphaned `jose.*` mypy override is removed from the root
  `pyproject.toml`.
- The secret is a **runtime-only** `SecretStr`, supplied to identity-service via the
  `WORKER_IDENTITY_JWT_SECRET` environment variable. In production it must be ≥32 bytes
  and provisioned at runtime (never committed; no hardcoded production default). A
  dev/test default exists only for the `LOCAL`/`TEST` environments.

## Consequences

- Simpler key story for the monorepo-internal slice — one shared secret, rotated via an
  ops runbook until the upgrade lands.
- **Idempotent upgrade path (documented, not built in Phase 2):** RS256 (asymmetric) +
  external key storage (Vault / a K8s secret / a rotated JWKS endpoint) is the logical
  upgrade when (a) external validators need the public key without the secret, or
  (b) tokens cross a trust boundary. The migration seam is narrow: a `TokenManager`
  constructor switch (algorithm + key material) and, for external verification, a
  `jwt.PyJWKClient` replacing the shared secret. This is targeted at Phase 6
  (developer-intelligence / multi-service) and Phase 10 (hardening), not Phase 2.
- HS256's shared-secret rotation is an ops concern noted here so it is not lost; it is
  not a code item for Phase 2.

## Verification

```
uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py \
  -k "access_token_roundtrip or refresh_token or wrong_expected_type or expired_token or tampered_signature" -v
```

Passes. `grep -RIn "python-jose\|from jose" packages apps` returns nothing.
`make check` green (72 passed, 2 skipped).
