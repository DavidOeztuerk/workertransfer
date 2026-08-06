"""Composition-Root der Applications-Service-App (ADR-0003)."""

from __future__ import annotations

from typing import Any

from applications_service.configuration import ApplicationsServiceSettings
from applications_service.infrastructure.compose import compose_infrastructure, outbox_runner
from applications_service.presentation.http.router import build_router
from fastapi import FastAPI
from sqlalchemy.ext.asyncio import create_async_engine
from worker_auth import JwtAuthMiddleware, TokenManager
from worker_platform.presentation.app import create_api_app
from worker_tenancy import ClaimTenantResolver


def build_app(settings: ApplicationsServiceSettings) -> FastAPI:
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    tokens = TokenManager(secret=settings.jwt_secret.get_secret_value())

    def verify_access_token(token: str) -> Any:
        return tokens.verify_token(token, expected_type="access")

    return create_api_app(
        settings,
        tenant_resolver=ClaimTenantResolver(),
        auth_middleware=JwtAuthMiddleware,
        auth_middleware_kwargs={"verify": verify_access_token},
        routers=(build_router(deps),),
        # Der Outbox-Zusteller läuft im Dienst mit (ADR-0025).
        background=(outbox_runner(deps, settings),),
    )


__all__ = ["build_app"]
