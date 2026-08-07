"""Composition-Root wiring for consent-service infrastructure (ADR-0003)."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_auth import TokenManager
from worker_database import UnitOfWork
from worker_events import EventBus

from consent_service.configuration import ConsentServiceSettings
from consent_service.infrastructure.clock import SystemClock
from consent_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemyConsentEventRepository,
)


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    """Yield a UoW plus per-request repos bound to one session.

    Both repos share the session, which is what makes the consent write and its
    audit row a single transaction (ADR-0012).
    """
    uow = UnitOfWork(session_factory)
    async with uow:
        repos = {
            "consent": SqlAlchemyConsentEventRepository(uow.session),
            "audit": SqlAlchemyAuditRepository(uow.session),
        }
        yield uow, repos


async def _noop_domain_event_handler(_event: Any) -> None:
    """Side-effect seam for future cross-service reactions.

    Audit is persisted synchronously inside the command's UoW and is NOT routed
    through here (ADR-0012). This exists so a later consumer — a notification, a
    projection rebuild — has somewhere to hook in without touching the commands.
    Phase 3's EventBus is purely in-process; the outbox is Phase 9.
    """
    return None


def compose_infrastructure(
    settings: ConsentServiceSettings,
    engine: AsyncEngine,
    *,
    eventbus: EventBus | None = None,
) -> dict[str, Any]:
    session_factory: async_sessionmaker[AsyncSession] = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False, autoflush=False
    )
    bus = eventbus if eventbus is not None else EventBus()
    return {
        "engine": engine,
        "session_factory": session_factory,
        "request_scope": request_scope,
        # This service verifies tokens; it never issues them. identity-service
        # signs with the same HS256 secret (ADR-0007/ADR-0015 — one trust domain
        # until the gateway arrives in Phase 10).
        "tokens": TokenManager(settings.jwt_secret.get_secret_value()),
        "clock": SystemClock(),
        "eventbus": bus,
        "domain_event_handler": _noop_domain_event_handler,
    }
