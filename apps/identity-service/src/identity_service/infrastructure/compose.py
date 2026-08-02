"""Composition-Root wiring for identity-service infrastructure (ADR-0003)."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork
from worker_events import EventBus

from identity_service.configuration import IdentityServiceSettings
from identity_service.domain.user import UserLoggedIn, UserRegistered
from identity_service.infrastructure.auth.hasher import BcryptPasswordAdapter
from identity_service.infrastructure.auth.jwt_service import JwTokenService
from identity_service.infrastructure.clock import SystemClock
from identity_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemyCompanyRepository,
    SqlAlchemyInvitationRepository,
    SqlAlchemyMembershipRepository,
    SqlAlchemySessionRepository,
    SqlAlchemyUserRepository,
    SqlAlchemyVerificationTokenRepository,
)
from identity_service.infrastructure.mail import SmtpMailer


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    """Yield a UoW + per-request repos bound to one session.

    Repositories are constructed with ``uow.session`` (the public property that
    raises RuntimeError if the UoW has not been entered) rather than the
    private ``uow._session``.
    """
    uow = UnitOfWork(session_factory)
    async with uow:
        repos = {
            "users": SqlAlchemyUserRepository(uow.session),
            "memberships": SqlAlchemyMembershipRepository(uow.session),
            "invitations": SqlAlchemyInvitationRepository(uow.session),
            "sessions": SqlAlchemySessionRepository(uow.session),
            "audit": SqlAlchemyAuditRepository(uow.session),
            "tokens": SqlAlchemyVerificationTokenRepository(uow.session),
            "companies": SqlAlchemyCompanyRepository(uow.session),
        }
        yield uow, repos


async def _noop_domain_event_handler(_event: Any) -> None:
    """Production seam for future domain-event side-effects (notifications, etc.).

    Phase 2 has no cross-service consumer for ``UserLoggedIn``/``UserRegistered``
    yet. Audit persistence is synchronous inside the command's UoW (ADR-0012) —
    it is NOT routed through here. This handler exists so a future side-effect
    handler has a place to hook in without touching the commands.
    """
    return None


def compose_infrastructure(
    settings: IdentityServiceSettings,
    engine: AsyncEngine,
    *,
    eventbus: EventBus | None = None,
) -> dict[str, Any]:
    session_factory: async_sessionmaker[AsyncSession] = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False, autoflush=False
    )
    bus = eventbus if eventbus is not None else EventBus()
    # Subscription seam (Task 22, ADR-0012): no-op handlers for the domain
    # events the Task-17 commands publish on this bus after a UoW commit.
    bus.subscribe(UserLoggedIn, _noop_domain_event_handler)
    bus.subscribe(UserRegistered, _noop_domain_event_handler)
    return {
        "engine": engine,
        "session_factory": session_factory,
        "request_scope": request_scope,
        "hasher": BcryptPasswordAdapter(rounds=settings.bcrypt_rounds),
        "tokens": JwTokenService(
            settings.jwt_secret.get_secret_value(),
            access_expire_minutes=settings.jwt_access_token_expire_minutes,
            refresh_expire_minutes=settings.jwt_refresh_token_expire_minutes,
        ),
        "clock": SystemClock(),
        "eventbus": bus,
        # settings.smtp_password is a SecretStr | None (never logged); calling
        # .get_secret_value() unconditionally on None crashes every service
        # start, so it is only unwrapped when a password is actually set.
        "mailer": SmtpMailer(
            host=settings.smtp_host,
            port=settings.smtp_port,
            mail_from=settings.mail_from,
            username=settings.smtp_username,
            password=settings.smtp_password.get_secret_value() if settings.smtp_password else None,
            use_tls=settings.smtp_use_tls,
        ),
    }
