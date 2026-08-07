"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork
from worker_outbox import OutboxDispatcher, run_forever

from applications_service.configuration import ApplicationsServiceSettings
from applications_service.infrastructure.clock import SystemClock
from applications_service.infrastructure.consent import HttpConsentWriter
from applications_service.infrastructure.database.models import OUTBOX
from applications_service.infrastructure.database.repositories import (
    SqlAlchemyApplicationRepository,
)
from applications_service.infrastructure.jobs import HttpJobLookup
from applications_service.infrastructure.notify import HttpNotifier

__all__ = ["compose_infrastructure", "outbox_runner", "request_scope"]


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
        # Feuern und vergessen: ein Fehlschlag hier darf niemals den
        # Vorgang scheitern lassen, der ihn ausgelöst hat.
        "notify": HttpNotifier(
            base_url=settings.identity_base_url,
            secret=settings.notify_secret.get_secret_value(),
        ),
        "jobs": HttpJobLookup(base_url=settings.jobs_base_url),
    }


def outbox_runner(deps: dict[str, Any], settings: ApplicationsServiceSettings) -> Any:
    """Der Dauerläufer, den die App startet und beendet (ADR-0025).

    Wortgleich zu transfer-service. Bewusst kopiert und nicht geteilt: es sind
    zehn Zeilen Verdrahtung in einer Composition-Root, und ein gemeinsames
    Paket dafür wäre ein Kopplungspunkt über eine Dienstgrenze (ADR-0003/0004).
    """
    dispatcher = OutboxDispatcher(
        session_factory=deps["session_factory"],
        table=OUTBOX,
        delivery=deps["notify"],
    )

    async def run() -> None:
        await run_forever(
            dispatcher,
            interval_seconds=settings.outbox_interval_seconds,
            sleep=asyncio.sleep,
        )

    return run
