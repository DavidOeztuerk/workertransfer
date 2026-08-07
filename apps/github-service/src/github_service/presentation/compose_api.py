"""Composition-Root für die HTTP-App des github-service (ADR-0003).

Explicit, not a fluent builder: a reader sees in this one file exactly which
cross-cutting behaviours are active and in which order. The kernel owns the
middleware order (correlation -> auth -> tenant -> security); this service
supplies only its own routers and adapters.
"""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from github_service.configuration import GithubServiceSettings
from github_service.infrastructure.compose import compose_infrastructure
from github_service.presentation.http.erasure_router import build_erasure_router
from github_service.presentation.http.router import build_router
from sqlalchemy.ext.asyncio import create_async_engine
from worker_auth import JwtAuthMiddleware, TokenManager
from worker_platform.presentation.app import create_api_app


def build_app(settings: GithubServiceSettings) -> FastAPI:
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps = compose_infrastructure(settings, engine)

    # Ohne diese Verdrahtung ist JEDER Endpunkt unauthentifiziert — der
    # Prinzipal bleibt leer, und `_subject()` antwortet 401. Der Generator
    # lieferte sie nicht mit; siehe die Vorlage, die das jetzt tut.
    tokens = TokenManager(secret=settings.jwt_secret.get_secret_value())

    def verify_access_token(token: str) -> Any:
        return tokens.verify_token(token, expected_type="access")

    return create_api_app(
        settings,
        auth_middleware=JwtAuthMiddleware,
        auth_middleware_kwargs={"verify": verify_access_token},
        routers=(build_router(deps), build_erasure_router(deps)),
    )


__all__ = ["build_app"]
