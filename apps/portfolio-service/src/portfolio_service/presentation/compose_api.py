"""Composition-Root der Portfolio-Service-App (ADR-0003)."""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from portfolio_service.configuration import PortfolioServiceSettings
from portfolio_service.infrastructure.compose import compose_infrastructure
from portfolio_service.presentation.http.erasure_router import build_erasure_router
from portfolio_service.presentation.http.router import build_router
from sqlalchemy.ext.asyncio import create_async_engine
from worker_auth import JwtAuthMiddleware, TokenManager
from worker_platform.presentation.app import create_api_app
from worker_tenancy import ClaimTenantResolver


def build_app(settings: PortfolioServiceSettings) -> FastAPI:
    # create_async_engine ist träge — die App lässt sich ohne erreichbare
    # Datenbank bauen, und /health/live fasst sie nie an.
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    tokens = TokenManager(secret=settings.jwt_secret.get_secret_value())

    def verify_access_token(token: str) -> Any:
        return tokens.verify_token(token, expected_type="access")

    return create_api_app(
        settings,
        # Der Tenant kommt aus dem Claim, nie aus einem Header (ADR-0009/0017).
        tenant_resolver=ClaimTenantResolver(),
        auth_middleware=JwtAuthMiddleware,
        auth_middleware_kwargs={"verify": verify_access_token},
        routers=(build_router(deps), build_erasure_router(deps)),
    )


__all__ = ["build_app"]
