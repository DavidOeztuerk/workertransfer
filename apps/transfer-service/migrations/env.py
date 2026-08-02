"""Async Alembic env.py (ADR-0010) for the transfer-service database.

Per-service migrations: this service owns its own history and its own database.
SQLAlchemy 2 async cannot drive Alembic's default synchronous env, hence the
async_engine_from_config + connection.run_sync pattern.
"""

from __future__ import annotations

import asyncio
import os
import sys
from logging.config import fileConfig
from pathlib import Path
from typing import Any

from alembic import context
from sqlalchemy import pool
from sqlalchemy.engine import Connection
from sqlalchemy.ext.asyncio import async_engine_from_config

# Make `transfer_service` importable when alembic runs from apps/transfer-service.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

# Import models so their tables register on Base.metadata (autogenerate target).
from transfer_service.infrastructure.database import models as _models  # registers tables
from worker_database import Base

config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

_ = _models  # imported for its side effect only

target_metadata = Base.metadata


def _database_url() -> str:
    return os.environ.get(
        "WORKER_DATABASE_URL",
        os.environ.get(
            "DATABASE_URL", "postgresql+asyncpg://worker:worker@127.0.0.1:5432/transfer_service"
        ),
    )


def run_migrations_offline() -> None:
    context.configure(
        url=_database_url(),
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )
    with context.begin_transaction():
        context.run_migrations()


def do_run_migrations(connection: Connection) -> None:
    context.configure(connection=connection, target_metadata=target_metadata)
    with context.begin_transaction():
        context.run_migrations()


async def run_migrations_online() -> None:
    section: dict[str, Any] | None = config.get_section(config.config_ini_section, {})
    if section is not None:
        section["sqlalchemy.url"] = _database_url()
        engine = async_engine_from_config(section, prefix="sqlalchemy.", poolclass=pool.NullPool)
        async with engine.connect() as connection:
            await connection.run_sync(do_run_migrations)
        await engine.dispose()


if context.is_offline_mode():
    run_migrations_offline()
else:
    asyncio.run(run_migrations_online())
