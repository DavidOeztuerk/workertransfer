"""SqlAlchemy implementations of the application repository ports."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import delete, select
from sqlalchemy.ext.asyncio import AsyncSession

from identity_service.domain.audit import AuditEvent
from identity_service.domain.company import Company, EmailDomain
from identity_service.domain.invitation import Invitation, InvitationStatus
from identity_service.domain.membership import MembershipRole, MembershipView
from identity_service.domain.notification import NotificationKind, NotificationPreference
from identity_service.domain.session import SessionView
from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash, UserId
from identity_service.domain.verification import TokenPurpose, VerificationToken
from identity_service.infrastructure.database.models import (
    AuditEventModel,
    EmailVerificationTokenModel,
    InvitationModel,
    NotificationPreferenceModel,
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
        # Alle veränderlichen Felder, nicht nur die, die heute jemand ändert:
        # ein künftiger Passwort-Wechsel würde sonst lautlos verlorengehen —
        # dieselbe Falle, gegen die diese Methode existiert.
        row.email = user.email.value
        row.password_hash = user.password_hash.value
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

    async def role_of(self, user_id: UUID, tenant_id: UUID) -> MembershipRole | None:
        """Die Rolle, oder `None` wenn keine Mitgliedschaft besteht.

        Getrennt von `is_member`, weil der Aufrufer beides braucht und ein
        `is_member` mit anschließendem Rollen-Lookup zwei Abfragen wären, die
        auseinanderlaufen können.
        """
        stmt = select(UserTenantMembershipModel.role).where(
            UserTenantMembershipModel.user_id == user_id,
            UserTenantMembershipModel.tenant_id == tenant_id,
        )
        role = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if role is None else MembershipRole(role)

    async def count_admins_for_update(self, tenant_id: UUID) -> int:
        """Zählt die Administratoren und sperrt die Zeilen bis zum Commit.

        Ohne die Sperre könnten zwei Administratoren, die gleichzeitig
        „Verlassen" drücken, beide zwei sehen und beide durchkommen — das
        Unternehmen bliebe ohne Administrator zurück, also genau in dem
        verwaisten Zustand, den die Regel verhindern soll. Der Fall ist selten,
        die Folge unumkehrbar ohne Datenbankzugriff; deshalb wird er gesperrt
        und nicht gehofft.
        """
        stmt = (
            select(UserTenantMembershipModel.id)
            .where(
                UserTenantMembershipModel.tenant_id == tenant_id,
                UserTenantMembershipModel.role == str(MembershipRole.ADMIN),
            )
            .with_for_update()
        )
        return len((await self._session.execute(stmt)).all())

    async def remove(self, user_id: UUID, tenant_id: UUID) -> None:
        await self._session.execute(
            delete(UserTenantMembershipModel).where(
                UserTenantMembershipModel.user_id == user_id,
                UserTenantMembershipModel.tenant_id == tenant_id,
            )
        )

    async def list_members(self, tenant_id: UUID) -> list[tuple[UUID, str, MembershipRole]]:
        stmt = (
            select(
                UserTenantMembershipModel.user_id,
                UserModel.display_name,
                UserTenantMembershipModel.role,
            )
            .join(UserModel, UserModel.id == UserTenantMembershipModel.user_id)
            .where(UserTenantMembershipModel.tenant_id == tenant_id)
            .order_by(UserTenantMembershipModel.granted_at)
        )
        rows = (await self._session.execute(stmt)).all()
        return [(row[0], row[1], MembershipRole(row[2])) for row in rows]

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


def _invitation_to_domain(row: InvitationModel) -> Invitation:
    return Invitation(
        id=row.id,
        tenant_id=row.tenant_id,
        email=Email(row.email),
        role=MembershipRole(row.role),
        invited_by=row.invited_by,
        status=InvitationStatus(row.status),
        created_at=row.created_at,
        expires_at=row.expires_at,
        accepted_at=row.accepted_at,
    )


class SqlAlchemyInvitationRepository:
    """Der Token lebt nur als Hash hier.

    Der Klartext geht per Mail raus. Wer die Datenbank liest, kann damit keine
    Einladung annehmen — dieselbe Regel wie bei den Bestätigungs-Tokens.
    """

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(self, invitation: Invitation, token_hash: str) -> None:
        self._session.add(
            InvitationModel(
                id=invitation.id,
                tenant_id=invitation.tenant_id,
                email=invitation.email.value,
                role=str(invitation.role),
                invited_by=invitation.invited_by,
                status=str(invitation.status),
                token_hash=token_hash,
                created_at=invitation.created_at,
                expires_at=invitation.expires_at,
                accepted_at=invitation.accepted_at,
            )
        )

    async def get_by_token_hash(self, token_hash: str) -> Invitation | None:
        stmt = select(InvitationModel).where(InvitationModel.token_hash == token_hash)
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _invitation_to_domain(row)

    async def get(self, invitation_id: UUID) -> Invitation | None:
        row = await self._session.get(InvitationModel, invitation_id)
        return None if row is None else _invitation_to_domain(row)

    async def find_open(self, tenant_id: UUID, email: Email) -> Invitation | None:
        stmt = select(InvitationModel).where(
            InvitationModel.tenant_id == tenant_id,
            InvitationModel.email == email.value,
            InvitationModel.status == str(InvitationStatus.PENDING),
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _invitation_to_domain(row)

    async def list_open(self, tenant_id: UUID) -> list[Invitation]:
        stmt = (
            select(InvitationModel)
            .where(
                InvitationModel.tenant_id == tenant_id,
                InvitationModel.status == str(InvitationStatus.PENDING),
            )
            .order_by(InvitationModel.created_at.desc())
        )
        rows = (await self._session.execute(stmt)).scalars()
        return [_invitation_to_domain(row) for row in rows]

    async def save(self, invitation: Invitation) -> None:
        row = await self._session.get(InvitationModel, invitation.id)
        if row is None:
            return
        row.status = str(invitation.status)
        row.accepted_at = invitation.accepted_at


class SqlAlchemyNotificationPreferenceRepository:
    """Fehlt die Zeile, gilt die Voreinstellung — sie wird nicht vorsorglich angelegt.

    Eine Zeile je Konto bei der Registrierung zu schreiben hieße, für jeden
    Menschen einen Datensatz zu führen, der nichts aussagt, was die
    Voreinstellung nicht auch sagt.
    """

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, user_id: UUID) -> NotificationPreference:
        row = await self._session.get(NotificationPreferenceModel, user_id)
        if row is None:
            return NotificationPreference.default(user_id)
        return NotificationPreference(
            user_id=row.user_id,
            kinds={
                NotificationKind.RESUME_REQUEST: row.resume_request,
                NotificationKind.MARKET_REQUEST: row.market_request,
                NotificationKind.APPLICATION_UPDATE: row.application_update,
                NotificationKind.TRANSFER_UPDATE: row.transfer_update,
            },
            last_sent_at=row.last_sent_at,
        )

    async def save(self, preference: NotificationPreference) -> None:
        row = await self._session.get(NotificationPreferenceModel, preference.user_id)
        if row is None:
            row = NotificationPreferenceModel(user_id=preference.user_id)
            self._session.add(row)
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.resume_request = preference.wants(NotificationKind.RESUME_REQUEST)
        row.market_request = preference.wants(NotificationKind.MARKET_REQUEST)
        row.application_update = preference.wants(NotificationKind.APPLICATION_UPDATE)
        row.transfer_update = preference.wants(NotificationKind.TRANSFER_UPDATE)
        row.last_sent_at = preference.last_sent_at
