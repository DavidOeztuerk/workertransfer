"""Compose the identity-service FastAPI app (Sub-step 2.6).

Installs the service's routers, the claim-based tenant resolver, and the JWT
auth middleware through the platform ``create_api_app`` compose-hook (Task 19),
so the kernel owns middleware order (correlation → auth → tenant → security)
and the service supplies only its own concerns. The ``ClaimTenantResolver``
derives the tenant from ``request.state.user`` (set by ``AuthMiddleware``) — the
production tenant source (ADR-0009); browser ``X-Tenant-ID`` headers are never
an authenticated tenant source in production.
"""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from sqlalchemy.ext.asyncio import create_async_engine
from worker_platform.presentation.app import create_api_app
from worker_tenancy import ClaimTenantResolver

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.compose import compose_infrastructure
from identity_service.presentation.auth_middleware import AuthMiddleware
from identity_service.presentation.http.company_router import build_company_router
from identity_service.presentation.http.me_router import build_me_router
from identity_service.presentation.http.router import build_auth_router


def build_app(settings: IdentityServiceSettings) -> FastAPI:
    # create_async_engine is lazy (it does not connect), so importing/constructing
    # the app on a machine without a reachable DB is fine — the smoke test that
    # only hits /health/live never opens a connection.
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    # Install everything through the kernel compose-hook (Task 19+21): the
    # kernel owns middleware order (correlation -> auth -> tenant -> security);
    # the service supplies its routers, the claim-based tenant resolver, and
    # the auth middleware (with its tokens injected via kwargs — ADR-0002: the
    # kernel learns no business logic, only a generic kwargs dict crosses the
    # boundary). The ClaimTenantResolver reads scope["state"]["user"] which the
    # AuthMiddleware sets; the hook's outer-to-inner order (auth outside tenant)
    # guarantees state.user is set before the tenant resolver runs.
    return create_api_app(
        settings,
        tenant_resolver=ClaimTenantResolver(),
        auth_middleware=AuthMiddleware,
        auth_middleware_kwargs={"tokens": deps["tokens"]},
        routers=(build_auth_router(deps), build_me_router(deps), build_company_router(deps)),
    )


__all__ = ["build_app"]
