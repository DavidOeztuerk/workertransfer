# Phase 2 — Identity & Tenancy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first real vertical slice — a user can register, log in, receive HS256 JWTs (access+refresh), the tenant comes from the authenticated JWT claim (never a browser header in prod), every security-sensitive action is persisted as a PII-free audit event in the same UoW transaction, backed by a real Postgres migration, with Testcontainers integration tests and a German `/login` frontend route.

**Architecture:** Clean Architecture, inward-pointing (Presentation → Application → Domain; Infrastructure implements Application ports). `worker-platform` stays the runtime kernel; `worker-auth`/`worker-database`/`worker-events`/`worker-tenancy` are composable libraries wired in per service via a Composition-Root (ADR-0002/0003). Slice order is inside→out (Ansatz A): Domain → Persistence/Migration → worker-auth repair → Application+HTTP → Tenant claims-switch+consolidation → Frontend.

**Tech Stack:** Python 3.14 (`uv` workspace), FastAPI, SQLAlchemy 2 async + asyncpg, Alembic 1.18, `bcrypt` 5.x (direct, no passlib), PyJWT 2.13 (HS256), pydantic 2, pytest + `testcontainers[postgres]`, React 19 + TanStack Query + TanStack Router, `pnpm`/`turbo`.

## Global Constraints

- **Python 3.14 required** (`.python-version`); ruff `line-length=100`, `target-version=py314`, selects `E F I B UP ASYNC RUF`; ruff ignores `S101` only in `**/tests/**`.
- **`make check` before every commit** (fail-fast order: `ruff format --check` → `ruff check` → `mypy packages apps` → `pytest`). `make fix` for format/import autogen. mypy is strict everywhere **except `tests/`**; no `Any` shortcuts in domain/application.
- **No `pip`/`poetry`/`npm`/`yarn`.** Python = `uv`; frontend = `pnpm`. Frontend gate: `pnpm --filter @workertransfer/web run check` then `run test` before frontend commits.
- **Branch `phase-2-identity-tenancy`** (off `develop`). **Commit per sub-step. No PRs to `main`.**
- **No secrets/tokens/CVs/contracts/raw source in repo or logs.** JWT secret is runtime-only (`SecretStr`); passwords are never logged; `correlation_id` does not log request bodies.
- **Tenant identity from authenticated claims in production, never a browser header.** `allow_development_tenant_header` default `False`; header resolver only `LOCAL`/`DEVELOPMENT`/`TEST`.
- **AI drafts, humans decide** — not exercised in Phase 2 code, but no autonomous ranking/contact/rejection decisions exist anywhere.
- `worker-core` primitives: `Entity` (mutable dataclass, `id: UUID` default `uuid4`, identity equality), `ValueObject` (frozen dataclass marker), `DomainEvent` (frozen, fields `event_id: UUID`, `occurred_at: datetime`), `Result[TValue]` (`.ok(v)`, `.fail(DomainError)`, `.is_success`, `.value`, `.error`), `DomainError(code: str, message: str)`.

**Spec:** [`docs/superpowers/specs/2026-07-16-phase-2-identity-tenancy-design.md`](../specs/2026-07-16-phase-2-identity-tenancy-design.md)

---

## File Structure

New/modified files (locked decomposition):

```
packages/worker-auth/src/worker_auth/
├── password.py                 NEW — BcryptPasswordHasher (bcrypt>=4 direct), PasswordTooLong, PasswordHashError
├── jwt.py                      NEW — TokenManager (PyJWT HS256), TokenPayload, InvalidToken, ExpiredToken
├── __init__.py                 MODIFY — new re-exports; remove python-jose import
packages/worker-auth/pyproject.toml   MODIFY — drop python-jose/passlib, add bcrypt>=4.0,<5.0; keep pyjwt, authlib, cryptography, pydantic, pydantic-settings
packages/worker-auth/tests/
├── test_smoke_worker_auth.py   MODIFY — exercise real hash/verify + real HS256 roundtrip (blocker resolved)

packages/worker-tenancy/src/worker_tenancy/
├── __init__.py                 MODIFY (Sub-step 2.6) — re-export platform context; ClaimTenantResolver scope-based

packages/worker-platform/src/worker_platform/presentation/
├── app.py                      MODIFY (Sub-step 2.6) — create_api_app(..., *, routers, tenant_resolver) compose hook

packages/worker-database/src/worker_database/
├── __init__.py                 MODIFY (Sub-step 2.2) — confirm Base is single autogenerate target (likely no code change; docstring)

apps/identity-service/
├── pyproject.toml              MODIFY — add worker-auth, worker-database, worker-events, worker-tenancy, bcrypt, pyjwt, sqlalchemy[asyncio], asyncpg, alembic, psycopg; dev: testcontainers[postgres], httpx
├── src/identity_service/
│   ├── configuration.py        MODIFY — IdentityServiceSettings: JWT_SECRET, DATABASE_URL, JWT expiry
│   ├── main.py                 MODIFY — wire build_app via compose
│   ├── domain/
│   │   ├── __init__.py          NEW
│   │   ├── value_objects.py     NEW — Email, PasswordHash, UserId, TenantId
│   │   ├── user.py              NEW — User aggregate, AccountStatus, transitions, events
│   │   ├── audit.py             NEW — AuditEvent, AuditAction, AUDIT_METADATA_ALLOWLIST
│   │   └── services.py          NEW — PasswordHashing port, TokenService port, Clock port
│   ├── application/
│   │   ├── __init__.py          NEW
│   │   ├── ports.py             NEW — UserRepository, SessionRepository, AuditRepository (async protocols)
│   │   ├── commands.py          NEW — RegisterUser, AuthenticateUser, RefreshToken, RevokeToken + handlers
│   │   └── mediator.py          NEW — compose + register handlers
│   ├── infrastructure/
│   │   ├── __init__.py          NEW
│   │   ├── database/__init__.py NEW
│   │   ├── database/models.py   NEW — UserModel, SessionModel, AuditEventModel (PG types)
│   │   ├── database/repositories.py NEW — Sqlalchemy impls of ports
│   │   ├── auth/__init__.py     NEW
│   │   ├── auth/jwt_service.py  NEW — JwTokenService (adapts worker_auth.TokenManager to domain TokenService port)
│   │   ├── auth/hasher.py       NEW — BcryptPasswordAdapter (adapts worker_auth password.py to domain PasswordHashing)
│   │   ├── clock.py             NEW — SystemClock (Clock port impl)
│   │   └── compose.py           NEW — Composition Root: UoW, repos, services, EventBus, mediator
│   └── presentation/
│       ├── __init__.py          NEW
│       ├── http/__init__.py     NEW
│       ├── http/router.py       NEW — /auth/register, /auth/login, /auth/refresh, /auth/logout, /me
│       ├── auth_middleware.py   NEW — Verify JWT, set request.state.user
│       └── compose_api.py       NEW — build_app(settings): platform create_api_app + routers + claim resolver + auth mw
├── alembic.ini                 NEW
├── migrations/
│   ├── env.py                  NEW — async env.py (async_engine_from_url), target_metadata = Base.metadata
│   ├── script.py.mako          NEW — standard alembic template
│   └── versions/0001_init_users_sessions_audit.py NEW — hand-written first revision
└── tests/
    ├── __init__.py             NEW (if collection needs it — see Task 4.1 detail)
    ├── test_smoke_identity_service.py MODIFY — keep /health; (integration separate)
    ├── unit/
    │   ├── __init__.py         NEW
    │   ├── test_user.py        NEW
    │   ├── test_value_objects.py NEW
    │   ├── test_audit.py       NEW
    │   ├── test_hashing_port.py NEW
    │   └── test_commands.py    NEW (fake repos)
    └── integration/
        ├── __init__.py         NEW
        ├── conftest.py         NEW — Testcontainers PG fixture, alembic upgrade head
        ├── test_migrations.py  NEW
        ├── test_repository_roundtrip.py NEW
        └── test_auth_endpoints.py NEW — end-to-end incl. tenant-claim assertion

docs/adr/
├── 0006-password-hashing-bcrypt-direct.md     NEW (Sub-step 2.1)
├── 0007-jwt-signing-hs256-pyjwt.md            NEW (Sub-step 2.1)
├── 0008-auth-flow-password-not-oidc.md        NEW (Sub-step 2.3)
├── 0010-alembic-per-service-async.md          NEW (Sub-step 2.2)
├── 0011-integration-testcontainers-postgres.md NEW (Sub-step 2.4)
├── 0009-tenant-context-canon-platform.md      NEW (Sub-step 2.6)
└── 0012-audit-event-sync-uow-pii-allowlist.md NEW (Sub-step 2.7)

pyproject.toml (root)             MODIFY (Sub-step 2.4) — add testcontainers[postgres] to [dependency-groups] dev
.github/workflows/ci.yml          MODIFY (Sub-step 2.4/2.9) — Docker for testcontainers integration step

apps/web/
├── package.json                MODIFY (Sub-step 2.8) — add @tanstack/react-router
└── src/
    ├── app.tsx                  MODIFY (Sub-step 2.8) — minimal router root
    ├── app.test.tsx            MODIFY (Sub-step 2.8) — router-aware
    ├── routes/                 NEW
    │   ├── __root.tsx          NEW (if file-router) or wiring in app.tsx (code-router) — see Task 8.1
    │   ├── login.tsx           NEW — German login page
    │   └── home.tsx            NEW — landing (existing hero content moved here)
    ├── auth/                   NEW
    │   ├── client.ts           NEW — fetch wrapper, cookie auth, tenant from claim
    │   ├── client.test.ts      NEW
    │   └── query-client.ts     NEW — TanStack Query client
    └── env.ts                  NEW — VITE_API_BASE_URL loader
```

**Domain layering rule:** `domain/` imports only `worker_core` and `worker_events`. `application/` imports `domain/` + `worker_core`. `infrastructure/` imports `application/ports` + `worker_*` libs. `presentation/` imports `application` + `worker_platform`. No upward arrows.

---

## Sub-step 2.1 — Repair `worker-auth` (bcrypt direct + PyJWT HS256)

**Sub-step goal:** Unbreak the password hashing and JWT issuance. Produce a `worker-auth` library with `BcryptPasswordHasher` (bcrypt>=4 direct) and `TokenManager` (PyJWT, HS256). No `python-jose`, no `passlib`. Smoke test exercises the real code. Write ADR-0006 + ADR-0007.

### Task 1: BcryptPasswordHasher (password hashing, direct bcrypt)

**Status:** ✅ DONE (2026-07-17). `worker_auth.password.BcryptPasswordHasher` (bcrypt>=4 direct, rounds=12) + `PasswordTooLong`/`PasswordHashError`; passlib machinery removed from `__init__.py`; module-level `hash_password`/`verify_password` re-export from the new module. Tests: 4 new (roundtrip / module funcs / >72 bytes / malformed hash) all pass. `make check` green (67 passed, 2 skipped). `python-jose` + old `TokenManager` left untouched (Task 2 rewrites them).

**Files:**
- Create: `packages/worker-auth/src/worker_auth/password.py`
- Modify: `packages/worker-auth/src/worker_auth/__init__.py` (lines 1-10, 58-63)
- Test: `packages/worker-auth/tests/test_smoke_worker_auth.py`

**Interfaces:**
- Produces: `BcryptPasswordHasher` with `hash_password(password: str) -> str` and `verify_password(plain: str, hashed: str) -> bool`; exceptions `PasswordTooLong`, `PasswordHashError`.
- Produces module-level functions `hash_password(password: str) -> str` / `verify_password(plain: str, hashed: str) -> bool` backed by a module-default hasher (kept for back-compat smoke call sites; identity-service uses the class).
- bcrypt 5.x **raises `ValueError`** for inputs >72 bytes → `hash_password` must catch that and raise `PasswordTooLong` (clearer, typed). App-level length policy lives in Task 5; here we only translate the raw error.

- [ ] **Step 1: Write the failing tests**

Append to `packages/worker-auth/tests/test_smoke_worker_auth.py` (keep the existing `test_smoke_token_manager_and_payload` — it is rewritten in Task 2):

```python
import bcrypt
import pytest

from worker_auth import BcryptPasswordHasher, PasswordTooLong, hash_password, verify_password


def test_hash_and_verify_roundtrip() -> None:
    hasher = BcryptPasswordHasher()
    hashed = hasher.hash_password("correct horse battery staple")
    assert hashed != "correct horse battery staple"
    assert hashed.startswith("$2b$") or hashed.startswith("$2y$")
    assert hasher.verify_password("correct horse battery staple", hashed) is True
    assert hasher.verify_password("wrong password", hashed) is False


def test_module_functions_backed_by_default_hasher() -> None:
    hashed = hash_password("hunter2")
    assert hashed.startswith("$2")
    assert verify_password("hunter2", hashed) is True
    assert verify_password("nope", hashed) is False


def test_password_longer_than_72_bytes_raises_password_too_long() -> None:
    hasher = BcryptPasswordHasher()
    long_password = "a" * 73  # 73 ASCII bytes > 72
    with pytest.raises(PasswordTooLong):
        hasher.hash_password(long_password)


def test_verify_with_malformed_hash_returns_false() -> None:
    hasher = BcryptPasswordHasher()
    assert hasher.verify_password("anything", "not-a-bcrypt-hash") is False
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py -k "hash_and_verify or module_functions or password_longer or malformed_hash" -v`
Expected: FAIL with `ImportError: cannot import name 'BcryptPasswordHasher' ... from 'worker_auth'` (and the passlib-backed `hash_password` may raise the `bcrypt.__about__` `AttributeError` when actually called).

- [ ] **Step 3: Write minimal implementation**

Create `packages/worker-auth/src/worker_auth/password.py`:

```python
"""Password hashing with bcrypt (direct, no passlib indirection)."""

from __future__ import annotations

import bcrypt

__all__ = ["BcryptPasswordHasher", "PasswordTooLong", "PasswordHashError", "hash_password", "verify_password"]

_BCRYPT_MAX_BYTES = 72


class PasswordHashError(Exception):
    """Unexpected failure while hashing or verifying a password."""


class PasswordTooLong(PasswordHashError):
    """The password exceeds bcrypt's 72-byte input limit."""


class BcryptPasswordHasher:
    """Hashes passwords with bcrypt (cost 12) and verifies them in constant time."""

    def __init__(self, rounds: int = 12) -> None:
        self.rounds = rounds

    def hash_password(self, password: str) -> str:
        password_bytes = password.encode("utf-8")
        if len(password_bytes) > _BCRYPT_MAX_BYTES:
            raise PasswordTooLong(
                f"Password is {len(password_bytes)} bytes, bcrypt limit is {_BCRYPT_MAX_BYTES}"
            )
        try:
            salt = bcrypt.gensalt(rounds=self.rounds)
            hashed = bcrypt.hashpw(password_bytes, salt)
        except ValueError as exc:  # belt-and-braces: bcrypt raises again if the check above missed an edge
            raise PasswordTooLong(str(exc)) from exc
        except Exception as exc:  # pragma: no cover - defensive
            raise PasswordHashError("Failed to hash password") from exc
        return hashed.decode("utf-8")

    def verify_password(self, plain: str, hashed: str) -> bool:
        try:
            hashed_bytes = hashed.encode("utf-8")
            plain_bytes = plain.encode("utf-8")
        except Exception as exc:  # pragma: no cover - defensive
            raise PasswordHashError("Failed to encode inputs") from exc
        try:
            return bcrypt.checkpw(plain_bytes, hashed_bytes)
        except ValueError:
            # Malformed hash string — treat as "does not match" rather than crash.
            return False


_default_hasher = BcryptPasswordHasher()


def hash_password(password: str) -> str:
    return _default_hasher.hash_password(password)


def verify_password(plain: str, hashed: str) -> bool:
    return _default_hasher.verify_password(plain, hashed)
```

Modify `packages/worker-auth/src/worker_auth/__init__.py` — first, **delete** the passlib machinery (lines 7 and 10: `from passlib.context import CryptContext` and `pwd_context = CryptContext(...)`) and the module-level `hash_password`/`verify_password` block (lines 58-63). Add the re-export of the new password module. The top of the file becomes:

```python
"""Authentication: JWT, OAuth2, OIDC, API Keys, Refresh tokens, Session management."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import UUID, uuid4

from pydantic import BaseModel

from worker_auth.password import (
    BcryptPasswordHasher,
    PasswordHashError,
    PasswordTooLong,
    hash_password,
    verify_password,
)
```

Leave the existing `TokenPayload` (lines 13-21) and `TokenManager` (lines 24-55) untouched for now — Task 2 rewrites `TokenManager` to use PyJWT. Keep `from jose import jwt` for this step only if the import is still referenced; Task 2 removes it. **If** you delete the `jose` import here and `TokenManager` still references `jwt.encode`, do that rewrite in Task 2 — for Task 1 just ensure `make check` is green, which requires the jose import to remain until Task 2 (or temporarily stubbing). **Cleanest path:** leave `from jose import jwt` in place for Task 1, the smoke test only calls hashing now.

At the bottom of `__init__.py`, the module-level `hash_password`/`verify_password` you deleted are now re-exported from `worker_auth.password` via the import above — no duplicate definitions.

Add an `__all__` if none exists (it does not today). After Task 1 it should contain:

```python
__all__ = [
    "TokenManager",
    "TokenPayload",
    "BcryptPasswordHasher",
    "PasswordHashError",
    "PasswordTooLong",
    "hash_password",
    "verify_password",
]
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py -k "hash_and_verify or module_functions or password_longer or malformed_hash" -v`
Expected: PASS (4 passed). The pre-existing `test_smoke_token_manager_and_payload` still passes (it only checks field storage, no JWT encoding).

- [ ] **Step 5: Run `make check` and fix**

Run: `make check`
Expected: ruff format check clean, ruff check clean, mypy clean (no new untyped defs in `password.py` — all params/returns annotated), pytest green (65 + 4 new). If mypy complains about `bcrypt.gensalt`/`bcrypt.hashpw` return types, they are typed in `bcrypt`'s stubs; if not, add a targeted `cast("bytes", ...)` and a `# type: ignore[no-any-return]` only where the upstream is genuinely untyped (verify with `uv run mypy packages/worker-auth`).

- [ ] **Step 6: Commit**

```bash
git add packages/worker-auth/src/worker_auth/password.py packages/worker-auth/src/worker_auth/__init__.py packages/worker-auth/tests/test_smoke_worker_auth.py
git commit -m "worker-auth: bcrypt-direct password hashing (fixes passlib blocker)

Replace passlib.CryptContext with a direct bcrypt>=4 BcryptPasswordHasher.
hash_password/verify_password now exercise real bcrypt (rounds=12); the
Phase-1 passlib/bcrypt `__about__` AttributeError blocker is resolved.
>72-byte passwords raise PasswordTooLong (bcrypt 5.x raises ValueError).
Module-level hash_password/verify_password kept as a default-hasher facade
for back-compat smoke call sites."
```

### Task 2: TokenManager → PyJWT (HS256) + TokenPayload rewrite

**Status:** ✅ DONE (2026-07-18). `worker_auth.jwt.TokenManager` (PyJWT HS256, access+refresh, `verify_token(*, expected_type)` → `InvalidToken`/`ExpiredToken`); `TokenPayload` moved to `jwt.py`. `python-jose`/`passlib[bcrypt]` dropped from `worker-auth` deps; `bcrypt>=4.0,<5.0` added (resolved 4.3.0). `__init__.py` re-exports from `jwt`+`password`. `jose.*`/`passlib.*` mypy overrides removed from root `pyproject.toml`. `uv sync` uninstalled jose/ecdsa/rsa. Tests: 10 (6 JWT + 4 password) all pass. `make check` green (72 passed, 2 skipped). `grep -RIn "import jose|from jose|passlib|pwd_context" packages apps` returns nothing.

**Files:**
- Create: `packages/worker-auth/src/worker_auth/jwt.py`
- Modify: `packages/worker-auth/src/worker_auth/__init__.py` (remove `from jose import jwt`; remove inline `TokenManager`/`TokenPayload`; re-export from `jwt.py`)
- Modify: `packages/worker-auth/pyproject.toml` (drop `python-jose`, drop `passlib[bcrypt]`, add `bcrypt>=4.0,<5.0`; keep `pyjwt`, `authlib`, `cryptography`, `pydantic`, `pydantic-settings`)
- Modify: `packages/worker-auth/tests/test_smoke_worker_auth.py` (rewrite the old `test_smoke_token_manager_and_payload` to exercise real HS256 roundtrip)
- Test: `packages/worker-auth/tests/test_smoke_worker_auth.py`

**Interfaces:**
- Produces: `TokenManager(secret: str, *, algorithm: str = "HS256", access_token_expire_minutes: int = 15, refresh_token_expire_minutes: int = 1440)` with `create_access_token(user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]) -> str`, `create_refresh_token(user_id: UUID, tenant_id: UUID, *, session_jti: str) -> str`, `verify_token(token: str, *, expected_type: str) -> TokenPayload`.
- Produces: `TokenPayload(sub: UUID, tenant_id: UUID, roles: list[str], permissions: list[str], exp: int, iat: int, type: str, jti: str)` (pydantic).
- Produces: `InvalidToken`, `ExpiredToken` exceptions (`InvalidToken` base; `ExpiredToken` subclass) wrapping `jwt.exceptions.InvalidTokenError` / `ExpiredSignatureError`.
- `algorithm` defaults to `HS256`; `RS256` no longer constructed at module import (the old code imported RSA-flavored encode paths lazily via jose, so there was no import-time RSA requirement — but the old `create_access_token` would fail at call-time without a real key). PyJWT HS256 needs only the secret string.

- [ ] **Step 1: Write the failing tests**

Replace `test_smoke_token_manager_and_payload` in `packages/worker-auth/tests/test_smoke_worker_auth.py` with real-roundtrip tests (and add an import for `InvalidToken`/`ExpiredToken`):

```python
import time
from uuid import uuid4

import pytest

from worker_auth import ExpiredToken, InvalidToken, TokenManager, TokenPayload


def test_access_token_roundtrip() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()

    token = manager.create_access_token(user_id, tenant_id, roles=["user"], permissions=["read"])
    decoded = manager.verify_token(token, expected_type="access")

    assert decoded.sub == user_id
    assert decoded.tenant_id == tenant_id
    assert decoded.roles == ["user"]
    assert decoded.permissions == ["read"]
    assert decoded.type == "access"
    assert isinstance(decoded.jti, str) and len(decoded.jti) > 0


def test_refresh_token_has_distinct_type_and_jti() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()

    token = manager.create_refresh_token(user_id, tenant_id, session_jti="session-jti-123")
    decoded = manager.verify_token(token, expected_type="refresh")

    assert decoded.type == "refresh"
    assert decoded.jti == "session-jti-123"


def test_wrong_expected_type_rejects() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()

    access = manager.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    with pytest.raises(InvalidToken):
        manager.verify_token(access, expected_type="refresh")


def test_expired_token_raises_expired() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()
    token = manager.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    # Expire it deterministically: re-encode with a past exp via the manager's internal helper path
    # is not exposed; instead sleep is not deterministic in tests — use a token with 0-minute expiry.
    zero_min = TokenManager(secret=secret, access_token_expire_minutes=0)
    expired = zero_min.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    import time as _t

    _t.sleep(1)  # ensure exp (now) is in the past by ≥1s
    with pytest.raises(ExpiredToken):
        manager.verify_token(expired, expected_type="access")


def test_tampered_signature_rejected() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()
    token = manager.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    tampered = token[:-4] + "aaaa"
    with pytest.raises(InvalidToken):
        manager.verify_token(tampered, expected_type="access")


def test_tokenpayload_model_fields() -> None:
    now = int(time.time())
    payload = TokenPayload(
        sub=uuid4(),
        tenant_id=uuid4(),
        roles=["admin"],
        permissions=["*"],
        exp=now + 60,
        iat=now,
        type="access",
        jti="j",
    )
    assert payload.type == "access"
    assert payload.roles == ["admin"]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py -v`
Expected: FAIL — `TokenManager(...)` no longer accepts `private_key`/`public_key` kwarg names in the new signature (old signature: `__init__(private_key, public_key, algorithm="RS256", ...)`), so `test_access_token_roundtrip` errors with a `TypeError` (unexpected keyword `secret`) or the old `create_access_token`/`verify_token` semantics break the assertion.

- [ ] **Step 3: Write minimal implementation**

Create `packages/worker-auth/src/worker_auth/jwt.py`:

```python
"""JWT issuance and verification with PyJWT (HS256)."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID, uuid4

import jwt as pyjwt
from pydantic import BaseModel

__all__ = ["TokenManager", "TokenPayload", "InvalidToken", "ExpiredToken"]


class InvalidToken(Exception):
    """A JWT could not be verified (bad signature, malformed, wrong type)."""


class ExpiredToken(InvalidToken):
    """A JWT's exp claim is in the past."""


class TokenPayload(BaseModel):
    sub: UUID
    tenant_id: UUID
    roles: list[str] = []
    permissions: list[str] = []
    exp: int
    iat: int
    type: str  # "access" | "refresh"
    jti: str


class TokenManager:
    def __init__(
        self,
        secret: str,
        *,
        algorithm: str = "HS256",
        access_token_expire_minutes: int = 15,
        refresh_token_expire_minutes: int = 1440,
    ) -> None:
        if algorithm != "HS256":
            raise ValueError(f"Only HS256 is supported in Phase 2, got {algorithm!r}")
        self.secret = secret
        self.algorithm = algorithm
        self.access_token_expire_minutes = access_token_expire_minutes
        self.refresh_token_expire_minutes = refresh_token_expire_minutes

    def create_access_token(
        self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]
    ) -> str:
        return self._encode(
            user_id=user_id,
            tenant_id=tenant_id,
            roles=roles,
            permissions=permissions,
            token_type="access",
            expire_minutes=self.access_token_expire_minutes,
            jti=str(uuid4()),
        )

    def create_refresh_token(self, user_id: UUID, tenant_id: UUID, *, session_jti: str) -> str:
        return self._encode(
            user_id=user_id,
            tenant_id=tenant_id,
            roles=[],
            permissions=[],
            token_type="refresh",
            expire_minutes=self.refresh_token_expire_minutes,
            jti=session_jti,
        )

    def verify_token(self, token: str, *, expected_type: str) -> TokenPayload:
        try:
            claims: dict[str, Any] = pyjwt.decode(token, self.secret, algorithms=[self.algorithm])
        except pyjwt.ExpiredSignatureError as exc:
            raise ExpiredToken("Token expired") from exc
        except pyjwt.InvalidTokenError as exc:
            raise InvalidToken("Token could not be verified") from exc
        if claims.get("type") != expected_type:
            raise InvalidToken(f"Expected token type {expected_type!r}, got {claims.get('type')!r}")
        try:
            return TokenPayload(**claims)
        except Exception as exc:
            raise InvalidToken("Token claims did not match the expected schema") from exc

    def _encode(
        self,
        *,
        user_id: UUID,
        tenant_id: UUID,
        roles: list[str],
        permissions: list[str],
        token_type: str,
        expire_minutes: int,
        jti: str,
    ) -> str:
        now = datetime.now(UTC)
        payload: dict[str, Any] = {
            "sub": str(user_id),
            "tenant_id": str(tenant_id),
            "roles": roles,
            "permissions": permissions,
            "exp": int((now + timedelta(minutes=expire_minutes)).timestamp()),
            "iat": int(now.timestamp()),
            "type": token_type,
            "jti": jti,
        }
        return str(pyjwt.encode(payload, self.secret, algorithm=self.algorithm))
```

Modify `packages/worker-auth/src/worker_auth/__init__.py` to **remove** `from jose import jwt` and the inline `TokenPayload`/`TokenManager` classes (lines 13-55) and the now-unused `datetime`/`timedelta`/`uuid4` imports if they become unused. Replace the imports block and re-export from the new modules. Final `__init__.py`:

```python
"""Authentication: JWT, OAuth2, OIDC, API Keys, Refresh tokens, Session management."""

from __future__ import annotations

from worker_auth.jwt import ExpiredToken, InvalidToken, TokenManager, TokenPayload
from worker_auth.password import (
    BcryptPasswordHasher,
    PasswordHashError,
    PasswordTooLong,
    hash_password,
    verify_password,
)

__all__ = [
    "TokenManager",
    "TokenPayload",
    "InvalidToken",
    "ExpiredToken",
    "BcryptPasswordHasher",
    "PasswordHashError",
    "PasswordTooLong",
    "hash_password",
    "verify_password",
]
```

Modify `packages/worker-auth/pyproject.toml` — the `dependencies = [...]` block becomes:

```toml
dependencies = [
    "pyjwt>=2.8.0,<3.0.0",
    "authlib>=1.3.0,<2.0.0",
    "cryptography>=42.0.0,<50.0.0",
    "bcrypt>=4.0.0,<5.0.0",
    "pydantic>=2.8.0,<3.0.0",
    "pydantic-settings>=2.4.0,<3.0.0",
]
```
(Drops `python-jose` and `passlib[bcrypt]`; adds `bcrypt`.)

- [ ] **Step 4: Re-sync deps**

Run: `uv sync --all-packages --all-groups`
Expected: lockfile updates; `python-jose` and `passlib` removed from the `worker-auth` env; `bcrypt` already installed (5.0.0) satisfies the pin.

- [ ] **Step 5: Run tests to verify they pass**

Run: `uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py -v`
Expected: PASS (all password tests + all JWT tests; ~10 passed).

- [ ] **Step 6: Run `make check` and fix**

Run: `make check`
Expected: green. Watch for: mypy on `pyjwt.encode`/`pyjwt.decode` return types (PyJWT ships type hints; if a generic `dict[str, Any]` round-trip trips `no-any-return`, the `str(...)` / explicit payload typing above handles it). Remove the now-orphaned `jose`/`passlib` mypy-overrides in root `pyproject.toml` `[tool.mypy]` overrides **if they exist** (grep `uv run` config first: `grep -n "jose\|passlib" pyproject.toml`); leave others untouched.

- [ ] **Step 7: Commit**

```bash
git add packages/worker-auth/pyproject.toml packages/worker-auth/src/worker_auth/__init__.py packages/worker-auth/src/worker_auth/jwt.py packages/worker-auth/tests/test_smoke_worker_auth.py uv.lock pyproject.toml
git commit -m "worker-auth: PyJWT HS256 TokenManager (drops python-jose)

Rewrite TokenManager on PyJWT (HS256, shared secret). Access and refresh
tokens carry sub/tenant_id/roles/permissions/exp/iat/type/jti; verify_token
validates the type claim and wraps PyJWT exceptions in InvalidToken/ExpiredToken.
python-jose removed from deps (unmaintained, CVEs); bcrypt added explicitly.
RS256 + Vault/K8s key storage documented as Phase-6/10 upgrade in ADR-0007."
```

### Task 3: ADR-0006 (password hashing) + ADR-0007 (JWT signing)

**Status:** ✅ DONE (2026-07-18). `docs/adr/0006-password-hashing-bcrypt-direct.md` and `docs/adr/0007-jwt-signing-hs256-pyjwt.md` written (Accepted, cross-linked to ADR-0002 + the spec, full Context/Decision/Consequences/Verification). Docs-only; `make check` still green (72 passed, 2 skipped).

**Files:**
- Create: `docs/adr/0006-password-hashing-bcrypt-direct.md`
- Create: `docs/adr/0007-jwt-signing-hs256-pyjwt.md`

**Interfaces:**
- Consumes: the decisions recorded in the approved spec (§2).
- Produces: two ADRs (Status: Accepted, Date: 2026-07-16), cross-linked to the spec and to ADR-0002.

- [ ] **Step 1: Write ADR-0006**

Create `docs/adr/0006-password-hashing-bcrypt-direct.md` following the existing ADR shape (see `0005-canon-resolution-duplicates.md` for the section template: `# ADR-0006: <Title>` → `Date`, `Status: Accepted`, `Relates:` → `Context` → `Decision` → `Consequences` → `Verification`).

Content summary (write full prose, not bullets-only):
- **Context:** Phase 1 smoke (`packages/worker-auth/tests/test_smoke_worker_auth.py`) documented the blocker: `passlib.CryptContext(schemes=["bcrypt"])` fails with `AttributeError: module 'bcrypt' has no attribute '__about__'` — passlib (last release 2023) is incompatible with `bcrypt`>=4.x. `worker-auth`'s `hash_password`/`verify_password` were therefore never callable. No password hashes exist yet → no hash-migration burden.
- **Decision:** Use `bcrypt`>=4.x **directly** (no passlib). `BcryptPasswordHasher` (cost 12) wraps `bcrypt.hashpw`/`checkpw`. bcrypt 5.x raises `ValueError` for >72-byte inputs → translated to `PasswordTooLong` (typed, caller-facing). Module-level `hash_password`/`verify_password` kept as a default-hasher facade for back-compat smoke call sites; identity-service uses the class (Task 4).
- **Consequences:** One crypto primitive to maintain; passlib removed from deps (crypto-attack-surface narrows; `python-jose` also leaves in ADR-0007). `argon2-cffi` is addable later as an additional scheme without a single-scheme lock-in (a future hasher selection is a config choice, not a rewrite). >72-byte-password handling is explicit (no silent truncation, which older bcrypt did and which is a footgun).
- **Verification:** `uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py -k "hash_and_verify or module_functions or password_longer or malformed_hash"` passes (real bcrypt exercised, not stubbed).

- [ ] **Step 2: Write ADR-0007**

Create `docs/adr/0007-jwt-signing-hs256-pyjwt.md`:
- **Context:** `worker-auth.TokenManager` defaulted to `RS256` via `python-jose`, requiring an RSA keypair at call time and depending on an unmaintained library (last release 2022, unpatched CVEs, no Python-3.14-maintenance). Phase 2's tokens are consumed only by worker-internal services in the same trust domain; RS256's asymmetry (external validators / cross-trust-boundary) is not needed yet.
- **Decision:** **HS256 + PyJWT** with a shared secret stored as a `SecretStr` via `WORKER_IDENTITY_JWT_SECRET` (≥32 bytes in production; runtime-only, never committed; dev/test default only in `LOCAL`/`TEST`). `python-jose` dropped; `pyjwt` (already declared) retained. `TokenManager.verify_token(token, *, expected_type)` validates the `type` claim (access vs refresh) and wraps PyJWT exceptions in `InvalidToken`/`ExpiredToken`.
- **Consequences:** Simpler key story for the monorepo-internal slice. **Upgrade path documented:** RS256 (asymmetric) + external key storage (Vault / K8s secret / rotated JWKS) is the Phase-6 (developer-intelligence multi-service) / Phase-10 (hardening) upgrade; a `TokenManager` constructor switch + `jwks-client` integration is the migration seam. Until then HS256's shared-secret rotation is an ops runbook item.
- **Verification:** `uv run pytest packages/worker-auth/tests/test_smoke_worker_auth.py -k "access_token_roundtrip or refresh_token or wrong_expected_type or expired_token or tampered_signature"` passes; `grep -RIn "python-jose\|from jose" packages apps` returns nothing.

- [ ] **Step 3: Commit**

```bash
git add docs/adr/0006-password-hashing-bcrypt-direct.md docs/adr/0007-jwt-signing-hs256-pyjwt.md
git commit -m "docs(adr): 0006 bcrypt-direct hashing, 0007 HS256+PyJWT signing"
```

---

## Sub-step 2.2 — Alembic infra in `worker-database` + `worker-cli` repair + ADR-0010

**Sub-step goal:** Provide a reusable async-Alembic substrate (`worker-database` keeps `Base` as the single autogenerate target; nothing else changes there) and repair `worker-cli migrate/upgrade` to actually create/apply per-service revisions. ADR-0010 records the pro-service-async decision. The identity-service env.py + first migration are written in Sub-step 2.4; this sub-step only establishes the *generic* pieces.

### Task 4: ADR-0010 (Alembic per-service, async) — written early because it governs 2.4

**Status:** ✅ DONE (2026-07-18). `docs/adr/0010-alembic-per-service-async.md` written (Accepted): per-service `alembic.ini` + `apps/<service>/migrations/` with async `env.py` via `async_engine_from_config` + `connection.run_sync`; `worker_database.Base.metadata` as single autogenerate target; shared `alembic.ini`/multi-env rejected (ADR-0004); `worker-cli migrate/upgrade` pre-check referenced. Docs-only; `make check` green (72 passed, 2 skipped).

> Spec §2 writes ADR-0010 in 2.4, but the decision governs the env.py we build in 2.4. Writing it now (early) avoids divergence.

**Files:**
- Create: `docs/adr/0010-alembic-per-service-async.md`

- [ ] **Step 1: Write ADR-0010**

`docs/adr/0010-alembic-per-service-async.md`:
- **Context:** ULTRAPLAN + ADR-0004: no shared database, no cross-service repository. Each service owns exactly its tables. `worker-database` ships `Base` (`DeclarativeBase`) + Mixins + async UoW but no Alembic setup; `worker-cli migrate/upgrade` are thin `alembic` shells that fail without a service-local `alembic.ini` + `migrations/`. SQLAlchemy 2 async (`asyncpg`) needs the async `env.py` pattern (a synchronous Alembic `env.py` cannot use an async engine).
- **Decision:** **Per-service Alembic.** Each service owns `apps/<service>/alembic.ini` + `apps/<service>/migrations/` (`env.py`, `script.py.mako`, `versions/`). `env.py` runs **async** via `async_engine_from_url` + `connection.run_sync(do_migrations)` (SQLAlchemy 2 async pattern). `Base.metadata` from `worker-database` is the single shared `target_metadata` (the autogenerate import target); service models import `worker_database.Base` so their tables register on the same metadata. `worker migrate <msg> --service <s>` runs `alembic revision --autogenerate` in `apps/<s>`; `worker upgrade --service <s>` runs `alembic upgrade head`. A shared `alembic.ini`/multi-env is **rejected** (approaches the shared-DB anti-pattern of ADR-0004).
- **Consequences:** New services scaffold with an alembic.ini + migrations/ (the `worker-cli` `new-service` template should add these — noted as follow-up, not blocking Phase 2). Migration history lives with the service that owns the tables. Autogenerate compares service models against `Base.metadata` (which includes only that service's imported models).
- **Verification:** In Sub-step 2.4, `worker migrate "init" --service identity-service` (or the hand-written `0001`) applies; integration tests apply it against a Testcontainers pg (Sub-step 2.4 Task 16).

- [ ] **Step 2: Commit**

```bash
git add docs/adr/0010-alembic-per-service-async.md
git commit -m "docs(adr): 0010 Alembic per-service async env.py"
```

### Task 5: Repair `worker-cli migrate/upgrade` to verify alembic.ini + give actionable feedback

**Status:** ✅ DONE (2026-07-18). Hoisted `subprocess` import; added `_alembic_dir_for(service)`. `migrate`/`upgrade` now pre-check `apps/<service>/alembic.ini` and exit 1 with an ADR-0010-referencing message when missing (no bare alembic subprocess failure). `test_migrate_reports_missing_alembic_ini` (CliRunner, bogus `nonexistent-service` to stay independent of 2.4 scaffolding) passes. `make check` green (73 passed, 2 skipped). This is a pure-CLI test — no Docker/DB involved.

**Files:**
- Modify: `packages/worker-cli/src/worker_cli/__init__.py:291-325` (`migrate`, `upgrade`, the `_generate_infrastructure`-adjacent helpers are untouched)

**Interfaces:**
- Consumes: existing Typer `app`.
- Produces: `worker migrate --service <s>` exits non-zero with a clear message if `apps/<s>/alembic.ini` is missing (instead of the bare `alembic` subprocess stderr); same for `upgrade`. Behavior unchanged when the file exists.

- [ ] **Step 1: Write the failing test**

Add to `packages/worker-cli/tests/test_smoke_worker_cli.py` (this file exists from Phase 1.5; append):

```python
from typer.testing import CliRunner

from worker_cli import app


def test_migrate_reports_missing_alembic_ini(tmp_path, monkeypatch) -> None:
    # Point the CLI at a fake apps root with no alembic.ini
    # The CLI uses cwd=f"apps/{service}"; we monkeypatch subprocess.run to avoid a real shell,
    # and pre-check by the CLI itself must fire first.
    runner = CliRunner()
    # We cannot easily redirect apps/; instead assert the CLI *checks* the file.
    # Run with a service that has no alembic.ini in the real repo (apps/identity-service lacks one
    # until Sub-step 2.4). If it already exists by the time you run this, use a bogus service name.
    result = runner.invoke(app, ["migrate", "msg", "--service", "nonexistent-service"])
    assert result.exit_code != 0
    assert "alembic.ini" in result.stdout or "alembic.ini" in (result.stderr or "")
```

> The `nonexistent-service` trick keeps the test independent of whether `identity-service` already has an alembic.ini at the time the test runs.

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest packages/worker-cli/tests/test_smoke_worker_cli.py::test_migrate_reports_missing_alembic_ini -v`
Expected: FAIL — the current `migrate` does not pre-check (it shells out `alembic` whose cwd `apps/nonexistent-service` does not exist, the subprocess fails, the CLI prints "Migration creation failed" but `alembic.ini` is not the asserted substring).

- [ ] **Step 3: Write minimal implementation**

Modify the `migrate` and `upgrade` functions at `packages/worker-cli/src/worker_cli/__init__.py:291-325` to add a pre-check (the `_generate_cqrs`-style path checking is the local idiom; inline a small helper):

```python
import subprocess
from pathlib import Path


def _alembic_dir_for(service: str) -> Path:
    return Path(f"apps/{service}")


@app.command()
def migrate(
    message: str = typer.Argument(..., help="Migration message"),
    service: str = typer.Option(..., help="Target service"),
) -> None:
    """Create Alembic migration"""
    service_dir = _alembic_dir_for(service)
    if not (service_dir / "alembic.ini").is_file():
        console.print(
            f"[red]No alembic.ini at {service_dir}/alembic.ini[/red] "
            f"(per-service Alembic, see ADR-0010): run `worker new-service`"
        )
        raise typer.Exit(code=1)
    result = subprocess.run(
        ["uv", "run", "alembic", "revision", "--autogenerate", "-m", message],
        cwd=str(service_dir),
    )
    if result.returncode == 0:
        console.print("[green]Migration created successfully![/green]")
    else:
        console.print("[red]Migration creation failed[/red]")


@app.command()
def upgrade(
    service: str = typer.Option(..., help="Target service"),
    revision: str = typer.Option("head", help="Target revision"),
) -> None:
    """Run database migrations"""
    service_dir = _alembic_dir_for(service)
    if not (service_dir / "alembic.ini").is_file():
        console.print(
            f"[red]No alembic.ini at {service_dir}/alembic.ini[/red] "
            f"(per-service Alembic, see ADR-0010)."
        )
        raise typer.Exit(code=1)
    result = subprocess.run(
        ["uv", "run", "alembic", "upgrade", revision],
        cwd=str(service_dir),
    )
    if result.returncode == 0:
        console.print("[green]Migrations applied successfully![/green]")
    else:
        console.print("[red]Migration failed[/red]")
```

> The `import subprocess` / `from pathlib import Path` go to the top of the file (they were inline `import subprocess` per-function before — hoist once).

- [ ] **Step 4: Run test to verify it passes**

Run: `uv run pytest packages/worker-cli/tests/test_smoke_worker_cli.py::test_migrate_reports_missing_alembic_ini -v`
Expected: PASS.

- [ ] **Step 5: Run `make check` and commit**

Run: `make check` → expected green. Then:

```bash
git add packages/worker-cli/src/worker_cli/__init__.py packages/worker-cli/tests/test_smoke_worker_cli.py
git commit -m "worker-cli: verify per-service alembic.ini before migrate/upgrade (ADR-0010)"
```

---

## Sub-step 2.3 — identity-service Domain (ValueObjects, User, Audit) + ADR-0008 + ADR-0012

**Sub-step goal:** Pure-domain layer with no transport/ORM/JWT dependency: `Email`/`PasswordHash`/`UserId`/`TenantId` value objects, the `User` aggregate with `AccountStatus` lifecycle, `AuditEvent`/`AuditAction` with a PII allowlist, and the `PasswordHashing`/`TokenService`/`Clock` service ports. ADR-0008 records the password-flow-not-OIDC choice; ADR-0012 records the audit model (its *full* EventBus wiring lands in 2.7). All domain tests are unit (no Docker).

### Task 6: Add identity-service deps + settings skeleton

**Status:** ✅ DONE (2026-07-18). `apps/identity-service/pyproject.toml` deps extended (fastapi, sqlalchemy[asyncio], asyncpg, alembic, psycopg[binary], pydantic, pydantic-settings, worker-auth/database/events/tenancy/config) — resolved via root `[tool.uv.sources]` workspace table, no service-local sources block needed. `configuration.IdentityServiceSettings` adds `jwt_secret` SecretStr, `database_url`, `jwt_access/refresh_token_expire_minutes`, `bcrypt_rounds`. `uv sync` installed psycopg + greenlet. Smoke import prints `identity-service 8001 dev-...`. `make check` green (73 passed, 2 skipped). bcrypt/pyjwt come transitively via worker-auth (single canonical home, ADR-0002).

**Files:**
- Modify: `apps/identity-service/pyproject.toml` (`dependencies` + `[tool.uv.sources]`)
- Modify: `apps/identity-service/src/identity_service/configuration.py`

**Interfaces:**
- Produces: `IdentityServiceSettings` with fields `jwt_secret: SecretStr`, `database_url: str`, `jwt_access_token_expire_minutes: int = 15`, `jwt_refresh_token_expire_minutes: int = 1440`, `bcrypt_rounds: int = 12`. Keeps the inherited `PlatformSettings` knobs (`service_name`, `port`, `environment`, `allow_development_tenant_header`, …).

- [ ] **Step 1: Add deps**

Modify `apps/identity-service/pyproject.toml` `dependencies` to:

```toml
dependencies = [
  "uvicorn[standard]>=0.40.0,<1.0.0",
  "fastapi>=0.115.0,<1.0.0",
  "sqlalchemy[asyncio]>=2.0.0,<3.0.0",
  "asyncpg>=0.30.0,<1.0.0",
  "alembic>=1.13.0,<2.0.0",
  "psycopg[binary]>=3.1.0,<4.0.0",
  "pydantic>=2.8.0,<3.0.0",
  "pydantic-settings>=2.4.0,<3.0.0",
  "worker-core",
  "worker-platform",
  "worker-shared",
  "worker-auth",
  "worker-database",
  "worker-events",
  "worker-tenancy",
  "worker-config",
]
```

And add workspace sources to `[tool.uv.sources]` if not present:

```toml
[tool.uv.sources]
worker-core = { workspace = true }
worker-platform = { workspace = true }
worker-shared = { workspace = true }
worker-auth = { workspace = true }
worker-database = { workspace = true }
worker-events = { workspace = true }
worker-tenancy = { workspace = true }
worker-config = { workspace = true }
```

> `bcrypt` and `pyjwt` come transitively via `worker-auth` (do not re-declare here — single canonical home, ADR-0002). `psycopg[binary]` is for Alembic's sync DDL runner used by the Testcontainers `upgrade head` helper (Sub-step 2.4); the runtime uses asyncpg.

- [ ] **Step 2: Sync**

Run: `uv sync --all-packages --all-groups`
Expected: lockfile resolves the new deps.

- [ ] **Step 3: Extend settings**

Replace `apps/identity-service/src/identity_service/configuration.py`:

```python
"""Identity-service-specific configuration."""

from __future__ import annotations

from pydantic import SecretStr

from worker_platform.configuration import PlatformSettings


class IdentityServiceSettings(PlatformSettings):
    service_name: str = "identity-service"
    port: int = 8001

    # Phase 2 security knobs (runtime-only; never committed defaults in prod).
    jwt_secret: SecretStr = SecretStr("dev-only-secret-change-me-in-production-32bytes")
    database_url: str = "postgresql+asyncpg://worker:worker@127.0.0.1:5432/identity"
    jwt_access_token_expire_minutes: int = 15
    jwt_refresh_token_expire_minutes: int = 1440
    bcrypt_rounds: int = 12
```

- [ ] **Step 4: Run a smoke import**

Run: `uv run python -c "from identity_service.configuration import IdentityServiceSettings; s=IdentityServiceSettings(); print(s.service_name, s.port, s.jwt_secret.get_secret_value()[:4]+'...')"`
Expected: prints `identity-service 8001 dev-...` without import error.

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/pyproject.toml apps/identity-service/src/identity_service/configuration.py uv.lock
git commit -m "identity-service: settings skeleton (jwt_secret SecretStr, database_url)"
```

### Task 7: Value objects (Email, PasswordHash, UserId, TenantId)

**Status:** ✅ DONE (2026-07-18). `domain/value_objects.py` implements `Email` (lowercased + pragmatic-regex validation, rejects garbage/empty/>254-char segments incl. the 1000-char extreme case, raises `InvalidEmail` `DomainError`, equality by normalized value), `PasswordHash` (opaque, accepts an already-hashed string only), `UserId(UUID)`, and `TenantId(UUID)` (non-nil, `ValueError`). Built on the `worker_core.ValueObject` frozen+slots marker: validation runs through a manual `__init__` that writes fields via `object.__setattr__` because the dataclass is frozen — the plan-flagged pattern compiles cleanly under mypy-strict. 6 unit tests pass (`uv run pytest apps/identity-service/tests/unit/test_value_objects.py -v` → 6 passed). `make check` green (79 passed, 2 skipped, +6 over Task 6's 73). No `tests/unit/__init__.py` — intentionally omitted per the Phase-1 collection convention (unique test filenames, no `tests/__init__.py`) to avoid turning `tests/` into a package and disturbing pytest's rootdir import under `testpaths = ["apps", "packages"]`. Committed as `baed2b3`.

**Files:**
- Create: `apps/identity-service/src/identity_service/domain/__init__.py`
- Create: `apps/identity-service/src/identity_service/domain/value_objects.py`
- Test: `apps/identity-service/tests/unit/test_value_objects.py` (no `__init__.py` — see Status note)

**Interfaces:**
- Produces: `Email(raw: str)` (frozen `ValueObject`; lowercased; validates with a pragmatic regex; equality by lowercased value; raises `InvalidEmail`), `PasswordHash(value: str)` (frozen; opaque; `PasswordHash(value)` accepts an already-hashed string only), `UserId(value: UUID)`, `TenantId(value: UUID)` (frozen; non-nil).
- Consumes: `worker_core.ValueObject`, `worker_core.DomainError`.

- [ ] **Step 1: Write the failing tests**

`apps/identity-service/tests/unit/__init__.py` — empty file.

`apps/identity-service/tests/unit/test_value_objects.py`:

```python
from uuid import UUID, uuid4

import pytest

from identity_service.domain.value_objects import (
    Email,
    InvalidEmail,
    PasswordHash,
    TenantId,
    UserId,
)


def test_email_lowercases_and_validates() -> None:
    assert Email("Alice@Example.COM").value == "alice@example.com"
    assert Email("Alice@Example.COM") == Email("alice@example.com")


def test_email_rejects_garbage() -> None:
    with pytest.raises(InvalidEmail):
        Email("not-an-email")
    with pytest.raises(InvalidEmail):
        Email("")


def test_email_rejects_local_part_longer_than_limit() -> None:
    # guard against unreasonable inputs blowing up indexes
    with pytest.raises(InvalidEmail):
        Email("a" * 1000 + "@example.com")


def test_password_hash_is_opaque() -> None:
    h = PasswordHash("$2b$12$abc")
    assert h.value == "$2b$12$abc"


def test_user_id_and_tenant_id_wrap_uuid() -> None:
    u = uuid4()
    assert UserId(u).value == u
    assert TenantId(u).value == u


def test_tenant_id_rejects_nil() -> None:
    with pytest.raises(ValueError):
        TenantId(UUID("00000000-0000-0000-0000-000000000000"))
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest apps/identity-service/tests/unit/test_value_objects.py -v`
Expected: FAIL with `ModuleNotFoundError: No module named 'identity_service.domain'`.

- [ ] **Step 3: Write implementation**

`apps/identity-service/src/identity_service/domain/__init__.py` — empty file (will hold re-exports later; keep empty now).

`apps/identity-service/src/identity_service/domain/value_objects.py`:

```python
"""Identity-domain value objects."""

from __future__ import annotations

import re
from uuid import UUID

from worker_core import DomainError, ValueObject

__all__ = [
    "Email",
    "InvalidEmail",
    "PasswordHash",
    "TenantId",
    "UserId",
]

_EMAIL_RE = re.compile(r"^[^@\s]{1,254}@[^@\s]{1,254}\.[^@\s]{2,254}$")


class InvalidEmail(DomainError):
    def __init__(self, raw: str) -> None:
        super().__init__("invalid_email", f"Not a valid email: {raw!r}")


class Email(ValueObject):
    value: str

    def __init__(self, raw: str) -> None:
        if not isinstance(raw, str) or not _EMAIL_RE.match(raw):
            raise InvalidEmail(raw)
        object.__setattr__(self, "value", raw.lower())

    def __eq__(self, other: object) -> bool:
        return isinstance(other, Email) and self.value == other.value

    def __hash__(self) -> int:
        return hash(self.value)


class PasswordHash(ValueObject):
    value: str

    def __init__(self, value: str) -> None:
        object.__setattr__(self, "value", value)


class UserId(ValueObject):
    value: UUID

    def __init__(self, value: UUID) -> None:
        object.__setattr__(self, "value", value)


_NIL_UUID = UUID("00000000-0000-0000-0000-000000000000")


class TenantId(ValueObject):
    value: UUID

    def __init__(self, value: UUID) -> None:
        if value == _NIL_UUID:
            raise ValueError("TenantId must not be the nil UUID")
        object.__setattr__(self, "value", value)
```

> ValueObject is a frozen dataclass marker with `slots=True`; we set fields via `object.__setattr__` because the dataclass is frozen. We define `__init__` manually rather than using dataclass fields so we can validate. (`__slots__` + frozen dataclass + custom `__init__` works because we never declared dataclass fields.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest apps/identity-service/tests/unit/test_value_objects.py -v`
Expected: PASS (6 passed).

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/src/identity_service/domain/__init__.py apps/identity-service/src/identity_service/domain/value_objects.py apps/identity-service/tests/unit/__init__.py apps/identity-service/tests/unit/test_value_objects.py
git commit -m "identity-service/domain: Email/PasswordHash/UserId/TenantId value objects"
```

### Task 8: Audit model (AuditEvent, AuditAction, PII allowlist)

**Files:**
- Create: `apps/identity-service/src/identity_service/domain/audit.py`
- Test: `apps/identity-service/tests/unit/test_audit.py`

**Status:** ✅ DONE (2026-07-18). `domain/audit.py` implements `AuditAction` (StrEnum: register/login_success/login_failure/token_refresh/token_revoke), `AuditEvent` (frozen slots dataclass subclassing `worker_core.DomainEvent`), `AuditMetadataError` (`DomainError`), and `AUDIT_METADATA_ALLOWLIST` (frozenset `{"reason","ip","user_agent"}`). `__post_init__` raises `AuditMetadataError` for any metadata key off-list → PII-free by construction (the `{"email": ...}` sneak-in test case is rejected at construction). `actor_id`/`tenant_id` nullable for unknown-actor failed logins. 4 unit tests pass (`uv run pytest apps/identity-service/tests/unit/test_audit.py -v` → 4 passed). `make check` green (83 passed, 2 skipped, +4 over Task 7's 79). Committed as `9b15a31`.

**Deviation from the plan snippet (recorded):** the plan's `AuditEvent` redeclared the base defaults (`event_id`/`occurred_at`) *after* its own non-default fields. That trips `TypeError: non-default argument 'actor_id' follows default argument 'occurred_at'` at dataclass construction, because `DomainEvent`'s inherited `event_id`/`occurred_at` already carry defaults in the MRO-merged field order. Fix: the own required fields are declared with `field(kw_only=True)` — keyword-only fields are excluded from the positional init ordering rule, so the inherited defaults legally precede them. Caller-visible call sites all pass kwargs, so the `kw_only` move is invisible. `uuid4`/`datetime`/`UTC` imports dropped (the `DomainEvent` base supplies those defaults).

**Interfaces:**
- Produces: `AuditAction` (StrEnum: `REGISTER`, `LOGIN_SUCCESS`, `LOGIN_FAILURE`, `TOKEN_REFRESH`, `TOKEN_REVOKE`), `AuditEvent` (frozen dataclass: `event_id`, `occurred_at`, `actor_id: UUID | None`, `tenant_id: UUID | None`, `action: AuditAction`, `target_id: UUID | None`, `correlation_id: str | None`, `metadata: dict[str, str]`), `AuditMetadataError` (DomainError), `AUDIT_METADATA_ALLOWLIST` frozenset of allowed keys.
- Rule: constructing `AuditEvent` with a metadata key not in the allowlist raises `AuditMetadataError`.
- `actor_id`/`tenant_id` are nullable (unknown actor at failed login).

- [ ] **Step 1: Write failing tests**

`apps/identity-service/tests/unit/test_audit.py`:

```python
from datetime import UTC, datetime
from uuid import UUID, uuid4

import pytest

from identity_service.domain.audit import (
    AUDIT_METADATA_ALLOWLIST,
    AuditEvent,
    AuditAction,
    AuditMetadataError,
)


def test_audit_action_values() -> None:
    assert AuditAction.REGISTER == "register"
    assert AuditAction.LOGIN_SUCCESS == "login_success"
    assert AuditAction.LOGIN_FAILURE == "login_failure"
    assert AuditAction.TOKEN_REFRESH == "token_refresh"
    assert AuditAction.TOKEN_REVOKE == "token_revoke"


def test_audit_event_allows_only_allowlist_metadata_keys() -> None:
    ev = AuditEvent(
        occurred_at=datetime.now(UTC),
        actor_id=uuid4(),
        tenant_id=uuid4(),
        action=AuditAction.LOGIN_SUCCESS,
        target_id=None,
        correlation_id="corr",
        metadata={"reason": "ok", "ip": "127.0.0.1"},
    )
    assert ev.metadata == {"reason": "ok", "ip": "127.0.0.1"}


def test_audit_event_rejects_unknown_metadata_key() -> None:
    with pytest.raises(AuditMetadataError):
        AuditEvent(
            occurred_at=datetime.now(UTC),
            actor_id=None,
            tenant_id=None,
            action=AuditAction.LOGIN_FAILURE,
            target_id=None,
            correlation_id=None,
            metadata={"email": "pii@example.com"},  # PII sneak-in attempt, rejected
        )


def test_audit_event_actor_nullable_for_unknown_login() -> None:
    ev = AuditEvent(
        occurred_at=datetime.now(UTC),
        actor_id=None,
        tenant_id=None,
        action=AuditAction.LOGIN_FAILURE,
        target_id=None,
        correlation_id=None,
        metadata={"reason": "unknown_user"},
    )
    assert ev.actor_id is None
    assert "user_agent" in AUDIT_METADATA_ALLOWLIST
```

- [ ] **Step 2: Run to verify fail**

Run: `uv run pytest apps/identity-service/tests/unit/test_audit.py -v`
Expected: FAIL `ModuleNotFoundError`.

- [ ] **Step 3: Write implementation**

`apps/identity-service/src/identity_service/domain/audit.py`:

```python
"""Audit-event domain model — PII-free by construction."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import UTC, datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError, DomainEvent

__all__ = [
    "AUDIT_METADATA_ALLOWLIST",
    "AuditAction",
    "AuditEvent",
    "AuditMetadataError",
]


class AuditAction(StrEnum):
    REGISTER = "register"
    LOGIN_SUCCESS = "login_success"
    LOGIN_FAILURE = "login_failure"
    TOKEN_REFRESH = "token_refresh"
    TOKEN_REVOKE = "token_revoke"


# Only non-PII technical metadata may be recorded. Passwords, emails,
# consent payloads, and tokens are forbidden by construction.
AUDIT_METADATA_ALLOWLIST: frozenset[str] = frozenset({"reason", "ip", "user_agent"})


class AuditMetadataError(DomainError):
    def __init__(self, key: str) -> None:
        super().__init__(
            "audit_metadata_not_allowlisted",
            f"Metadata key {key!r} is not in the audit PII allowlist",
        )


@dataclass(frozen=True, slots=True)
class AuditEvent(DomainEvent):
    actor_id: UUID | None
    tenant_id: UUID | None
    action: AuditAction
    target_id: UUID | None
    correlation_id: str | None
    metadata: dict[str, str]
    event_id: UUID = field(default_factory=uuid4)
    occurred_at: datetime = field(default_factory=lambda: datetime.now(UTC))

    def __post_init__(self) -> None:
        for key in self.metadata:
            if key not in AUDIT_METADATA_ALLOWLIST:
                raise AuditMetadataError(key)
```

- [ ] **Step 4: Run to verify pass**

Run: `uv run pytest apps/identity-service/tests/unit/test_audit.py -v`
Expected: PASS (4 passed).

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/src/identity_service/domain/audit.py apps/identity-service/tests/unit/test_audit.py
git commit -m "identity-service/domain: AuditEvent + PII allowlist (ADR-0012)"
```

### Task 9: User aggregate + AccountStatus lifecycle + events + service ports

**Status:** ✅ DONE (2026-07-19). `domain/services.py` defines the `PasswordHashing` / `Clock` / `TokenService` Protocols (minimal surface, no PyJWT/JWT import — the infrastructure adapter in 2.4 implements them). `domain/user.py` defines `AccountStatus` (StrEnum: pending/active/suspended/disabled), the `UserAlreadyExists`/`InvalidCredentials`/`AccountDisabled` `DomainError`s, the `UserRegistered`/`UserLoggedIn` domain events (frozen slots `DomainEvent` subclasses with a `to_dict()` that stringifies UUID/timestamps), and the `User` aggregate. 4 unit tests pass (`uv run pytest apps/identity-service/tests/unit/test_user.py -v` → 4 passed). `make check` green (87 passed, 2 skipped, +4 over Task 8's 83). Committed as `ada10f8`.

**Two deliberate deviations from the plan snippet (both recorded):**
1. **`User` is a plain class, not a `worker_core.Entity` subclass.** Subclassing `Entity` is mypy-strict-blocked at three points (probe-verified): (a) `Entity.id` carries a default (`uuid4`), so `User`'s non-default fields violate "non-default follows default" init ordering; (b) overriding `Entity.id: UUID` with `id: UserId` is an incompatible assignment; (c) `Entity.__eq__` compares `self.id == other.id` which would be `UserId == UUID` (type-mismatched) rather than value-equal UUIDs. The plan's own fallback (line 1531) named this path: `User` declares its own `id: UserId` field and hand-writes `__eq__`/`__hash__` against `self.id` (now `UserId == UserId` value-equality via the frozen dataclass-generated `__eq__`). Identity equality and hashing are preserved.
2. **`UserRegistered`/`UserLoggedIn` own fields are `field(kw_only=True)`.** Same `DomainEvent`-default-ordering constraint that hit `AuditEvent` (Task 8): the base contributes `event_id`/`occurred_at` defaults, and own non-default fields would otherwise follow a default. Keyword-only fields are excluded from positional init ordering, so the inherited defaults legally precede them. Callers (the tests, and the 2.5 command handlers) pass everything by keyword, so the move is invisible.

**Test correction (persistence-boundary rule):** the plan's `test_record_login_emits_event_and_does_not_store_password` asserted `len(events) == 1` after `User.register()` + `record_login()` with a single trailing `pull_events()`. That falsely modeled register+login as one uncommitted transaction; in the real command flow they are two separate handlers (`RegisterUserHandler` / `LoginHandler`), each pulling its own events at its persist point, and `handle_login` reloads a fresh event-empty User from the repo before `record_login()`. Plan-we search confirmed this rule is violated **only here** — all later command-handler sites (`handle_register` 2781, `handle_login` 2836, repo test 2608, 2.7 tests 3819) already model the boundary correctly. Fix applied to the **test only**: `pull_events()` after `register()` (assert 1 × `UserRegistered`), then after `record_login()` (assert 1 × `UserLoggedIn`). Domain code untouched.

**Files:**
- Create: `apps/identity-service/src/identity_service/domain/services.py` (ports)
- Create: `apps/identity-service/src/identity_service/domain/user.py` (aggregate)
- Test: `apps/identity-service/tests/unit/test_user.py`

**Interfaces:**
- Produces `PasswordHashing` (Protocol): `hash(plain: str) -> PasswordHash`, `verify(plain: str, hashed: PasswordHash) -> bool`.
- Produces `TokenService` (Protocol): `issue_access(user) -> str`, `issue_refresh(user, jti) -> str`, `verify_access(token) -> tuple[AuthPrincipal, ...]` — **only the method *signatures* used by commands are pinned here**; the full impl in 2.4 may extend. Keep this a minimal Protocol so the domain doesn't depend on PyJWT.
- Produces `Clock` (Protocol): `now() -> datetime`.
- Produces `AccountStatus` (StrEnum: `PENDING`, `ACTIVE`, `SUSPENDED`, `DISABLED`).
- Produces `User` (aggregate, `worker_core.Entity` subclass-ish): factory `User.register(email, password_hash, display_name, tenant_id, clock) -> User` (creates `ACTIVE` synchronously, raises `UserRegistered` event); instance `verify_password(plain, hasher) -> bool`; `AccountStatus`-gated login helper `assert_can_log_in() -> None` (raises `AccountDisabled` when not `ACTIVE`).
- **Persistence-boundary rule (Task 9 test correction):** `User.register()` and `User.record_login()` belong to **separate command handlers** (`RegisterUserHandler` / `LoginHandler`), each with its own `pull_events()` at its persistence point. The aggregate accumulates events in its in-memory backlog, but a real command flow never accumulates `UserRegistered` + `UserLoggedIn` in the same backlog across handlers — `handle_login` reloads a fresh, event-empty User from the repository before calling `record_login()`. **Event counts in tests are therefore asserted per command-handler boundary, never cumulatively across use-cases.** Domain code (`User`, `pull_events`, backlog mechanics) is not altered to satisfy a single test; the test mirrors the boundary.
- Produces events `UserRegistered`, `UserLoggedIn`.
- DomainErrors: `AccountDisabled`, `InvalidCredentials`, `UserAlreadyExists`.
- `AuthPrincipal` lives in the application/port layer (Task 11) — domain `User` exposes `user.id`, `user.tenant_id`, `user.roles` so the principal can be built by callers.

- [ ] **Step 1: Write failing tests**

`apps/identity-service/tests/unit/test_user.py`:

```python
from datetime import UTC, datetime
from uuid import UUID, uuid4

import pytest

from identity_service.domain.audit import AuditEvent, AuditAction
from identity_service.domain.user import (
    AccountDisabled,
    AccountStatus,
    InvalidCredentials,
    User,
    UserAlreadyExists,
    UserLoggedIn,
    UserRegistered,
)
from identity_service.domain.value_objects import Email, PasswordHash, TenantId, UserId


class _FakeHasher:
    def __init__(self) -> None:
        self.store: dict[str, str] = {}

    def hash(self, plain: str) -> PasswordHash:
        h = "fake$" + plain[::-1]
        self.store[h] = plain
        return PasswordHash(h)

    def verify(self, plain: str, hashed: PasswordHash) -> bool:
        return self.store.get(hashed.value) == plain


def _now() -> datetime:
    return datetime(2026, 7, 16, 12, 0, 0, tzinfo=UTC)


def test_register_creates_active_user_with_event() -> None:
    user = User.register(
        email=Email("alice@example.com"),
        password_hash=PasswordHash("$2b$12$x"),
        display_name="Alice",
        tenant_id=TenantId(uuid4()),
        now=_now(),
    )
    assert user.status is AccountStatus.ACTIVE
    assert user.email == Email("alice@example.com")
    assert isinstance(user.id, UserId)
    events = user.pull_events()
    assert len(events) == 1
    assert isinstance(events[0], UserRegistered)
    pe = user.pull_events()
    assert pe == []  # events pulled are cleared


def test_verify_password_delegates_to_hasher() -> None:
    hasher = _FakeHasher()
    h = hasher.hash("s3cret")
    user = User.register(
        email=Email("b@example.com"),
        password_hash=h,
        display_name="B",
        tenant_id=TenantId(uuid4()),
        now=_now(),
    )
    assert user.verify_password("s3cret", hasher) is True
    assert user.verify_password("wrong", hasher) is False


def test_assert_can_log_in_requires_active() -> None:
    user = User.register(
        email=Email("c@example.com"),
        password_hash=PasswordHash("$2b$12$y"),
        display_name="C",
        tenant_id=TenantId(uuid4()),
        now=_now(),
    )
    user.status = AccountStatus.SUSPENDED
    with pytest.raises(AccountDisabled):
        user.assert_can_log_in()
    user.status = AccountStatus.ACTIVE
    user.assert_can_log_in()  # no raise


def test_record_login_emits_event_and_does_not_store_password() -> None:
    user = User.register(
        email=Email("d@example.com"),
        password_hash=PasswordHash("$2b$12$z"),
        display_name="D",
        tenant_id=TenantId(uuid4()),
        now=_now(),
    )
    user.record_login(jti="jti-1", now=_now())
    events = user.pull_events()
    assert len(events) == 1
    assert isinstance(events[0], UserLoggedIn)
    assert events[0].jti == "jti-1"
    # the event payload never carries the password or plaintext:
    assert "password" not in events[0].to_dict()
```

- [ ] **Step 2: Run to verify fail**

Run: `uv run pytest apps/identity-service/tests/unit/test_user.py -v`
Expected: FAIL `ModuleNotFoundError`.

- [ ] **Step 3: Write implementation**

`apps/identity-service/src/identity_service/domain/services.py`:

```python
"""Domain service ports (interfaces) — no transport/ORM/JWT imports."""

from __future__ import annotations

from datetime import datetime
from typing import Protocol

from identity_service.domain.value_objects import PasswordHash

__all__ = ["Clock", "PasswordHashing", "TokenService"]


class PasswordHashing(Protocol):
    def hash(self, plain: str) -> PasswordHash: ...
    def verify(self, plain: str, hashed: PasswordHash) -> bool: ...


class Clock(Protocol):
    def now(self) -> datetime: ...


class TokenService(Protocol):
    # Minimal surface the application layer needs from a token service.
    # The infrastructure adapter (JwTokenService, Sub-step 2.4) implements this.
    def issue_access_token(
        self, user_id: object, tenant_id: object, roles: list[str], permissions: list[str]
    ) -> str: ...

    def issue_refresh_token(self, user_id: object, tenant_id: object, *, session_jti: str) -> str: ...
```

`apps/identity-service/src/identity_service/domain/user.py`:

```python
"""User aggregate and account lifecycle."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError, DomainEvent, Entity

from identity_service.domain.services import PasswordHashing
from identity_service.domain.value_objects import Email, PasswordHash, TenantId, UserId

__all__ = [
    "AccountDisabled",
    "AccountStatus",
    "InvalidCredentials",
    "User",
    "UserAlreadyExists",
    "UserLoggedIn",
    "UserRegistered",
]


class AccountStatus(StrEnum):
    PENDING = "pending"
    ACTIVE = "active"
    SUSPENDED = "suspended"
    DISABLED = "disabled"


class UserAlreadyExists(DomainError):
    def __init__(self, email: str) -> None:
        super().__init__("user_already_exists", f"A user with email {email!r} already exists in this tenant")


class InvalidCredentials(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_credentials", "Invalid credentials")


class AccountDisabled(DomainError):
    def __init__(self) -> None:
        super().__init__("account_disabled", "Account is not active")


@dataclass(frozen=True, slots=True)
class UserRegistered(DomainEvent):
    user_id: UUID
    tenant_id: UUID
    email: str  # PII: stays in the domain event, never crosses into AuditEvent


@dataclass(frozen=True, slots=True)
class UserLoggedIn(DomainEvent):
    user_id: UUID
    tenant_id: UUID
    jti: str


@dataclass(eq=False, slots=True)
class User(Entity):
    id: UserId
    tenant_id: TenantId
    email: Email
    password_hash: PasswordHash
    display_name: str
    roles: tuple[str, ...]
    status: AccountStatus
    _events: list[DomainEvent] = field(default_factory=list)

    @classmethod
    def register(
        cls,
        *,
        email: Email,
        password_hash: PasswordHash,
        display_name: str,
        tenant_id: TenantId,
        now: datetime,
        roles: tuple[str, ...] = ("user",),
    ) -> "User":
        user = cls(
            id=UserId(uuid4()),
            tenant_id=tenant_id,
            email=email,
            password_hash=password_hash,
            display_name=display_name,
            roles=roles,
            status=AccountStatus.ACTIVE,  # Phase 2: synchronous activation, no email verification
            _events=[],
        )
        user._events.append(
            UserRegistered(
                user_id=user.id.value,
                tenant_id=user.tenant_id.value,
                email=user.email.value,
                occurred_at=now,
            )
        )
        return user

    def verify_password(self, plain: str, hasher: PasswordHashing) -> bool:
        return hasher.verify(plain, self.password_hash)

    def assert_can_log_in(self) -> None:
        if self.status is not AccountStatus.ACTIVE:
            raise AccountDisabled()

    def record_login(self, *, jti: str, now: datetime) -> None:
        self._events.append(
            UserLoggedIn(
                user_id=self.id.value,
                tenant_id=self.tenant_id.value,
                jti=jti,
                occurred_at=now,
            )
        )

    def pull_events(self) -> list[DomainEvent]:
        events = list(self._events)
        self._events.clear()
        return events
```

> The `User` aggregate is a mutable dataclass (`eq=False` so it inherits `Entity` identity equality, `slots=True`). The `User` field *types* use our value objects; the `Entity` base provides `id: UUID` default — but we override `id` with our `UserId` wrapper to keep the aggregate's own id type; subclass field override in `@dataclass(eq=False)` is allowed because the base default is `UUID` and we redeclare `id: UserId`. If mypy complains about the field-type override, declare `User` as a plain class (not `Entity` dataclass subclass) and implement `__eq__`/`__hash__` via `self.id.value`. Verify with `make check`; default to whichever mypy accepts.

- [ ] **Step 4: Run to verify pass**

Run: `uv run pytest apps/identity-service/tests/unit/test_user.py -v`
Expected: PASS (4 passed).

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/src/identity_service/domain/services.py apps/identity-service/src/identity_service/domain/user.py apps/identity-service/tests/unit/test_user.py
git commit -m "identity-service/domain: User aggregate + AccountStatus lifecycle + ports"
```

### Task 10: ADR-0008 (password-flow, not OIDC)

**Status:** ✅ DONE (2026-07-19). `docs/adr/0008-auth-flow-password-not-oidc.md` written in the established ADR format (Status / Date / Relates / Context / Decision / Consequences / Upgrade path / Verification). Relates to ADR-0002, 0006, 0007, the Phase-2 design spec, and `product-scope.md` (both link targets verified to exist). Decision: self-hosted password flow — identity-service owns the `User` aggregate (Task 9) and issues HS256 access+refresh JWTs (ADR-0007) via `POST /auth/register` + `POST /auth/login`; refresh rotates a server-side `sessions` jti; no external IdP, no OIDC-provider endpoints in Phase 2. Both upgrade seams (OIDC-provider via `authlib` on the `TokenService` port; external IdP as an alternative federated-login path) documented as Phase-6/10 and reversible without re-doing Phase 2. The ULTRAPLAN "OIDC/OAuth2-Einstieg" checkbox is intentionally deferred, not silently dropped. Doc-only task — no `make check` impact; link targets verified. Committed as `a4dd5cf`.

**Files:**
- Create: `docs/adr/0008-auth-flow-password-not-oidc.md`

- [ ] **Step 1: Write ADR-0008**

- **Context:** ULTRAPLAN §Phase 2 names an "OIDC/OAuth2-Einstieg". Phase 3 builds the Consent-Ledger against a candidate-owned profile → the identity must live in this repo (an external IdP would mean no user aggregate here, undermining the domain-first goal and the consent binding). OIDC-as-*provider* (authlib Authorization-Code + `/callback`) is ~3× the slice scope (Authorization endpoint, grant-type mapping, state handling, frontend redirect) without an internal consumer needing it yet.
- **Decision:** Phase 2 implements a **self-hosted password flow** — identity-service owns the `User` aggregate, `POST /auth/register` + `POST /auth/login` issue HS256 access + refresh JWTs (ADR-0007). Refresh uses a server-side `sessions` jti ledger (rotate on refresh). No external IdP, no OIDC provider endpoints in Phase 2.
- **Upgrade path documented:** If/when multiple agents/services need a real Authorization-Code flow, `authlib` (already a transitive dep via worker-auth) supplies the OIDC-provider endpoints; the existing `TokenService` port + `User` aggregate are the seam (the JWT issuance changes, the domain does not). An external IdP can also be added later as an alternative `PasswordHashing`/federated-login path; both are Phase-6/10.
- **Consequences:** Consent in Phase 3 binds to `User.tenant_id` + `User.id` — both already claim-authenticated after Phase 2. The OIDC checkbox from ULTRAPLAN is intentionally deferred, not silently dropped.
- **Verification:** `POST /auth/login` returns a JWT; `GET /me` echoes `tenant_id` from the claim (Sub-step 2.5/2.6).

- [ ] **Step 2: Commit**

```bash
git add docs/adr/0008-auth-flow-password-not-oidc.md
git commit -m "docs(adr): 0008 password-flow now, OIDC-provider as upgrade path"
```

---

## Sub-step 2.4 — Persistence: models, repositories, adapters, first migration, Testcontainers

**Sub-step goal:** Infrastructure layer that implements the domain ports against Postgres, a Composition-Root wiring it, the first Alembic revision, and a Testcontainers integration fixture (ADR-0011). After this sub-step the service still has no HTTP endpoints (those are 2.5), but the persistence substrate is real and integration-tested.

### Task 11: Add Testcontainers + httpx to root dev group

**Status:** ✅ DONE (2026-07-19). Root `[dependency-groups].dev` extended with `httpx>=0.27,<1` (0.28.1) and `testcontainers[postgres]>=4,<5` (4.14.2, pulls `docker==7.2.0`). `httpx2` retained (out of scope to remove per plan note). `psycopg[binary]` is already an identity-service runtime dep (Task 6). `uv sync --all-packages --all-groups` resolved 318 packages, installed 2; `uv.lock` updated. Smoke import verified: `from testcontainers.postgres import PostgresContainer; import httpx`. `make check` green (87 passed, 2 skipped — no new test files yet). **Docker-daemon runtime requirement** for the Sub-step 2.4 integration tests is noted but not exercised here (no container started in this task). Committed as `ec86181`. ADR-0011 (Testcontainers) is recorded later in the sub-step.

**Files:**
- Modify: `pyproject.toml` (root) `[dependency-groups].dev`
- Verify: `uv.lock` updates

**Interfaces:**
- Produces: `testcontainers[postgres]>=4.0.0,<5.0.0` and `httpx>=0.27.0,<1.0.0` available to all package tests (`httpx` already declared as `httpx2`; check if `httpx2` suffices for `TestClient` async — it does not; add canonical `httpx`). `psycopg[binary]` is a runtime dep of identity-service (Task 6) used by the migrations runner.

- [ ] **Step 1: Add deps**

Modify root `pyproject.toml` `[dependency-groups]` `dev`:

```toml
dev = [
  "httpx>=0.27.0,<1.0.0",
  "httpx2>=2.5.0,<3.0.0",
  "mypy>=1.19.1,<2.0.0",
  "pytest>=9.0.2,<10.0.0",
  "pytest-asyncio>=1.3.0,<2.0.0",
  "ruff>=0.15.0,<1.0.0",
  "testcontainers[postgres]>=4.0.0,<5.0.0",
]
```

> Keep `httpx2` if something else uses it; add canonical `httpx` for FastAPI `AsyncClient`. If `httpx2` is unused after this, leave it (out of scope to remove).

- [ ] **Step 2: Sync**

Run: `uv sync --all-packages --all-groups`
Expected: `testcontainers` installs in the dev env. Verify: `uv run python -c "import testcontainers; from testcontainers.postgres import PostgresContainer; print('ok')"` → `ok`.

- [ ] **Step 3: `make check` (pytest may now collect new files later) and commit**

Run: `make check` → green (no new test files yet). Then:

```bash
git add pyproject.toml uv.lock
git commit -m "dev: add testcontainers[postgres] + httpx for Phase 2 integration tests (ADR-0011)"
```

### Task 12: SQLAlchemy models (users, sessions, audit_events)

**Status:** ✅ DONE (2026-07-19). `infrastructure/database/models.py` defines `UserModel` (users; `TimestampMixin`+`VersionMixin`;`UniqueConstraint(tenant_id, email)` **declaratively** — not the plan's flagged `.__call__` hack), `SessionModel` (sessions; `user_id` FK→users.id `ON DELETE CASCADE`, unique `refresh_jti`, tz `expires_at`, nullable `revoked_at`), and `AuditEventModel` (audit_events; nullable `actor_id` idx, `audit_action` enum, nullable `target_id`, `correlation_id`, tz `occurred_at`, JSONB metadata). PG-native types: `UUID(as_uuid=True)`, `JSONB`, `CITEXT`, tz-aware `DateTime`, `Enum` with `values_callable` so PG enums store the `StrEnum` lowercase values. Unused `Interval`/`text` imports dropped (plan flags). Smoke import verified (`UserModel.__tablename__` / `SessionModel` / `AuditEventModel` register on `Base.metadata`; `AuditEventModel.__table__.c.metadata.name == 'metadata'` JSONB). `make check` green (87 passed, 2 skipped, no new tests). Committed as `cb1c80c`.

**Deviation from the plan snippet (recorded):** the plan named the `AuditEventModel` column `metadata`, but `metadata` is **reserved on `DeclarativeBase`** — `Base.metadata` is SQLAlchemy's `MetaData` object, and declarative mapping raises `InvalidRequestError: Attribute name 'metadata' is reserved`. The Python attribute is `meta`; the DB column keeps the name `metadata` via `mapped_column("metadata", JSONB, ...)` for compatibility with the domain `AuditEvent.metadata` schema, JSONB queries, and migration 0001. The Task 13 repository mapper translates `meta ↔ metadata`.

**Files:**
- Create: `apps/identity-service/src/identity_service/infrastructure/__init__.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/database/__init__.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/database/models.py`

**Interfaces:**
- Produces: `UserModel`, `SessionModel`, `AuditEventModel` on `worker_database.Base`; PG-native types (`UUID`, `JSONB`, `ENUM`, `citext`); the `AccountStatus`/`AuditAction` enums mapped to PG enums.
- Consumes: `worker_database.Base`, `TimestampMixin`, `VersionMixin`; `identity_service.domain.audit.AuditAction`; `identity_service.domain.user.AccountStatus`.

- [ ] **Step 1: Write models**

`apps/identity-service/src/identity_service/infrastructure/__init__.py` — empty.

`apps/identity-service/src/identity_service/infrastructure/database/__init__.py` — empty.

`apps/identity-service/src/identity_service/infrastructure/database/models.py`:

```python
"""SQLAlchemy 2 models for identity-service (Postgres-native types)."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID, uuid4

from sqlalchemy import DateTime, Enum, ForeignKey, Interval, String, Text, text
from sqlalchemy.dialects.postgresql import CITEXT, JSONB, UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from worker_database import Base, TimestampMixin, VersionMixin

from identity_service.domain.audit import AuditAction
from identity_service.domain.user import AccountStatus

__all__ = ["AuditEventModel", "SessionModel", "UserModel"]


class UserModel(Base, TimestampMixin, VersionMixin):
    __tablename__ = "users"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True, default=uuid4)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    email: Mapped[str] = mapped_column(CITEXT, nullable=False)
    password_hash: Mapped[str] = mapped_column(Text, nullable=False)
    display_name: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[AccountStatus] = mapped_column(
        Enum(AccountStatus, name="account_status", values_callable=lambda e: [m.value for m in e]),
        nullable=False,
        default=AccountStatus.ACTIVE,
    )
    roles: Mapped[list[str]] = mapped_column(JSONB, nullable=False, default=list)

    __table_args__ = (
        # unique email per tenant (two tenants may share the same local-part)
        # specified by a UniqueConstraint below
    )


from sqlalchemy import UniqueConstraint  # noqa: E402

UniqueConstraint("tenant_id", "email", name="uq_users_tenant_email").__call__(UserModel.__table__)  # type: ignore[attr-defined]
```

> **Avoid the post-hoc `UniqueConstraint` hack.** Place it declaratively instead — replace the `__table_args__ = (...)` block and the trailing lines with:

```python
from sqlalchemy import UniqueConstraint
```
at the top imports, and in the class body:

```python
    __table_args__ = (UniqueConstraint("tenant_id", "email", name="uq_users_tenant_email"),)
```

Use the declarative form (the hacky `.__call__` form above is only shown to flag the pitfall — do not commit it). The implementation step writes the clean declarative version.

`SessionModel`:

```python
class SessionModel(Base, TimestampMixin):
    __tablename__ = "sessions"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True, default=uuid4)
    user_id: Mapped[UUID] = mapped_column(
        PG_UUID(as_uuid=True), ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True
    )
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    refresh_jti: Mapped[str] = mapped_column(String(128), nullable=False, unique=True)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
```

> `Interval` import unused — drop it from the imports list when writing the final file.

`AuditEventModel`:

```python
class AuditEventModel(Base, TimestampMixin):
    __tablename__ = "audit_events"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True, default=uuid4)
    actor_id: Mapped[UUID | None] = mapped_column(PG_UUID(as_uuid=True), nullable=True, index=True)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    action: Mapped[AuditAction] = mapped_column(
        Enum(AuditAction, name="audit_action", values_callable=lambda e: [m.value for m in e]),
        nullable=False,
    )
    target_id: Mapped[UUID | None] = mapped_column(PG_UUID(as_uuid=True), nullable=True)
    correlation_id: Mapped[str | None] = mapped_column(String(64), nullable=True)
    occurred_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    metadata: Mapped[dict[str, str]] = mapped_column(JSONB, nullable=False, default=dict)
```

> `text` import unused — drop it.

- [ ] **Step 2: Smoke-import the models (no DB needed)**

Run: `uv run python -c "from identity_service.infrastructure.database.models import UserModel, SessionModel, AuditEventModel; print('models import ok', UserModel.__tablename__)"`
Expected: `models import ok users` (tables register on `Base.metadata`).

- [ ] **Step 3: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/src/identity_service/infrastructure/__init__.py apps/identity-service/src/identity_service/infrastructure/database/__init__.py apps/identity-service/src/identity_service/infrastructure/database/models.py
git commit -m "identity-service/infra: SQLAlchemy models (users/sessions/audit_events, PG types)"
```

### Task 13: Repository implementations + Clock adapter + JWT adapter + bcrypt adapter

**Status:** ✅ DONE (2026-07-19). `application/ports.py` (`AuthPrincipal`, `TokenPair`, `UserRepository`/`SessionRepository`/`AuditRepository` async Protocols; `SessionRepository.get_by_jti -> SessionModel | None` per plan note 1837 — sessions are infra-owned read-model, so ports.py imports `SessionModel`), `infrastructure/clock.py` (`SystemClock`), `infrastructure/auth/hasher.py` (`BcryptPasswordAdapter` over `worker_auth.BcryptPasswordHasher`), `infrastructure/auth/jwt_service.py` (`JwTokenService` over `worker_auth.TokenManager`; `issue_*` + `verify_*_token -> AuthPrincipal` from `TokenPayload.sub`/`tenant_id`/`roles`; jti-vs-sessions validation stays in the command — 2.5), `infrastructure/database/repositories.py` (`SqlAlchemyUserRepository`/`SessionRepository`/`AuditRepository`; the audit `append` writes the `meta` attribute which persists as the `metadata` JSONB column — the Task-12 `DeclarativeBase`-reservation rename). Test: `test_hashing_port.py` (adapter hash/verify + >72-byte `PasswordTooLong` through the domain port), 2 unit tests pass, no Docker. Smoke-import of all adapters/ports/repos verified. `make check` green (89 passed, 2 skipped, +2 over Task 12). Committed as `9f17c95`.

**Deviation from the plan snippet:** `@override` dropped (plan note 2046 anticipated this) — mypy accepts the Protocol matches without the decorator; against Protocols it adds only noise. `SessionRepository.add`/`revoke` and the `Protocol` use the keyword-only `add(*, user_id, ...)` form (plan note 2046 alignment); the loose `ports.py` arg types were tightened to concrete `datetime`/`UUID` so mypy-strict is happy and the concrete repo matches the Protocol signature. The unused `pydantic.ValidationError` import in the plan's `jwt_service` snippet was not carried (PyJWT errors are wrapped inside `TokenManager.verify_token`, not by Pydantic-ValidationError handling at the adapter layer). The `meta` (not `metadata`) keyword on `AuditEventModel(...)` reflects the Task-12 rename.

**Files:**
- Create: `apps/identity-service/src/identity_service/application/__init__.py`
- Create: `apps/identity-service/src/identity_service/application/ports.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/database/repositories.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/clock.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/auth/__init__.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/auth/hasher.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/auth/jwt_service.py`
- Test: `apps/identity-service/tests/unit/test_hashing_port.py`

**Interfaces:**
- Produces: `UserRepository`/`SessionRepository`/`AuditRepository` async Protocols (in `application/ports.py`) with `get_by_email(tenant_id, email)`, `get_by_id(id)`, `add(user)`, etc.
- Produces: `SqlAlchemyUserRepository`, `SqlAlchemySessionRepository`, `SqlAlchemyAuditRepository` (async, take an `AsyncSession`).
- Produces: `SystemClock` (impl of `domain.services.Clock`), `BcryptPasswordAdapter` (impl of `PasswordHashing`), `JwTokenService` (impl of `TokenService`; wraps `worker_auth.TokenManager`; `issue_access_token`/`issue_refresh_token`; `verify_access_token`/`verify_refresh_token` return an `AuthPrincipal`).
- Produces: `AuthPrincipal` (frozen dataclass `user_id: UUID`, `tenant_id: UUID`, `roles: list[str]`) in `application/ports.py`.
- Produces: `TokenPair` (frozen dataclass `access: str`, `refresh: str`) in `application/ports.py`.

- [ ] **Step 1: Write `application/ports.py`**

`apps/identity-service/src/identity_service/application/__init__.py` — empty.

`apps/identity-service/src/identity_service/application/ports.py`:

```python
"""Application-layer ports (interfaces) + shared DTOs used across commands."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol
from uuid import UUID

from identity_service.domain.audit import AuditEvent
from identity_service.domain.user import User

__all__ = [
    "AuditRepository",
    "AuthPrincipal",
    "SessionRepository",
    "TokenPair",
    "UserRepository",
]


@dataclass(frozen=True, slots=True)
class AuthPrincipal:
    user_id: UUID
    tenant_id: UUID
    roles: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class TokenPair:
    access: str
    refresh: str


class UserRepository(Protocol):
    async def get_by_email(self, tenant_id: UUID, email: str) -> User | None: ...
    async def get_by_id(self, user_id: UUID) -> User | None: ...
    async def add(self, user: User) -> None: ...


class SessionRepository(Protocol):
    async def add(self, user_id: UUID, tenant_id: UUID, refresh_jti: str, expires_at, now) -> None: ...
    async def get_by_jti(self, refresh_jti: str): ...  # returns SessionModel | None (infra type)
    async def revoke(self, refresh_jti: str, revoked_at) -> None: ...


class AuditRepository(Protocol):
    async def append(self, event: AuditEvent) -> None: ...
```

> Keep the port signatures focused; refine the exact arg types in the implementation. `SessionRepository.get_by_jti` returning the model directly is acceptable because sessions are a persistence concern (the read-model is infra-owned, not domain-owned — `SessionModel` is not a domain aggregate).

- [ ] **Step 2: Write the infrastructure adapters**

`apps/identity-service/src/identity_service/infrastructure/clock.py`:

```python
"""System clock — production implementation of the domain Clock port."""

from __future__ import annotations

from datetime import UTC, datetime


class SystemClock:
    def now(self) -> datetime:
        return datetime.now(UTC)
```

`apps/identity-service/src/identity_service/infrastructure/auth/__init__.py` — empty.

`apps/identity-service/src/identity_service/infrastructure/auth/hasher.py`:

```python
"""Bcrypt adapter — bridges worker-auth BcryptPasswordHasher to the domain port."""

from __future__ import annotations

from worker_auth import BcryptPasswordHasher

from identity_service.domain.value_objects import PasswordHash


class BcryptPasswordAdapter:
    def __init__(self, *, rounds: int = 12) -> None:
        self._hasher = BcryptPasswordHasher(rounds=rounds)

    def hash(self, plain: str) -> PasswordHash:
        return PasswordHash(self._hasher.hash_password(plain))

    def verify(self, plain: str, hashed: PasswordHash) -> bool:
        return self._hasher.verify_password(plain, hashed.value)
```

`apps/identity-service/src/identity_service/infrastructure/auth/jwt_service.py`:

```python
"""JWT adapter — bridges worker-auth TokenManager to the domain TokenService port."""

from __future__ import annotations

from uuid import UUID

from pydantic import ValidationError
from worker_auth import ExpiredToken, InvalidToken, TokenManager

from identity_service.application.ports import AuthPrincipal, TokenPair


class JwTokenService:
    def __init__(self, secret: str, *, access_expire_minutes: int = 15, refresh_expire_minutes: int = 1440) -> None:
        self._manager = TokenManager(
            secret=secret,
            access_token_expire_minutes=access_expire_minutes,
            refresh_token_expire_minutes=refresh_expire_minutes,
        )

    def issue_access_token(
        self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]
    ) -> str:
        return self._manager.create_access_token(user_id, tenant_id, roles, permissions)

    def issue_refresh_token(self, user_id: UUID, tenant_id: UUID, *, session_jti: str) -> str:
        return self._manager.create_refresh_token(user_id, tenant_id, session_jti=session_jti)

    def issue_pair(
        self, *, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str], session_jti: str
    ) -> TokenPair:
        access = self.issue_access_token(user_id, tenant_id, roles, permissions)
        refresh = self.issue_refresh_token(user_id, tenant_id, session_jti=session_jti)
        return TokenPair(access=access, refresh=refresh)

    def _verify(self, token: str, *, expected_type: str) -> AuthPrincipal:
        payload = self._manager.verify_token(token, expected_type=expected_type)
        return AuthPrincipal(user_id=payload.sub, tenant_id=payload.tenant_id, roles=tuple(payload.roles))

    def verify_access_token(self, token: str) -> AuthPrincipal:
        return self._verify(token, expected_type="access")

    def verify_refresh_token(self, token: str) -> AuthPrincipal:
        return self._verify(token, expected_type="refresh")
```

> The `verify_refresh_token` returns a principal but the *jti validation* against the sessions table happens in the command (Sub-step 2.5), not here. `worker_auth.TokenManager.verify_token` already rejects wrong-type tokens.

`apps/identity-service/src/identity_service/infrastructure/database/repositories.py` — async repos on `AsyncSession`, mapping `UserModel` ↔ `User` domain aggregate:

```python
"""SqlAlchemy implementations of the application repository ports."""

from __future__ import annotations

from datetime import datetime
from typing import override
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from identity_service.application.ports import AuditRepository
from identity_service.domain.audit import AuditEvent
from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash, TenantId, UserId
from identity_service.infrastructure.database.models import (
    AuditEventModel,
    SessionModel,
    UserModel,
)


def _to_domain(row: UserModel) -> User:
    return User(
        id=UserId(row.id),
        tenant_id=TenantId(row.tenant_id),
        email=Email(row.email),
        password_hash=PasswordHash(row.password_hash),
        display_name=row.display_name,
        roles=tuple(row.roles),
        status=AccountStatus(row.status),
    )


class SqlAlchemyUserRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_email(self, tenant_id: UUID, email: str) -> User | None:
        stmt = select(UserModel).where(UserModel.tenant_id == tenant_id, UserModel.email == email.lower())
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return _to_domain(row) if row is not None else None

    async def get_by_id(self, user_id: UUID) -> User | None:
        row = await self._session.get(UserModel, user_id)
        return _to_domain(row) if row is not None else None

    async def add(self, user: User) -> None:
        self._session.add(
            UserModel(
                id=user.id.value,
                tenant_id=user.tenant_id.value,
                email=user.email.value,
                password_hash=user.password_hash.value,
                display_name=user.display_name,
                status=user.status,
                roles=list(user.roles),
            )
        )
        await self._session.flush()


class SqlAlchemySessionRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(
        self, *, user_id: UUID, tenant_id: UUID, refresh_jti: str, expires_at: datetime
    ) -> None:
        self._session.add(
            SessionModel(
                user_id=user_id,
                tenant_id=tenant_id,
                refresh_jti=refresh_jti,
                expires_at=expires_at,
            )
        )
        await self._session.flush()

    async def get_by_jti(self, refresh_jti: str) -> SessionModel | None:
        stmt = select(SessionModel).where(SessionModel.refresh_jti == refresh_jti)
        return (await self._session.execute(stmt)).scalar_one_or_none()

    async def revoke(self, refresh_jti: str, revoked_at: datetime) -> None:
        stmt = select(SessionModel).where(SessionModel.refresh_jti == refresh_jti)
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        if row is not None and row.revoked_at is None:
            row.revoked_at = revoked_at
            await self._session.flush()


class SqlAlchemyAuditRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    @override
    async def append(self, event: AuditEvent) -> None:
        self._session.add(
            AuditEventModel(
                actor_id=event.actor_id,
                tenant_id=event.tenant_id,
                action=event.action,
                target_id=event.target_id,
                correlation_id=event.correlation_id,
                occurred_at=event.occurred_at,
                metadata=dict(event.metadata),
            )
        )
        await self._session.flush()
```

> `SessionRepository` Protocol in `ports.py` is loose on arg types; the concrete `SqlAlchemySessionRepository.add` is the canonical signature — keep them aligned (update the Protocol in Task 13 Step 1 to match: `add(*, user_id, tenant_id, refresh_jti, expires_at)`). `@override` requires the Protocol method to match exactly — if mypy objects to `@override` against a `Protocol`, remove the decorator.

- [ ] **Step 3: Write the (failing-then-passing) hasher adapter unit test**

`apps/identity-service/tests/unit/test_hashing_port.py`:

```python
import pytest

from identity_service.domain.value_objects import PasswordHash
from identity_service.infrastructure.auth.hasher import BcryptPasswordAdapter


def test_adapter_hash_and_verify() -> None:
    ad = BcryptPasswordAdapter(rounds=4)  # low rounds only in tests
    hashed = ad.hash("hunter2")
    assert isinstance(hashed, PasswordHash)
    assert hashed.value.startswith("$2")
    assert ad.verify("hunter2", hashed) is True
    assert ad.verify("wrong", hashed) is False


def test_adapter_rejects_overlong_via_domain_port() -> None:
    ad = BcryptPasswordAdapter(rounds=4)
    from worker_auth import PasswordTooLong

    with pytest.raises(PasswordTooLong):
        ad.hash("a" * 73)
```

- [ ] **Step 4: Run tests**

Run: `uv run pytest apps/identity-service/tests/unit/ -v`
Expected: PASS (value_objects + audit + user + hashing_port).

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/src/identity_service/application/__init__.py apps/identity-service/src/identity_service/application/ports.py apps/identity-service/src/identity_service/infrastructure/clock.py apps/identity-service/src/identity_service/infrastructure/auth/ apps/identity-service/src/identity_service/infrastructure/database/repositories.py apps/identity-service/tests/unit/test_hashing_port.py
git commit -m "identity-service/app+infra: ports, repositories, bcrypt/JWT/clock adapters"
```

### Task 14: Alembic config + async env.py + script template + first migration

**Status:** ✅ DONE (2026-07-19). Established the per-service async Alembic scaffold (ADR-0010) and the first hand-written revision `0001_init_users_sessions_audit`. Four files: `alembic.ini` (script_location=migrations, prepend_sys_path=., `sqlalchemy.url` unset — env.py derives from `WORKER_DATABASE_URL`/`DATABASE_URL`/dev default); `migrations/env.py` (async env via `async_engine_from_config`+`run_sync(do_run_migrations)`, imports `identity_service.infrastructure.database.models` so tables register on `worker_database.Base.metadata`); `migrations/script.py.mako` (standard template, modernized `str | None` / `collections.abc.Sequence`); `migrations/versions/0001_init_users_sessions_audit.py` (hand-written `upgrade()` creating users+sessions+audit_events + the `account_status`/`audit_action` PG enums, JSONB server-defaults, FK ON DELETE CASCADE, `UniqueConstraint(tenant_id, email)`; DB column `metadata` JSONB consistent with the Task-12 ORM `meta` attribute). **Offline SQL verified** (`alembic upgrade head --sql` emits CREATE TYPE ×2, all three CREATE TABLEs, indexes, FK, unique constraint, JSONB defaults, alembic_version row). `make check` green (89 passed, 2 skipped, no new tests). Committed as `809d8b2`.

**Three deliberate deviations/extensions over the plan snippet:** (1) `env.py` builds the engine **inside** the `if section is not None` guard (plan snippet built it outside → mypy-fails `section["sqlalchemy.url"] = ...` over `dict | None`); (2) the plan's bogus `op=op` placeholder line (plan note 2341) was dropped, and `typing.Union`/`typing.Sequence` modernized to `X | None` / `collections.abc.Sequence` for py314 ruff-cleanliness; (3) a `downgrade()` was **added** (plan had none) — drops audit_events, sessions, users (FK order) then drops both PG enums with `checkfirst=True` (reversible, eases Testcontainers tear-down). User-approved retention of the downgrade.

**Post-Task-14 schema fix (surfaced by Task 16, committed with Task 16 as `3c53a78`):** `upgrade()` now prepends `CREATE EXTENSION IF NOT EXISTS citext` before `create_table("users")`, since the `users.email CITEXT` column needs the citext contrib enabled per-database. The original 0001 omitted it — offline-SQL Step 5 never surfaced it (citext is only absent at runtime on a fresh PG). The Task-16 integration run first failed on `type "citext" does not exist`, proving this is a real production-schema fix (a fresh `alembic upgrade head` in prod would have failed without it), not just a test fix. `downgrade()` leaves citext installed (shared contrib extension).

**Files:**
- Create: `apps/identity-service/alembic.ini`
- Create: `apps/identity-service/migrations/env.py`
- Create: `apps/identity-service/migrations/script.py.mako`
- Create: `apps/identity-service/migrations/versions/0001_init_users_sessions_audit.py`

**Interfaces:**
- Consumes: `worker_database.Base.metadata` (the autogenerate target — `models.py` registers on it by importing `Base`).
- Produces: `alembic upgrade head` created all three tables + the two PG enums. The migration import path `identity_service.infrastructure.database.models` is importable when running `alembic` from `apps/identity-service`.

- [ ] **Step 1: Write `alembic.ini`**

`apps/identity-service/alembic.ini`:

```ini
[alembic]
script_location = migrations
prepend_sys_path = .
# sqlalchemy.url is intentionally unset; env.py derives it from WORKER_DATABASE_URL.
sqlalchemy.url =

[loggers]
keys = root,sqlalchemy,alembic

[handlers]
keys = console

[formatters]
keys = generic

[logger_root]
level = WARN
handlers = console
qualname =

[logger_sqlalchemy]
level = WARN
handlers =
qualname = sqlalchemy.engine

[logger_alembic]
level = INFO
handlers =
qualname = alembic

[handler_console]
class = StreamHandler
args = (sys.stderr,)
level = NOTSET
formatter = generic

[formatter_generic]
format = %(levelname)-5.5s [%(name)s] %(message)s
datefmt = %H:%M:%S
```

- [ ] **Step 2: Write async `env.py`**

`apps/identity-service/migrations/env.py`:

```python
"""Async Alembic env.py (ADR-0010) for the identity-service database."""

from __future__ import annotations

import asyncio
import os
import sys
from logging.config import fileConfig
from pathlib import Path

from alembic import context
from sqlalchemy import pool
from sqlalchemy.engine import Connection
from sqlalchemy.ext.asyncio import async_engine_from_config

# Make `identity_service` importable when alembic runs from apps/identity-service.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from worker_database import Base  # noqa: E402

# Import models so their tables register on Base.metadata (autogenerate target).
from identity_service.infrastructure.database import models  # noqa: E402, F401

config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

target_metadata = Base.metadata


def _database_url() -> str:
    return os.environ.get(
        "WORKER_DATABASE_URL", os.environ.get("DATABASE_URL", "postgresql+asyncpg://worker:worker@127.0.0.1:5432/identity")
    )


def run_migrations_offline() -> None:
    context.configure(
        url=_database_url(),
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )
    with context.begin_transaction():
        context.run_migrations()


def do_run_migrations(connection: Connection) -> None:
    context.configure(connection=connection, target_metadata=target_metadata)
    with context.begin_transaction():
        context.run_migrations()


async def run_migrations_online() -> None:
    section = config.get_section(config.config_ini_section, {})
    if section is not None:
        section["sqlalchemy.url"] = _database_url()
    engine = async_engine_from_config(section, prefix="sqlalchemy.", poolclass=pool.NullPool)
    async with engine.connect() as connection:
        await connection.run_sync(do_run_migrations)
    await engine.dispose()


if context.is_offline_mode():
    run_migrations_offline()
else:
    asyncio.run(run_migrations_online())
```

> `async_engine_from_config(section, ...)` — `section` is `dict[str, Any] | None`; guard with the `if section is not None` already shown.

- [ ] **Step 3: Write `script.py.mako`** (standard Alembic template)

`apps/identity-service/migrations/script.py.mako`:

```mako
"""${message}

Revision ID: ${up_revision}
Revises: ${down_revision | comma,n}
Create Date: ${create_date}
"""
from __future__ import annotations

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
${imports if imports else ""}

revision: str = ${repr(up_revision)}
down_revision: Union[str, None] = ${repr(down_revision)}
branch_labels: Union[str, Sequence[str], None] = ${repr(branch_labels)}
depends_on: Union[str, Sequence[str], None] = ${repr(depends_on)}


def upgrade() -> None:
    ${upgrades if upgrades else "pass"}


def downgrade() -> None:
    ${downgrades if downgrades else "pass"}
```

- [ ] **Step 4: Write the first revision by hand**

> Autogenerate needs a live DB; we hand-write `0001` so it applies cleanly on the empty Testcontainers pg (verified in Task 16). Future revisions use `worker migrate` against a live DB.

`apps/identity-service/migrations/versions/0001_init_users_sessions_audit.py`:

```python
"""init users sessions audit_events

Revision ID: 0001_init_users_sessions_audit
Revises:
Create Date: 2026-07-16
"""
from __future__ import annotations

from typing import Sequence, Union

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects.postgresql import CITEXT, JSONB, UUID as PG_UUID

from identity_service.domain.audit import AuditAction
from identity_service.domain.user import AccountStatus

revision: str = "0001_init_users_sessions_audit"
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    account_status = sa.Enum(
        AccountStatus, name="account_status", values_callable=lambda e: [m.value for m in e]
    )
    audit_action = sa.Enum(
        AuditAction, name="audit_action", values_callable=lambda e: [m.value for m in e]
    )
    op.create_table(
        "users",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("email", CITEXT, nullable=False),
        sa.Column("password_hash", sa.Text, nullable=False),
        sa.Column("display_name", sa.String(255), nullable=False),
        sa.Column("status", account_status, nullable=False, server_default=AccountStatus.ACTIVE.value),
        sa.Column("roles", JSONB, nullable=False, server_default=sa.text("'[]'::jsonb")),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.Column("version", sa.Integer, nullable=False, server_default="1"),
        sa.UniqueConstraint("tenant_id", "email", name="uq_users_tenant_email"),
    )

    op.create_table(
        "sessions",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("user_id", PG_UUID(as_uuid=True), sa.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("refresh_jti", sa.String(128), nullable=False, unique=True),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("revoked_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
    )

    op.create_table(
        "audit_events",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("actor_id", PG_UUID(as_uuid=True), nullable=True, index=True),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("action", audit_action, nullable=False),
        sa.Column("target_id", PG_UUID(as_uuid=True), nullable=True),
        sa.Column("correlation_id", sa.String(64), nullable=True),
        sa.Column("occurred_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("metadata", JSONB, nullable=False, server_default=sa.text("'{}'::jsonb")),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        op=op,  # noqa: F841 — keep op in scope; remove this no-op line when finalizing.
    )
```

> Drop the bogus `op=op` line when finalizing — it is a placeholder to remind not to leave unused vars. The real `op.create_table("audit_events", ...)` call closes the statement. (`op.create_table(...)` returns `None`.)

- [ ] **Step 5: Verify offline SQL generation**

Run: `WORKER_DATABASE_URL=postgresql+asyncpg://x:x@127.0.0.1/x uv run alembic upgrade head --sql`
Expected: emits `CREATE TABLE users ...`, `CREATE TABLE sessions ...`, `CREATE TYPE account_status ...`, `CREATE TYPE audit_action ...`, `SELECT alembic_version...` SQL (offline mode does not connect).

- [ ] **Step 6: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add apps/identity-service/alembic.ini apps/identity-service/migrations
git commit -m "identity-service: alembic.ini + async env.py + 0001_init migration (ADR-0010)"
```

### Task 15: Composition-Root (`compose.py`)

**Status:** ✅ DONE (2026-07-19). `infrastructure/compose.py` implements `compose_infrastructure(settings, engine) -> dict[str, Any]` returning the wiring bundle `{"engine", "session_factory", "request_scope", "hasher", "tokens", "clock", "eventbus"}` (ADR-0003 — explicit Composition-Root, no fluent `PlatformBuilder`). `request_scope` is an `asynccontextmanager` yielding `(UnitOfWork, repos)` for one request transaction, repos built with `uow.session` (the public property that raises if the UoW has not been entered). Hasher takes `settings.bcrypt_rounds`; `JwTokenService` takes the unsealed `jwt_secret` + access/refresh expire minutes; `SystemClock` and `EventBus` are bare singletons (domain-event publication wiring lands in 2.7). Smoke import verified; `make check` green (89 passed, 2 skipped, no new tests). Committed as `deab353`.

**Deviation from the plan snippet:** repos use `uow.session` (not the private `uow._session` the plan snippet flagged — plan note 2434); the unused `Callable` import in the plan snippet was dropped.

**Files:**
- Create: `apps/identity-service/src/identity_service/infrastructure/compose.py`

**Interfaces:**
- Produces: `compose_infrastructure(settings: IdentityServiceSettings, engine: AsyncEngine) -> dict[str, Any]` returning `{"engine", "session_factory", "uow_factory", "userRepository", "sessionRepository", "auditRepository", "hasher", "tokens", "clock", "eventbus"}` (a wiring bundle). Commands (Sub-step 2.5) take the pieces they need from this bundle.
- The UoW factory returns an async context manager yielding the `UnitOfWork` (from `worker_database`) plus fresh per-request repositories bound to its session.

- [ ] **Step 1: Write `compose.py`**

```python
"""Composition-Root wiring for identity-service infrastructure (ADR-0003)."""

from __future__ import annotations

from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker

from worker_database import UnitOfWork
from worker_events import EventBus

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.auth.hasher import BcryptPasswordAdapter
from identity_service.infrastructure.auth.jwt_service import JwTokenService
from identity_service.infrastructure.clock import SystemClock
from identity_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemySessionRepository,
    SqlAlchemyUserRepository,
)


@asynccontextmanager
async def request_scope(session_factory: async_sessionmaker[AsyncSession]) -> AsyncIterator[
    tuple[UnitOfWork, dict[str, Any]]
]:
    """Yield a UoW + per-request repos bound to one session."""
    uow = UnitOfWork(session_factory)
    async with uow:
        assert uow._session is not None  # set by __aenter__
        repos = {
            "users": SqlAlchemyUserRepository(uow.session),
            "sessions": SqlAlchemySessionRepository(uow.session),
            "audit": SqlAlchemyAuditRepository(uow.session),
        }
        yield uow, repos


def compose_infrastructure(
    settings: IdentityServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    session_factory: async_sessionmaker[AsyncSession] = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False, autoflush=False
    )
    return {
        "engine": engine,
        "session_factory": session_factory,
        "request_scope": request_scope,
        "hasher": BcryptPasswordAdapter(rounds=settings.bcrypt_rounds),
        "tokens": JwTokenService(
            settings.jwt_secret.get_secret_value(),
            access_expire_minutes=settings.jwt_access_token_expire_minutes,
            refresh_expire_minutes=settings.jwt_refresh_token_expire_minutes,
        ),
        "clock": SystemClock(),
        "eventbus": EventBus(),
    }
```

> `UnitOfWork._session` is private; accessing it is acceptable within our own composition-root but flag it. A cleaner option: expose `UnitOfWork.session` only after `__aenter__` (it already does — `self.session` property raises if `_session is None`). Use `uow.session` instead of `uow._session`, dropping the assert. **Use `uow.session` in the final file.**

- [ ] **Step 2: `make check` and commit**

Run: `uv run python -c "import identity_service.infrastructure.compose as c; print('compose import ok')"` then `make check` → green. Then:

```bash
git add apps/identity-service/src/identity_service/infrastructure/compose.py
git commit -m "identity-service/infra: Composition-Root bundle (compose_infrastructure)"
```

### Task 16: Testcontainers integration fixture + migration roundtrip test + ADR-0011

**Status:** ✅ DONE (2026-07-19). First Docker-exercising tests in Phase 2 — **3/3 pass green against a real `postgres:17-alpine` container** (Docker verified up locally). Files: `tests/integration/__init__.py` (empty package marker; **no** `tests/__init__.py` root → preserves the Phase-1 collection convention), `tests/integration/_docker.py` (`_docker_available()` guard helper, raw-string docstring so backtick escapes stay literal under py314), `tests/integration/conftest.py` (session-scoped `postgres_url` from `PostgresContainer("postgres:17-alpine", driver="asyncpg")` with asyncpg URL normalization; function-scoped `engine`/`session_factory`/`session` with per-test schema reset; enables `CREATE EXTENSION IF NOT EXISTS citext` before `create_all` because `Base.metadata` does not emit CREATE EXTENSION); `tests/integration/test_migrations.py` (applies `alembic upgrade head` via the **Alembic Python API** — `Config`+`command.upgrade` with `WORKER_DATABASE_URL` set in-process and restored — hermetic, preferred over the plan's fragile `uv run alembic` subprocess per plan note 2610; asserts users/sessions/audit_events exist); `tests/integration/test_repository_roundtrip.py` (user-repo add+commit + CITEXT case-insensitive `get_by_email`; session+audit repos roundtrip that **first persists a real User** and reuses its `id.value`); `docs/adr/0011-integration-testcontainers-postgres.md` (root-dev `testcontainers`, `tests/integration` layout, offline-skip green-equivalence, session-scoped container + per-test reset, relative-import helper choice, Alembic-Python-API migration path). `make check` green (**92 passed, 2 skipped**, +3 over Task 15's 89). Committed as `3c53a78`.

**Two deviations from the plan snippet:** (1) the plan's cross-package `from tests.integration.conftest import _docker_available` would require a `tests/__init__.py` root package (breaks Phase-1 collection convention) — replaced by a relative-import `_docker.py` helper inside the `tests/integration/` package, imported by both conftest and the test modules. (2) the plan's migration test used a `uv run alembic` subprocess + the plan's session/audit roundtrip used a bare `uuid4()` for `sessions.user_id` (FK violation — `sessions.user_id → users.id ON DELETE CASCADE` enforces it). Both fixed: Alembic-Python-API hermetic migration path; and the roundtrip test persists a real User first and reuses its `id.value`.

**Surfaced a real production-schema gap in Task 14's migration** (committed alongside Task 16): `0001` now prepends `CREATE EXTENSION IF NOT EXISTS citext` to its `upgrade()`. Without it, a fresh `alembic upgrade head` in production would fail on `type "citext" does not exist` — the offline-SQL Step-5 check in Task 14 never surfaced it (citext is only absent at runtime on a fresh PG). The integration run first failed on exactly this, proving the fix is a production-schema fix, not just a test fix.

**Files:**
- Create: `apps/identity-service/tests/__init__.py` (only if collection needs it; Phase-1 smoke is at `tests/test_smoke_identity_service.py` already; the `unit/` and `integration/` subpackages have their own `__init__.py`). Add `tests/integration/__init__.py`.
- Create: `apps/identity-service/tests/integration/conftest.py`
- Create: `apps/identity-service/tests/integration/test_migrations.py`
- Create: `apps/identity-service/tests/integration/test_repository_roundtrip.py`
- Create: `docs/adr/0011-integration-testcontainers-postgres.md`

**Interfaces:**
- Produces: `postgres_url` (session-scoped fixture → asyncpg URL), `engine` + `session_factory` + `uow` (function-scoped, fresh schema via `Base.metadata` + `alembic upgrade head`). `skip_if_no_docker` helper.

- [ ] **Step 1: Write `conftest.py`**

`apps/identity-service/tests/integration/__init__.py` — empty.

`apps/identity-service/tests/integration/conftest.py`:

```python
"""Testcontainers PostgreSQL fixture for identity-service (ADR-0011).

Skips the whole suite if Docker/testcontainers is unavailable (offline runs)."""

from __future__ import annotations

import asyncio
import shutil
import socket
import subprocess
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

import pytest
import pytest_asyncio
from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker, create_async_engine

# models must import so Base.metadata has our tables *and* so alembic env.py sees them identically
from worker_database import Base  # noqa: F401

from identity_service.infrastructure.database import models  # noqa: F401, F401


def _docker_available() -> bool:
    return shutil.which("docker") is not None and _docker_daemon_up()


def _docker_daemon_up() -> bool:
    try:
        socket.create_connection(("127.0.0.1", 2375), timeout=0.2)  # rarely open; fall through
    except OSError:
        pass
    r = subprocess.run(["docker", "info"], capture_output=True, text=True, timeout=15)
    return r.returncode == 0


pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available (ADR-0011 offline-skip)")


@pytest.fixture(scope="session")
def event_loop():
    loop = asyncio.new_event_loop()
    yield loop
    loop.close()


@pytest.fixture(scope="session")
def postgres_url() -> str:
    from testcontainers.postgres import PostgresContainer

    container = PostgresContainer("postgres:17-alpine", driver="asyncpg")
    container.start()
    url = container.get_connection_url()
    # asyncpg driver suffix
    if not url.startswith("postgresql+asyncpg"):
        url = url.replace("postgresql://", "postgresql+asyncpg://")
    yield url
    container.stop()


@pytest_asyncio.fixture
async def engine(postgres_url: str) -> AsyncIterator[AsyncEngine]:
    # fresh database per test: create a schema-only DB, or just drop_all/create_all on a fresh engine.
    # Alembic runs DDL; we apply via the offline runner against our metadata for speed in tests.
    eng = create_async_engine(postgres_url, pool_pre_ping=True)
    async with eng.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
        await conn.run_sync(Base.metadata.create_all)
    yield eng
    async with eng.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await eng.dispose()


@pytest_asyncio.fixture
async def session_factory(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False, autoflush=False)


@pytest_asyncio.fixture
async def session(session_factory: async_sessionmaker[AsyncSession]) -> AsyncIterator[AsyncSession]:
    async with session_factory() as s:
        yield s
```

> This fixture pair is acceptable for repository roundtrips. The *migration-applied* path (Task 16 Step 2 test) applies Alembic `upgrade head` rather than `create_all`, to prove the revision is correct. Keep both fixtures; the migration test creates its own engine + applies alembic programmatically.

- [ ] **Step 2: Write the migration roundtrip test**

`apps/identity-service/tests/integration/test_migrations.py`:

```python
"""Verify the 0001_init revision applies cleanly via alembic upgrade head."""

from __future__ import annotations

import asyncio
import os
import sys
from pathlib import Path

import pytest
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from tests.integration.conftest import _docker_available  # reuse the skip guard

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


def _alembic_config_args(pg_url: str) -> list[str]:
    env = {**os.environ, "WORKER_DATABASE_URL": pg_url}
    return ["-c", "alembic", "upgrade", "head"], env


def test_alembic_upgrade_head_creates_tables(postgres_url: str) -> None:
    # run alembic as a subprocess in apps/identity-service so env.py's sys.path insert works
    apps_dir = Path(__file__).resolve().parents[2]  # apps/identity-service
    base = Path("apps/identity-service")
    args, env = _alembic_config_args(postgres_url)
    import subprocess

    r = subprocess.run(
        ["uv", "run", "alembic", "upgrade", "head"],
        cwd=str(base),
        env=env,
        capture_output=True,
        text=True,
    )
    assert r.returncode == 0, r.stderr + r.stdout

    async def _check() -> None:
        eng = create_async_engine(postgres_url)
        async with eng.connect() as conn:
            tables = (await conn.execute(text("SELECT tablename FROM pg_tables WHERE schemaname='public'"))).all()
            names = {row[0] for row in tables}
            assert {"users", "sessions", "audit_events"} <= names
        await eng.dispose()

    asyncio.run(_check())
```

> Adjust the `__file__`-relative path: the migration test runs `alembic upgrade head` as a subprocess in `apps/identity-service` with `WORKER_DATABASE_URL` set to the container URL. If `uv run alembic` inside the cwd is problematic under the test harness, run the alembic Python API directly (`from alembic.config import Config; from alembic import command; command.upgrade(cfg, "head")`) with `script_location=...` and `WORKER_DATABASE_URL` set — preferred for hermeticity. Use whichever passes in `make check`.

- [ ] **Step 3: Write the repository roundtrip test**

`apps/identity-service/tests/integration/test_repository_roundtrip.py`:

```python
from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash, TenantId
from identity_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemySessionRepository,
    SqlAlchemyUserRepository,
)
from identity_service.domain.audit import AuditAction, AuditEvent
from tests.integration.conftest import _docker_available

import pytest

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


async def test_user_repository_add_then_get_by_email(session) -> None:
    repo = SqlAlchemyUserRepository(session)
    tenant = uuid4()
    user = User.register(
        email=Email("repo@example.com"),
        password_hash=PasswordHash("$2b$12$x"),
        display_name="Repo",
        tenant_id=TenantId(tenant),
        now=datetime.now(UTC),
    )
    await repo.add(user)
    await session.commit()

    found = await repo.get_by_email(tenant, "REPO@example.com")  # CITEXT case-insensitive
    assert found is not None
    assert found.email == Email("repo@example.com")
    assert found.status is AccountStatus.ACTIVE


async def test_session_audit_repositories_roundtrip(session) -> None:
    tenant = uuid4()
    sess_repo = SqlAlchemySessionRepository(session)
    audit_repo = SqlAlchemyAuditRepository(session)

    await sess_repo.add(
        user_id=uuid4(), tenant_id=tenant, refresh_jti="jti-x", expires_at=datetime.now(UTC) + timedelta(days=1)
    )
    await audit_repo.append(
        AuditEvent(
            occurred_at=datetime.now(UTC),
            actor_id=None,
            tenant_id=tenant,
            action=AuditAction.LOGIN_FAILURE,
            target_id=None,
            correlation_id="c1",
            metadata={"reason": "unknown_user"},
        )
    )
    await session.commit()

    s = await sess_repo.get_by_jti("jti-x")
    assert s is not None and s.refresh_jti == "jti-x"
```

- [ ] **Step 4: Run integration tests (Docker must be up)**

Run: `uv run pytest apps/identity-service/tests/integration -v`
Expected: PASS (migrations test + 2 roundtrip tests) if Docker is running; SKIP (`reason="Docker not available"`) if not — both are valid for `make check` (skips don't fail). Confirm they actually ran green locally (Docker is up per §environment).

- [ ] **Step 5: Write ADR-0011**

`docs/adr/0011-integration-testcontainers-postgres.md`:
- **Context:** DoD requires "Tests für Domain + Integration (Testcontainers)". `testcontainers` was not installed (Phase-1 baseline); Docker 29.6.1 is available locally. sqlite is unsuitable (UUID/JSONB/ENUM/timezone PG features matter for tenancy + audit). The CLAUDE.md proxy note: do not assume a dep is installable — verify; `testcontainers[postgres]>=4` pins cleanly.
- **Decision:** `testcontainers[postgres]` added to root `[dependency-groups].dev` (shared across services). Integration tests live under `apps/<service>/tests/integration/`, module-scoped skip guard `skipif(not _docker_available(), reason="Docker not available (ADR-0011 offline-skip)")` so offline runs are green-equivalent (skips, not fails). Session-scoped PG container; per-test schema reset. Alembic `upgrade head` is exercised as a subprocess or alembic-python-API to prove the migration is correct (not just `create_all`).
- **Consequences:** CI must run Docker for the integration step (Sub-step 2.9); docs the GitHub-Actions services approach. Offline contributors see skips, not red. Other services adopt the same fixture pair.
- **Verification:** `uv run pytest apps/identity-service/tests/integration -v` runs 3 tests green when Docker is up.

- [ ] **Step 6: `make check` and commit**

Run: `make check` → green (integration tests pass-or-skip). Then:

```bash
git add apps/identity-service/tests/integration apps/identity-service/tests/__init__.py docs/adr/0011-integration-testcontainers-postgres.md
git commit -m "identity-service/integration: Testcontainers PG fixture + migration/repo roundtrip (ADR-0011)"
```

---

## Sub-step 2.5 — Application commands + HTTP endpoints + auth middleware

**Sub-step goal:** Wire the CQRS application layer (`RegisterUser`/`AuthenticateUser`/`RefreshToken`/`RevokeToken` command handlers running inside a UoW) and the HTTP surface (`/auth/register`, `/auth/login`, `/auth/refresh`, `/auth/logout`, `/me`) plus the JWT auth middleware that sets `request.state.user`. The tenant must come from the JWT claim for `/me` (and later for all protected routes), but this sub-step still uses the platform's default resolver for the tenant contextvar — the *consolidation* of `worker-tenancy` + `request.state.user`→contextvar is Sub-step 2.6. A mini `PasswordPolicy` lives in the domain (since `worker-security` is headers-middleware-only).

### Task 17: Application commands (handlers inside UoW) + PasswordPolicy + command unit tests

**Files:**
- Create: `apps/identity-service/src/identity_service/application/commands.py`
- Create: `apps/identity-service/src/identity_service/domain/password_policy.py`
- Create: `apps/identity-service/src/identity_service/application/mediator.py`
- Test: `apps/identity-service/tests/unit/test_commands.py`

**Interfaces:**
- Produces: `PasswordPolicy` (`validate(plain: str) -> None`, raises `WeakPassword` for <12 chars or empty — Phase-2 minimum; reject >72 bytes early since bcrypt does anyway).
- Produces: commands `RegisterUserCommand(email, password, display_name, tenant_id)`, `AuthenticateUserCommand(email, password, tenant_id)`, `RefreshTokenCommand(refresh_token)`, `RevokeTokenCommand(refresh_token)`; each with a handler `handle(*, deps, uow) -> Result`.
- Deps bundle passed to handlers: `{"hasher", "tokens", "clock", "eventbus", "settings"}`.
- Login failure path: every failed login persists an `AuditEvent(LOGIN_FAILURE)` (actor_id `None` when user unknown; `reason` from `{"unknown_user"|"bad_password"|"disabled"}`); then raises `InvalidCredentials`. Success persists `Session(refresh_jti)` + `AuditEvent(LOGIN_SUCCESS)` + `UserLoggedIn` domain event; returns `TokenPair`. **All within the same UoW transaction** — atomicity.

- [ ] **Step 1: Write `domain/password_policy.py`**

```python
"""Mini password policy (Phase 2). worker-security is headers-middleware-only, so the
readable-policy floor lives in the domain until a shared policy module exists."""

from __future__ import annotations

from worker_core import DomainError

__all__ = ["PasswordPolicy", "WeakPassword"]

_MAX_BYTES = 72
_MIN_CHARS = 12


class WeakPassword(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__("weak_password", f"Password rejected: {reason}")


class PasswordPolicy:
    def validate(self, plain: str) -> None:
        if not plain:
            raise WeakPassword("must not be empty")
        if len(plain) < _MIN_CHARS:
            raise WeakPassword(f"must be at least {_MIN_CHARS} characters")
        if len(plain.encode("utf-8")) > _MAX_BYTES:
            raise WeakPassword("exceeds 72 bytes")
```

- [ ] **Step 2: Write `application/commands.py`**

```python
"""Authentication CQRS commands + handlers (run inside a UoW)."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from identity_service.application.ports import TokenPair
from identity_service.domain.audit import AuditAction, AuditEvent
from identity_service.domain.password_policy import PasswordPolicy
from identity_service.domain.user import (
    AccountDisabled,
    InvalidCredentials,
    User,
    UserAlreadyExists,
)
from identity_service.domain.value_objects import Email, TenantId

__all__ = [
    "AuthenticateUserCommand",
    "RefreshTokenCommand",
    "RegisterUserCommand",
    "RevokeTokenCommand",
]


def _correlation_id() -> str | None:
    from worker_platform.context import get_correlation_id  # local import: keep domain dep-free

    return get_correlation_id()


@dataclass(frozen=True, slots=True)
class RegisterUserCommand:
    email: str
    password: str
    display_name: str
    tenant_id: UUID


async def handle_register(cmd: RegisterUserCommand, *, deps: dict[str, Any], repos: dict[str, Any]) -> Result[User]:
    hasher = deps["hasher"]
    policy: PasswordPolicy = PasswordPolicy()
    try:
        policy.validate(cmd.password)
        existing = await repos["users"].get_by_email(cmd.tenant_id, cmd.email)
        if existing is not None:
            # Audit the failed attempt too, but keep the 409 semantics in the router.
            raise UserAlreadyExists(cmd.email)
        user = User.register(
            email=Email(cmd.email),
            password_hash=hasher.hash(cmd.password),
            display_name=cmd.display_name,
            tenant_id=TenantId(cmd.tenant_id),
            now=deps["clock"].now(),
        )
        await repos["users"].add(user)
        await repos["audit"].append(
            AuditEvent(
                occurred_at=deps["clock"].now(),
                actor_id=user.id.value,
                tenant_id=user.tenant_id.value,
                action=AuditAction.REGISTER,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    for ev in user.pull_events():
        await deps["eventbus"].publish(ev)
    return Result.ok(user)


@dataclass(frozen=True, slots=True)
class AuthenticateUserCommand:
    email: str
    password: str
    tenant_id: UUID


async def handle_login(
    cmd: AuthenticateUserCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[TokenPair]:
    hasher = deps["hasher"]
    tokens = deps["tokens"]
    clock = deps["clock"]
    now = clock.now()

    async def _audit_failure(reason: str, *, actor_id: UUID | None) -> None:
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=actor_id,
                tenant_id=cmd.tenant_id,
                action=AuditAction.LOGIN_FAILURE,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={"reason": reason},
            )
        )

    try:
        user = await repos["users"].get_by_email(cmd.tenant_id, cmd.email)
        if user is None:
            await _audit_failure("unknown_user", actor_id=None)
            raise InvalidCredentials()
        if not user.verify_password(cmd.password, hasher):
            await _audit_failure("bad_password", actor_id=user.id.value)
            raise InvalidCredentials()
        try:
            user.assert_can_log_in()
        except AccountDisabled:
            await _audit_failure("disabled", actor_id=user.id.value)
            raise InvalidCredentials() from None  # map to generic 401, keep reason in audit only

        import secrets

        jti = secrets.token_urlsafe(16)
        user.record_login(jti=jti, now=now)
        await repos["sessions"].add(
            user_id=user.id.value,
            tenant_id=user.tenant_id.value,
            refresh_jti=jti,
            expires_at=now + timedelta(minutes=deps["settings"].jwt_refresh_token_expire_minutes),
        )
        pair = tokens.issue_pair(
            user_id=user.id.value,
            tenant_id=user.tenant_id.value,
            roles=list(user.roles),
            permissions=[],
            session_jti=jti,
        )
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=user.id.value,
                tenant_id=user.tenant_id.value,
                action=AuditAction.LOGIN_SUCCESS,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
        for ev in user.pull_events():
            await deps["eventbus"].publish(ev)
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(pair)


@dataclass(frozen=True, slots=True)
class RefreshTokenCommand:
    refresh_token: str


async def handle_refresh(
    cmd: RefreshTokenCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[TokenPair]:
    tokens = deps["tokens"]
    clock = deps["clock"]
    from worker_auth import InvalidToken

    try:
        principal = tokens.verify_refresh_token(cmd.refresh_token)
    except (InvalidToken, Exception):
        return Result.fail(InvalidCredentials())  # generic; reason not audited to avoid enumeration signal
    row = await repos["sessions"].get_by_jti(cmd.refresh_token)  # jti lookup by token id/hash
    # NOTE: refresh_jti is the *jti* issued at login. The session row stores the jti, not the token.
    # To look up, derive the jti from the verified token — done by re-decoding to read jti:
    from worker_auth import TokenManager as _TM  # to read the jti claim

    # Simpler: verify_refresh_token returns a principal; we need the jti claim too.
    # Extend AuthPrincipal to carry jti in Task 13's JwTokenService (see note below).
    if row is None or row.revoked_at is not None or row.expires_at <= clock.now():
        return Result.fail(InvalidCredentials())
    # rotate: revoke old, mint new
    now = clock.now()
    await repos["sessions"].revoke(row.refresh_jti, now)
    import secrets

    new_jti = secrets.token_urlsafe(16)
    await repos["sessions"].add(
        user_id=principal.user_id,
        tenant_id=principal.tenant_id,
        refresh_jti=new_jti,
        expires_at=now + timedelta(minutes=deps["settings"].jwt_refresh_token_expire_minutes),
    )
    pair = tokens.issue_pair(
        user_id=principal.user_id,
        tenant_id=principal.tenant_id,
        roles=[] if not principal.roles else list(principal.roles),
        permissions=[],
        session_jti=new_jti,
    )
    await repos["audit"].append(
        AuditEvent(
            occurred_at=now,
            actor_id=principal.user_id,
            tenant_id=principal.tenant_id,
            action=AuditAction.TOKEN_REFRESH,
            target_id=None,
            correlation_id=_correlation_id(),
            metadata={"reason": "rotation"},
        )
    )
    return Result.ok(pair)
```

> **Refinement required before commit (capture in code, not as a TODO):** the refresh handler must read the `jti` from the verified token. Extend `AuthPrincipal` to carry `jti: str` (Task 13 `JwTokenService._verify` already has the decoded `TokenPayload` — set `jti=payload.jti`). Then `handle_refresh` uses `principal.jti` for `get_by_jti(principal.jti)` (the session table is keyed by jti, not by token). Drop the broken `TokenManager` re-import + `get_by_jti(cmd.refresh_token)` lines. The plan states this explicitly because it is a real correctness fix, not optional polish.

- [ ] **Step 3: Write `application/mediator.py`**

```python
"""Command dispatcher: wraps a handler in a UoW + audit publish. Slim; the
worker_platform.application.cqrs.Mediator is available but Phase-2 commands
own their transaction surface here for clarity (ADR-0003 explicit registration)."""

from __future__ import annotations

from typing import Any

from worker_core import Result


class CommandMediator:
    def __init__(self, deps: dict[str, Any], request_scope) -> None:
        self._deps = deps
        self._request_scope = request_scope

    async def run(self, handler, repos_dict_factory) -> None:
        ...
        # Minimal: per command we call the handler explicitly in the router with a
        # freshly-scoped UoW. The router does: async with request_scope(session_factory) as (uow, repos): await handler(cmd, deps=deps, repos=repos)
```

> The router (Task 18) wires the request scope explicitly; `mediator.py` is kept thin/importable but the router drives the UoW. If you prefer the platform CQRS `Mediator`, register the four handlers there — but the per-request UoW binding (open the session, commit on success) is router-controlled. Pick the explicit router-driven form; delete the placeholder `run` above.

- [ ] **Step 4: Write command unit tests (fake repos)**

`apps/identity-service/tests/unit/test_commands.py`:

```python
from datetime import UTC, datetime, timedelta
from uuid import UUID, uuid4

import pytest

from identity_service.application.commands import (
    AuthenticateUserCommand,
    RegisterUserCommand,
)
from identity_service.domain.user import InvalidCredentials, UserAlreadyExists
from identity_service.domain.value_objects import PasswordHash
from identity_service.infrastructure.auth.hasher import BcryptPasswordAdapter


class _FakeUsers:
    def __init__(self) -> None:
        self.by_email: dict[tuple[UUID, str], object] = {}

    async def get_by_email(self, tenant_id, email):
        return self.by_email.get((tenant_id, email.lower()))

    async def add(self, user):
        self.by_email[(user.tenant_id.value, user.email.value)] = user


class _FakeSessions:
    def __init__(self):
        self.rows: dict[str, dict] = {}

    async def add(self, *, user_id, tenant_id, refresh_jti, expires_at):
        self.rows[refresh_jti] = {"user_id": user_id, "tenant_id": tenant_id, "expires_at": expires_at, "revoked_at": None}

    async def get_by_jti(self, jti):
        return self.rows.get(jti)

    async def revoke(self, jti, revoked_at):
        if jti in self.rows:
            self.rows[jti]["revoked_at"] = revoked_at


class _FakeAudit:
    def __init__(self):
        self.events: list = []

    async def append(self, event):
        self.events.append(event)


class _StupidHasher:
    def hash(self, plain):
        return PasswordHash("h$" + plain)

    def verify(self, plain, hashed):
        return hashed.value == "h$" + plain


def _deps(hasher):
    class _S:
        jwt_refresh_token_expire_minutes = 60

    return {"hasher": hasher, "tokens": None, "clock": _Clock(), "eventbus": _Bus(), "settings": _S()}


class _Clock:
    def now(self):
        return datetime(2026, 7, 16, 12, 0, 0, tzinfo=UTC) + timedelta(seconds=0)


class _Bus:
    async def publish(self, ev):
        pass


def test_register_creates_user_and_audit() -> None:
    hasher = _StupidHasher()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    cmd = RegisterUserCommand(email="a@b.com", password="strongpassword1", display_name="A", tenant_id=tenant)
    import asyncio

    res = asyncio.get_event_loop().run_until_complete(handle_register_local(cmd, hasher, repos, _deps(hasher)))
    assert res.is_success
    assert len(repos["audit"].events) == 1


# helper local because the handler signature requires the deps dict + repos split
async def handle_register_local(cmd, hasher, repos, deps):
    from identity_service.application.commands import handle_register

    return await handle_register(cmd, deps=deps, repos=repos)
```

> This test sketches fake-repo unit testing. The exact asyncio execution (`asyncio.get_event_loop()` is deprecated) — use `asyncio.run(...)` in a sync test, or mark the test async (pytest-asyncio auto-mode). Write the test **async** (no `run_until_complete`): `async def test_register_creates_user_and_audit(): res = await handle_register(...)`.

- [ ] **Step 5: Run, fix `AuthPrincipal.jti`, run again**

Run: `uv run pytest apps/identity-service/tests/unit/test_commands.py -v`
Expected: PASS after extending `AuthPrincipal` with `jti` and `JwTokenService._verify` to populate it. Fix any mypy issues.

- [ ] **Step 6: `make check` and commit**

```bash
git add apps/identity-service/src/identity_service/domain/password_policy.py apps/identity-service/src/identity_service/application/commands.py apps/identity-service/src/identity_service/application/mediator.py apps/identity-service/tests/unit/test_commands.py apps/identity-service/src/identity_service/application/ports.py apps/identity-service/src/identity_service/infrastructure/auth/jwt_service.py
git commit -m "identity-service/app: register/login/refresh commands, PasswordPolicy, fake-repo tests"
```

### Task 18: HTTP router + auth middleware + compose_api + integration endpoint tests

**Files:**
- Create: `apps/identity-service/src/identity_service/presentation/__init__.py`
- Create: `apps/identity-service/src/identity_service/presentation/http/__init__.py`
- Create: `apps/identity-service/src/identity_service/presentation/http/router.py`
- Create: `apps/identity-service/src/identity_service/presentation/auth_middleware.py`
- Create: `apps/identity-service/src/identity_service/presentation/compose_api.py`
- Modify: `apps/identity-service/src/identity_service/main.py`
- Test: `apps/identity-service/tests/integration/test_auth_endpoints.py`

**Interfaces:**
- Consumes: Sub-step 2.6's `create_api_app(..., *, tenant_resolver, auth_middleware, routers)` *will* add the compose hook, but **for 2.5** we build the identity app via `compose_api.build_app` that *also* uses the platform factory for health/security/correlation and then adds the auth router + auth middleware directly (so 2.5 works without the kernel hook). Sub-step 2.6 formalizes the kernel hook and swaps the tenant resolver to claim-based. For 2.5 the tenant contextvar stays `NoTenantResolver` (header-gated off in prod).
- Produces: `build_app(settings: IdentityServiceSettings) -> FastAPI`.
- Produces: `AuthMiddleware` (ASGI, sets `request.state.user = AuthPrincipal | None`).
- Produces: `auth_router` with the four endpoints.

- [ ] **Step 1: Write `presentation/http/router.py`**

```python
"""HTTP endpoints for authentication."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, Cookie, HTTPException, Request, status
from pydantic import BaseModel
from worker_database import UnitOfWork

from identity_service.application.commands import (
    AuthenticateUserCommand,
    RefreshTokenCommand,
    RegisterUserCommand,
    RevokeTokenCommand,
    handle_login,
    handle_refresh,
    handle_register,
)
from identity_service.application.ports import TokenPair
from identity_service.domain.user import UserAlreadyExists

auth_router = APIRouter(prefix="/auth", tags=["auth"])


class RegisterBody(BaseModel):
    email: str
    password: str
    display_name: str
    tenant_id: UUID


class LoginBody(BaseModel):
    email: str
    password: str
    tenant_id: UUID


def _set_cookies(response, pair: TokenPair) -> None:
    response.set_cookie("access", pair.access, httponly=True, samesite="strict", secure=request_for_secure())
    response.set_cookie("refresh", pair.refresh, httponly=True, samesite="strict", secure=request_for_secure(), path="/auth")
```

> `request_for_secure()` is a placeholder — replace with `settings.environment is Environment.PRODUCTION` (pass settings into the router factory). Refactor: make `auth_router` a function `build_auth_router(deps)` that closes over `settings` so `secure=` is determined cleanly.

Resume the router (after restructuring as `build_auth_router(deps)` returning `APIRouter`):

```python
def build_auth_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(prefix="/auth", tags=["auth"])
    settings = deps["settings"]
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _secure() -> bool:
        from worker_platform.configuration import Environment

        return settings.environment is Environment.PRODUCTION

    @router.post("/register", status_code=status.HTTP_201_CREATED)
    async def register(body: RegisterBody) -> dict[str, str]:
        cmd = RegisterUserCommand(body.email, body.password, body.display_name, body.tenant_id)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_register(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err.__cause__ if err else None, UserAlreadyExists) or isinstance(err, UserAlreadyExists):
                raise HTTPException(status.HTTP_409_CONFLICT, "user already exists")
            raise HTTPException(status.HTTP_400_BAD_REQUEST, err.message if err else "invalid")
        return {"status": "registered"}

    @router.post("/login")
    async def login(body: LoginBody) -> dict[str, str]:
        cmd = AuthenticateUserCommand(body.email, body.password, body.tenant_id)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_login(cmd, deps=deps, repos=repos)
        if not result.is_success:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid credentials")
        pair = result.value
        from fastapi import Response  # local

        resp = _cookies_pair(pair, _secure())
        return resp  # see helper below
    ...
    return router
```

> This is illustrative of the shape; the committed file must be complete and self-consistent (no `...`, no `request_for_secure` placeholder, no `_cookies_pair` undefined). Write each endpoint fully: `/login` sets two cookies and returns `{"status":"ok"}`; `/refresh` reads the `refresh` cookie, calls `handle_refresh`, rotates; `/logout` revokes the session jti; `/me` reads `request.state.user` (set by the auth middleware — `request.state.user.tenant_id`) and returns `{user_id, tenant_id, roles}`. Map domain errors to HTTP statuses as specified (§7 of the spec): 409 for `UserAlreadyExists`, 401 generic for `InvalidCredentials`/`AccountDisabled`.

- [ ] **Step 2: Write `presentation/auth_middleware.py`**

```python
"""JWT auth middleware — sets request.state.user = AuthPrincipal | None."""

from __future__ import annotations

from collections.abc import Awaitable, Callable
from typing import Any

from starlette.types import ASGIApp, Message, Receive, Scope, Send

from identity_service.application.ports import AuthPrincipal


def build_auth_middleware(tokens: Any) -> type:
    class AuthMiddleware:
        def __init__(self, app: ASGIApp) -> None:
            self.app = app

        async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
            if scope["type"] != "http":
                await self.app(scope, receive, send)
                return
            principal = _extract_principal(scope, tokens)
            scope.setdefault("state", {})
            scope["state"]["user"] = principal
            await self.app(scope, receive, send)

    return AuthMiddleware


def get_request_user(scope: Scope) -> AuthPrincipal | None:
    state = scope.get("state") or {}
    return state.get("user")


def _extract_principal(scope: Scope, tokens: Any) -> AuthPrincipal | None:
    headers = scope.get("headers") or ()
    auth = None
    for name, value in headers:
        if name == b"authorization":
            auth = value.decode("latin-1")
            break
    if auth is None or not auth.lower().startswith("bearer "):
        return None
    token = auth[7:].strip()
    try:
        return tokens.verify_access_token(token)
    except Exception:
        return None
```

> The handler reads the `Authorization` header from the raw ASGI scope; on any failure, sets `user = None` (endpoints decide). `request.state.user` in Starlette maps from `scope["state"]["user"]`.

- [ ] **Step 3: Write `presentation/compose_api.py`**

```python
"""Compose the identity-service FastAPI app."""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from sqlalchemy.ext.asyncio import create_async_engine

from worker_platform.presentation.app import create_api_app  # used for health/security/correlation shell

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.compose import compose_infrastructure
from identity_service.presentation.auth_middleware import build_auth_middleware
from identity_service.presentation.http.router import build_auth_router

# /me router (reads request.state.user) is built here too.
from identity_service.presentation.http.me_router import build_me_router


def build_app(settings: IdentityServiceSettings) -> FastAPI:
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    app = create_api_app(settings)  # platform shell: health, security headers, correlation, problem errors
    app.include_router(build_auth_router(deps))
    app.include_router(build_me_router(deps))
    AuthMiddleware = build_auth_middleware(deps["tokens"])
    app.add_middleware(AuthMiddleware)
    return app
```

> `build_me_router(deps)` lives in `presentation/http/me_router.py` — one endpoint `GET /me` reading `request.state.user` via the `get_request_user` helper; return `{user_id, tenant_id, roles}`; 401 if no user. Write it as a sibling of `router.py`.

- [ ] **Step 4: Update `main.py`**

Replace the `create_app` body to call `build_app`:

```python
from __future__ import annotations

import uvicorn

from identity_service.configuration import IdentityServiceSettings
from identity_service.presentation.compose_api import build_app


def create_app(settings: IdentityServiceSettings | None = None) -> FastAPI:
    return build_app(settings or IdentityServiceSettings())


app = create_app()


def run() -> None:
    settings = IdentityServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
```

- [ ] **Step 5: Update the existing smoke test**

`apps/identity-service/tests/test_smoke_identity_service.py` stays (it tests `/health`); the app now boots with a DB engine, so the smoke test must not require a live DB — keep it hitting `/health/live`. If `create_app()` constructs an engine eagerly on import, the smoke import will fail without a DB URL. **Fix:** make `build_app` construct the engine lazily on first request, OR set `WORKER_DATABASE_URL` env override in the smoke test `conftest` to an in-memory-only-for-import sentinel that's never connected (the engine is lazy — `create_async_engine` is lazy). Keep `create_async_engine` lazy (it does not connect), so the smoke import is fine. Ensure the smoke test still passes:

Run: `uv run pytest apps/identity-service/tests/test_smoke_identity_service.py -v` → PASS.

- [ ] **Step 6: Write integration endpoint tests**

`apps/identity-service/tests/integration/test_auth_endpoints.py`:

```python
from __future__ import annotations

from httpx import ASGITransport, AsyncClient

from tests.integration.conftest import _docker_available, postgres_url

import pytest

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


async def test_register_login_me_refresh_logout_roundtrip(postgres_url: str) -> None:
    # Build the app with this test's DB url via env override.
    import os

    os.environ["WORKER_DATABASE_URL"] = postgres_url
    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    settings = IdentityServiceSettings()
    # apply migrations to this container db
    import subprocess
    from pathlib import Path

    r = subprocess.run(["uv", "run", "alembic", "upgrade", "head"], cwd="apps/identity-service", env=os.environ, capture_output=True, text=True)
    assert r.returncode == 0, r.stderr + r.stdout

    import secrets

    settings.jwt_secret = settings.jwt_secret  # keep default dev secret (tests)
    os.environ["WORKER_JWT_SECRET"] = "a" * 40
    settings = IdentityServiceSettings()  # re-read with new secret
    app = build_app(settings)
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        tenant = "11111111-1111-1111-1111-111111111111"
        reg = await client.post("/auth/register", json={"email": "e@x.com", "password": "strongpassword1", "display_name": "E", "tenant_id": tenant})
        assert reg.status_code == 201, reg.text
        login = await client.post("/auth/login", json={"email": "e@x.com", "password": "strongpassword1", "tenant_id": tenant})
        assert login.status_code == 200, login.text
        access = login.cookies.get("access")
        assert access
        me = await client.get("/me", headers={"Authorization": f"Bearer {access}"})
        assert me.status_code == 200
        body = me.json()
        assert body["tenant_id"] == tenant
        refresh = login.cookies.get("refresh")
        assert refresh
        rf = await client.post("/auth/refresh", cookies={"refresh": refresh})
        assert rf.status_code == 200, rf.text
        lo = await client.post("/auth/logout", cookies={"refresh": rf.cookies.get("refresh", refresh)})
        assert lo.status_code in (200, 204)
```

> This E2E test asserts the DoD: register → login → `/me` returns the claim's `tenant_id` → refresh rotates → logout revokes. Refine cookie handling (httpx `AsyncClient` cookie jar across requests) per httpx docs; the `.cookies.get("refresh")` access may need `client`-level stateful cookies. Keep the assertions tight.

- [ ] **Step 7: Run integration tests; `make check`; commit**

Run: `uv run pytest apps/identity-service/tests/integration -v` (Docker up) → PASS.
Run: `make check` → green.

```bash
git add apps/identity-service/src/identity_service/presentation apps/identity-service/src/identity_service/main.py apps/identity-service/tests/integration/test_auth_endpoints.py apps/identity-service/tests/test_smoke_identity_service.py
git commit -m "identity-service/http: /auth/{register,login,refresh,logout}, /me, JWT auth middleware, E2E tests"
```

---

## Sub-step 2.6 — Tenant consolidation (ADR-0009) + `create_api_app` compose-hook

**Sub-step goal:** Remove the tenant-context dualism (ADR-0005 follow-up): `worker-tenancy` becomes a thin re-export of the platform canon and defines a *scope-based* `ClaimTenantResolver` that reads `request.state.user`. The platform `create_api_app` gains a compose-hook (`tenant_resolver`, `auth_middleware` optional) so the identity-service wires the claim-based resolver as the *production* tenant source (header resolver remains dev/test-only). ADR-0009 records it. An integration test asserts the hard constraint: with `allow_development_tenant_header=False`, an `X-Tenant-ID` header is ignored and the tenant comes only from the JWT claim.

### Task 19: Extend `worker-platform` `create_api_app` with a compose-hook

**Files:**
- Modify: `packages/worker-platform/src/worker_platform/presentation/app.py:23-58`

**Interfaces:**
- Changes: `create_api_app(settings: PlatformSettings, *, readiness_checks=(), tenant_resolver: TenantResolver | None = None, auth_middleware: type[ASGIMiddleware] | None = None, routers: Iterable[APIRouter] = ())`. When `tenant_resolver` is `None`, behavior is unchanged (current `NoTenantResolver`/`DevelopmentHeaderTenantResolver` logic). When supplied, it *overrides* the default. `auth_middleware` is added innermost-before-routes (Starlette outer-last → added before `TenantContextMiddleware`/`SecurityHeaders` so it runs *inside* them, i.e. after correlation/tenant/security in the call chain). `routers` are `include_router`-ed.

- [ ] **Step 1: Write a failing platform test**

`packages/worker-platform/tests/test_app_compose_hook.py`:

```python
from fastapi import APIRouter
from fastapi.testclient import TestClient

from worker_platform.configuration import Environment, PlatformSettings
from worker_platform.presentation.app import create_api_app
from worker_platform.presentation.middleware import TenantResolver


class _FixedResolver:
    def resolve(self, scope) -> str | None:
        return "from-claim"


def test_create_api_app_accepts_tenant_resolver_and_routers() -> None:
    settings = PlatformSettings(environment=Environment.TEST)
    r = APIRouter()

    @r.get("/probe")
    def probe() -> dict[str, str]:
        from worker_platform.context import get_tenant_id

        return {"tenant": get_tenant_id() or "none"}

    app = create_api_app(settings, tenant_resolver=_FixedResolver(), routers=(r,))
    client = TestClient(app)
    resp = client.get("/probe")
    assert resp.status_code == 200
    assert resp.json()["tenant"] == "from-claim"


def test_create_api_app_default_resolver_unchanged_when_none() -> None:
    settings = PlatformSettings(environment=Environment.PRODUCTION, allow_development_tenant_header=False)
    app = create_api_app(settings)
    client = TestClient(app)
    # /health/live exists; /me does not; no router added — smoke only
    resp = client.get("/health/live")
    assert resp.status_code in (200, 404)
```

- [ ] **Step 2: Run to verify fail**

Run: `uv run pytest packages/worker-platform/tests/test_app_compose_hook.py -v`
Expected: FAIL — `create_api_app()` does not accept `tenant_resolver`/`routers` kwargs (`TypeError: unexpected keyword argument 'tenant_resolver'`).

- [ ] **Step 3: Modify `app.py`**

```python
from __future__ import annotations

from collections.abc import Iterable
from typing import Any

from fastapi import APIRouter, FastAPI

from worker_platform.configuration import Environment, PlatformSettings
from worker_platform.logging import configure_logging
from worker_platform.presentation.errors import register_exception_handlers
from worker_platform.presentation.health import ReadinessCheck, create_health_router
from worker_platform.presentation.middleware import (
    CorrelationIdMiddleware,
    DevelopmentHeaderTenantResolver,
    NoTenantResolver,
    SecurityHeadersMiddleware,
    TenantContextMiddleware,
    TenantResolver,
)


def create_api_app(
    settings: PlatformSettings,
    *,
    readiness_checks: Iterable[ReadinessCheck] = (),
    tenant_resolver: TenantResolver | None = None,
    auth_middleware: Any | None = None,
    routers: Iterable[APIRouter] = (),
) -> FastAPI:
    """Create a secure, observable HTTP entry point. No business endpoints by default;
    services register their routers + auth middleware via the compose-hook kwargs."""

    configure_logging()
    docs_url = "/docs" if settings.enable_docs else None
    openapi_url = "/openapi.json" if settings.enable_docs else None
    app = FastAPI(
        title=settings.service_name,
        version=settings.service_version,
        docs_url=docs_url,
        redoc_url=None,
        openapi_url=openapi_url,
    )
    register_exception_handlers(app)
    app.include_router(create_health_router(settings.service_name, readiness_checks))
    for router in routers:
        app.include_router(router)

    if tenant_resolver is not None:
        resolved_tenant = tenant_resolver
    elif settings.allow_development_tenant_header and settings.environment in {
        Environment.LOCAL,
        Environment.DEVELOPMENT,
        Environment.TEST,
    }:
        resolved_tenant = DevelopmentHeaderTenantResolver(enabled=True)
    else:
        resolved_tenant = NoTenantResolver()

    # last added = outermost. Order chosen: outermost CorrelationId → TenantContext → SecurityHeaders innermost.
    if auth_middleware is not None:
        app.add_middleware(auth_middleware)
    app.add_middleware(
        SecurityHeadersMiddleware,
        enforce_https=settings.environment is Environment.PRODUCTION,
    )
    app.add_middleware(TenantContextMiddleware, resolver=resolved_tenant)
    app.add_middleware(CorrelationIdMiddleware)
    return app
```

> Auth middleware added *before* `TenantContextMiddleware` (Starlette: earlier add = inner). So in the request path the order is CorrelationId (outer) → TenantContext → SecurityHeaders → AuthMiddleware (inner) → route. That places auth *after* tenant-context set, which is wrong for the claim resolver (it needs `request.state.user`). **Correction:** the auth middleware must run *outside* the tenant-context middleware so it can set `state.user` first. Add auth middleware *after* `TenantContextMiddleware` (so it is outer). Reorder the block so the final add order (outer→inner) is: `CorrelationId` (outermost), `AuthMiddleware`, `TenantContextMiddleware`, `SecurityHeaders` (innermost). That means in code (Starlette last-added-is-outer): add `SecurityHeaders` first, then `TenantContextMiddleware`, then `auth_middleware`, then `CorrelationId`. Write it in that order:

```python
    app.add_middleware(
        SecurityHeadersMiddleware,
        enforce_https=settings.environment is Environment.PRODUCTION,
    )
    app.add_middleware(TenantContextMiddleware, resolver=resolved_tenant)
    if auth_middleware is not None:
        app.add_middleware(auth_middleware)
    app.add_middleware(CorrelationIdMiddleware)
```

- [ ] **Step 4: Run to verify pass**

Run: `uv run pytest packages/worker-platform/tests/test_app_compose_hook.py -v`
Expected: PASS (2 passed).

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add packages/worker-platform/src/worker_platform/presentation/app.py packages/worker-platform/tests/test_app_compose_hook.py
git commit -m "worker-platform: create_api_app compose-hook (tenant_resolver, auth_middleware, routers)"
```

### Task 20: Consolidate `worker-tenancy` → re-export + scope-based `ClaimTenantResolver` + tests

**Files:**
- Modify: `packages/worker-tenancy/src/worker_tenancy/__init__.py` (whole-file rewrite)
- Modify: `packages/worker-middleware/src/worker_middleware/__init__.py` (consumers of `NoTenantResolver`/`TenantResolver` — confirm they keep working; the platform scope-based `TenantResolver` Protocol has the same `resolve(scope)->str|None` so `NoTenantResolver` from the platform satisfies it. **Preferred direction:** have `worker-tenancy` re-export the platform's `NoTenantResolver`/`TenantResolver` rather than keep duplicate classes.)
- Modify: `packages/worker-tenancy/tests/test_smoke_worker_tenancy.py` (update to assert re-exports + claim resolver)
- Test: `packages/worker-tenancy/tests/test_claim_resolver.py`

**Interfaces:**
- Produces: `ClaimTenantResolver` (scope-based, reads `scope["state"]["user"]`, returns `str(user.tenant_id)` or `None`); re-exports `NoTenantResolver`, `TenantResolver`, `DevelopmentHeaderTenantResolver`, `get_tenant_id`, `tenant_context` from `worker_platform.context`/`worker_platform.presentation.middleware`.
- Removes: the `_tenant_id: ContextVar[UUID|None]`, `_tenant_context` dict contextvar, the `resolve(request: Request)->UUID|None` signatures, the `HeaderTenantResolver`/`SubdomainTenantResolver` UUID-returning variants (the platform `DevelopmentHeaderTenantResolver` supersedes the header one for local/test; `SubdomainTenantResolver` stays as a stub returning `None` but scope-based signature).
- The `worker-middleware` consumer keeps working because it imports `NoTenantResolver, TenantResolver` — pointing them at the platform re-exports (which satisfy the platform `TenantResolver` Protocol).

- [ ] **Step 1: Write failing tests**

`packages/worker-tenancy/tests/test_claim_resolver.py`:

```python
from types import SimpleNamespace
from uuid import UUID

from worker_tenancy import ClaimTenantResolver


def _scope_with_user(user):
    return {"type": "http", "state": {"user": user}}


def test_claim_resolver_reads_tenant_from_user_state() -> None:
    principal = SimpleNamespace(tenant_id=UUID("22222222-2222-2222-2222-222222222222"))
    resolver = ClaimTenantResolver()
    assert resolver.resolve(_scope_with_user(principal)) == "22222222-2222-2222-2222-222222222222"


def test_claim_resolver_returns_none_when_no_user() -> None:
    resolver = ClaimTenantResolver()
    assert resolver.resolve({"type": "http", "state": {}}) is None


def test_claim_resolver_returns_none_when_user_none() -> None:
    resolver = ClaimTenantResolver()
    assert resolver.resolve(_scope_with_user(None)) is None


def test_reexports_match_platform_canon_identity() -> None:
    import worker_platform.context as canon
    import worker_tenancy

    assert worker_tenancy.get_tenant_id is canon.get_tenant_id
    assert worker_tenancy.tenant_context is canon.tenant_context
```

- [ ] **Step 2: Run to verify fail**

Run: `uv run pytest packages/worker-tenancy/tests/test_claim_resolver.py -v`
Expected: FAIL — `worker_tenancy.ClaimTenantResolver` exists but its `resolve` takes `Request`, not `scope` (signature mismatch → `resolve(scope)` returns a coroutine or fails `TypeError`).

- [ ] **Step 3: Rewrite `worker-tenancy/__init__.py`**

```python
"""Multi-tenancy: thin re-export of the platform tenant-context canon (ADR-0009).

The canonical tenant contextvar lives in worker_platform.context (str-form of the
UUID). This package keeps a Dünnschicht-reexport for consumers and adds the
ClaimTenantResolver, which derives the tenant id from request.state.user (set by
the identity-service auth middleware) — the production tenant source."""

from __future__ import annotations

from starlette.types import Scope

from worker_platform.context import get_tenant_id, tenant_context

__all__ = [
    "ClaimTenantResolver",
    "DevelopmentHeaderTenantResolver",
    "NoTenantResolver",
    "TenantResolver",
    "get_tenant_id",
    "tenant_context",
]

# Platform canon re-exports (identity-checked in tests).
from worker_platform.presentation.middleware import (  # noqa: F401  (re-export)
    DevelopmentHeaderTenantResolver,
    NoTenantResolver,
    TenantResolver,
)


class ClaimTenantResolver:
    """Production tenant source: read tenant_id from the authenticated principal
    attached at request.state.user by the auth middleware. Returns the str-form
    of the UUID for the platform str-typed contextvar."""

    def __init__(self, claim_attr: str = "tenant_id") -> None:
        self.claim_attr = claim_attr

    def resolve(self, scope: Scope) -> str | None:
        state = scope.get("state") or {}
        user = state.get("user")
        if user is None:
            return None
        value = getattr(user, self.claim_attr, None)
        if value is None:
            return None
        return str(value)
```

> `HeaderTenantResolver`, `SubdomainTenantResolver`, `set_tenant_id`, `get_tenant_context`, `set_tenant_context` are **removed**. Audit who imports them before deleting: only the smoke test references the UUID-typed helpers; the consumer `worker-middleware` imports `NoTenantResolver, TenantResolver` (both re-exported). Update `worker-middleware` smoke test if it imported the removed symbols. `SubdomainTenantResolver` is documented as a stub in ADR-0005; removing the stub is acceptable (a real subdomain resolver is a Phase-4 concern, scope-based, re-added there) — note the removal in ADR-0009.

- [ ] **Step 4: Fix `worker-middleware` + its smoke test if needed**

Confirm `packages/worker-middleware/src/worker_middleware/__init__.py` imports only `NoTenantResolver, TenantResolver` (both still valid). If it imported `HeaderTenantResolver`/`SubdomainTenantResolver`, remove that import. Re-run its smoke test.

Run: `uv run pytest packages/worker-middleware/tests/test_smoke_worker_middleware.py packages/worker-tenancy/tests -v`
Expected: PASS (smoke + new claim-resolver tests).

- [ ] **Step 5: `make check` and commit**

Run: `make check` → green. Then:

```bash
git add packages/worker-tenancy/src/worker_tenancy/__init__.py packages/worker-tenancy/tests packages/worker-middleware
git commit -m "worker-tenancy: consolidate to platform canon re-export + scope-based ClaimTenantResolver (ADR-0009 prep)"
```

### Task 21: Wire identity-service to claim-based tenant resolver + tenant-source assertion test + ADR-0009

**Files:**
- Modify: `apps/identity-service/src/identity_service/presentation/compose_api.py` (use the compose-hook: `tenant_resolver=ClaimTenantResolver()`, `auth_middleware=AuthMiddleware`, `routers=(...)`; drop the manual `app.add_middleware` after `create_api_app` since the hook now wires it).
- Test: `apps/identity-service/tests/integration/test_tenant_source.py`

- [ ] **Step 1: Update `compose_api.py`**

```python
def build_app(settings: IdentityServiceSettings) -> FastAPI:
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    AuthMiddleware = build_auth_middleware(deps["tokens"])
    claim_resolver = ClaimTenantResolver()
    auth_router = build_auth_router(deps)
    me_router = build_me_router(deps)

    return create_api_app(
        settings,
        tenant_resolver=claim_resolver,
        auth_middleware=AuthMiddleware,
        routers=(auth_router, me_router),
    )
```

> Drop the previous manual `app.add_middleware(AuthMiddleware)` + `app.include_router(...)` lines (now handled by the hook). The `ClaimTenantResolver` reads `scope["state"]["user"]` which the `AuthMiddleware` sets — and the hook's add order (auth outer to tenant) guarantees `state.user` is set before the tenant resolver runs.

- [ ] **Step 2: Write the tenant-source assertion test**

`apps/identity-service/tests/integration/test_tenant_source.py`:

```python
from __future__ import annotations

import os
import subprocess
from uuid import uuid4

import pytest
from httpx import ASGITransport, AsyncClient

from tests.integration.conftest import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


async def test_x_tenant_id_header_ignored_in_production_mode(postgres_url: str) -> None:
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    os.environ["WORKER_JWT_SECRET"] = "a" * 40
    os.environ["WORKER_ENVIRONMENT"] = "production"
    os.environ["WORKER_ALLOW_DEVELOPMENT_TENANT_HEADER"] = "false"
    subprocess.run(["uv", "run", "alembic", "upgrade", "head"], cwd="apps/identity-service", env=os.environ, check=True, capture_output=True)

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    settings = IdentityServiceSettings()
    app = build_app(settings)
    tenant = uuid4()
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
        await client.post(
            "/auth/register",
            json={"email": "ts@example.com", "password": "strongpassword1", "display_name": "TS", "tenant_id": str(tenant)},
            headers={"X-Tenant-ID": "00000000-0000-0000-0000-000000000000"},  # spoof attempt, must be ignored
        )
        login = await client.post(
            "/auth/login",
            json={"email": "ts@example.com", "password": "strongpassword1", "tenant_id": str(tenant)},
            headers={"X-Tenant-ID": "00000000-0000-0000-0000-000000000000"},  # spoof attempt, must be ignored
        )
        assert login.status_code == 200
        access = login.cookies.get("access")
        me = await client.get("/me", headers={"Authorization": f"Bearer {access}", "X-Tenant-ID": str(uuid4())})
        assert me.status_code == 200
        assert me.json()["tenant_id"] == str(tenant)  # from the CLAIM, not the header

    os.environ.pop("WORKER_ENVIRONMENT", None)
    os.environ.pop("WORKER_ALLOW_DEVELOPMENT_TENANT_HEADER", None)
```

> This is the hard-constraint DoD test. `WORKER_ENVIRONMENT=production` + `allow_development_tenant_header=false` means the platform falls back to `NoTenantResolver` when no claim resolver is supplied — but here we *supply* `ClaimTenantResolver`, so the tenant comes from the claim regardless of headers. The `/me` result `tenant_id == str(tenant)` proves the header was ignored. (Also: the `register`/`login` endpoints take `tenant_id` in the JSON body — the `X-Tenant-ID` header is irrelevant to register/login since the tenant there is from the request, not the contextvar; the assertion that matters is `/me`, a protected endpoint.)

- [ ] **Step 3: Run, `make check`, commit**

Run: `uv run pytest apps/identity-service/tests/integration/test_tenant_source.py -v` → PASS. `make check` → green.

```bash
git add apps/identity-service/src/identity_service/presentation/compose_api.py apps/identity-service/tests/integration/test_tenant_source.py
git commit -m "identity-service: claim-based tenant resolver wired (header ignored in prod, assertion test)"
```

- [ ] **Step 4: Write ADR-0009**

`docs/adr/0009-tenant-context-canon-platform.md`:
- **Context:** ADR-0005 deferred the tenant-context dualism to Phase 2. Verified two incompatible implementations: platform `worker_platform.context._tenant_id: str|None` + scope-based `TenantResolver.resolve(scope)->str|None`; worker-tenancy `_tenant_id: UUID|None` + `tenant_context` dict contextvar + `resolve(request: Request)->UUID|None`. Only the platform is on the live request path (`create_api_app` middleware). `worker-middleware` consumes `worker-tenancy.NoTenantResolver`/`TenantResolver` symbolically.
- **Decision:** Kanon = `worker_platform.context` (str-form of the UUID; no runtime break to the running platform). `worker-tenancy` becomes a Dünnschicht-reexport of the platform `TenantResolver` Protocol / `NoTenantResolver` / `DevelopmentHeaderTenantResolver` + `get_tenant_id` / `tenant_context`. Adds the **scope-based** `ClaimTenantResolver(claim_attr="tenant_id")` reading `scope["state"]["user"]` (set by the identity-service auth middleware) — the production tenant source. UUID-kanonische Repräsentation stays UUID (the value object), but the contextvar holds its str form. Removes `HeaderTenantResolver`, `SubdomainTenantResolver` (stub), the UUID-typed helper contextvars (`set_tenant_id`/`get_tenant_context`/`set_tenant_context`). `worker-platform.create_api_app` gets a compose-hook (`tenant_resolver`/`auth_middleware`/`routers`) so a service installs its claim resolver without the kernel learning business logic (ADR-0002 boundary preserved).
- **Consequences:** One contextvar, one resolver signature (`scope`), one tenant source in prod (claims). Dev/test header support unchanged (gated). A subdomain resolver (Phase 4) is re-added scope-based here when needed. The tenant-str-vs-UUID cosmetic is explicit: consumers that need a `UUID` parse the str; the contextvar keeps `str` for zero-break.
- **Verification:** `test_tenant_source.py` proves `X-Tenant-ID` is ignored in production and `/me` returns the claim tenant; `test_claim_resolver.py` proves identity-equality of re-exports to the platform canon.

- [ ] **Step 5: Commit ADR**

```bash
git add docs/adr/0009-tenant-context-canon-platform.md
git commit -m "docs(adr): 0009 tenant-context canon = worker_platform.context, tenancy reexport"
```

---

## Sub-step 2.7 — Audit EventBus wiring (ADR-0012 full) + audit integration tests

**Sub-step goal:** The commands already persist `AuditEvent`s synchronously inside the UoW (Sub-step 2.5). This sub-step formalizes the audit path as a `worker_events.EventBus`-published + handler-persisted flow (so future async/outbox can swap the handler without touching commands), proves atomicity (audit and login commit together or not at all), and records ADR-0012 fully. No new security behavior — the PII-allowlist is already enforced at `AuditEvent` construction (Task 8).

### Task 22: Audit handler subscribes on EventBus + atomicity integration test + ADR-0012

**Files:**
- Modify: `apps/identity-service/src/identity_service/infrastructure/compose.py` (register an audit handler on the `EventBus` that calls `SqlAlchemyAuditRepository.append` — but **note**: the synchronous-in-UoW path already appends; the EventBus path is a *secondary* notification for `UserLoggedIn`/`UserRegistered` domain events, not a replacement for audit persistence. Clarify scope: ADR-0012 keeps audit persistence synchronous in-UoW; the EventBus publishes the *domain* events `UserRegistered`/`UserLoggedIn` to handlers that may emit side-effects (e.g. future notifications). Audit events are NOT republished — they are already persisted. So this task *documents and wires* the domain-event publication, not a parallel audit channel.)
- Test: `apps/identity-service/tests/integration/test_audit_atomicity.py`
- Create: `docs/adr/0012-audit-event-sync-uow-pii-allowlist.md`

- [ ] **Step 1: Write the atomicity test**

`apps/identity-service/tests/integration/test_audit_atomicity.py`:

```python
from __future__ import annotations

import os
import subprocess
from uuid import uuid4

import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from tests.integration.conftest import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


async def test_successful_login_persists_user_logged_in_and_audit_success(postgres_url: str) -> None:
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    os.environ["WORKER_JWT_SECRET"] = "a" * 40
    subprocess.run(["uv", "run", "alembic", "upgrade", "head"], cwd="apps/identity-service", env=os.environ, check=True, capture_output=True)
    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    app = build_app(IdentityServiceSettings())
    tenant = uuid4()
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
        await client.post("/auth/register", json={"email": "au@example.com", "password": "strongpassword1", "display_name": "AU", "tenant_id": str(tenant)})
        login = await client.post("/auth/login", json={"email": "au@example.com", "password": "strongpassword1", "tenant_id": str(tenant)})
        assert login.status_code == 200

    eng = create_async_engine(postgres_url)
    async with eng.connect() as conn:
        actions = [r[0] for r in (await conn.execute(text("SELECT action FROM audit_events WHERE tenant_id=:t"), {"t": tenant})).all()]
    await eng.dispose()
    assert "register" in actions
    assert "login_success" in actions


async def test_failed_login_persists_login_failure_audit(postgres_url: str) -> None:
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    os.environ["WORKER_JWT_SECRET"] = "a" * 40
    subprocess.run(["uv", "run", "alembic", "upgrade", "head"], cwd="apps/identity-service", env=os.environ, check=True, capture_output=True)
    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    app = build_app(IdentityServiceSettings())
    tenant = uuid4()
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
        login = await client.post("/auth/login", json={"email": "nope@example.com", "password": "wrongpassword1", "tenant_id": str(tenant)})
        assert login.status_code == 401

    eng = create_async_engine(postgres_url)
    async with eng.connect() as conn:
        rows = (await conn.execute(text("SELECT action, actor_id, metadata FROM audit_events WHERE tenant_id=:t"), {"t": tenant})).all()
    await eng.dispose()
    assert any(r[0] == "login_failure" and r[1] is None for r in rows)  # unknown user → actor_id NULL
```

- [ ] **Step 2: Wire the EventBus handler in `compose.py`**

Add, after `"eventbus": EventBus()`:

```python
    eventbus: EventBus = EventBus()

    async def _on_user_logged_in(event) -> None:
        # Phase 2: domain events have no cross-service consumer yet. The handler is a
        # no-op seam for future side-effects (notifications). Audit persistence itself
        # is synchronous inside the command's UoW (ADR-0012), NOT routed through here.
        return None

    eventbus.subscribe(UserLoggedIn, _on_user_logged_in)
    eventbus.subscribe(UserRegistered, _on_user_logged_in)
    bundle["eventbus"] = eventbus
```

> The commands already call `await deps["eventbus"].publish(ev)` after persisting (Task 17); this handler is the subscription seam. Import `UserLoggedIn`/`UserRegistered` from `identity_service.domain.user`.

- [ ] **Step 3: Run, `make check`, commit**

Run: `uv run pytest apps/identity-service/tests/integration/test_audit_atomicity.py -v` → PASS. `make check` → green.

```bash
git add apps/identity-service/src/identity_service/infrastructure/compose.py apps/identity-service/tests/integration/test_audit_atomicity.py
git commit -m "identity-service: audit EventBus seam + login success/failure atomicity tests"
```

- [ ] **Step 4: Write ADR-0012**

`docs/adr/0012-audit-event-sync-uow-pii-allowlist.md`:
- **Context:** `worker_events` ships an in-process `EventBus` with no persistence. Phase 2 must persist audit events for security-sensitive actions (DoD) without leaking Consent-Ledger PII (Phase 3 future). Outbox/Inbox is ULTRAPLAN Phase 9 — not now.
- **Decision:** `AuditEvent` is a *service-owned* domain type in `identity_service.domain.audit` (not in shared `worker-events`; ADR-0004 — audit payload is service-specific). Its `metadata` is a **validated allowlist** (`reason`, `ip`, `user_agent`); construction with any other key raises `AuditMetadataError`. Audit persistence is **synchronous inside the same UoW transaction** as the security command → atomicity (login + audit succeed/fail together). The in-process `EventBus` publishes `UserRegistered`/`UserLoggedIn` *domain* events as a side-effect seam (no audit republish — audit is already persisted). `actor_id` is nullable (unknown user at failed login). `audit_events` are **not** cascade-deleted with users (retention).
- **Upgrade path documented:** Outbox (Phase 9) can replace the synchronous `AuditRepository.append` call with an outbox-row insert + async worker — the `AuditEvent` type and `metadata` allowlist stay unchanged; only the persistence mechanism changes.
- **Consequences:** Audit never carries the password, email, consent payloads, or tokens (enforced at construction, verified by `test_audit.py`). Consent (Phase 3) cannot accidentally leak into audit. Retention/anonymisation is a later GDPR step.
- **Verification:** `test_audit.py` (PII-allowlist), `test_audit_atomicity.py` (login_success/login_failure persisted; actor_id NULL for unknown).

- [ ] **Step 5: Commit ADR**

```bash
git add docs/adr/0012-audit-event-sync-uow-pii-allowlist.md
git commit -m "docs(adr): 0012 AuditEvent sync-UoW, PII allowlist, Outbox=Phase9"
```

---

## Sub-step 2.8 — Frontend `/login` (German, TanStack Router, cookie-auth)

**Sub-step goal:** The first real web route — a German `/login` page that posts email+password (+tenant) to `POST /auth/login`, receives access+refresh in HTTP-only cookies, and bootstraps a TanStack Query client that derives the tenant from the authenticated `/me` claim. TypeScript-strict (`pnpm check`), Vitest-green (`pnpm test`). Code-router in `app.tsx` (no file-system router scaffold present; add `@tanstack/react-router` and use a `createRouter` in code).

### Task 23: Add TanStack Router dep + auth client + env

**Files:**
- Modify: `apps/web/package.json` (add `@tanstack/react-router`)
- Create: `apps/web/src/env.ts`
- Create: `apps/web/src/auth/client.ts`
- Create: `apps/web/src/auth/query-client.ts`
- Test: `apps/web/src/auth/client.test.ts`

**Interfaces:**
- Produces: `API_BASE_URL` (from `import.meta.env.VITE_API_BASE_URL`, falls back to `http://127.0.0.1:8001`); `apiClient` (fetch wrapper with `credentials: "include"` — cookie jar); `queryClient`.
- `LoginResult` type `{ ok: true } | { ok: false; message: string }`.

- [ ] **Step 1: Add dep**

```bash
pnpm --filter @workertransfer/web add @tanstack/react-router
```

- [ ] **Step 2: Write `env.ts`**

```ts
const raw = import.meta.env.VITE_API_BASE_URL;
export const API_BASE_URL = typeof raw === "string" && raw.length > 0 ? raw : "http://127.0.0.1:8001";
```

> `noUncheckedIndexedAccess` (tsconfig.base) — index access returns `T | undefined`. `import.meta.env` is `ImportMetaEnv` whose keys are optional; access via the `VITE_API_BASE_URL` literal gives `string | undefined`. The guard above is the recommended pattern.

- [ ] **Step 3: Write `auth/client.ts`**

```ts
import { API_BASE_URL } from "../env";

export { API_BASE_URL };

export type LoginResult = { ok: true } | { ok: false; message: string };

export interface LoginInput {
  email: string;
  password: string;
  tenantId: string;
}

export async function login(input: LoginInput): Promise<LoginResult> {
  const res = await fetch(`${API_BASE_URL}/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email: input.email, password: input.password, tenant_id: input.tenantId }),
  });
  if (res.ok) {
    return { ok: true };
  }
  let message = "Anmeldung fehlgeschlagen";
  try {
    const body = (await res.json()) as { detail?: string };
    if (typeof body.detail === "string" && body.detail.length > 0) message = body.detail;
  } catch {
    // keep default german message
  }
  return { ok: false, message };
}

export interface MeResponse {
  user_id: string;
  tenant_id: string;
  roles: readonly string[];
}

export async function fetchMe(): Promise<MeResponse | null> {
  const res = await fetch(`${API_BASE_URL}/me`, { credentials: "include" });
  if (!res.ok) return null;
  return (await res.json()) as MeResponse;
}
```

- [ ] **Step 4: Write `auth/query-client.ts`**

```ts
import { QueryClient } from "@tanstack/react-query";

export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
});
```

- [ ] **Step 5: Write `auth/client.test.ts`**

```ts
import { afterEach, describe, expect, it, vi } from "vitest";

import { fetchMe, login } from "./client";

const ok = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

afterEach(() => vi.restoreAllMocks());

describe("login", () => {
  it("returns ok on 200", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ status: "ok" })));
    const r = await login({ email: "a@b.com", password: "strongpassword1", tenantId: "11111111-1111-1111-1111-111111111111" });
    expect(r).toEqual({ ok: true });
  });

  it("returns a german message on 401", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ detail: "invalid credentials" }, 401)));
    const r = await login({ email: "a@b.com", password: "wrong", tenantId: "11111111-1111-1111-1111-111111111111" });
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.message.length).toBeGreaterThan(0);
  });
});

describe("fetchMe", () => {
  it("returns null on 401", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ detail: "no" }, 401)));
    expect(await fetchMe()).toBeNull();
  });
  it("returns the principal on 200", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ user_id: "u", tenant_id: "t", roles: ["user"] })));
    const me = await fetchMe();
    expect(me?.tenant_id).toBe("t");
  });
});
```

- [ ] **Step 6: Run `pnpm check` + `pnpm test`**

Run: `pnpm --filter @workertransfer/web run check && pnpm --filter @workertransfer/web run test`
Expected: tsc green, vitest green (client tests pass).

- [ ] **Step 7: Commit**

```bash
git add apps/web/package.json apps/web/src/env.ts apps/web/src/auth
git commit -m "web: auth client (cookie-based) + query client + env + tests"
```

### Task 24: Login route + router root + wiring + route test

**Files:**
- Create: `apps/web/src/routes/login.tsx`
- Create: `apps/web/src/routes/login.test.tsx`
- Create: `apps/web/src/routes/home.tsx`
- Modify: `apps/web/src/app.tsx` (move hero content to `home.tsx`, wire a code-router)
- Modify: `apps/web/src/main.tsx` (render the router)
- Modify: `apps/web/src/app.test.tsx` (adjust the existing heading assertion to `home.tsx`)

- [ ] **Step 1: Write `routes/login.tsx`**

```tsx
import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import { type LoginInput, login } from "../auth/client";

export function LoginRoute() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [tenantId, setTenantId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const input: LoginInput = { email, password, tenantId };
    const result = await login(input);
    setBusy(false);
    if (result.ok) {
      window.location.href = "/";
    } else {
      setError(result.message);
    }
  }

  return (
    <main>
      <section aria-labelledby="login-title">
        <Card>
          <h1 id="login-title">Anmelden</h1>
          <form onSubmit={onSubmit}>
            <label>
              E-Mail
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
            </label>
            <label>
              Passwort
              <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
            </label>
            <label>
              Mandant-ID
              <input value={tenantId} onChange={(e) => setTenantId(e.target.value)} required placeholder="00000000-0000-0000-0000-000000000000" />
            </label>
            {error !== null ? <p role="alert">{error}</p> : null}
            <Button type="submit" disabled={busy}>{busy ? "Anmeldung läuft…" : "Anmelden"}</Button>
          </form>
        </Card>
      </section>
    </main>
  );
}
```

- [ ] **Step 2: Write `login.test.tsx`**

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { LoginRoute } from "./login";

afterEach(() => vi.restoreAllMocks());

describe("LoginRoute", () => {
  it("renders the german heading and submits", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ status: "ok" }), { status: 200 })));
    render(<LoginRoute />);
    expect(screen.getByRole("heading", { name: "Anmelden" })).toBeInTheDocument();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Mandant-ID"), "11111111-1111-1111-1111-111111111111");
    delete (window as { location?: Location }).location;
    Object.defineProperty(window, "location", { value: { href: "" }, writable: true });
    await user.click(screen.getByRole("button", { name: "Anmelden" }));
  });
});
```

> `@testing-library/user-event` is not yet a dep — add it: `pnpm --filter @workertransfer/web add -D @testing-library/user-event`. The redirect side-effect stubs `window.location`; keep the test minimal (assert submission does not throw + heading renders).

- [ ] **Step 3: Write `routes/home.tsx`** (move the existing hero from `app.tsx`):

Copy the body of the current `apps/web/src/app.tsx` `App` function into `routes/home.tsx` as `export function HomeRoute()`. Keep the imports of `Button, Card` from `@workertransfer/ui` and the `foundations` array. Return the same JSX.

- [ ] **Step 4: Wire a code-router in `app.tsx`**

Replace `apps/web/src/app.tsx`:

```tsx
import { Link, Outlet, createRootRoute, createRoute, createRouter, useRouter } from "@tanstack/react-router";

import { HomeRoute } from "./routes/home";
import { LoginRoute } from "./routes/login";

const rootRoute = createRootRoute({ component: RootLayout });
const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomeRoute });
const loginRoute = createRoute({ getParentRoute: () => rootRoute, path: "/login", component: LoginRoute });

function RootLayout() {
  const router = useRouter();
  const current = router.state.location.pathname;
  return (
    <>
      <nav aria-label="Hauptnavigation">
        <Link to="/">Start</Link>
        {current !== "/login" ? <Link to="/login">Anmelden</Link> : null}
      </nav>
      <Outlet />
    </>
  );
}

const routeTree = rootRoute.addChildren([homeRoute, loginRoute]);
export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

export function App() {
  return <RouterProvider router={router} />;
}

import { RouterProvider } from "@tanstack/react-router";
```

> The `RouterProvider` import at the bottom is ugly — move it to the top with the other `@tanstack/react-router` imports. The final file has one import block. Keep the `App` component returning `<RouterProvider>`.

- [ ] **Step 5: Update `main.tsx`**

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "@tanstack/react-router";

import { router } from "./app";
import "./styles.css";
import "@workertransfer/ui/styles.css";

const root = document.getElementById("root");
if (root) {
  createRoot(root).render(<StrictMode><RouterProvider router={router} /></StrictMode>);
}
```

- [ ] **Step 6: Update `app.test.tsx`** (the heading previously asserted in `App` moved to `HomeRoute`):

```tsx
import { render } from "@testing-library/react";
import { MemoryHistory, createMemoryHistory } from "@tanstack/react-router";

import { router } from "./app";

describe("App routing", () => {
  it("renders the landing heading at /", () => {
    const history: MemoryHistory = createMemoryHistory({ initialEntries: ["/"] });
    // TanStack test: use router with memory history requires `router.history` swap;
    // simplest: render HomeRoute directly.
    void history;
  });
});
```

> The router-with-memory-history pattern in TanStack needs the testing adapter. Simpler: directly render `HomeRoute` and assert the existing heading, and render `LoginRoute` to assert "Anmelden". Rewrite `app.test.tsx` to import `HomeRoute` and `LoginRoute` directly and assert each renders its heading — drop the router-history dance.

- [ ] **Step 7: Run `pnpm check` + `pnpm test`**

Run: `pnpm --filter @workertransfer/web run check && pnpm --filter @workertransfer/web run test`
Expected: tsc green, vitest green (home + login + client tests). Fix any `Register`-type / `verbatimModuleSyntax` issues (use `import type` for type-only imports).

- [ ] **Step 8: Commit**

```bash
git add apps/web/src apps/web/package.json apps/web/src/app.test.tsx apps/web/src/main.tsx
git commit -m "web: /login route (German) + TanStack router root + cookie-auth, tests"
```

---

## Sub-step 2.9 — CI Docker for integration tests + ROADMAP + final verify

**Sub-step goal:** Enable Testcontainers integration tests in GitHub Actions (Docker service), update `docs/ROADMAP.md` Phase-2 status, and run the full gate (`make check` + `pnpm check`/`pnpm test`) for the final green.

### Task 25: CI Docker + ROADMAP + final verify

**Files:**
- Modify: `.github/workflows/ci.yml` (ensure Docker is available during the Testcontainers integration step)
- Modify: `docs/ROADMAP.md` (Phase 2 status → ✅ with sub-step notes)

- [ ] **Step 1: Inspect current CI**

Run: `uv run --quiet cat .github/workflows/ci.yml || cat .github/workflows/ci.yml`
> Read the file with the Read tool; the `uv run cat` is not idiomatic — use `Read .github/workflows/ci.yml`.

- [ ] **Step 2: Add Docker to the integration job**

GitHub-hosted runners already have Docker. If the workflow runs `uv run pytest` (which collects the integration tests → skips if no Docker), the only change needed is to **not** skip them: ensure the job does not set a flag that disables Docker. If the workflow uses `services:` to spin a sidecar, that is unnecessary — Testcontainers manages its own container, so no `services:` block is needed. **Concrete edit:** confirm the pytest step runs on a runner where Docker is available (the default `ubuntu-latest` has Docker). Add a comment noting Testcontainers needs Docker and that skips are acceptable on Docker-less runners. Add `--reruns 0` is not needed. No structural edit unless the job pinned `container:` — if it did (rare), unset it so the outer Docker daemon is reachable.

> Realistic edit: if the workflow file has a single `pytest` step, leave it. Add a `.github/workflows/README.md` line? No — keep the change minimal: a one-line `# Testcontainers needs Docker; tests skip on Docker-less runners (ADR-0011)` comment above the pytest step.

- [ ] **Step 3: Update ROADMAP**

Change the Phase 2 row:
`| 2 | Identity & Tenancy | ✅ | OIDC/OAuth, JWT, Claims-Tenant, Audit, DB-Migration |`
and add a Phase-2 status section under the Phase-1 section, mirroring its format:
- ✅ 2.1 worker-auth repaired (bcrypt direct + PyJWT HS256)
- ✅ 2.2 Alembic pro-service async + worker-cli repair (ADR-0010)
- ✅ 2.3 identity-domain (User, value objects, AuditEvent, ports)
- ✅ 2.4 persistence + 0001_init migration + Testcontainers (ADR-0011)
- ✅ 2.5 application commands + HTTP /auth + /me + auth middleware
- ✅ 2.6 tenant consolidation (ADR-0009) + claim-based resolver + header-ignored-in-prod test
- ✅ 2.7 audit EventBus seam + atomicity tests (ADR-0012)
- ✅ 2.8 frontend /login (German, TanStack Router, cookie-auth)
- ✅ 2.9 CI Docker + ROADMAP + final verify
- ADRs written: 0006, 0007, 0008, 0009, 0010, 0011, 0012
- DoD met: register/login → JWT, tenant from claim, audit persisted, DB migration, Testcontainers integration, frontend /login, `make check` + `pnpm check`/`pnpm test` green.

- [ ] **Step 4: Final full gate**

Run: `make check`
Expected: green (ruff format → ruff check → mypy → pytest; integration tests pass-or-skip).

Run: `pnpm --filter @workertransfer/web run check && pnpm --filter @workertransfer/web run test`
Expected: green.

Run: `pnpm check && pnpm test` (turbo, all workspaces)
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci.yml docs/ROADMAP.md
git commit -m "ci+docs: Phase 2 complete — Testcontainers in CI, ROADMAP ✅, full gate green"
```

- [ ] **Step 6 (DoD self-check):** Confirm each ULTRAPLAN §Phase 2 DoD line:
  - User can log in → `POST /auth/login` returns 200 + cookies (`test_auth_endpoints.py`).
  - receives a JWT → access cookie set, `TokenManager` HS256 (`worker-auth` tests).
  - Tenant from claim → `GET /me` returns claim tenant; `X-Tenant-ID` ignored in prod (`test_tenant_source.py`).
  - Audit persisted → `register`/`login_success`/`login_failure` rows in `audit_events` (`test_audit_atomicity.py`).
  - DB migration → `0001_init_users_sessions_audit` applies (`test_migrations.py`).
  - Domain + Integration (Testcontainers) → unit suite + integration suite green.
  - CI green → `make check` + `pnpm check`/`pnpm test` green; CI Tail.

The branch `phase-2-identity-tenancy` now holds the complete slice. No PR to `main` (per the user's constraint); a PR to `develop` is the integration path — out of scope of this plan unless the user requests it.

---

## Self-Review (plan-author, fresh-eyes)

**1. Spec coverage:**
- bcrypt-direct hashing — Task 1 (2.1). ✅
- HS256 + PyJWT, jose dropped — Task 2 (2.1). ✅
- password-flow-not-OIDC + upgrade path — ADR-0008, Task 10. ✅
- tenant canon = platform, tenancy reexport, scope-based ClaimTenantResolver — Tasks 19-21 + ADR-0009. ✅
- pro-service async Alembic — Tasks 4-5, 14 + ADR-0010. ✅
- Testcontainers PostgreSQL + skip-if-no-docker — Task 11, 16 + ADR-0011. ✅
- AuditEvent own type, sync UoW, PII allowlist, outbox=Phase9 — Task 8, 22 + ADR-0012. ✅
- User aggregate + AccountStatus + synchronous ACTIVE — Task 9. ✅
- Email/PasswordHash/UserId/TenantId value objects — Task 7. ✅
- PG models users/sessions/audit_events, UNIQUE(tenant_id,email), FK cascade — Task 12, 14. ✅
- register.tenant_id from request — Task 17 `RegisterUserCommand.tenant_id` in body. ✅
- auth middleware sets user=None on bad token — Task 18 `auth_middleware.py`. ✅
- access+refresh JWT + refresh rotation (sessions jti ledger) — Task 17 + Task 18. ✅
- frontend /login German cookie-auth — Tasks 23-24. ✅
- worker-security password policy (fallback) — mini `PasswordPolicy`, Task 17. ✅ (spec said "adopt if present, else mini" — verified worker-security is headers-only → mini, documented.)
- rate-limiting excluded, TODO marker — **gap:** no TODO marker placed in router (spec §7 asked for a TODO marker). Add a `# TODO Phase-10: enforce per-IP rate-limiting (worker-ratelimit) before external exposure` comment in `build_auth_router`. Add as Step 8a of Task 18. Add now.
- audit does not store Email (PII) — Task 8 metadata allowlist + `AuditEvent` never carries email; `UserRegistered` carries email in the *domain event*, not in Audit. ✅
- make check per sub-step + commit per sub-step — every task ends with `make check` + commit. ✅

**2. Placeholder scan:** Several tasks use illustrative `...` or `op=op`/`RequestResponseEndpoint`-style "do not commit" notes — these are explicit *pitfall flags*, not placeholders; each is annotated "write the full file". Two genuine gaps found and fixed inline above: (a) AuthMiddleware add-order corrected (outer–inner); (b) `AuthPrincipal.jti` extension flagged as a real correctness fix. The `request_for_secure()` placeholder in Task 18 Step 1 is flagged to be replaced by `build_auth_router(deps)` reading `settings.environment`. ✅ after fixes.

**3. Type consistency:** `TokenPair`, `AuthPrincipal`, `AuditAction`, `AccountStatus`, `AuditEvent`, `User`, value objects, `TokenManager` signature — cross-checked across tasks. `AuthPrincipal` gains `jti` (Task 13→17). `SqlAlchemyUserRepository.get_by_email` returns `User | None`; command handles `None` → `InvalidCredentials`. `verify_refresh_token` returns `AuthPrincipal` (with `jti`) used by `handle_refresh`. Consistent. ✅

**Gaps fixed inline during self-review:**
- Added Step 8a (Task 18): rate-limiting TODO marker in `build_auth_router`.
- Re-stated the AuthMiddleware add-order correction in Task 19 Step 3.

---






