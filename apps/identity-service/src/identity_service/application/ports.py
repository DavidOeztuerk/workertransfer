"""Application-layer ports (interfaces) + shared DTOs used across commands."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Protocol
from uuid import UUID

from identity_service.domain.audit import AuditEvent
from identity_service.domain.company import Company
from identity_service.domain.membership import MembershipRole, MembershipView
from identity_service.domain.session import SessionView
from identity_service.domain.user import User
from identity_service.domain.verification import TokenPurpose, VerificationToken

__all__ = [
    "AuditRepository",
    "AuthPrincipal",
    "CompanyRepository",
    "Mailer",
    "MembershipRepository",
    "SessionRepository",
    "TokenPair",
    "UserRepository",
    "VerificationTokenRepository",
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
    # Aggregate kommen losgelöst aus dem Repository (_to_domain baut ein neues
    # Objekt). Eine Mutation am Aggregat erreicht die Datenbank deshalb NUR
    # über save() — ohne das ginge z. B. die Freischaltung lautlos verloren.
    async def save(self, user: User) -> None: ...


class MembershipRepository(Protocol):
    async def is_member(self, user_id: UUID, tenant_id: UUID) -> bool: ...
    async def list_for_user(self, user_id: UUID) -> list[UUID]: ...
    async def add(self, user_id: UUID, tenant_id: UUID, role: MembershipRole) -> None: ...
    async def list_for_user_detailed(self, user_id: UUID) -> list[MembershipView]: ...


class VerificationTokenRepository(Protocol):
    async def add(self, token: VerificationToken) -> None: ...
    async def get_by_hash(self, token_hash: str) -> VerificationToken | None: ...
    async def consume(self, token_id: UUID, at: datetime) -> None: ...
    async def consume_open_for(
        self, user_id: UUID, purpose: TokenPurpose, at: datetime
    ) -> None: ...


class CompanyRepository(Protocol):
    async def add(self, company: Company) -> None: ...
    async def get_by_domain(self, domain: str) -> Company | None: ...
    async def get_by_id(self, company_id: UUID) -> Company | None: ...


class SessionRepository(Protocol):
    async def add(
        self, *, user_id: UUID, tenant_id: UUID | None, refresh_jti: str, expires_at: datetime
    ) -> None: ...
    async def get_by_jti(self, refresh_jti: str) -> SessionView | None: ...
    async def revoke(self, refresh_jti: str, revoked_at: datetime) -> None: ...


class AuditRepository(Protocol):
    async def append(self, event: AuditEvent) -> None: ...


class Mailer(Protocol):
    """Versand ist ein Port, damit die Application kein SMTP kennt."""

    async def send(self, *, to: str, subject: str, body: str) -> None: ...
