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
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine
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
            },
            headers=spoof_header,
        )
        assert register.status_code == 201, register.text

        # Email confirmation is a later task; activate directly via the DB so
        # this test can still exercise login end to end — same seam used below
        # for the out-of-band membership grant.
        activate_engine = create_async_engine(postgres_url)
        try:
            async with activate_engine.begin() as conn:
                await conn.execute(
                    text("UPDATE users SET status = 'active' WHERE email = :email"),
                    {"email": "tenant-source@example.com"},
                )
        finally:
            await activate_engine.dispose()

        login = await client.post(
            "/auth/login",
            json={"email": "tenant-source@example.com", "password": "strongpassword1"},
            headers=spoof_header,
        )
        assert login.status_code == 200, login.text
        access = login.cookies.get("access")
        assert access is not None

        # Logging in makes you a person, not a company — even while shouting a
        # tenant header at every request (ADR-0017).
        me = await client.get(
            "/me",
            headers={"Authorization": f"Bearer {access}", "X-Tenant-ID": str(tenant)},
        )
        assert me.status_code == 200, me.text
        assert me.json()["tenant_id"] is None
        user_id = me.json()["user_id"]

        # Asking for a company without being a member is refused, header or not.
        denied = await client.post(
            f"/auth/tenant/{tenant}",
            headers={"Authorization": f"Bearer {access}", **spoof_header},
        )
        assert denied.status_code == 403, denied.text

    # Grant the membership out-of-band: writing memberships belongs to a future
    # company-service, so the DB is the honest seam for this test.
    engine = create_async_engine(postgres_url)
    try:
        async with engine.begin() as conn:
            await conn.execute(
                text(
                    "INSERT INTO user_tenant_memberships (id, user_id, tenant_id) "
                    "VALUES (gen_random_uuid(), :u, :t)"
                ),
                {"u": user_id, "t": str(tenant)},
            )
    finally:
        await engine.dispose()

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        login = await client.post(
            "/auth/login",
            json={"email": "tenant-source@example.com", "password": "strongpassword1"},
        )
        access = login.cookies.get("access")

        switched = await client.post(
            f"/auth/tenant/{tenant}",
            headers={"Authorization": f"Bearer {access}", **spoof_header},
        )
        assert switched.status_code == 200, switched.text
        tenant_access = switched.cookies.get("access")
        assert tenant_access is not None

        # Now a tenant IS active — and it is the one membership allowed, never
        # the one the header keeps claiming.
        me = await client.get(
            "/me",
            headers={"Authorization": f"Bearer {tenant_access}", "X-Tenant-ID": str(uuid4())},
        )
        assert me.status_code == 200, me.text
        assert me.json()["tenant_id"] == str(tenant)
