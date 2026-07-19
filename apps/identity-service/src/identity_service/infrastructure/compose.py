"""Composition-Root wiring for identity-service infrastructure (ADR-0003)."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork
from worker_events import EventBus

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.auth.hasher import BcryptPasswordAdapter
from identity_service.infrastructure.auth.jwt_service import JwTokenService
from identity_service.infrastructure.clock import SystemClock
from identity_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemySessionRepository,
    SqlAlchemyUserRepository,
)


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
            "sessions": SqlAlchemySessionRepository(uow.session),
            "audit": SqlAlchemyAuditRepository(uow.session),
        }
        yield uow, repos


def compose_infrastructure(
    settings: IdentityServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    session_factory: async_sessionmaker[AsyncSession] = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False, autoflush=False
    )
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
        "eventbus": EventBus(),
    }
