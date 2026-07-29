"""Verify the 0001_init revision applies cleanly via alembic upgrade head.

Uses the Alembic Python API (hermetic) rather than a subprocess — the plan
note (2610) flags the alembic CLI/subprocess path as fragile under the test
harness and names the Python API as preferred.
"""

from __future__ import annotations

import asyncio
import os
from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


def test_alembic_upgrade_head_creates_tables(postgres_url: str) -> None:
    service_dir = Path(__file__).resolve().parents[2]  # apps/identity-service
    cfg = Config()
    cfg.set_main_option("script_location", str(service_dir / "migrations"))
    # env.py reads WORKER_DATABASE_URL; set it in-process for the upgrade.
    prior = os.environ.get("WORKER_DATABASE_URL")
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    try:
        command.upgrade(cfg, "head")
    finally:
        if prior is None:
            os.environ.pop("WORKER_DATABASE_URL", None)
        else:
            os.environ["WORKER_DATABASE_URL"] = prior

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
