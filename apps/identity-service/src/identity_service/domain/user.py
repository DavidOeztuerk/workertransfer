"""User aggregate and account lifecycle."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError, DomainEvent

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
        super().__init__(
            "user_already_exists", f"A user with email {email!r} already exists in this tenant"
        )


class InvalidCredentials(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_credentials", "Invalid credentials")


class AccountDisabled(DomainError):
    def __init__(self) -> None:
        super().__init__("account_disabled", "Account is not active")


def _event_dict(event: DomainEvent) -> dict[str, object]:
    return {k: getattr(event, k) for k in event.__dataclass_fields__}


@dataclass(frozen=True, slots=True)
class UserRegistered(DomainEvent):
    # kw_only required: DomainEvent base contributes event_id/occurred_at defaults,
    # own non-default fields would otherwise follow a default (init-ordering error).
    user_id: UUID = field(kw_only=True)
    tenant_id: UUID = field(kw_only=True)
    email: str = field(
        kw_only=True
    )  # PII: stays in the domain event, never crosses into AuditEvent

    def to_dict(self) -> dict[str, object]:
        base = _event_dict(self)
        base["event_id"] = str(self.event_id)
        base["user_id"] = str(self.user_id)
        base["tenant_id"] = str(self.tenant_id)
        base["occurred_at"] = self.occurred_at.isoformat()
        return base


@dataclass(frozen=True, slots=True)
class UserLoggedIn(DomainEvent):
    user_id: UUID = field(kw_only=True)
    tenant_id: UUID = field(kw_only=True)
    jti: str = field(kw_only=True)

    def to_dict(self) -> dict[str, object]:
        base = _event_dict(self)
        base["event_id"] = str(self.event_id)
        base["user_id"] = str(self.user_id)
        base["tenant_id"] = str(self.tenant_id)
        base["occurred_at"] = self.occurred_at.isoformat()
        return base


@dataclass(eq=False, slots=True)
class User:
    """User aggregate. Plain class (not a worker_core.Entity subclass) so its
    identity field can be the value-object ``UserId`` rather than the raw
    ``UUID`` the shared ``Entity`` base declares — overriding that base field
    type is mypy-strict-incompatible (incompatible assignment), and inheriting
    ``Entity.id``'s default would break the "non-default follows default"
    init-ordering rule. Identity equality + hash are hand-written against
    ``self.id.value`` (see ADR-aligned Phase-2 note in the plan, Task 9)."""

    id: UserId
    tenant_id: TenantId
    email: Email
    password_hash: PasswordHash
    display_name: str
    roles: tuple[str, ...]
    status: AccountStatus
    _events: list[DomainEvent] = field(default_factory=list)

    def __eq__(self, other: object) -> bool:
        return isinstance(other, User) and self.id == other.id

    def __hash__(self) -> int:
        return hash(self.id)

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
    ) -> User:
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
