"""Testcontainers PostgreSQL fixture for consent-service (ADR-0011).

Skips the whole suite when Docker is unavailable — offline runs stay green
(skips, not failures). GitHub Actions' ubuntu-latest has Docker, so these
really execute there.
"""

from __future__ import annotations

from collections.abc import AsyncIterator

import pytest
import pytest_asyncio

# Importing models registers the tables on Base.metadata — the same module the
# alembic env.py imports, so both paths see an identical schema.
from consent_service.infrastructure.database import models  # noqa: F401
from consent_service.infrastructure.database.base import Base
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(
    not _docker_available(), reason="Docker not available (ADR-0011 offline-skip)"
)


@pytest.fixture(scope="session")
def postgres_url() -> AsyncIterator[str]:
    from testcontainers.postgres import PostgresContainer

    container = PostgresContainer("postgres:17-alpine", driver="asyncpg")
    container.start()
    url = container.get_connection_url()
    if not url.startswith("postgresql+asyncpg"):
        url = url.replace("postgresql://", "postgresql+asyncpg://")
    yield url
    container.stop()


@pytest_asyncio.fixture
async def engine(postgres_url: str) -> AsyncIterator[AsyncEngine]:
    eng = create_async_engine(postgres_url, pool_pre_ping=True)
    async with eng.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
        await conn.run_sync(Base.metadata.create_all)
    yield eng
    async with eng.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await eng.dispose()


@pytest_asyncio.fixture
async def session_factory(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False, autoflush=False)


@pytest_asyncio.fixture
async def session(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[AsyncSession]:
    async with session_factory() as s:
        yield s
