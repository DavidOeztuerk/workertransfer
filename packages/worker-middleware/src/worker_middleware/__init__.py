"""HTTP middleware facade over worker-correlation and worker-tenancy."""

from __future__ import annotations

from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint
from starlette.requests import Request
from starlette.responses import Response
from starlette.types import ASGIApp
from worker_tenancy import NoTenantResolver, TenantResolver


class TenantContextMiddleware(BaseHTTPMiddleware):
    def __init__(self, app: ASGIApp, resolver: TenantResolver | None = None) -> None:
        super().__init__(app)
        self._resolver = resolver or NoTenantResolver()

    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        from worker_tenancy import set_tenant_id

        tenant_id = self._resolver.resolve(request)
        if tenant_id:
            set_tenant_id(tenant_id)
        return await call_next(request)


class SecurityHeadersMiddleware(BaseHTTPMiddleware):
    def __init__(self, app: ASGIApp, enforce_https: bool = False) -> None:
        super().__init__(app)
        self._enforce_https = enforce_https

    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        response = await call_next(request)
        response.headers.setdefault("X-Content-Type-Options", "nosniff")
        response.headers.setdefault("X-Frame-Options", "DENY")
        response.headers.setdefault("Referrer-Policy", "no-referrer")
        response.headers.setdefault(
            "Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'"
        )
        if self._enforce_https:
            response.headers.setdefault(
                "Strict-Transport-Security", "max-age=31536000; includeSubDomains"
            )
        return response


class CompressionMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        response = await call_next(request)
        response.headers.setdefault("Content-Encoding", "gzip")
        return response


class ExceptionHandlingMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        try:
            return await call_next(request)
        except Exception as e:
            from worker_exceptions import to_problem_detail

            problem = to_problem_detail(e, request.url.path)
            return Response(
                content=problem.model_dump_json(),
                status_code=problem.status,
                media_type="application/problem+json",
            )
