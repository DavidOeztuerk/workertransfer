"""Unit tests for the auth command handlers (fake repos, no Docker)."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID, uuid4

from identity_service.application.commands import (
    AuthenticateUserCommand,
    RefreshTokenCommand,
    RegisterUserCommand,
    RevokeTokenCommand,
    handle_login,
    handle_refresh,
    handle_register,
    handle_revoke,
)
from identity_service.application.ports import AuthPrincipal, TokenPair
from identity_service.domain.audit import AuditAction
from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash


def is_success(result: Any) -> bool:
    return result.is_success


def fail_err(result: Any) -> Any:
    return result.error


class _FakeUsers:
    def __init__(self) -> None:
        self.by_email: dict[tuple[UUID, str], User] = {}

    async def get_by_email(self, tenant_id: UUID, email: str) -> User | None:
        return self.by_email.get((tenant_id, email.lower()))

    async def add(self, user: User) -> None:
        self.by_email[(user.tenant_id.value, user.email.value)] = user


class _FakeSessionRow:
    def __init__(
        self, *, user_id: UUID, tenant_id: UUID, refresh_jti: str, expires_at: datetime
    ) -> None:
        self.user_id = user_id
        self.tenant_id = tenant_id
        self.refresh_jti = refresh_jti
        self.expires_at = expires_at
        self.revoked_at: datetime | None = None


class _FakeSessions:
    def __init__(self) -> None:
        self.rows: dict[str, _FakeSessionRow] = {}

    async def add(
        self, *, user_id: UUID, tenant_id: UUID, refresh_jti: str, expires_at: datetime
    ) -> None:
        self.rows[refresh_jti] = _FakeSessionRow(
            user_id=user_id,
            tenant_id=tenant_id,
            refresh_jti=refresh_jti,
            expires_at=expires_at,
        )

    async def get_by_jti(self, refresh_jti: str) -> _FakeSessionRow | None:
        return self.rows.get(refresh_jti)

    async def revoke(self, refresh_jti: str, revoked_at: datetime) -> None:
        row = self.rows.get(refresh_jti)
        if row is not None and row.revoked_at is None:
            row.revoked_at = revoked_at


class _FakeAudit:
    def __init__(self) -> None:
        self.events: list[Any] = []

    async def append(self, event: Any) -> None:
        self.events.append(event)


class _StupidHasher:
    def hash(self, plain: str) -> PasswordHash:
        return PasswordHash("h$" + plain)

    def verify(self, plain: str, hashed: PasswordHash) -> bool:
        return hashed.value == "h$" + plain


class _FakeTokens:
    """Round-trips jti into the principal so refresh/revoke logic is testable."""

    def __init__(self) -> None:
        self.invalid_tokens: set[str] = set()

    def issue_pair(
        self,
        *,
        user_id: UUID,
        tenant_id: UUID,
        roles: list[str],
        permissions: list[str],
        session_jti: str,
    ) -> TokenPair:
        # Encode jti into both tokens so verify_* can recover it.
        return TokenPair(access=f"acc:{session_jti}", refresh=f"ref:{session_jti}")

    def verify_refresh_token(self, token: str) -> AuthPrincipal:
        if token in self.invalid_tokens or not token.startswith("ref:"):
            raise ValueError("invalid")
        jti = token[len("ref:") :]
        return AuthPrincipal(
            user_id=UUID("00000000-0000-0000-0000-000000000001"),
            tenant_id=UUID("00000000-0000-0000-0000-000000000002"),
            roles=("user",),
            jti=jti,
        )


class _Clock:
    def __init__(self, start: datetime | None = None) -> None:
        self._t = start or datetime(2026, 7, 16, 12, 0, 0, tzinfo=UTC)

    def now(self) -> datetime:
        return self._t

    def advance(self, delta: timedelta) -> None:
        self._t = self._t + delta


class _Bus:
    def __init__(self) -> None:
        self.published: list[Any] = []

    async def publish(self, ev: Any) -> None:
        self.published.append(ev)


class _Settings:
    jwt_refresh_token_expire_minutes = 60


def _deps(
    clock: _Clock, tokens: _FakeTokens, bus: _Bus, hasher: _StupidHasher | None = None
) -> dict[str, Any]:
    return {
        "hasher": hasher or _StupidHasher(),
        "tokens": tokens,
        "clock": clock,
        "eventbus": bus,
        "settings": _Settings(),
    }


async def test_register_creates_user_and_audit() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    deps = _deps(clock, tokens, bus, hasher)
    tenant = uuid4()
    cmd = RegisterUserCommand(
        email="A@B.com", password="strongpassword1", display_name="A", tenant_id=tenant
    )

    res = await handle_register(cmd, deps=deps, repos=repos)

    assert is_success(res)
    assert fail_err(res) is None
    user = res.value
    assert isinstance(user, User)
    # Email is lowercased by the value object.
    assert user.email == Email("a@b.com")
    # One audit event: REGISTER.
    assert len(repos["audit"].events) == 1
    assert repos["audit"].events[0].action is AuditAction.REGISTER
    # UserRegistered was published to the bus.
    assert len(bus.published) == 1


async def test_register_rejects_duplicate_email() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    deps = _deps(clock, tokens, bus, hasher)
    tenant = uuid4()
    cmd = RegisterUserCommand(
        email="dup@example.com", password="strongpassword1", display_name="D", tenant_id=tenant
    )
    await handle_register(cmd, deps=deps, repos=repos)

    second = await handle_register(cmd, deps=deps, repos=repos)

    assert not is_success(second)
    assert fail_err(second) is not None
    assert fail_err(second).code == "user_already_exists"


async def test_login_success_returns_token_pair_and_persists_session_audit() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    # Seed a registered user directly into the fake users repo.
    await handle_register(
        RegisterUserCommand(
            email="alice@example.com",
            password="strongpassword1",
            display_name="Alice",
            tenant_id=tenant,
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    repos["audit"].events.clear()
    bus.published.clear()

    res = await handle_login(
        AuthenticateUserCommand(
            email="ALICE@example.com", password="strongpassword1", tenant_id=tenant
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert is_success(res)
    pair = res.value
    assert isinstance(pair, TokenPair)
    assert pair.access.startswith("acc:")
    assert pair.refresh.startswith("ref:")
    # One session row was added.
    assert len(repos["sessions"].rows) == 1
    # One LOGIN_SUCCESS audit (UserLoggedIn published as a domain event too).
    assert any(ev.action is AuditAction.LOGIN_SUCCESS for ev in repos["audit"].events)
    assert len(bus.published) == 1


async def test_login_unknown_user_audits_failure_with_none_actor() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}

    res = await handle_login(
        AuthenticateUserCommand(
            email="nobody@example.com", password="whatever12345", tenant_id=uuid4()
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert not is_success(res)
    assert fail_err(res).code == "invalid_credentials"
    failures = [ev for ev in repos["audit"].events if ev.action is AuditAction.LOGIN_FAILURE]
    assert len(failures) == 1
    assert failures[0].actor_id is None
    assert failures[0].metadata["reason"] == "unknown_user"


async def test_login_bad_password_audits_failure_with_user_actor() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    seeded = await handle_register(
        RegisterUserCommand(
            email="bob@example.com",
            password="strongpassword1",
            display_name="Bob",
            tenant_id=tenant,
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    seeded_user = seeded.value
    repos["audit"].events.clear()
    bus.published.clear()

    res = await handle_login(
        AuthenticateUserCommand(
            email="bob@example.com", password="wrongpassword99", tenant_id=tenant
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert not is_success(res)
    failures = [ev for ev in repos["audit"].events if ev.action is AuditAction.LOGIN_FAILURE]
    assert len(failures) == 1
    assert failures[0].actor_id == seeded_user.id.value
    assert failures[0].metadata["reason"] == "bad_password"


async def test_login_disabled_user_audits_failure() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    seeded = await handle_register(
        RegisterUserCommand(
            email="cara@example.com",
            password="strongpassword1",
            display_name="Cara",
            tenant_id=tenant,
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    cara = seeded.value
    cara.status = AccountStatus.SUSPENDED
    repos["audit"].events.clear()
    bus.published.clear()

    res = await handle_login(
        AuthenticateUserCommand(
            email="cara@example.com", password="strongpassword1", tenant_id=tenant
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert not is_success(res)
    failures = [ev for ev in repos["audit"].events if ev.action is AuditAction.LOGIN_FAILURE]
    assert len(failures) == 1
    assert failures[0].metadata["reason"] == "disabled"


async def test_refresh_rotates_jti_and_revokes_old_session() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    await handle_register(
        RegisterUserCommand(
            email="rob@example.com",
            password="strongpassword1",
            display_name="Rob",
            tenant_id=tenant,
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    login = await handle_login(
        AuthenticateUserCommand(
            email="rob@example.com", password="strongpassword1", tenant_id=tenant
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    old_refresh = login.value.refresh
    old_jti = old_refresh[len("ref:") :]
    assert old_jti in repos["sessions"].rows
    assert repos["sessions"].rows[old_jti].revoked_at is None
    repos["audit"].events.clear()

    res = await handle_refresh(
        RefreshTokenCommand(refresh_token=old_refresh),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert is_success(res)
    new_pair = res.value
    # Old session revoked.
    assert repos["sessions"].rows[old_jti].revoked_at is not None
    # New session exists, distinct jti.
    new_jti = new_pair.refresh[len("ref:") :]
    assert new_jti != old_jti
    assert new_jti in repos["sessions"].rows
    # TOKEN_REFRESH audit emitted.
    assert any(ev.action is AuditAction.TOKEN_REFRESH for ev in repos["audit"].events)


async def test_refresh_rejects_revoked_session() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    await handle_register(
        RegisterUserCommand(
            email="x@example.com",
            password="strongpassword1",
            display_name="X",
            tenant_id=tenant,
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    login = await handle_login(
        AuthenticateUserCommand(
            email="x@example.com", password="strongpassword1", tenant_id=tenant
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    refresh = login.value.refresh
    # Revoke the session out-of-band (simulating a prior logout).
    await repos["sessions"].revoke(refresh[len("ref:") :], clock.now())

    res = await handle_refresh(
        RefreshTokenCommand(refresh_token=refresh),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert not is_success(res)
    assert fail_err(res).code == "invalid_credentials"


async def test_refresh_rejects_bad_token_without_auditing_pii() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}

    res = await handle_refresh(
        RefreshTokenCommand(refresh_token="not-a-real-token"),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )

    assert not is_success(res)
    assert fail_err(res).code == "invalid_credentials"
    # No audit event for a bad token (avoids enumeration / leaks nothing).
    assert repos["audit"].events == []


async def test_revoke_revokes_session_and_is_idempotent() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {"users": _FakeUsers(), "sessions": _FakeSessions(), "audit": _FakeAudit()}
    tenant = uuid4()
    await handle_register(
        RegisterUserCommand(
            email="y@example.com",
            password="strongpassword1",
            display_name="Y",
            tenant_id=tenant,
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    login = await handle_login(
        AuthenticateUserCommand(
            email="y@example.com", password="strongpassword1", tenant_id=tenant
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    refresh = login.value.refresh
    jti = refresh[len("ref:") :]
    repos["audit"].events.clear()

    res = await handle_revoke(
        RevokeTokenCommand(refresh_token=refresh),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    assert is_success(res)
    assert repos["sessions"].rows[jti].revoked_at is not None
    assert any(ev.action is AuditAction.TOKEN_REVOKE for ev in repos["audit"].events)

    # Idempotent: revoking again reports success and does not double-audit.
    repos["audit"].events.clear()
    res2 = await handle_revoke(
        RevokeTokenCommand(refresh_token=refresh),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    assert is_success(res2)
    assert repos["audit"].events == []
