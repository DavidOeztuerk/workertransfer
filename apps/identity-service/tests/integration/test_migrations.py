"""Verify the 0001_init revision applies cleanly via alembic upgrade head.

Uses the Alembic Python API (hermetic) rather than a subprocess — the plan
note (2610) flags the alembic CLI/subprocess path as fragile under the test
harness and names the Python API as preferred.
"""

from __future__ import annotations

import asyncio
import os
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path
from uuid import uuid4

import pytest
from alembic import command
from alembic.config import Config
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_MIGRATIONS_ROOT = Path(__file__).resolve().parents[2]  # apps/identity-service


def _alembic_config() -> Config:
    cfg = Config()
    cfg.set_main_option("script_location", str(_MIGRATIONS_ROOT / "migrations"))
    return cfg


@contextmanager
def _worker_database_url(postgres_url: str) -> Iterator[None]:
    # env.py reads WORKER_DATABASE_URL; set it in-process for the duration of
    # the alembic command(s), then restore whatever was there before.
    prior = os.environ.get("WORKER_DATABASE_URL")
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    try:
        yield
    finally:
        if prior is None:
            os.environ.pop("WORKER_DATABASE_URL", None)
        else:
            os.environ["WORKER_DATABASE_URL"] = prior


def test_alembic_upgrade_head_creates_tables(postgres_url: str) -> None:
    cfg = _alembic_config()
    with _worker_database_url(postgres_url):
        command.upgrade(cfg, "head")

    async def _check() -> None:
        eng = create_async_engine(postgres_url)
        async with eng.connect() as conn:
            rows = (
                await conn.execute(
                    text("SELECT tablename FROM pg_tables WHERE schemaname='public'")
                )
            ).all()
            names = {row[0] for row in rows}
            assert {"users", "sessions", "audit_events"} <= names
        await eng.dispose()

    asyncio.run(_check())


def test_orphaned_membership_gets_a_placeholder_tenant(postgres_url: str) -> None:
    """Von Hand eingefügte Mitgliedschaften dürfen die Migration nicht sprengen
    und nicht stillschweigend verschwinden.

    Genau dieser Fall existiert in jeder Entwicklungsdatenbank: der Smoke-Test
    hat Mitgliedschaften per INSERT angelegt, bevor es `tenants` gab.
    """
    cfg = _alembic_config()
    user_id, orphan_tenant = uuid4(), uuid4()

    with _worker_database_url(postgres_url):
        command.downgrade(cfg, "base")
        command.upgrade(cfg, "0002_tenant_optional_memberships")

        async def _insert_orphan() -> None:
            eng = create_async_engine(postgres_url)
            try:
                async with eng.begin() as conn:
                    await conn.execute(
                        text(
                            "INSERT INTO users "
                            "(id, email, password_hash, display_name, status, roles) "
                            "VALUES (:u, 'orphan@example.com', 'x', 'O', 'active', '[]'::jsonb)"
                        ),
                        {"u": str(user_id)},
                    )
                    await conn.execute(
                        text(
                            "INSERT INTO user_tenant_memberships (id, user_id, tenant_id) "
                            "VALUES (gen_random_uuid(), :u, :t)"
                        ),
                        {"u": str(user_id), "t": str(orphan_tenant)},
                    )
            finally:
                await eng.dispose()

        asyncio.run(_insert_orphan())

        command.upgrade(cfg, "head")

    async def _check() -> tuple[int, str]:
        eng = create_async_engine(postgres_url)
        try:
            async with eng.connect() as conn:
                kept = (
                    await conn.execute(
                        text("SELECT count(*) FROM user_tenant_memberships WHERE tenant_id = :t"),
                        {"t": str(orphan_tenant)},
                    )
                ).scalar_one()
                placeholder = (
                    await conn.execute(
                        text("SELECT domain FROM tenants WHERE id = :t"), {"t": str(orphan_tenant)}
                    )
                ).scalar_one()
            return kept, placeholder
        finally:
            await eng.dispose()

    kept, placeholder = asyncio.run(_check())

    assert kept == 1, "die Mitgliedschaft wurde stillschweigend gelöscht"
    assert placeholder.endswith(".invalid")
