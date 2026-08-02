"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork

from transfer_service.configuration import TransferServiceSettings
from transfer_service.infrastructure.clock import SystemClock
from transfer_service.infrastructure.consent import HttpConsentGate
from transfer_service.infrastructure.database.repositories import (
    SqlAlchemyMarketStatusRepository,
)

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    uow = UnitOfWork(session_factory)
    async with uow:
        yield uow, {"market": SqlAlchemyMarketStatusRepository(uow.session)}


def compose_infrastructure(
    settings: TransferServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        # Bei jedem Fremdabruf gefragt, ohne Cache (ADR-0013).
        "consent": HttpConsentGate(base_url=settings.consent_base_url),
    }
