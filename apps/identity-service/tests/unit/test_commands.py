"""Unit tests for the auth command handlers (fake repos, no Docker)."""

from __future__ import annotations

from dataclasses import replace
from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID, uuid4

from identity_service.application.commands import (
    AuthenticateUserCommand,
    CreateCompanyCommand,
    ListMembershipsQuery,
    RefreshTokenCommand,
    RegisterUserCommand,
    ResendVerificationCommand,
    RevokeTokenCommand,
    SwitchTenantCommand,
    VerifyEmailCommand,
    dispatch_all,
    handle_create_company,
    handle_list_memberships,
    handle_login,
    handle_refresh,
    handle_register,
    handle_resend,
    handle_revoke,
    handle_switch_tenant,
    handle_verify_email,
)
from identity_service.application.ports import AuthPrincipal, TokenPair
from identity_service.domain.audit import AuditAction
from identity_service.domain.membership import MembershipRole
from identity_service.domain.session import SessionView
from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash
from identity_service.infrastructure.tokens import hash_token


async def _register_via(cmd: Any, *, deps: dict[str, Any], repos: dict[str, Any]) -> Any:
    """Spiegelt den Router: Handler sammelt, Versand passiert danach."""
    outbox: list[Any] = []
    result = await handle_register(cmd, deps=deps, repos=repos, outbox=outbox)
    await dispatch_all(outbox, deps)
    return result


async def _resend_via(cmd: Any, *, deps: dict[str, Any], repos: dict[str, Any]) -> Any:
    outbox: list[Any] = []
    result = await handle_resend(cmd, deps=deps, repos=repos, outbox=outbox)
    await dispatch_all(outbox, deps)
    return result


def is_success(result: Any) -> bool:
    return result.is_success


def fail_err(result: Any) -> Any:
    return result.error


class _FakeUsers:
    def __init__(self) -> None:
        # Keyed by email alone: a person is one account platform-wide (ADR-0017).
        self.by_email: dict[str, User] = {}
        self.by_id: dict[UUID, User] = {}

    async def get_by_email(self, email: str) -> User | None:
        return self.by_email.get(email.lower())

    async def get_by_id(self, user_id: UUID) -> User | None:
        return self.by_id.get(user_id)

    async def add(self, user: User) -> None:
        self.by_email[user.email.value] = user
        self.by_id[user.id.value] = user

    async def save(self, user: User) -> None:
        self.by_email[user.email.value] = user
        self.by_id[user.id.value] = user


class _FakeMemberships:
    def __init__(self, members: set[tuple[UUID, UUID]] | None = None) -> None:
        self.members = members or set()
        self.added: list[Any] = []

    async def is_member(self, user_id: UUID, tenant_id: UUID) -> bool:
        return (user_id, tenant_id) in self.members

    async def list_for_user(self, user_id: UUID) -> list[UUID]:
        return [t for u, t in self.members if u == user_id]

    async def add(self, user_id: UUID, tenant_id: UUID, role: Any) -> None:
        self.members.add((user_id, tenant_id))
        self.added.append((user_id, tenant_id, role))

    async def list_for_user_detailed(self, user_id: UUID) -> list[Any]:
        return [entry for entry in self.added if entry[0] == user_id]


class _FakeCompanies:
    def __init__(self) -> None:
        self.by_domain: dict[str, Any] = {}

    async def add(self, company: Any) -> None:
        self.by_domain[company.domain.value] = company

    async def get_by_domain(self, domain: str) -> Any:
        return self.by_domain.get(domain.lower())

    async def get_by_id(self, company_id: UUID) -> Any:
        return next((c for c in self.by_domain.values() if c.id == company_id), None)


class _FakeSessions:
    def __init__(self) -> None:
        self.rows: dict[str, SessionView] = {}

    async def add(
        self, *, user_id: UUID, tenant_id: UUID | None, refresh_jti: str, expires_at: datetime
    ) -> None:
        self.rows[refresh_jti] = SessionView(
            user_id=user_id,
            tenant_id=tenant_id,
            refresh_jti=refresh_jti,
            expires_at=expires_at,
            revoked_at=None,
        )

    async def get_by_jti(self, refresh_jti: str) -> SessionView | None:
        return self.rows.get(refresh_jti)

    async def revoke(self, refresh_jti: str, revoked_at: datetime) -> None:
        row = self.rows.get(refresh_jti)
        if row is not None and row.revoked_at is None:
            self.rows[refresh_jti] = SessionView(
                user_id=row.user_id,
                tenant_id=row.tenant_id,
                refresh_jti=row.refresh_jti,
                expires_at=row.expires_at,
                revoked_at=revoked_at,
            )


class _FakeAudit:
    def __init__(self) -> None:
        self.events: list[Any] = []

    async def append(self, event: Any) -> None:
        self.events.append(event)


class _StupidHasher:
    def __init__(self) -> None:
        # Zählt Aufrufe: die Zeitangleichung gegen Enumeration lässt sich so
        # deterministisch prüfen, ohne eine Uhr zu messen.
        self.hash_calls = 0

    def hash(self, plain: str) -> PasswordHash:
        self.hash_calls += 1
        return PasswordHash("h$" + plain)

    def verify(self, plain: str, hashed: PasswordHash) -> bool:
        return hashed.value == "h$" + plain


class _FakeTokens:
    """Merkt sich je jti, was ausgestellt wurde, und gibt es beim Prüfen zurück.

    Die frühere Fassung lieferte immer dieselbe Benutzer-ID und `tenant_id=None`,
    egal was ausgestellt worden war. Damit ließ sich über `handle_refresh` nichts
    aussagen, was mit dem Tenant zu tun hat — und genau dort saß eine Lücke:
    der Refresh übernahm den Tenant ungeprüft. Ein Fake, der die Eingabe
    wegwirft, kann den Fehler nicht finden, den er verstecken hilft.
    """

    def __init__(self) -> None:
        self.invalid_tokens: set[str] = set()
        self.issued_tenants: list[UUID | None] = []
        self._issued: dict[str, tuple[UUID, UUID | None, tuple[str, ...]]] = {}

    def issue_pair(
        self,
        *,
        user_id: UUID,
        tenant_id: UUID | None,
        roles: list[str],
        permissions: list[str],
        session_jti: str,
    ) -> TokenPair:
        self.issued_tenants.append(tenant_id)
        self._issued[session_jti] = (user_id, tenant_id, tuple(roles))
        # Encode jti into both tokens so verify_* can recover it.
        return TokenPair(access=f"acc:{session_jti}", refresh=f"ref:{session_jti}")

    def verify_refresh_token(self, token: str) -> AuthPrincipal:
        if token in self.invalid_tokens or not token.startswith("ref:"):
            raise ValueError("invalid")
        jti = token[len("ref:") :]
        user_id, tenant_id, roles = self._issued.get(
            jti, (UUID("00000000-0000-0000-0000-000000000001"), None, ("user",))
        )
        return AuthPrincipal(user_id=user_id, tenant_id=tenant_id, roles=roles, jti=jti)


class _FakeTokenRepo:
    """Spiegelt VerificationTokenRepository über ein dict, keyed by hash."""

    def __init__(self) -> None:
        self.rows: dict[str, Any] = {}

    async def add(self, token: Any) -> None:
        self.rows[token.token_hash] = token

    async def get_by_hash(self, token_hash: str) -> Any:
        return self.rows.get(token_hash)

    async def consume(self, token_id: Any, at: Any) -> None:
        for key, row in list(self.rows.items()):
            if row.token_id == token_id and row.consumed_at is None:
                self.rows[key] = replace(row, consumed_at=at)

    async def consume_open_for(self, user_id: Any, purpose: Any, at: Any) -> None:
        for key, row in list(self.rows.items()):
            if row.user_id == user_id and row.purpose is purpose and row.consumed_at is None:
                self.rows[key] = replace(row, consumed_at=at)


class _FakeMailer:
    def __init__(self) -> None:
        self.sent: list[tuple[str, str, str]] = []

    async def send(self, *, to: str, subject: str, body: str) -> None:
        self.sent.append((to, subject, body))


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
    public_web_url = "http://localhost:5173"


def _deps(
    clock: _Clock,
    tokens: _FakeTokens,
    bus: _Bus,
    hasher: _StupidHasher | None = None,
    mailer: _FakeMailer | None = None,
) -> dict[str, Any]:
    return {
        "hasher": hasher or _StupidHasher(),
        # Achtung: deps["tokens"] ist der JWT-Aussteller, repos["tokens"] das
        # Verifikations-Token-Repository. Gleicher Name, zwei Dinge.
        "tokens": tokens,
        "clock": clock,
        "eventbus": bus,
        "settings": _Settings(),
        "mailer": mailer or _FakeMailer(),
    }


async def test_register_creates_user_and_audit() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    deps = _deps(clock, tokens, bus, hasher)
    cmd = RegisterUserCommand(email="A@B.com", password="strongpassword1", display_name="A")

    res = await _register_via(cmd, deps=deps, repos=repos)

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


async def test_registering_a_known_address_reports_success_and_warns_the_owner() -> None:
    """Kein Enumerationskanal.

    Ein 409 würde "ist diese Person hier?" beantworten, ohne den Consent-Ledger
    zu fragen — auf einem Transfermarkt die Information, die jemanden den
    Arbeitsplatz kosten kann (product-scope.md: Auffindbarkeit gehört der
    Person). Die Antwort ist deshalb identisch; der echte Besitzer erfährt vom
    Versuch.
    """
    mailer = _FakeMailer()
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    cmd = RegisterUserCommand(email="dup@example.com", password="strongpassword1", display_name="D")
    await _register_via(cmd, deps=deps, repos=repos)
    mailer.sent.clear()

    second = await _register_via(cmd, deps=deps, repos=repos)

    assert is_success(second)
    # Kein zweites Konto und kein zweiter Bestätigungs-Token.
    assert len(repos["users"].by_email) == 1
    assert len(repos["tokens"].rows) == 1
    # Aber der Besitzer wird gewarnt.
    assert len(mailer.sent) == 1
    to, _subject, body = mailer.sent[0]
    assert to == "dup@example.com"
    assert "versucht" in body.lower()


async def test_login_success_returns_token_pair_and_persists_session_audit() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    # Seed a registered, activated user directly into the fake users repo —
    # login requires a confirmed account since Task 4.
    await _register_active(repos, _deps(clock, tokens, bus, hasher), "alice@example.com")
    repos["audit"].events.clear()
    bus.published.clear()

    res = await handle_login(
        AuthenticateUserCommand(email="ALICE@example.com", password="strongpassword1"),
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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }

    res = await handle_login(
        AuthenticateUserCommand(email="nobody@example.com", password="whatever12345"),
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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    seeded = await _register_via(
        RegisterUserCommand(
            email="bob@example.com",
            password="strongpassword1",
            display_name="Bob",
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    seeded_user = seeded.value
    repos["audit"].events.clear()
    bus.published.clear()

    res = await handle_login(
        AuthenticateUserCommand(email="bob@example.com", password="wrongpassword99"),
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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    seeded = await _register_via(
        RegisterUserCommand(
            email="cara@example.com",
            password="strongpassword1",
            display_name="Cara",
        ),
        deps=_deps(clock, tokens, bus, hasher),
        repos=repos,
    )
    cara = seeded.value
    cara.status = AccountStatus.SUSPENDED
    repos["audit"].events.clear()
    bus.published.clear()

    res = await handle_login(
        AuthenticateUserCommand(email="cara@example.com", password="strongpassword1"),
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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    await _register_active(repos, _deps(clock, tokens, bus, hasher), "rob@example.com")
    login = await handle_login(
        AuthenticateUserCommand(email="rob@example.com", password="strongpassword1"),
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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    await _register_active(repos, _deps(clock, tokens, bus, hasher), "x@example.com")
    login = await handle_login(
        AuthenticateUserCommand(email="x@example.com", password="strongpassword1"),
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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }

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
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    await _register_active(repos, _deps(clock, tokens, bus, hasher), "y@example.com")
    login = await handle_login(
        AuthenticateUserCommand(email="y@example.com", password="strongpassword1"),
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


# --- Tenant switching (ADR-0017) ------------------------------------------


async def _register(repos: dict[str, Any], deps: dict[str, Any], email: str) -> User:
    res = await _register_via(
        RegisterUserCommand(email=email, password="strongpassword1", display_name="X"),
        deps=deps,
        repos=repos,
    )
    user: User = res.value
    return user


async def _register_active(repos: dict[str, Any], deps: dict[str, Any], email: str) -> User:
    """Registrieren und sofort freischalten.

    Die E-Mail-Bestätigung hat ihre eigenen Tests; diese hier prüfen Login,
    Refresh und Revoke und wollen nur ein benutzbares Konto.
    """
    res = await _register_via(
        RegisterUserCommand(email=email, password="strongpassword1", display_name="X"),
        deps=deps,
        repos=repos,
    )
    user: User = res.value
    user.activate(now=deps["clock"].now())
    # A real command handler would persist + publish activation in its own
    # transaction before login's transaction ever reloads the user. _FakeUsers
    # hands back the same in-memory instance instead of a fresh aggregate, so
    # EmailVerified would otherwise still be sitting unpublished and leak into
    # whatever the caller's next handler (login) publishes. Drain it here.
    user.pull_events()
    return user


async def test_login_issues_a_token_without_a_tenant() -> None:
    # A person logging in is not acting for any company yet.
    tokens = _FakeTokens()
    clock, bus, hasher = _Clock(), _Bus(), _StupidHasher()
    deps = _deps(clock, tokens, bus, hasher)
    repos: dict[str, Any] = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    await _register_active(repos, deps, "solo@example.com")

    res = await handle_login(
        AuthenticateUserCommand(email="solo@example.com", password="strongpassword1"),
        deps=deps,
        repos=repos,
    )

    assert is_success(res)
    assert tokens.issued_tenants == [None]
    assert all(ev.tenant_id is None for ev in repos["audit"].events)


async def test_switching_to_a_company_you_belong_to_mints_a_tenant_token() -> None:
    tokens = _FakeTokens()
    clock, bus, hasher = _Clock(), _Bus(), _StupidHasher()
    deps = _deps(clock, tokens, bus, hasher)
    repos: dict[str, Any] = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    user = await _register_active(repos, deps, "recruiter@example.com")
    tenant = uuid4()
    repos["memberships"].members.add((user.id.value, tenant))
    repos["audit"].events.clear()
    tokens.issued_tenants.clear()

    res = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user.id.value, tenant_id=tenant), deps=deps, repos=repos
    )

    assert is_success(res)
    assert tokens.issued_tenants == [tenant]
    assert [ev.action for ev in repos["audit"].events] == [AuditAction.TENANT_SWITCH]
    assert repos["audit"].events[0].tenant_id == tenant


async def test_switching_to_a_company_you_do_not_belong_to_is_refused_and_audited() -> None:
    tokens = _FakeTokens()
    clock, bus, hasher = _Clock(), _Bus(), _StupidHasher()
    deps = _deps(clock, tokens, bus, hasher)
    repos: dict[str, Any] = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    user = await _register(repos, deps, "outsider@example.com")
    repos["audit"].events.clear()
    tokens.issued_tenants.clear()
    stranger = uuid4()

    res = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user.id.value, tenant_id=stranger), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "not_a_member"
    # No token was minted...
    assert tokens.issued_tenants == []
    # ...and the refusal is on the record, with the tenant that was asked for.
    assert [ev.action for ev in repos["audit"].events] == [AuditAction.TENANT_SWITCH_DENIED]
    assert repos["audit"].events[0].tenant_id == stranger


async def test_membership_of_one_company_does_not_grant_another() -> None:
    tokens = _FakeTokens()
    clock, bus, hasher = _Clock(), _Bus(), _StupidHasher()
    deps = _deps(clock, tokens, bus, hasher)
    repos: dict[str, Any] = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    user = await _register_active(repos, deps, "multi@example.com")
    allowed, forbidden = uuid4(), uuid4()
    repos["memberships"].members.add((user.id.value, allowed))

    ok = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user.id.value, tenant_id=allowed), deps=deps, repos=repos
    )
    denied = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user.id.value, tenant_id=forbidden), deps=deps, repos=repos
    )

    assert is_success(ok)
    assert not is_success(denied)


async def test_the_tenant_session_is_separate_from_the_person_session() -> None:
    # The person's refresh token keeps working; the tenant-bound one is its own
    # row, so it can be revoked without logging the person out entirely.
    tokens = _FakeTokens()
    clock, bus, hasher = _Clock(), _Bus(), _StupidHasher()
    deps = _deps(clock, tokens, bus, hasher)
    repos: dict[str, Any] = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    user = await _register_active(repos, deps, "two@example.com")
    tenant = uuid4()
    repos["memberships"].members.add((user.id.value, tenant))
    await handle_login(
        AuthenticateUserCommand(email="two@example.com", password="strongpassword1"),
        deps=deps,
        repos=repos,
    )

    await handle_switch_tenant(
        SwitchTenantCommand(user_id=user.id.value, tenant_id=tenant), deps=deps, repos=repos
    )

    rows = list(repos["sessions"].rows.values())
    assert len(rows) == 2
    assert sorted([r.tenant_id is None for r in rows]) == [False, True]


# --- E-Mail-Bestätigung -----------------------------------------------------


def _confirm_repos() -> dict[str, Any]:
    return {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }


def _raw_token_from(mailer: _FakeMailer) -> str:
    """Der Klartext existiert nur in der Mail — genau wie in Produktion."""
    body = mailer.sent[-1][2]
    return body.split("/verify?token=", 1)[1].split()[0]


async def test_registration_creates_a_token_and_sends_a_mail() -> None:
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)

    res = await _register_via(
        RegisterUserCommand(email="neu@example.com", password="strongpassword1", display_name="N"),
        deps=deps,
        repos=repos,
    )

    assert is_success(res)
    assert len(repos["tokens"].rows) == 1
    assert len(mailer.sent) == 1
    to, _subject, _body = mailer.sent[0]
    assert to == "neu@example.com"
    # Der Klartext steht in der Mail, in der Datenbank nur sein Hash.
    raw = _raw_token_from(mailer)
    assert hash_token(raw) in repos["tokens"].rows
    assert raw not in repos["tokens"].rows


async def test_verify_activates_the_account_and_consumes_the_token() -> None:
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="v@example.com", password="strongpassword1", display_name="V"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(mailer)
    repos["audit"].events.clear()

    res = await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    assert is_success(res)
    user = await repos["users"].get_by_email("v@example.com")
    assert user is not None and user.status is AccountStatus.ACTIVE
    assert repos["tokens"].rows[hash_token(raw)].is_consumed() is True
    assert repos["audit"].events[-1].action is AuditAction.EMAIL_VERIFIED


async def test_clicking_the_same_link_twice_is_not_an_error() -> None:
    """Der zweite Klick ist kein Fehler.

    Das Konto ist genau so freigeschaltet, wie der Klick es wollte — eine rote
    Fehlermeldung wäre eine Lüge über den Zustand. Wer den Token hat, hatte
    ohnehin die Mail, hier wird also nichts verraten.
    """
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="w@example.com", password="strongpassword1", display_name="W"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(mailer)
    await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    second = await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    assert is_success(second)
    user = await repos["users"].get_by_email("w@example.com")
    assert user is not None and user.status is AccountStatus.ACTIVE


async def test_an_expired_token_is_refused() -> None:
    mailer = _FakeMailer()
    repos = _confirm_repos()
    clock = _Clock()
    deps = _deps(clock, _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="x@example.com", password="strongpassword1", display_name="X"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(mailer)
    clock.advance(timedelta(hours=25))

    res = await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    assert not is_success(res)
    # Abgelaufen ist von ungültig unterscheidbar, damit die Oberfläche gezielt
    # "erneut senden" anbieten kann — der Token kennt ohnehin nur der Empfänger.
    assert fail_err(res).code == "token_expired"


async def test_an_unknown_token_is_refused() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus())

    res = await handle_verify_email(
        VerifyEmailCommand(token="niemals-ausgestellt"), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "token_invalid"


async def test_resend_invalidates_the_previous_token() -> None:
    """Sonst blieben beliebig viele gültige Links gleichzeitig in Umlauf, und
    der älteste — womöglich fehlgeleitete — funktionierte weiter."""
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="y@example.com", password="strongpassword1", display_name="Y"),
        deps=deps,
        repos=repos,
    )
    first = _raw_token_from(mailer)

    await _resend_via(ResendVerificationCommand(email="y@example.com"), deps=deps, repos=repos)
    second = _raw_token_from(mailer)

    assert first != second
    assert repos["tokens"].rows[hash_token(first)].is_consumed() is True
    assert repos["tokens"].rows[hash_token(second)].is_consumed() is False


async def test_the_old_link_stops_working_after_a_resend() -> None:
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="yy@example.com", password="strongpassword1", display_name="Y"),
        deps=deps,
        repos=repos,
    )
    first = _raw_token_from(mailer)
    await _resend_via(ResendVerificationCommand(email="yy@example.com"), deps=deps, repos=repos)

    res = await handle_verify_email(VerifyEmailCommand(token=first), deps=deps, repos=repos)

    assert not is_success(res)
    assert fail_err(res).code == "token_invalid"


async def test_resend_for_an_unknown_address_reports_success_without_sending() -> None:
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)

    res = await _resend_via(
        ResendVerificationCommand(email="niemand@example.com"), deps=deps, repos=repos
    )

    assert is_success(res)
    assert mailer.sent == []


async def test_resend_for_an_already_active_account_sends_nothing() -> None:
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="z@example.com", password="strongpassword1", display_name="Z"),
        deps=deps,
        repos=repos,
    )
    await handle_verify_email(
        VerifyEmailCommand(token=_raw_token_from(mailer)), deps=deps, repos=repos
    )
    mailer.sent.clear()

    res = await _resend_via(
        ResendVerificationCommand(email="z@example.com"), deps=deps, repos=repos
    )

    assert is_success(res)
    assert mailer.sent == []


async def test_a_failing_mailer_does_not_fail_the_registration() -> None:
    """Das Konto existiert, also darf die Antwort nicht behaupten, es sei
    schiefgegangen. Der Reparaturweg ist "erneut senden" (Spec §5)."""

    class _BrokenMailer:
        async def send(self, *, to: str, subject: str, body: str) -> None:
            raise OSError("smtp down")

    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus())
    deps["mailer"] = _BrokenMailer()

    res = await _register_via(
        RegisterUserCommand(email="m@example.com", password="strongpassword1", display_name="M"),
        deps=deps,
        repos=repos,
    )

    assert is_success(res)
    assert len(repos["users"].by_email) == 1
    assert len(repos["tokens"].rows) == 1


async def test_switching_tenant_with_an_unconfirmed_account_returns_a_result() -> None:
    """Regression: handle_switch_tenant fing nur AccountDisabled, sodass der
    neuere EmailNotConfirmed unbehandelt durchgeschlagen wäre statt ein
    Result.fail zu werden."""
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus())
    user = await _register(repos, deps, "pending@example.com")
    tenant = uuid4()
    repos["memberships"].members.add((user.id.value, tenant))

    res = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user.id.value, tenant_id=tenant), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "email_not_confirmed"


# --- Unternehmensanlage (ADR-0017/0018) -------------------------------------


async def _confirmed_user(repos: dict[str, Any], deps: dict[str, Any], email: str) -> User:
    """Registrieren und über den echten Token-Weg bestätigen."""
    await _register_via(
        RegisterUserCommand(email=email, password="strongpassword1", display_name="C"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(deps["mailer"])
    await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)
    user = await repos["users"].get_by_email(email)
    assert user is not None
    return user


async def test_creating_a_company_derives_the_domain_and_makes_the_creator_admin() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())
    user = await _confirmed_user(repos, deps, "anna@firma.de")
    repos["audit"].events.clear()

    res = await handle_create_company(
        CreateCompanyCommand(user_id=user.id.value, name="Firma GmbH"), deps=deps, repos=repos
    )

    assert is_success(res)
    # Die Domain kommt aus der Adresse, nicht aus dem Kommando.
    assert res.value.domain.value == "firma.de"
    assert repos["memberships"].added[0][2] is MembershipRole.ADMIN
    audit = repos["audit"].events[-1]
    assert audit.action is AuditAction.COMPANY_CREATED
    # Anders als persönliche Handlungen trägt diese Zeile einen Tenant.
    assert audit.tenant_id == res.value.id


async def test_a_pending_account_cannot_create_a_company() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())
    user = await _register(repos, deps, "unbestaetigt@firma.de")

    res = await handle_create_company(
        CreateCompanyCommand(user_id=user.id.value, name="Firma"), deps=deps, repos=repos
    )

    assert not is_success(res)
    # Eine unbestätigte Adresse beweist keine Domain.
    assert fail_err(res).code == "account_not_confirmed"


async def test_a_public_domain_cannot_be_claimed() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())
    user = await _confirmed_user(repos, deps, "max@gmail.com")

    res = await handle_create_company(
        CreateCompanyCommand(user_id=user.id.value, name="Nicht Google"), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "public_email_domain"


async def test_a_private_address_still_registers_and_confirms_normally() -> None:
    """Der Wechselwillige und der Arbeitssuchende brauchen kein Unternehmen —
    die Sperrliste gilt NUR beim Beanspruchen einer Domain."""
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())

    user = await _confirmed_user(repos, deps, "privat@gmx.de")

    assert user.status is AccountStatus.ACTIVE


async def test_a_taken_domain_is_refused() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())
    first = await _confirmed_user(repos, deps, "bob@firma.de")
    await handle_create_company(
        CreateCompanyCommand(user_id=first.id.value, name="Erste"), deps=deps, repos=repos
    )
    second = await _confirmed_user(repos, deps, "carla@firma.de")

    res = await handle_create_company(
        CreateCompanyCommand(user_id=second.id.value, name="Zweite"), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "domain_already_claimed"


async def test_a_blank_name_is_refused() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())
    user = await _confirmed_user(repos, deps, "leer@firma.de")

    res = await handle_create_company(
        CreateCompanyCommand(user_id=user.id.value, name="   "), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "invalid_company_name"


async def test_listing_memberships_returns_what_the_creator_got() -> None:
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=_FakeMailer())
    user = await _confirmed_user(repos, deps, "dora@firma.de")
    await handle_create_company(
        CreateCompanyCommand(user_id=user.id.value, name="Firma GmbH"), deps=deps, repos=repos
    )

    res = await handle_list_memberships(
        ListMembershipsQuery(user_id=user.id.value), deps=deps, repos=repos
    )

    assert is_success(res)
    assert len(res.value) == 1


# --- Grenzen, die der Code-Review aufgedeckt hat ----------------------------


async def test_the_handler_enqueues_instead_of_sending() -> None:
    """Der Versand gehört hinter den Commit.

    Sendete der Handler selbst, ginge der Bestätigungslink raus, bevor die
    Token-Zeile committet ist — ein fehlgeschlagener Commit ließe die Person
    mit einem Link auf nichts zurück.
    """
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    outbox: list[Any] = []

    await handle_register(
        RegisterUserCommand(email="q@example.com", password="strongpassword1", display_name="Q"),
        deps=deps,
        repos=repos,
        outbox=outbox,
    )

    assert len(outbox) == 1
    assert mailer.sent == [], "der Handler darf nichts verschicken"

    await dispatch_all(outbox, deps)

    assert len(mailer.sent) == 1


async def test_a_known_address_costs_the_same_work_as_a_new_one() -> None:
    """Kein Enumerationskanal über die Antwortzeit.

    Ein früher Ausstieg vor dem Hashing wäre in Bruchteilen der Zeit zurück —
    bcrypt mit 12 Runden braucht ~300 ms. Der gleiche Statuscode verbirgt dann
    nichts mehr. Gezählt statt gemessen, damit der Test nicht flackert.
    """
    hasher = _StupidHasher()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), hasher=hasher, mailer=_FakeMailer())
    cmd = RegisterUserCommand(
        email="gleich@example.com", password="strongpassword1", display_name="G"
    )
    await _register_via(cmd, deps=deps, repos=repos)
    calls_after_first = hasher.hash_calls

    await _register_via(cmd, deps=deps, repos=repos)

    assert hasher.hash_calls == calls_after_first + 1


async def test_an_unknown_login_costs_the_same_work_as_a_known_one() -> None:
    hasher = _StupidHasher()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), hasher=hasher, mailer=_FakeMailer())
    await _register_active(repos, deps, "bekannt@example.com")
    baseline = hasher.hash_calls

    await handle_login(
        AuthenticateUserCommand(email="unbekannt@example.com", password="strongpassword1"),
        deps=deps,
        repos=repos,
    )

    assert hasher.hash_calls == baseline + 1


async def test_a_token_invalidated_by_a_resend_stays_dead() -> None:
    """Verbraucht + Konto noch PENDING heißt: durch erneutes Senden entwertet.

    Anders als beim Doppelklick darf dieser Link NICHT mehr freischalten —
    sonst wäre die Entwertung wirkungslos und ein fehlgeleiteter alter Link
    weiterhin brauchbar.
    """
    mailer = _FakeMailer()
    repos = _confirm_repos()
    deps = _deps(_Clock(), _FakeTokens(), _Bus(), mailer=mailer)
    await _register_via(
        RegisterUserCommand(email="alt@example.com", password="strongpassword1", display_name="A"),
        deps=deps,
        repos=repos,
    )
    first = _raw_token_from(mailer)
    await _resend_via(ResendVerificationCommand(email="alt@example.com"), deps=deps, repos=repos)

    res = await handle_verify_email(VerifyEmailCommand(token=first), deps=deps, repos=repos)

    assert not is_success(res)
    assert fail_err(res).code == "token_invalid"
    user = await repos["users"].get_by_email("alt@example.com")
    assert user is not None and user.status is AccountStatus.PENDING


async def test_refresh_drops_the_company_when_the_membership_is_gone() -> None:
    """Ein entzogener Zugang darf sich nicht per Refresh verlängern.

    `handle_switch_tenant` prüft die Mitgliedschaft, bevor es einen
    Unternehmens-Token ausstellt. Wenn `handle_refresh` den Tenant danach blind
    aus dem alten Token übernimmt, ist diese Prüfung genau einmal wirksam: wer
    einmal drin war, bleibt drin, solange er alle 24 Stunden erneuert.

    Fallen gelassen wird nur der Tenant, nicht die Sitzung — die Person selbst
    ist ja weiterhin angemeldet. Sie handelt danach als Person, was der
    Normalzustand ist (ADR-0017).
    """
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    memberships = _FakeMemberships()
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": memberships,
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    deps = _deps(clock, tokens, bus, hasher)
    await _register_active(repos, deps, "ex@firma.example")
    login = await handle_login(
        AuthenticateUserCommand(email="ex@firma.example", password="strongpassword1"),
        deps=deps,
        repos=repos,
    )
    user_id = tokens.verify_refresh_token(login.value.refresh).user_id
    tenant_id = uuid4()
    await memberships.add(user_id, tenant_id, "admin")
    switched = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user_id, tenant_id=tenant_id),
        deps=deps,
        repos=repos,
    )
    assert is_success(switched)
    assert tokens.verify_refresh_token(switched.value.refresh).tenant_id == tenant_id

    # Der Zugang wird entzogen.
    memberships.members.discard((user_id, tenant_id))

    refreshed = await handle_refresh(
        RefreshTokenCommand(refresh_token=switched.value.refresh), deps=deps, repos=repos
    )

    assert is_success(refreshed)
    assert tokens.verify_refresh_token(refreshed.value.refresh).tenant_id is None


async def test_refresh_keeps_the_company_while_the_membership_holds() -> None:
    hasher = _StupidHasher()
    tokens = _FakeTokens()
    clock = _Clock()
    bus = _Bus()
    memberships = _FakeMemberships()
    repos = {
        "users": _FakeUsers(),
        "tokens": _FakeTokenRepo(),
        "companies": _FakeCompanies(),
        "memberships": memberships,
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
    }
    deps = _deps(clock, tokens, bus, hasher)
    await _register_active(repos, deps, "bleibt@firma.example")
    login = await handle_login(
        AuthenticateUserCommand(email="bleibt@firma.example", password="strongpassword1"),
        deps=deps,
        repos=repos,
    )
    user_id = tokens.verify_refresh_token(login.value.refresh).user_id
    tenant_id = uuid4()
    await memberships.add(user_id, tenant_id, "member")
    switched = await handle_switch_tenant(
        SwitchTenantCommand(user_id=user_id, tenant_id=tenant_id), deps=deps, repos=repos
    )

    refreshed = await handle_refresh(
        RefreshTokenCommand(refresh_token=switched.value.refresh), deps=deps, repos=repos
    )

    assert tokens.verify_refresh_token(refreshed.value.refresh).tenant_id == tenant_id
