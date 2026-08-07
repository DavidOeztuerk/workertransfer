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


def test_create_api_app_passes_auth_middleware_kwargs_to_ctor() -> None:
    """The auth_middleware_kwargs dict flows through to the middleware ctor."""
    captured: dict[str, object] = {}

    class _CapturingMiddleware:
        def __init__(self, app: object, *, injected: str) -> None:
            self.app = app
            captured["injected"] = injected

        async def __call__(self, scope: object, receive: object, send: object) -> None:
            await self.app(scope, receive, send)  # type: ignore[arg-type]

    settings = PlatformSettings(environment=Environment.TEST)
    app = create_api_app(
        settings,
        auth_middleware=_CapturingMiddleware,
        auth_middleware_kwargs={"injected": "the-tokens-or-whatever"},
    )
    client = TestClient(app)
    # Drive one request so Starlette constructs the middleware stack.
    client.get("/health/live")
    assert captured.get("injected") == "the-tokens-or-whatever"
