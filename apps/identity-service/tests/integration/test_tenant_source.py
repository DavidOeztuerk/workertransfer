"""Tenant-source DoD test — the tenant comes from the JWT claim, never a header.

The hard constraint (docs/product-scope.md): in production, tenant identity must
NEVER come from a browser header. This proves it: with the claim-based resolver
wired (Task 21) and production settings, an X-Tenant-ID spoof attempt is ignored
and /me returns the tenant_id from the authenticated JWT claim.

Isolation notes:
- Schema: applies alembic fresh (downgrade base -> upgrade head) on a
  function-scoped fixture. The session-shared container's ``alembic_version``
  row would otherwise survive a prior test's ``Base.metadata.drop_all`` (the
  conftest ``engine`` fixture), making ``upgrade head`` a no-op and leaving
  ``users`` missing (UndefinedTableError). Forcing the re-apply makes this
  test independent of suite ordering.
- Env: production mode + allow_development_tenant_header are set via the
  ``monkeypatch`` fixture (auto-restored) so they never leak into later tests
  that read ``PlatformSettings()`` defaults.

Skips if Docker/testcontainers is unavailable (ADR-0011 offline-skip).
"""

from __future__ import annotations

from pathlib import Path
from uuid import uuid4

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from worker_database import Base

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]


@pytest.fixture
def migrated_schema(postgres_url: str, monkeypatch: pytest.MonkeyPatch) -> None:
    """Apply alembic upgrade head from a clean slate before the test.

    The session-shared container's schema state is unpredictable across the
    suite: some tests use alembic (``upgrade head``), others use the conftest
    ``engine`` fixture's ``Base.metadata.drop_all``/``create_all``. Crucially
    ``Base.metadata.drop_all`` leaves the ``alembic_version`` row in place
    (alembic owns that table, it is not in ``Base.metadata``), so a later
    ``upgrade head`` would no-op and leave ``users`` missing. We therefore
    reset deterministically and independently of ordering:

      1. ``Base.metadata.drop_all(checkfirst=True)`` — tolerant of already-absent
         mapped tables (``checkfirst`` is the default).
      2. drop the ``alembic_version`` table if it exists (raw SQL; checkfirst).
      3. ``command.upgrade head`` — re-applies 0001 cleanly.

    ``command.upgrade``/``downgrade`` drive env.py which calls asyncio.run ->
    cannot run inside the async-test loop, so this must be a sync fixture.
    """
    from sqlalchemy import create_engine, text

    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    monkeypatch.setenv("WORKER_DATABASE_URL", postgres_url)

    # Sync engine for the reset (alembic itself uses a sync engine).
    sync_url = postgres_url.replace("postgresql+asyncpg://", "postgresql+psycopg://")
    # Fall back to the psycopg driver's plain postgresql:// if psycopg isn't
    # installed; asyncpg URLs aren't valid for sync create_engine.
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


async def test_x_tenant_id_header_ignored_in_production_mode(
    postgres_url: str, migrated_schema: None, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Production mode + development-header off, set via monkeypatch (restored
    # post-test -> cannot leak into other tests' PlatformSettings() defaults).
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")
    monkeypatch.setenv("WORKER_ENVIRONMENT", "production")
    monkeypatch.setenv("WORKER_ALLOW_DEVELOPMENT_TENANT_HEADER", "false")

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    settings = IdentityServiceSettings()
    app = build_app(settings)
    transport = ASGITransport(app=app)

    tenant = uuid4()
    spoof_header = {"X-Tenant-ID": "00000000-0000-0000-0000-000000000000"}  # must be ignored

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        register = await client.post(
            "/auth/register",
            json={
                "email": "tenant-source@example.com",
                "password": "strongpassword1",
                "display_name": "Tenant Source",
                "tenant_id": str(tenant),
            },
            headers=spoof_header,
        )
        assert register.status_code == 201, register.text

        login = await client.post(
            "/auth/login",
            json={
                "email": "tenant-source@example.com",
                "password": "strongpassword1",
                "tenant_id": str(tenant),
            },
            headers=spoof_header,
        )
        assert login.status_code == 200, login.text
        access = login.cookies.get("access")
        assert access is not None

        # Protected endpoint + a *different* spoof X-Tenant-ID: must be ignored.
        me = await client.get(
            "/me",
            headers={"Authorization": f"Bearer {access}", "X-Tenant-ID": str(uuid4())},
        )
        assert me.status_code == 200, me.text
        # The tenant comes from the CLAIM, not the header.
        assert me.json()["tenant_id"] == str(tenant)
