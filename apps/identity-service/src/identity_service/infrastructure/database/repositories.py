"""SqlAlchemy implementations of the application repository ports."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

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
        stmt = select(UserModel).where(
            UserModel.tenant_id == tenant_id, UserModel.email == email.lower()
        )
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

    async def append(self, event: AuditEvent) -> None:
        self._session.add(
            AuditEventModel(
                actor_id=event.actor_id,
                tenant_id=event.tenant_id,
                action=event.action,
                target_id=event.target_id,
                correlation_id=event.correlation_id,
                occurred_at=event.occurred_at,
                meta=dict(event.metadata),
            )
        )
        await self._session.flush()
