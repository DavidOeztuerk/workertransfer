"""E2E integration test: register -> login -> /me -> refresh -> logout.

Exercises the full HTTP slice against a real Postgres container: the JWT auth
middleware, the four /auth endpoints, and /me reading request.state.user from
the claim's tenant_id (ADR-0008). Docker-gated; skips wholesale offline.
"""

from __future__ import annotations

import os
from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]  # apps/identity-service


@pytest.fixture(scope="module")
def migrated_schema(postgres_url: str) -> None:
    """Apply alembic upgrade head synchronously before the async tests run.

    ``command.upgrade`` drives ``env.py``, which calls ``asyncio.run`` -> it
    cannot run inside the async-test event loop. Running it in a sync
    module-scoped fixture (before pytest-asyncio enters its loop) sidesteps
    that collision and applies the schema once per module/container.
    """
    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    command.upgrade(cfg, "head")


async def test_register_login_me_refresh_logout_roundtrip(
    postgres_url: str, migrated_schema: None
) -> None:
    # Re-read settings so the service points at the migrated container DB.
    os.environ["WORKER_JWT_SECRET"] = "test-secret-with-at-least-thirty-two-bytes-xx"

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    settings = IdentityServiceSettings()
    app = build_app(settings)
    transport = ASGITransport(app=app)
    tenant = "11111111-1111-1111-1111-111111111111"
    email = "roundtrip@example.com"
    password = "strongpassword1"

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        reg = await client.post(
            "/auth/register",
            json={"email": email, "password": password, "display_name": "E", "tenant_id": tenant},
        )
        assert reg.status_code == 201, reg.text

        dup = await client.post(
            "/auth/register",
            json={"email": email, "password": password, "display_name": "E", "tenant_id": tenant},
        )
        assert dup.status_code == 409, dup.text

        login = await client.post(
            "/auth/login",
            json={"email": email, "password": password, "tenant_id": tenant},
        )
        assert login.status_code == 200, login.text
        # httpx AsyncClient keeps the cookie jar across requests.
        access = client.cookies.get("access")
        assert access
        refresh = client.cookies.get("refresh")
        assert refresh

        # Wrong password -> 401, audit-only reason (no leak).
        bad = await client.post(
            "/auth/login",
            json={"email": email, "password": "wrongpassword99", "tenant_id": tenant},
        )
        assert bad.status_code == 401

        me = await client.get("/me", headers={"Authorization": f"Bearer {access}"})
        assert me.status_code == 200, me.text
        body = me.json()
        assert body["tenant_id"] == tenant
        assert "user_id" in body
        assert body["roles"] == ["user"]

        # The browser path: the access token is httpOnly, so apps/web can only
        # send it back as a cookie (credentials: "include") and never as an
        # Authorization header. The client jar replays it automatically, so this
        # request carries no explicit header at all.
        me_cookie = await client.get("/me")
        assert me_cookie.status_code == 200, me_cookie.text
        assert me_cookie.json() == body

        # /me with neither header nor cookie -> 401. Needs a separate client:
        # the logged-in jar above replays its cookies on every request, which
        # is exactly the behaviour asserted just above.
        async with AsyncClient(transport=transport, base_url="http://test") as anon:
            me_anon = await anon.get("/me")
            assert me_anon.status_code == 401

        rf = await client.post("/auth/refresh")
        assert rf.status_code == 200, rf.text
        # The rotated refresh cookie replaces the old one in the client jar.
        new_refresh = client.cookies.get("refresh")
        assert new_refresh
        assert new_refresh != refresh  # rotation produced a new jti

        lo = await client.post("/auth/logout")
        assert lo.status_code == 204, lo.text

        # After logout, the revoked session is rejected on refresh.
        rej = await client.post("/auth/refresh")
        assert rej.status_code == 401

    # Sanity: the audit_events table captured the login/refresh/revoke trail.
    eng = create_async_engine(postgres_url)
    try:
        async with eng.connect() as conn:
            rows = (await conn.execute(text("SELECT action FROM audit_events ORDER BY id"))).all()
            actions = {row[0] for row in rows}
            assert {"register", "login_success", "token_refresh", "token_revoke"} <= actions
    finally:
        # Truncate so re-runs against the same container start with empty tables.
        async with eng.begin() as conn:
            await conn.execute(text("TRUNCATE users, sessions, audit_events CASCADE"))
        await eng.dispose()
