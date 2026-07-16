"""Security headers, crypto, and transport-hardening helpers."""

from __future__ import annotations

from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint
from starlette.requests import Request
from starlette.responses import Response
from starlette.types import ASGIApp


class SecurityHeadersMiddleware(BaseHTTPMiddleware):
    def __init__(self, app: ASGIApp, *, enforce_https: bool = False) -> None:
        super().__init__(app)
        self.enforce_https = enforce_https

    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        response = await call_next(request)

        headers = response.headers
        headers.setdefault("X-Content-Type-Options", "nosniff")
        headers.setdefault("X-Frame-Options", "DENY")
        headers.setdefault("Referrer-Policy", "no-referrer")
        headers.setdefault("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")

        if self.enforce_https:
            headers.setdefault("Strict-Transport-Security", "max-age=31536000; includeSubDomains")

        return response


__all__ = ["SecurityHeadersMiddleware"]
