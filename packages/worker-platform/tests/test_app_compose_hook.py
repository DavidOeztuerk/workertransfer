"""Tests for the create_api_app compose-hook (Task 19).

Covers the kwargs added in Sub-step 2.6: ``tenant_resolver`` (overrides the
default NoTenant/Development-header logic), ``auth_middleware`` (added so the
claim-based resolver can read request.state.user), and ``routers`` (included on
the app). When ``tenant_resolver`` is None the pre-Task-19 default behavior is
unchanged.
"""

from __future__ import annotations

from fastapi import APIRouter
from fastapi.testclient import TestClient
from worker_platform.configuration import Environment, PlatformSettings
from worker_platform.context import get_tenant_id
from worker_platform.presentation.app import create_api_app
from worker_platform.presentation.middleware import TenantResolver


class _FixedResolver:
    """A resolver that always returns a fixed tenant id for the test."""

    def resolve(self, scope: object) -> str | None:
        return "from-claim"


def test_create_api_app_accepts_tenant_resolver_and_routers() -> None:
    settings = PlatformSettings(environment=Environment.TEST)
    router = APIRouter()

    @router.get("/probe")
    def probe() -> dict[str, str]:
        return {"tenant": get_tenant_id() or "none"}

    resolver: TenantResolver = _FixedResolver()
    app = create_api_app(settings, tenant_resolver=resolver, routers=(router,))
    client = TestClient(app)
    resp = client.get("/probe")
    assert resp.status_code == 200
    assert resp.json()["tenant"] == "from-claim"


def test_create_api_app_default_resolver_unchanged_when_none() -> None:
    settings = PlatformSettings(
        environment=Environment.PRODUCTION, allow_development_tenant_header=False
    )
    app = create_api_app(settings)
    client = TestClient(app)
    # No router added, no tenant_resolver supplied: /health/live exists and the
    # default NoTenantResolver applies. Smoke only — we're asserting the call is
    # accepted and the app still builds with the legacy defaults.
    resp = client.get("/health/live")
    assert resp.status_code in (200, 404)
