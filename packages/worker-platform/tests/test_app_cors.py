"""Tests for the optional CORS compose-hook (Sub-step 2.8 enabler).

CORS is default-off (``cors_allow_origins == []``) so production behaviour is
unchanged: no ``CORSMiddleware`` is registered and OPTIONS preflight requests
are not answered with ``Access-Control-*`` headers. When a service supplies a
non-empty allowlist (dev/staging), ``create_api_app`` registers
``CORSMiddleware`` with ``allow_credentials=True`` so the browser accepts the
HTTP-only ``access``/``refresh`` cookies set by ``POST /auth/login`` across the
Vite↔service origin boundary. CORS is refused in PRODUCTION (same-origin behind
the gateway, ULTRAPLAN Phase 10).
"""

from __future__ import annotations

from fastapi import APIRouter
from fastapi.testclient import TestClient
from starlette.middleware.cors import CORSMiddleware
from worker_platform.configuration import Environment, PlatformSettings
from worker_platform.presentation.app import create_api_app


def _has_cors_middleware(app: object) -> bool:
    """True if the Starlette middleware stack contains CORSMiddleware."""

    cls = CORSMiddleware
    stack = app  # type: ignore[assignment]
    seen: set[int] = set()
    current: object = stack
    # The app exposes its middleware stack via app.user_middleware + app.middleware_stack.
    names = {
        mw.cls.__name__ if hasattr(mw, "cls") else type(mw).__name__
        for mw in getattr(stack, "user_middleware", ())
    }
    if cls.__name__ in names:
        return True
    # Fallback: walk the built middleware_stack linked via .app attributes.
    while current is not None and id(current) not in seen:
        seen.add(id(current))
        if type(current).__name__ == cls.__name__:
            return True
        current = getattr(current, "app", None)
    return False


def test_cors_off_by_default_no_cors_middleware_registered() -> None:
    """Empty allowlist => no CORSMiddleware in the stack at all."""

    settings = PlatformSettings(environment=Environment.TEST)  # cors_allow_origins=[]
    app = create_api_app(settings)
    # Force the middleware stack to be built (lazy in Starlette).
    _ = TestClient(app)
    assert _has_cors_middleware(app) is False


def test_cors_allowlist_registers_preflight_headers() -> None:
    """Non-empty allowlist => CORS middleware answers preflight with ACAO+credentials."""

    settings = PlatformSettings(
        environment=Environment.DEVELOPMENT,
        cors_allow_origins=["http://localhost:5173", "http://127.0.0.1:5173"],
    )
    router = APIRouter()

    @router.post("/auth/login")
    def login() -> dict[str, str]:
        return {"status": "ok"}

    app = create_api_app(settings, routers=(router,))
    client = TestClient(app)
    resp = client.options(
        "/auth/login",
        headers={
            "Origin": "http://localhost:5173",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type",
        },
    )
    assert resp.status_code == 200
    assert _has_cors_middleware(app) is True
    assert resp.headers.get("access-control-allow-origin") == "http://localhost:5173"
    assert resp.headers.get("access-control-allow-credentials") == "true"
    # content-type is allowed via the default allow_headers list.
    allowed = resp.headers.get("access-control-allow-headers", "")
    assert "content-type" in allowed.lower()


def test_cors_not_registered_in_production_even_if_allowlist_set() -> None:
    """CORS is a dev/staging concern; in PRODUCTION the allowlist is ignored.

    Production runs same-origin behind the gateway (ULTRAPLAN Phase 10); exposing
    allow-credentials CORS from the origin service would widen the cookie surface.
    The hook therefore refuses to register CORS when environment is PRODUCTION.
    """

    settings = PlatformSettings(
        environment=Environment.PRODUCTION,
        cors_allow_origins=["http://evil.example"],
    )
    app = create_api_app(settings)
    _ = TestClient(app)
    assert _has_cors_middleware(app) is False
