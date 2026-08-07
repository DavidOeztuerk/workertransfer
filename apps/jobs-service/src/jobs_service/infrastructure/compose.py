"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_ai import AnthropicDrafter, NullDrafter, TextDrafter
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
        "drafter": _drafter(settings),
    }


def _drafter(settings: JobsServiceSettings) -> TextDrafter:
    """Ohne Schlüssel ist die Funktion aus — dieselbe Regel wie im profile-service.

    Beide Dienste hängen an derselben Naht (`worker-ai`), aber jeder an seinem
    eigenen Schlüssel. Ein gemeinsamer Vermittler-Dienst wäre eine Hülle;
    geteilt ist das Paket, nicht ein Dienst (ADR-0024).
    """
    key = settings.anthropic_api_key.get_secret_value()
    if not key:
        return NullDrafter()
    return AnthropicDrafter(
        api_key=key, model=settings.drafting_model, base_url=settings.drafting_base_url
    )
