"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork
from worker_storage import LocalStorage

from portfolio_service.configuration import PortfolioServiceSettings
from portfolio_service.infrastructure.clock import SystemClock
from portfolio_service.infrastructure.consent import HttpConsentGate
from portfolio_service.infrastructure.database.repositories import SqlAlchemyPortfolioRepository

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    """UoW plus Repositories an EINER Session — wie in den übrigen Diensten."""
    uow = UnitOfWork(session_factory)
    async with uow:
        yield uow, {"portfolios": SqlAlchemyPortfolioRepository(uow.session)}


def compose_infrastructure(
    settings: PortfolioServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        # Bei jedem Fremdabruf gefragt, ohne Cache (ADR-0013).
        "consent": HttpConsentGate(base_url=settings.consent_base_url),
        # Erster echter Konsument der Ablage (ADR-0021). LocalStorage, weil es
        # das Backend ist, das läuft; `Storage` ist die Naht für ein zweites.
        "storage": LocalStorage(Path(settings.storage_root)),
        "max_attachment_bytes": settings.max_attachment_bytes,
    }
