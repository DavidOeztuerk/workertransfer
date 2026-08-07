# ADR-0006: Password hashing with bcrypt directly (no passlib)

- **Status:** Accepted
- **Date:** 2026-07-18
- **Relates:** ADR-0002 (worker-platform kernel, worker-* libraries), ADR-0007 (JWT signing), [Phase 2 design spec](../superpowers/specs/2026-07-16-phase-2-identity-tenancy-design.md) §2

## Context

`worker-auth` used `passlib.context.CryptContext(schemes=["bcrypt"])` for password
hashing. The Phase 1.5 smoke test (`packages/worker-auth/tests/test_smoke_worker_auth.py`)
documented the blocker: `hash_password`/`verify_password` were **not callable** —
invoking them raises

```
AttributeError: module 'bcrypt' has no attribute '__about__'
```

`passlib` (last meaningful release 2023) is incompatible with `bcrypt >= 4.x`: the
`bcrypt` library removed the internal `__about__` attribute passlib introspects, and
passlib is no longer actively maintained. The two libraries are version-skewed with no
upstream fix in sight.

No password hashes exist yet (Phase 2 is the first feature that registers users), so
there is **no hash-migration burden**: a swapping decision is free of backward-compat
cost today.

## Decision

Use `bcrypt >= 4.x` **directly** — no `passlib` indirection.

- `BcryptPasswordHasher` (cost 12; `bcrypt.hashpw` + `bcrypt.gensalt` / `bcrypt.checkpw`)
  lives in `packages/worker-auth/src/worker_auth/password.py`.
- bcrypt 5.x **raises `ValueError`** for inputs longer than 72 bytes (older bcrypt
  silently truncated the input — a footgun). `hash_password` catches this and re-raises
  a typed `PasswordTooLong` so callers (e.g. the domain `PasswordPolicy` in the
  identity-service application layer) get a clear, domain-meaningful error rather than a
  raw stdlib `ValueError`.
- `verify_password` with a malformed hash string returns `False` (does not crash) —
  a bad stored hash should read as "does not match", not as a 500.
- Module-level `hash_password`/`verify_password` are kept as a default-hasher facade so
  existing smoke call-sites keep importing them unchanged; the identity-service uses the
  `BcryptPasswordHasher` class (injectable, testable rounds).
- `passlib` is removed from `worker-auth`'s `dependencies`; `bcrypt>=4.0,<5.0` is added
  explicitly. The orphaned `passlib.*` mypy override is removed from the root
  `pyproject.toml`.

## Consequences

- One crypto primitive to maintain (`bcrypt`); the surface narrows (passlib, and — with
  ADR-0007 — `python-jose`, leave the dependency graph). Fewer unmaintained deps.
- `argon2-cffi` can be added later as an **additional** scheme without a single-scheme
  lock-in: a future hasher-selection is a config choice (a `PasswordHashing` port impl
  swap), not a rewrite. The identity-service domain already defines a `PasswordHashing`
  protocol (Sub-step 2.3) so the swap seam exists.
- `>72-byte-password` handling is **explicit** (typed `PasswordTooLong`) rather than
  silently truncated. The readable-policy floor (min length, max length) lives in the
  identity-service `PasswordPolicy` (Sub-step 2.5), not in this primitive.

## Verification

```
uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py \
  -k "hash_and_verify or module_functions or password_longer or malformed_hash" -v
```

Passes (real bcrypt exercised, not stubbed). `make check` green (72 passed, 2 skipped).
`grep -RIn "import passlib\|from passlib\|pwd_context" packages apps` returns nothing.
