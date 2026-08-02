"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork

from applications_service.configuration import ApplicationsServiceSettings
from applications_service.infrastructure.clock import SystemClock
from applications_service.infrastructure.consent import HttpConsentWriter
from applications_service.infrastructure.database.repositories import (
    SqlAlchemyApplicationRepository,
)
from applications_service.infrastructure.jobs import HttpJobLookup

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    uow = UnitOfWork(session_factory)
    async with uow:
        yield uow, {"applications": SqlAlchemyApplicationRepository(uow.session)}


def compose_infrastructure(
    settings: ApplicationsServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        "consent": HttpConsentWriter(base_url=settings.consent_base_url),
        "jobs": HttpJobLookup(base_url=settings.jobs_base_url),
    }
