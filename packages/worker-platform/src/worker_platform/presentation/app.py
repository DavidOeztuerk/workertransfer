"""FastAPI service factory for WorkerTransfer deployable applications."""

from __future__ import annotations

import asyncio
import contextlib
import logging
from collections.abc import AsyncIterator, Awaitable, Callable, Iterable, Mapping
from contextlib import asynccontextmanager
from typing import Any

from fastapi import APIRouter, FastAPI
from starlette.middleware.cors import CORSMiddleware

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
from worker_platform.presentation.throttle import Limit, ThrottleMiddleware


def create_api_app(
    settings: PlatformSettings,
    *,
    readiness_checks: Iterable[ReadinessCheck] = (),
    tenant_resolver: TenantResolver | None = None,
    auth_middleware: Any = None,
    auth_middleware_kwargs: dict[str, Any] | None = None,
    throttle_limits: Mapping[tuple[str, str], Limit] | None = None,
    trust_forwarded_for: bool = False,
    routers: Iterable[APIRouter] = (),
    background: Iterable[Callable[[], Awaitable[None]]] = (),
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
        lifespan=_lifespan_for(background),
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
    # Desired outer→inner order: CORS → CorrelationId → auth_middleware →
    # TenantContext → SecurityHeaders. CORS must be outermost so its preflight
    # short-circuit answers OPTIONS before any other middleware runs. The auth
    # middleware must run *outside* TenantContext so a claim-based tenant resolver
    # can read request.state.user (set by the auth middleware) before
    # TenantContext populates the contextvar. Therefore add innermost-first:
    # SecurityHeaders, TenantContext, auth_middleware, CorrelationId, CORS.
    app.add_middleware(
        SecurityHeadersMiddleware,
        enforce_https=settings.environment is Environment.PRODUCTION,
    )
    app.add_middleware(TenantContextMiddleware, resolver=resolved_tenant)
    if auth_middleware is not None:
        app.add_middleware(auth_middleware, **(auth_middleware_kwargs or {}))
    if throttle_limits:
        # Weiter außen als die Authentifizierung, damit ein Ratespiel gebremst
        # wird, BEVOR ein Passwort geprüft (und damit bcrypt gerechnet) wird —
        # sonst wäre die Bremse selbst der teuerste Teil des Angriffs. Weiter
        # innen als CorrelationId, damit die Absage eine Korrelations-ID trägt.
        app.add_middleware(
            ThrottleMiddleware,
            limits=throttle_limits,
            trust_forwarded_for=trust_forwarded_for,
        )
    app.add_middleware(CorrelationIdMiddleware)
    _add_cors_middleware(app, settings)
    return app


_logger = logging.getLogger("workertransfer.platform.background")


def _lifespan_for(
    background: Iterable[Callable[[], Awaitable[None]]],
) -> Callable[[FastAPI], Any]:
    """Dauerläufer, die mit der App leben — und mit ihr enden.

    Gebraucht für den Outbox-Zusteller: eine Schleife, die eine Tabelle liest
    und zustellt. Sie gehört in den Dienst und nicht in einen eigenen Prozess —
    ein weiteres Deployment, ein weiterer Gesundheitscheck und ein weiterer Ort
    zum Vergessen wären ein hoher Preis für eine `while`-Schleife.

    Zwei Dinge, die hier leicht falsch gemacht werden:

    1. **Abbrechen und WARTEN.** Nur `cancel()` zu rufen und weiterzugehen
       beendet den Prozess, während die Aufgabe noch mitten in einer
       Datenbank-Transaktion steckt. Deshalb das `await` hinter dem Abbruch.
    2. **Ein Absturz darf nicht still sein.** Eine Hintergrundaufgabe, die eine
       Ausnahme wirft, stirbt lautlos; die App läuft weiter und beantwortet
       Anfragen, aber es stellt niemand mehr zu. Genau der Zustand, den die
       Outbox abschaffen soll — deshalb wird er protokolliert.
    """
    runners = tuple(background)

    @asynccontextmanager
    async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
        tasks = [asyncio.create_task(_guarded(runner)) for runner in runners]
        try:
            yield
        finally:
            for task in tasks:
                task.cancel()
            for task in tasks:
                with contextlib.suppress(asyncio.CancelledError):
                    await task

    return lifespan


async def _guarded(runner: Callable[[], Awaitable[None]]) -> None:
    try:
        await runner()
    except asyncio.CancelledError:
        raise
    except Exception:
        _logger.exception("Hintergrundaufgabe beendet sich mit einem Fehler")


def _add_cors_middleware(app: FastAPI, settings: PlatformSettings) -> None:
    """Register CORSMiddleware when a non-empty allowlist is set outside prod.

    Default-off (empty allowlist => no CORS) and refused in PRODUCTION: the
    gateway terminates cross-origin traffic in production (ULTRAPLAN Phase 10),
    and exposing allow-credentials CORS from the origin service would widen the
    HTTP-only cookie surface. Called last so CORS is the outermost middleware.
    """

    if not settings.cors_allow_origins:
        return
    if settings.environment is Environment.PRODUCTION:
        return
    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.cors_allow_origins,
        allow_credentials=settings.cors_allow_credentials,
        allow_methods=settings.cors_allow_methods,
        allow_headers=settings.cors_allow_headers,
        expose_headers=settings.cors_expose_headers,
    )
