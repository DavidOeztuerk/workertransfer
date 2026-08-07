"""Composition der Infrastruktur: was die Handler an Adaptern bekommen."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker
from worker_database import UnitOfWork

from github_service.configuration import GithubServiceSettings
from github_service.infrastructure.clock import SystemClock
from github_service.infrastructure.consent import HttpConsentGate
from github_service.infrastructure.database.repositories import (
    SqlAlchemyGitHubConnectionRepository,
)
from github_service.infrastructure.github import HttpGitHub

__all__ = ["compose_infrastructure", "request_scope"]


@asynccontextmanager
async def request_scope(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[tuple[UnitOfWork, dict[str, Any]]]:
    uow = UnitOfWork(session_factory)
    async with uow:
        yield (uow, {"connections": SqlAlchemyGitHubConnectionRepository(uow.session)})


def compose_infrastructure(settings: GithubServiceSettings, engine: AsyncEngine) -> dict[str, Any]:
    return {
        "session_factory": async_sessionmaker(engine, expire_on_commit=False),
        "request_scope": request_scope,
        "clock": SystemClock(),
        # Bei jedem Fremdabruf gefragt, ohne Cache (ADR-0013).
        "consent": HttpConsentGate(base_url=settings.consent_base_url),
        # Nur auf Anstoß eines Menschen, nie von selbst.
        "github": HttpGitHub(token=settings.github_token.get_secret_value()),
        "settings": settings,
    }
