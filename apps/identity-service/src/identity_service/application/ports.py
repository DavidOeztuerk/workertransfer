"""Application-layer ports (interfaces) + shared DTOs used across commands."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Protocol
from uuid import UUID

from identity_service.domain.audit import AuditEvent
from identity_service.domain.user import User
from identity_service.infrastructure.database.models import SessionModel

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
    async def add(
        self, *, user_id: UUID, tenant_id: UUID, refresh_jti: str, expires_at: datetime
    ) -> None: ...
    async def get_by_jti(self, refresh_jti: str) -> SessionModel | None: ...
    async def revoke(self, refresh_jti: str, revoked_at: datetime) -> None: ...


class AuditRepository(Protocol):
    async def append(self, event: AuditEvent) -> None: ...
