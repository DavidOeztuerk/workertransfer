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

from collections.abc import Mapping
from typing import Any

from fastapi import FastAPI
from sqlalchemy.ext.asyncio import create_async_engine
from worker_platform.configuration import Environment
from worker_platform.presentation.app import create_api_app
from worker_platform.presentation.throttle import Limit
from worker_tenancy import ClaimTenantResolver

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.compose import compose_infrastructure, erasure_runner
from identity_service.presentation.auth_middleware import AuthMiddleware
from identity_service.presentation.http.account_router import build_account_router
from identity_service.presentation.http.company_router import build_company_router
from identity_service.presentation.http.me_router import build_me_router
from identity_service.presentation.http.notification_router import build_notification_router
from identity_service.presentation.http.router import build_auth_router

#: Die Grenzen des Auth-Rands, je Herkunft. Jede Zahl hat einen Grund:
#:
#: `login` bremst das Durchprobieren von Passwörtern — zehn Fehlversuche in
#: fünf Minuten überschreitet niemand, der sein Passwort sucht.
#: `verify-email` bremst das Raten von Bestätigungstoken.
#: `refresh` ist großzügig: ein offener Browser erneuert regelmäßig, und eine
#: Bremse, die den normalen Betrieb trifft, wird abgeschaltet.
#: `register` bremst das Anlegen von Wegwerfkonten.
#: `resend-verification` ist die strengste, und zwar nicht wegen uns: sie
#: schickt eine Mail an eine Adresse, die der Aufrufer **nennt**. Ohne Bremse
#: ist das ein Weg, einen fremden Posteingang zu fluten.
AUTH_LIMITS: Mapping[tuple[str, str], Limit] = {
    ("POST", "/auth/login"): Limit(times=10, seconds=300),
    ("POST", "/auth/verify-email"): Limit(times=20, seconds=300),
    ("POST", "/auth/refresh"): Limit(times=60, seconds=300),
    ("POST", "/auth/register"): Limit(times=20, seconds=3600),
    ("POST", "/auth/resend-verification"): Limit(times=5, seconds=3600),
}


def throttle_limits(
    settings: IdentityServiceSettings,
) -> Mapping[tuple[str, str], Limit] | None:
    """Ob gebremst wird — und wenn ja, womit.

    In LOCAL und TEST aus, weil dort jede Anfrage von derselben
    Gateway-Adresse kommt und die Bremse die eigene Testreihe träfe statt eines
    Angreifers. Ausdrücklich einschaltbar; siehe `auth_throttle_enabled`.
    """
    enabled = settings.auth_throttle_enabled
    if enabled is None:
        enabled = settings.environment not in {Environment.LOCAL, Environment.TEST}
    return AUTH_LIMITS if enabled else None


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
        throttle_limits=throttle_limits(settings),
        trust_forwarded_for=settings.trust_forwarded_for,
        routers=(
            build_auth_router(deps),
            build_me_router(deps),
            build_company_router(deps),
            build_notification_router(deps),
            build_account_router(deps),
        ),
        # Der Löschzusteller läuft im Dienst mit (ADR-0025/0027). Ohne
        # Versuchsobergrenze und mit wachsendem Abstand — eine Löschung darf
        # nicht aufgeben, und ein toter Empfänger darf kein Dauerfeuer werden.
        background=(erasure_runner(deps, settings),),
    )


__all__ = ["build_app"]
