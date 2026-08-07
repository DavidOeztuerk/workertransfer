"""Testcontainers PostgreSQL fixture for identity-service (ADR-0011).

Skips the whole suite if Docker/testcontainers is unavailable (offline runs
are green-equivalent: skips, not fails).
"""

from __future__ import annotations

from collections.abc import AsyncIterator

import pytest
import pytest_asyncio
from identity_service.infrastructure.database import models  # noqa: F401
from sqlalchemy import text
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)

# models must import so Base.metadata has our tables *and* so alembic env.py
# sees them identically (env.py imports the same models module).
from worker_database import Base

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
    # Normalize to the asyncpg driver suffix that async engines expect.
    if not url.startswith("postgresql+asyncpg"):
        url = url.replace("postgresql://", "postgresql+asyncpg://")
    yield url
    container.stop()


@pytest_asyncio.fixture
async def engine(postgres_url: str) -> AsyncIterator[AsyncEngine]:
    # Fresh schema per test via Base.metadata create/drop (Task 16 keeps a
    # separate Alembic-applied path for the migration correctness test).
    eng = create_async_engine(postgres_url, pool_pre_ping=True)
    async with eng.begin() as conn:
        # Base.metadata.create_all does not emit CREATE EXTENSION; the users
        # table uses CITEXT, so enable the citext contrib before create_all.
        await conn.execute(text("CREATE EXTENSION IF NOT EXISTS citext"))
        await conn.run_sync(Base.metadata.drop_all)
        await conn.run_sync(Base.metadata.create_all)
    yield eng
    async with eng.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await eng.dispose()


@pytest_asyncio.fixture
async def session_factory(
    engine: AsyncEngine,
) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False, autoflush=False)


@pytest_asyncio.fixture
async def session(
    session_factory: async_sessionmaker[AsyncSession],
) -> AsyncIterator[AsyncSession]:
    async with session_factory() as s:
        yield s
