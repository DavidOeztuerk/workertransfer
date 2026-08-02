"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork

from jobs_service.configuration import JobsServiceSettings
from jobs_service.infrastructure.clock import SystemClock
from jobs_service.infrastructure.database.repositories import SqlAlchemyJobRepository

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    """UoW plus Repositories an EINER Session — wie in den übrigen Diensten."""
    uow = UnitOfWork(session_factory)
    async with uow:
        yield uow, {"jobs": SqlAlchemyJobRepository(uow.session)}


def compose_infrastructure(settings: JobsServiceSettings, engine: AsyncEngine) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
    }
