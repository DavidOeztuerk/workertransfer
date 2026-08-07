"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork
from worker_outbox import OutboxDispatcher, run_forever

from transfer_service.configuration import TransferServiceSettings
from transfer_service.infrastructure.clock import SystemClock
from transfer_service.infrastructure.consent import HttpConsentGate
from transfer_service.infrastructure.database.models import OUTBOX
from transfer_service.infrastructure.database.repositories import (
    SqlAlchemyMarketRequestRepository,
    SqlAlchemyMarketStatusRepository,
    SqlAlchemyTransferRepository,
)
from transfer_service.infrastructure.notify import HttpNotifier

__all__ = ["compose_infrastructure", "outbox_runner", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    uow = UnitOfWork(session_factory)
    async with uow:
        yield (
            uow,
            {
                "market": SqlAlchemyMarketStatusRepository(uow.session),
                "requests": SqlAlchemyMarketRequestRepository(uow.session),
                "transfers": SqlAlchemyTransferRepository(uow.session),
            },
        )


def compose_infrastructure(
    settings: TransferServiceSettings, engine: AsyncEngine
) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        # Bei jedem Fremdabruf gefragt, ohne Cache (ADR-0013).
        "consent": HttpConsentGate(base_url=settings.consent_base_url),
        # Nicht mehr im Anfragepfad: der Router schreibt die Absicht in die
        # Outbox, dieser Adapter stellt sie im Hintergrund zu (ADR-0025). Ein
        # Fehlschlag lässt die Zeile liegen, statt sie zu verlieren.
        "notify": HttpNotifier(
            base_url=settings.identity_base_url,
            secret=settings.notify_secret.get_secret_value(),
        ),
    }


def outbox_runner(deps: dict[str, Any], settings: TransferServiceSettings) -> Any:
    """Der Dauerläufer, den die App startet und beendet.

    Als Funktion, die eine Funktion zurückgibt, damit die Composition-Root
    entscheidet und nicht das Paket: `create_api_app` bekommt etwas
    Aufrufbares, kein Objekt mit Lebenszyklus.
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
