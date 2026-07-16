"""Small ASGI middleware without BaseHTTPMiddleware context limitations."""

from __future__ import annotations

from collections.abc import Awaitable, Callable
from typing import Protocol
from uuid import UUID

from starlette.datastructures import Headers, MutableHeaders
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from worker_platform.context import correlation_context, normalize_correlation_id, tenant_context

HttpSend = Callable[[Message], Awaitable[None]]


class TenantResolver(Protocol):
    def resolve(self, scope: Scope) -> str | None: ...


class NoTenantResolver:
    """Safe default until authenticated claims become the tenant source."""

    def resolve(self, scope: Scope) -> str | None:
        del scope
        return None


class DevelopmentHeaderTenantResolver:
    """Local-only fixture support; never an authenticated production tenant source."""

    def __init__(self, enabled: bool) -> None:
        self._enabled = enabled

    def resolve(self, scope: Scope) -> str | None:
        if not self._enabled:
            return None
        value = Headers(scope=scope).get("X-Tenant-ID")
        if value is None:
            return None
        try:
            return str(UUID(value))
        except ValueError:
            return None


class CorrelationIdMiddleware:
    def __init__(self, app: ASGIApp) -> None:
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        correlation_id = normalize_correlation_id(Headers(scope=scope).get("X-Correlation-ID"))

        async def send_with_correlation_header(message: Message) -> None:
            if message["type"] == "http.response.start":
                MutableHeaders(scope=message)["X-Correlation-ID"] = correlation_id
            await send(message)

        with correlation_context(correlation_id):
            await self.app(scope, receive, send_with_correlation_header)


class TenantContextMiddleware:
    def __init__(self, app: ASGIApp, resolver: TenantResolver) -> None:
        self.app = app
        self.resolver = resolver

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        with tenant_context(self.resolver.resolve(scope)):
            await self.app(scope, receive, send)


class SecurityHeadersMiddleware:
    def __init__(self, app: ASGIApp, *, enforce_https: bool) -> None:
        self.app = app
        self.enforce_https = enforce_https

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        async def send_with_security_headers(message: Message) -> None:
            if message["type"] == "http.response.start":
                headers = MutableHeaders(scope=message)
                headers.setdefault("X-Content-Type-Options", "nosniff")
                headers.setdefault("X-Frame-Options", "DENY")
                headers.setdefault("Referrer-Policy", "no-referrer")
                headers.setdefault(
                    "Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'"
                )
                if self.enforce_https:
                    headers.setdefault(
                        "Strict-Transport-Security", "max-age=31536000; includeSubDomains"
                    )
            await send(message)

        await self.app(scope, receive, send_with_security_headers)
