"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork
from worker_outbox import OutboxDispatcher, run_forever

from resume_service.configuration import ResumeServiceSettings
from resume_service.infrastructure.clock import SystemClock
from resume_service.infrastructure.consent import HttpConsentGate
from resume_service.infrastructure.database.models import OUTBOX
from resume_service.infrastructure.database.repositories import (
    SqlAlchemyResumeRepository,
    SqlAlchemyResumeRequestRepository,
)
from resume_service.infrastructure.notify import HttpNotifier

__all__ = ["compose_infrastructure", "outbox_runner", "request_scope"]


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


def outbox_runner(deps: dict[str, Any], settings: ResumeServiceSettings) -> Any:
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
