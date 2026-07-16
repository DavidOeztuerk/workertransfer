"""FastAPI service factory for WorkerTransfer deployable applications."""

from __future__ import annotations

from collections.abc import Iterable

from fastapi import FastAPI

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
    settings: PlatformSettings, *, readiness_checks: Iterable[ReadinessCheck] = ()
) -> FastAPI:
    """Create a secure, observable HTTP entry point with no business endpoints."""

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

    tenant_resolver: TenantResolver
    if settings.allow_development_tenant_header and settings.environment in {
        Environment.LOCAL,
        Environment.DEVELOPMENT,
        Environment.TEST,
    }:
        tenant_resolver = DevelopmentHeaderTenantResolver(enabled=True)
    else:
        tenant_resolver = NoTenantResolver()

    # The last middleware added is outermost in Starlette's stack.
    app.add_middleware(
        SecurityHeadersMiddleware,
        enforce_https=settings.environment is Environment.PRODUCTION,
    )
    app.add_middleware(TenantContextMiddleware, resolver=tenant_resolver)
    app.add_middleware(CorrelationIdMiddleware)
    return app
