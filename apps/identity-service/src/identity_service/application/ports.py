"""Application-layer ports (interfaces) + shared DTOs used across commands."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Protocol
from uuid import UUID

from identity_service.domain.audit import AuditEvent
from identity_service.domain.session import SessionView
from identity_service.domain.user import User

__all__ = [
    "AuditRepository",
    "AuthPrincipal",
    "MembershipRepository",
    "SessionRepository",
    "TokenPair",
    "UserRepository",
]


@dataclass(frozen=True, slots=True)
class AuthPrincipal:
    user_id: UUID
    # None while acting as a natural person; set only after switching to a
    # company the user is a verified member of (ADR-0017).
    tenant_id: UUID | None
    roles: tuple[str, ...]
    jti: str  # the session jti — used by handle_refresh to key the sessions table


@dataclass(frozen=True, slots=True)
class TokenPair:
    access: str
    refresh: str


class UserRepository(Protocol):
    # No tenant: email identifies a person globally (ADR-0017).
    async def get_by_email(self, email: str) -> User | None: ...
    async def get_by_id(self, user_id: UUID) -> User | None: ...
    async def add(self, user: User) -> None: ...


class MembershipRepository(Protocol):
    async def is_member(self, user_id: UUID, tenant_id: UUID) -> bool: ...
    async def list_for_user(self, user_id: UUID) -> list[UUID]: ...


class SessionRepository(Protocol):
    async def add(
        self, *, user_id: UUID, tenant_id: UUID | None, refresh_jti: str, expires_at: datetime
    ) -> None: ...
    async def get_by_jti(self, refresh_jti: str) -> SessionView | None: ...
    async def revoke(self, refresh_jti: str, revoked_at: datetime) -> None: ...


class AuditRepository(Protocol):
    async def append(self, event: AuditEvent) -> None: ...
