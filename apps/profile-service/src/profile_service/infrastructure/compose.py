"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_ai import AnthropicDrafter, NullDrafter, TextDrafter
from worker_database import UnitOfWork

from profile_service.configuration import ProfileServiceSettings
from profile_service.infrastructure.clock import SystemClock
from profile_service.infrastructure.consent import HttpConsentGate
from profile_service.infrastructure.database.repositories import SqlAlchemyProfileRepository

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    """UoW plus Repositories an EINER Session — wie in identity und consent."""
    uow = UnitOfWork(session_factory)
    async with uow:
        yield uow, {"profiles": SqlAlchemyProfileRepository(uow.session)}


def compose_infrastructure(settings: ProfileServiceSettings, engine: AsyncEngine) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        # Der Ledger wird bei jedem Fremdabruf gefragt, ohne Cache (ADR-0013).
        "consent": HttpConsentGate(base_url=settings.consent_base_url),
        "drafter": _drafter(settings),
    }


def _drafter(settings: ProfileServiceSettings) -> TextDrafter:
    """Ohne Schlüssel ist die Funktion aus — und sagt das ehrlich.

    Die Voreinstellung ruft KEINEN fremden Dienst an. Eine, die es täte, würde
    den Text einer Person hinausschicken, weil jemand vergessen hat, etwas
    abzuschalten (ADR-0024).
    """
    key = settings.anthropic_api_key.get_secret_value()
    if not key:
        return NullDrafter()
    return AnthropicDrafter(api_key=key, model=settings.drafting_model)
