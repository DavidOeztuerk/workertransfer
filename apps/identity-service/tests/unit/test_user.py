from datetime import UTC, datetime

import pytest
from identity_service.domain.user import (
    AccountDisabled,
    AccountStatus,
    AlreadyActive,
    EmailNotConfirmed,
    User,
    UserLoggedIn,
    UserRegistered,
)
from identity_service.domain.value_objects import Email, PasswordHash, UserId


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


def test_register_creates_pending_user_with_event() -> None:
    # Email confirmation gates activation now — a freshly registered account
    # is not usable until activate() runs (see test_activate_* below).
    user = User.register(
        email=Email("alice@example.com"),
        password_hash=PasswordHash("$2b$12$x"),
        display_name="Alice",
        now=_now(),
    )
    assert user.status is AccountStatus.PENDING
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
        now=_now(),
    )
    assert user.verify_password("s3cret", hasher) is True
    assert user.verify_password("wrong", hasher) is False


def test_assert_can_log_in_requires_active() -> None:
    user = User.register(
        email=Email("c@example.com"),
        password_hash=PasswordHash("$2b$12$y"),
        display_name="C",
        now=_now(),
    )
    user.status = AccountStatus.SUSPENDED
    with pytest.raises(AccountDisabled):
        user.assert_can_log_in()
    user.status = AccountStatus.ACTIVE
    user.assert_can_log_in()  # no raise


def test_record_login_emits_event_and_does_not_store_password() -> None:
    # Command-Handler boundary: register() and record_login() belong to
    # separate command handlers (RegisterUserHandler / LoginHandler), each
    # with its own pull_events() at its persistence point. The domain
    # aggregate accumulates UserRegistered + UserLoggedIn in the same backlog
    # only when no pull is performed between them — which never happens in a
    # real command flow, where the repo reloads a fresh (event-empty) User
    # before calling record_login(). This test mirrors that boundary.
    user = User.register(
        email=Email("d@example.com"),
        password_hash=PasswordHash("$2b$12$z"),
        display_name="D",
        now=_now(),
    )
    # Command-Handler 1: RegisterUserHandler pulls its own event at persist.
    registered_events = user.pull_events()
    assert len(registered_events) == 1
    assert isinstance(registered_events[0], UserRegistered)

    # Command-Handler 2: LoginHandler (separate transaction, fresh aggregate).
    user.record_login(jti="jti-1", now=_now())
    login_events = user.pull_events()
    assert len(login_events) == 1
    assert isinstance(login_events[0], UserLoggedIn)
    assert login_events[0].jti == "jti-1"
    # the event payload never carries the password or plaintext:
    assert "password" not in login_events[0].to_dict()


async def test_registration_starts_pending() -> None:
    user = User.register(
        email=Email("p@example.com"),
        password_hash=PasswordHash("$2b$12$p"),
        display_name="P",
        now=_now(),
    )

    assert user.status is AccountStatus.PENDING


async def test_activate_makes_the_account_usable_and_emits_an_event() -> None:
    user = User.register(
        email=Email("q@example.com"),
        password_hash=PasswordHash("$2b$12$q"),
        display_name="Q",
        now=_now(),
    )
    user.pull_events()

    user.activate(now=_now())

    assert user.status is AccountStatus.ACTIVE
    events = user.pull_events()
    assert [type(e).__name__ for e in events] == ["EmailVerified"]


async def test_activating_twice_is_refused() -> None:
    user = User.register(
        email=Email("r@example.com"),
        password_hash=PasswordHash("$2b$12$r"),
        display_name="R",
        now=_now(),
    )
    user.activate(now=_now())

    with pytest.raises(AlreadyActive):
        user.activate(now=_now())


async def test_a_pending_account_is_refused_with_a_distinguishable_error() -> None:
    # EmailNotConfirmed statt AccountDisabled: der Router braucht den
    # Unterschied, um 403 statt 401 zu antworten (Spec §4.4). Ein gesperrtes
    # Konto und ein unbestätigtes sind nicht dasselbe Problem.
    user = User.register(
        email=Email("s@example.com"),
        password_hash=PasswordHash("$2b$12$s"),
        display_name="S",
        now=_now(),
    )

    with pytest.raises(EmailNotConfirmed):
        user.assert_can_log_in()


async def test_a_disabled_account_stays_generic() -> None:
    user = User.register(
        email=Email("t@example.com"),
        password_hash=PasswordHash("$2b$12$t"),
        display_name="T",
        now=_now(),
    )
    user.activate(now=_now())
    user.status = AccountStatus.DISABLED

    with pytest.raises(AccountDisabled):
        user.assert_can_log_in()
