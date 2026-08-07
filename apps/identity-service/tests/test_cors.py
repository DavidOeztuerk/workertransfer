"""CORS preflight coverage for the identity-service (Sub-step 2.8 enabler).

The web client at http://localhost:5173 posts credentials to /auth/login; the
browser refuses the HTTP-only cookie response unless the service answers the
cross-origin preflight with Access-Control-Allow-Origin + Allow-Credentials.
IdentityServiceSettings ships a dev allowlist; this test pins that the real
``build_app`` stack answers a Vite-origin preflight correctly. No DB is needed
(preflight short-circuits in the CORS middleware before the route handler).
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient
from identity_service.configuration import IdentityServiceSettings
from identity_service.main import create_app


@pytest.fixture
def settings() -> IdentityServiceSettings:
    # IdentityServiceSettings already defaults to a Vite dev allowlist.
    return IdentityServiceSettings()


def test_login_preflight_allows_vite_dev_origin(settings: IdentityServiceSettings) -> None:
    client = TestClient(create_app(settings))
    resp = client.options(
        "/auth/login",
        headers={
            "Origin": "http://localhost:5173",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type",
        },
    )
    assert resp.status_code == 200
    assert resp.headers["access-control-allow-origin"] == "http://localhost:5173"
    assert resp.headers["access-control-allow-credentials"] == "true"
    assert "content-type" in resp.headers["access-control-allow-headers"].lower()


def test_unknown_origin_is_not_allowed(settings: IdentityServiceSettings) -> None:
    client = TestClient(create_app(settings))
    resp = client.options(
        "/auth/login",
        headers={
            "Origin": "http://evil.example",
            "Access-Control-Request-Method": "POST",
        },
    )
    # Starlette's CORSMiddleware returns 400 Disallowed CORS origin for an
    # unlisted origin on a preflight; the key assertion is that no ACAO header
    # reflects the attacker origin back.
    assert resp.headers.get("access-control-allow-origin") != "http://evil.example"
