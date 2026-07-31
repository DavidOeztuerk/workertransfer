"""Composition-Root for the consent-service HTTP app (ADR-0003).

Replaces the Phase-3 placeholder that built a bare `FastAPI()` and therefore had
no correlation IDs, no security headers and no RFC-9457 problem details. Going
through the kernel's `create_api_app` means the middleware order (correlation →
auth → tenant → security) is owned in one place for every service.

This service verifies identity-service's tokens and issues none of its own
(ADR-0015): `JwtAuthMiddleware` comes from `worker-auth` rather than being copied,
so the header-or-cookie carrier logic has exactly one implementation.
"""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from sqlalchemy.ext.asyncio import create_async_engine
from worker_auth import JwtAuthMiddleware
from worker_platform.presentation.app import create_api_app
from worker_tenancy import ClaimTenantResolver

from consent_service.configuration import ConsentServiceSettings
from consent_service.infrastructure.compose import compose_infrastructure
from consent_service.presentation.http.router import build_consent_router


def build_app(settings: ConsentServiceSettings) -> FastAPI:
    # create_async_engine is lazy — it opens no connection — so building the app
    # on a machine without a reachable database is fine. /health/live never
    # touches it.
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    tokens = deps["tokens"]

    def verify_access_token(token: str) -> Any:
        return tokens.verify_token(token, expected_type="access")

    return create_api_app(
        settings,
        # The tenant comes from the JWT claim the auth middleware put on
        # scope["state"]["user"], never from a browser header (ADR-0009).
        tenant_resolver=ClaimTenantResolver(),
        auth_middleware=JwtAuthMiddleware,
        auth_middleware_kwargs={"verify": verify_access_token},
        routers=(build_consent_router(deps),),
    )


__all__ = ["build_app"]
