"""SqlAlchemy implementations of the application repository ports."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from identity_service.domain.audit import AuditEvent
from identity_service.domain.company import Company, EmailDomain
from identity_service.domain.membership import MembershipRole, MembershipView
from identity_service.domain.session import SessionView
from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash, UserId
from identity_service.domain.verification import TokenPurpose, VerificationToken
from identity_service.infrastructure.database.models import (
    AuditEventModel,
    EmailVerificationTokenModel,
    SessionModel,
    TenantModel,
    UserModel,
    UserTenantMembershipModel,
)


def _to_domain(row: UserModel) -> User:
    return User(
        id=UserId(row.id),
        email=Email(row.email),
        password_hash=PasswordHash(row.password_hash),
        display_name=row.display_name,
        roles=tuple(row.roles),
        status=AccountStatus(row.status),
    )


class SqlAlchemyUserRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_email(self, email: str) -> User | None:
        # Email is globally unique now (ADR-0017), so no tenant narrows this.
        stmt = select(UserModel).where(UserModel.email == email.lower())
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return _to_domain(row) if row is not None else None

    async def get_by_id(self, user_id: UUID) -> User | None:
        row = await self._session.get(UserModel, user_id)
        return _to_domain(row) if row is not None else None

    async def add(self, user: User) -> None:
        self._session.add(
            UserModel(
                id=user.id.value,
                email=user.email.value,
                password_hash=user.password_hash.value,
                display_name=user.display_name,
                status=user.status,
                roles=list(user.roles),
            )
        )
        await self._session.flush()

    async def save(self, user: User) -> None:
        """Schreibt die veränderlichen Felder des Aggregats zurück.

        _to_domain liefert ein losgelöstes Objekt, keine Session-gebundene Zeile.
        Ohne diesen Weg wäre jede Mutation (etwa User.activate) nur eine
        Änderung im Arbeitsspeicher und beim nächsten Request verschwunden.
        """
        row = await self._session.get(UserModel, user.id.value)
        if row is None:
            return
        row.status = user.status
        row.display_name = user.display_name
        row.roles = list(user.roles)
        await self._session.flush()


class SqlAlchemyMembershipRepository:
    """Reads which companies a person may act for. Writes belong to a future
    company-service — this slice only needs to answer "may they?"."""

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def is_member(self, user_id: UUID, tenant_id: UUID) -> bool:
        stmt = select(UserTenantMembershipModel.id).where(
            UserTenantMembershipModel.user_id == user_id,
            UserTenantMembershipModel.tenant_id == tenant_id,
        )
        return (await self._session.execute(stmt)).scalar_one_or_none() is not None

    async def list_for_user(self, user_id: UUID) -> list[UUID]:
        stmt = select(UserTenantMembershipModel.tenant_id).where(
            UserTenantMembershipModel.user_id == user_id
        )
        return list((await self._session.execute(stmt)).scalars().all())

    async def add(self, user_id: UUID, tenant_id: UUID, role: MembershipRole) -> None:
        self._session.add(
            UserTenantMembershipModel(user_id=user_id, tenant_id=tenant_id, role=role.value)
        )
        await self._session.flush()

    async def list_for_user_detailed(self, user_id: UUID) -> list[MembershipView]:
        stmt = (
            select(
                UserTenantMembershipModel.tenant_id,
                TenantModel.name,
                TenantModel.domain,
                UserTenantMembershipModel.role,
            )
            .join(TenantModel, TenantModel.id == UserTenantMembershipModel.tenant_id)
            .where(UserTenantMembershipModel.user_id == user_id)
        )
        rows = (await self._session.execute(stmt)).all()
        return [
            MembershipView(
                tenant_id=row.tenant_id,
                name=row.name,
                domain=row.domain,
                role=MembershipRole(row.role),
            )
            for row in rows
        ]


class SqlAlchemySessionRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(
        self, *, user_id: UUID, tenant_id: UUID | None, refresh_jti: str, expires_at: datetime
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

    async def get_by_jti(self, refresh_jti: str) -> SessionView | None:
        stmt = select(SessionModel).where(SessionModel.refresh_jti == refresh_jti)
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        if row is None:
            return None
        return SessionView(
            user_id=row.user_id,
            tenant_id=row.tenant_id,
            refresh_jti=row.refresh_jti,
            expires_at=row.expires_at,
            revoked_at=row.revoked_at,
        )

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


class SqlAlchemyVerificationTokenRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(self, token: VerificationToken) -> None:
        self._session.add(
            EmailVerificationTokenModel(
                id=token.token_id,
                user_id=token.user_id,
                token_hash=token.token_hash,
                purpose=token.purpose.value,
                expires_at=token.expires_at,
                consumed_at=token.consumed_at,
            )
        )
        await self._session.flush()

    async def get_by_hash(self, token_hash: str) -> VerificationToken | None:
        stmt = select(EmailVerificationTokenModel).where(
            EmailVerificationTokenModel.token_hash == token_hash
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        if row is None:
            return None
        return VerificationToken(
            token_id=row.id,
            user_id=row.user_id,
            token_hash=row.token_hash,
            purpose=TokenPurpose(row.purpose),
            expires_at=row.expires_at,
            consumed_at=row.consumed_at,
        )

    async def consume(self, token_id: UUID, at: datetime) -> None:
        row = await self._session.get(EmailVerificationTokenModel, token_id)
        if row is not None and row.consumed_at is None:
            row.consumed_at = at
            await self._session.flush()

    async def consume_open_for(self, user_id: UUID, purpose: TokenPurpose, at: datetime) -> None:
        """Entwertet offene Tokens, bevor ein neues ausgestellt wird — sonst
        blieben beliebig viele gültige Links gleichzeitig in Umlauf."""
        stmt = select(EmailVerificationTokenModel).where(
            EmailVerificationTokenModel.user_id == user_id,
            EmailVerificationTokenModel.purpose == purpose.value,
            EmailVerificationTokenModel.consumed_at.is_(None),
        )
        for row in (await self._session.execute(stmt)).scalars().all():
            row.consumed_at = at
        await self._session.flush()


class SqlAlchemyCompanyRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(self, company: Company) -> None:
        self._session.add(
            TenantModel(id=company.id, name=company.name, domain=company.domain.value)
        )
        await self._session.flush()

    async def get_by_domain(self, domain: str) -> Company | None:
        stmt = select(TenantModel).where(TenantModel.domain == domain)
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return self._to_domain(row) if row is not None else None

    async def get_by_id(self, company_id: UUID) -> Company | None:
        row = await self._session.get(TenantModel, company_id)
        return self._to_domain(row) if row is not None else None

    @staticmethod
    def _to_domain(row: TenantModel) -> Company:
        return Company(id=row.id, name=row.name, domain=EmailDomain(row.domain))
