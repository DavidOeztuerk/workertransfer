from datetime import UTC, datetime
from uuid import uuid4

import pytest
from identity_service.domain.user import (
    AccountDisabled,
    AccountStatus,
    User,
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
        tenant_id=TenantId(uuid4()),
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
