"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork

from companies_service.configuration import CompaniesServiceSettings
from companies_service.infrastructure.clock import SystemClock
from companies_service.infrastructure.database.repositories import (
    SqlAlchemyCompanyProfileRepository,
)

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    uow = UnitOfWork(session_factory)
    async with uow:
        yield uow, {"profiles": SqlAlchemyCompanyProfileRepository(uow.session)}


def compose_infrastructure(
    settings: CompaniesServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
    }
