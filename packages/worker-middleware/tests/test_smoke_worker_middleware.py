"""Smoke tests for worker-middleware (Phase 1.5).

Exercises the ASGI middleware constructors with a dummy ASGI app (Starlette
``BaseHTTPMiddleware.__init__`` stores the app; no request is dispatched).
``TenantContextMiddleware`` defaults its resolver to ``NoTenantResolver``.
``dispatch`` is NOT called — it would run a full request through the app.
"""

from worker_middleware import (
    CompressionMiddleware,
    SecurityHeadersMiddleware,
    TenantContextMiddleware,
)
from worker_tenancy import NoTenantResolver


def _dummy_app(scope, receive, send):
    return None


def test_smoke_tenant_context_middleware_default_resolver() -> None:
    middleware = TenantContextMiddleware(_dummy_app)

    assert isinstance(middleware._resolver, NoTenantResolver)


def test_smoke_security_headers_middleware_stores_flag() -> None:
    enforce = SecurityHeadersMiddleware(_dummy_app, enforce_https=True)

    assert enforce._enforce_https is True


def test_smoke_compression_middleware_constructs() -> None:
    CompressionMiddleware(_dummy_app)
