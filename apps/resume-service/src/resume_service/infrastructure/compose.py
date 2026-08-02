"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork

from resume_service.configuration import ResumeServiceSettings
from resume_service.infrastructure.clock import SystemClock
from resume_service.infrastructure.consent import HttpConsentGate
from resume_service.infrastructure.database.repositories import (
    SqlAlchemyResumeRepository,
    SqlAlchemyResumeRequestRepository,
)
from resume_service.infrastructure.notify import HttpNotifier

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    """UoW plus Repositories an EINER Session — wie in den übrigen Diensten."""
    uow = UnitOfWork(session_factory)
    async with uow:
        yield (
            uow,
            {
                "resumes": SqlAlchemyResumeRepository(uow.session),
                "requests": SqlAlchemyResumeRequestRepository(uow.session),
            },
        )


def compose_infrastructure(settings: ResumeServiceSettings, engine: AsyncEngine) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        # Der Ledger wird bei jedem Fremdabruf gefragt, ohne Cache (ADR-0013) —
        # und hier auch beschrieben, damit der Capability-String an genau einer
        # Stelle entsteht.
        "consent": HttpConsentGate(base_url=settings.consent_base_url),
        # Feuern und vergessen: ein Fehlschlag hier darf niemals den
        # Vorgang scheitern lassen, der ihn ausgelöst hat.
        "notify": HttpNotifier(
            base_url=settings.identity_base_url,
            secret=settings.notify_secret.get_secret_value(),
        ),
    }
