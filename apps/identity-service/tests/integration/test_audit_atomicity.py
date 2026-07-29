"""Audit atomicity DoD tests (Task 22, ADR-0012).

Proves audit persistence is synchronous inside the same UoW as the security
command (atomicity): after a successful register+login the audit_events table
holds a `register` and a `login_success` row; after a failed login (unknown
user) it holds a `login_failure` row with actor_id NULL (the actor is unknown
at failed login).

Mirrors the test_tenant_source.py isolation patterns (Task 21): order-
independent schema reset (alembic_version survives Base.metadata.drop_all) +
monkeypatch env (no leak). Skips if Docker/testcontainers is unavailable.
"""

from __future__ import annotations

from pathlib import Path
from uuid import uuid4

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncEngine, create_async_engine
from worker_database import Base

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]


@pytest.fixture
def migrated_schema(postgres_url: str, monkeypatch: pytest.MonkeyPatch) -> None:
    """Apply alembic upgrade head from a clean slate (order-independent reset).

    See test_tenant_source.py for the rationale: Base.metadata.drop_all leaves
    alembic_version in place, so a later upgrade head would no-op and leave
    users missing. Reset deterministically instead.
    """
    from sqlalchemy import create_engine

    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    monkeypatch.setenv("WORKER_DATABASE_URL", postgres_url)

    sync_url = postgres_url.replace("postgresql+asyncpg://", "postgresql+psycopg://")
    try:
        reset_engine = create_engine(sync_url)
    except Exception:
        reset_engine = create_engine(postgres_url.replace("postgresql+asyncpg://", "postgresql://"))
    with reset_engine.connect() as conn:
        Base.metadata.drop_all(conn, checkfirst=True)
        conn.execute(text("DROP TABLE IF EXISTS alembic_version"))
        conn.commit()
    reset_engine.dispose()

    command.upgrade(cfg, "head")


async def _audit_actions_for_tenant(engine: AsyncEngine, tenant: uuid4) -> list[tuple[str, object]]:
    async with engine.connect() as conn:
        rows = (
            await conn.execute(
                text("SELECT action, actor_id FROM audit_events WHERE tenant_id = :t"),
                {"t": str(tenant)},
            )
        ).all()
    return [(r[0], r[1]) for r in rows]


async def test_successful_login_persists_register_and_login_success_audits(
    postgres_url: str, migrated_schema: None, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    app = build_app(IdentityServiceSettings())
    transport = ASGITransport(app=app)
    tenant = uuid4()
    email = "audit@example.com"
    password = "strongpassword1"

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        reg = await client.post(
            "/auth/register",
            json={
                "email": email,
                "password": password,
                "display_name": "AU",
                "tenant_id": str(tenant),
            },
        )
        assert reg.status_code == 201, reg.text
        login = await client.post(
            "/auth/login",
            json={"email": email, "password": password, "tenant_id": str(tenant)},
        )
        assert login.status_code == 200, login.text

    engine = create_async_engine(postgres_url)
    try:
        actions = [action for action, _actor in await _audit_actions_for_tenant(engine, tenant)]
    finally:
        await engine.dispose()

    assert "register" in actions
    assert "login_success" in actions


async def test_failed_login_persists_login_failure_audit_with_null_actor(
    postgres_url: str, migrated_schema: None, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    app = build_app(IdentityServiceSettings())
    transport = ASGITransport(app=app)
    tenant = uuid4()

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        login = await client.post(
            "/auth/login",
            json={
                "email": "nope@example.com",
                "password": "wrongpassword1",
                "tenant_id": str(tenant),
            },
        )
        assert login.status_code == 401, login.text

    engine = create_async_engine(postgres_url)
    try:
        rows = await _audit_actions_for_tenant(engine, tenant)
    finally:
        await engine.dispose()

    assert any(action == "login_failure" and actor is None for action, actor in rows)
