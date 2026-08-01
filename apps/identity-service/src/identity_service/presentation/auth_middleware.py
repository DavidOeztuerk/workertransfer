"""JWT auth middleware — sets request.state.user = AuthPrincipal | None.

Thin service-specific binding over `worker_auth.JwtAuthMiddleware` (ADR-0015).
The token-carrier logic (Authorization header *or* `access` cookie) and the ASGI
plumbing live in `worker-auth` so consent-service and every later service share
one implementation instead of copying this file — a copy is how the
header-only blindness that made `GET /me` unreachable from the browser would
come back.

What stays here is the only service-specific part: turning a token into this
service's `AuthPrincipal` via its own `JwTokenService`.
"""

from __future__ import annotations

from starlette.types import ASGIApp, Scope
from worker_auth import DEFAULT_COOKIE_NAME, JwtAuthMiddleware

from identity_service.application.ports import AuthPrincipal
from identity_service.infrastructure.auth.jwt_service import JwTokenService

# Must match the cookie name set by POST /auth/login in presentation/http/router.py.
ACCESS_COOKIE_NAME = DEFAULT_COOKIE_NAME


class AuthMiddleware(JwtAuthMiddleware):
    """Installed via ``app.add_middleware(AuthMiddleware, tokens=tokens)``."""

    def __init__(self, app: ASGIApp, *, tokens: JwTokenService) -> None:
        super().__init__(
            app,
            verify=tokens.verify_access_token,
            cookie_name=ACCESS_COOKIE_NAME,
        )


def get_request_user(scope: Scope) -> AuthPrincipal | None:
    state = scope.get("state") or {}
    principal: AuthPrincipal | None = state.get("user")
    return principal
