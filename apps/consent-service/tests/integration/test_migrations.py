"""Verify 0001_init_consent applies cleanly via alembic upgrade head.

Uses the Alembic Python API rather than a subprocess (hermetic, and the CLI path
is fragile under the test harness).
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


def _upgrade(postgres_url: str) -> None:
    service_dir = Path(__file__).resolve().parents[2]  # apps/consent-service
    cfg = Config()
    cfg.set_main_option("script_location", str(service_dir / "migrations"))
    prior = os.environ.get("WORKER_DATABASE_URL")
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    try:
        command.upgrade(cfg, "head")
    finally:
        if prior is None:
            os.environ.pop("WORKER_DATABASE_URL", None)
        else:
            os.environ["WORKER_DATABASE_URL"] = prior


def test_upgrade_head_creates_the_ledger_tables(postgres_url: str) -> None:
    _upgrade(postgres_url)

    async def _check() -> None:
        eng = create_async_engine(postgres_url)
        try:
            async with eng.connect() as conn:
                tables = {
                    row[0]
                    for row in (
                        await conn.execute(
                            text("SELECT tablename FROM pg_tables WHERE schemaname='public'")
                        )
                    ).all()
                }
                assert {"consent_events", "audit_events"} <= tables

                # The projection index must exist, or every /consent/check does a
                # sequential scan over the whole ledger.
                indexes = {
                    row[0]
                    for row in (
                        await conn.execute(
                            text(
                                "SELECT indexname FROM pg_indexes WHERE tablename='consent_events'"
                            )
                        )
                    ).all()
                }
                assert "ix_consent_events_lookup" in indexes
        finally:
            await eng.dispose()

    asyncio.run(_check())


def test_action_check_constraint_rejects_unknown_actions(postgres_url: str) -> None:
    """The DB is the backstop; the domain validates first, but data outlives code."""
    _upgrade(postgres_url)

    async def _check() -> None:
        eng = create_async_engine(postgres_url)
        try:
            async with eng.begin() as conn:
                with pytest.raises(Exception) as excinfo:
                    await conn.execute(
                        text(
                            "INSERT INTO consent_events "
                            "(event_id, subject_id, capability, action, metadata, recorded_at) "
                            "VALUES (gen_random_uuid(), gen_random_uuid(), 'x.y', "
                            "'MAYBE', '{}'::jsonb, now())"
                        )
                    )
                assert "ck_consent_events_action" in str(excinfo.value)
        finally:
            await eng.dispose()

    asyncio.run(_check())
