"""Compose the identity-service FastAPI app (Sub-step 2.5).

Phase 2 builds the identity app via this ``build_app`` rather than adding a
kernel compose-hook to ``create_api_app`` (that consolidation is Sub-step
2.6, Task 19). For 2.5 the platform factory supplies health/security headers/
correlation/problem-errors, then this builder overlays the auth router, the
``/me`` router, and the JWT auth middleware.
"""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from sqlalchemy.ext.asyncio import create_async_engine
from worker_platform.presentation.app import create_api_app

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.compose import compose_infrastructure
from identity_service.presentation.auth_middleware import AuthMiddleware
from identity_service.presentation.http.me_router import build_me_router
from identity_service.presentation.http.router import build_auth_router


def build_app(settings: IdentityServiceSettings) -> FastAPI:
    # create_async_engine is lazy (it does not connect), so importing/constructing
    # the app on a machine without a reachable DB is fine — the smoke test that
    # only hits /health/live never opens a connection.
    engine = create_async_engine(settings.database_url, pool_pre_ping=True)
    deps: dict[str, Any] = compose_infrastructure(settings, engine)
    deps["settings"] = settings

    app = create_api_app(settings)  # platform shell: health, security, correlation, problem errors
    app.include_router(build_auth_router(deps))
    app.include_router(build_me_router(deps))
    app.add_middleware(AuthMiddleware, tokens=deps["tokens"])
    return app


__all__ = ["build_app"]
