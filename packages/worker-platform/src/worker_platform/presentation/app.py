"""FastAPI service factory for WorkerTransfer deployable applications."""

from __future__ import annotations

from collections.abc import Iterable
from typing import Any

from fastapi import APIRouter, FastAPI

from worker_platform.configuration import Environment, PlatformSettings
from worker_platform.logging import configure_logging
from worker_platform.presentation.errors import register_exception_handlers
from worker_platform.presentation.health import ReadinessCheck, create_health_router
from worker_platform.presentation.middleware import (
    CorrelationIdMiddleware,
    DevelopmentHeaderTenantResolver,
    NoTenantResolver,
    SecurityHeadersMiddleware,
    TenantContextMiddleware,
    TenantResolver,
)


def create_api_app(
    settings: PlatformSettings,
    *,
    readiness_checks: Iterable[ReadinessCheck] = (),
    tenant_resolver: TenantResolver | None = None,
    auth_middleware: Any = None,
    auth_middleware_kwargs: dict[str, Any] | None = None,
    routers: Iterable[APIRouter] = (),
) -> FastAPI:
    """Create a secure, observable HTTP entry point with no business endpoints.

    Services register their routers + auth middleware via the compose-hook
    kwargs (Sub-step 2.6). When ``tenant_resolver`` is ``None`` the legacy
    default (``DevelopmentHeaderTenantResolver`` when enabled+low-env, else
    ``NoTenantResolver``) is used unchanged; a supplied resolver overrides it.
    """

    configure_logging()
    docs_url = "/docs" if settings.enable_docs else None
    openapi_url = "/openapi.json" if settings.enable_docs else None
    app = FastAPI(
        title=settings.service_name,
        version=settings.service_version,
        docs_url=docs_url,
        redoc_url=None,
        openapi_url=openapi_url,
    )
    register_exception_handlers(app)
    app.include_router(create_health_router(settings.service_name, readiness_checks))
    for router in routers:
        app.include_router(router)

    if tenant_resolver is not None:
        resolved_tenant: TenantResolver = tenant_resolver
    elif settings.allow_development_tenant_header and settings.environment in {
        Environment.LOCAL,
        Environment.DEVELOPMENT,
        Environment.TEST,
    }:
        resolved_tenant = DevelopmentHeaderTenantResolver(enabled=True)
    else:
        resolved_tenant = NoTenantResolver()

    # Starlette: the last middleware added is outermost in the call chain.
    # Desired outer→inner order: CorrelationId → auth_middleware →
    # TenantContext → SecurityHeaders. The auth middleware must run *outside*
    # TenantContext so a claim-based tenant resolver can read request.state.user
    # (set by the auth middleware) before TenantContext populates the contextvar.
    # Therefore add innermost-first: SecurityHeaders, TenantContext,
    # auth_middleware, CorrelationId.
    app.add_middleware(
        SecurityHeadersMiddleware,
        enforce_https=settings.environment is Environment.PRODUCTION,
    )
    app.add_middleware(TenantContextMiddleware, resolver=resolved_tenant)
    if auth_middleware is not None:
        app.add_middleware(auth_middleware, **(auth_middleware_kwargs or {}))
    app.add_middleware(CorrelationIdMiddleware)
    return app
