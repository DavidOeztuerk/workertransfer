"""JWT auth middleware — sets request.state.user = AuthPrincipal | None.

Resolves the access token from the raw ASGI scope, preferring the
``Authorization: Bearer <token>`` header and falling back to the ``access``
cookie that ``POST /auth/login`` sets. Both carriers are needed: service-to-
service and CLI callers send the header, while the browser client
(``apps/web/src/auth/client.ts``) never sees the ``httpOnly`` token and can
only send it back as a cookie (``credentials: "include"``). Supporting the
header alone made ``GET /me`` unreachable from the web app.

On any failure (no token, malformed, invalid/expired) it sets ``user = None``
so endpoints can decide (``/me`` returns 401; the auth endpoints are public
and ignore it). Starlette's ``request.state`` maps from ``scope["state"]``.
"""

from __future__ import annotations

from http.cookies import SimpleCookie

from starlette.types import ASGIApp, Receive, Scope, Send

from identity_service.application.ports import AuthPrincipal
from identity_service.infrastructure.auth.jwt_service import JwTokenService

# Must match the cookie name set by POST /auth/login in presentation/http/router.py.
ACCESS_COOKIE_NAME = "access"


class AuthMiddleware:
    """ASGI middleware that attaches the verified principal to request state.

    Starlette's ``app.add_middleware(AuthMiddleware, tokens=tokens)`` passes
    the keyword options to this constructor after ``app``.
    """

    def __init__(self, app: ASGIApp, *, tokens: JwTokenService) -> None:
        self.app = app
        self._tokens = tokens

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        principal = _extract_principal(scope, self._tokens)
        scope.setdefault("state", {})
        scope["state"]["user"] = principal
        await self.app(scope, receive, send)


def get_request_user(scope: Scope) -> AuthPrincipal | None:
    state = scope.get("state") or {}
    return state.get("user")


def _extract_principal(scope: Scope, tokens: JwTokenService) -> AuthPrincipal | None:
    token = _bearer_token(scope) or _cookie_token(scope)
    if token is None:
        return None
    try:
        return tokens.verify_access_token(token)
    except Exception:
        return None


def _bearer_token(scope: Scope) -> str | None:
    for name, value in scope.get("headers") or ():
        if name == b"authorization":
            auth = value.decode("latin-1")
            if auth.lower().startswith("bearer "):
                return auth[7:].strip() or None
            return None
    return None


def _cookie_token(scope: Scope) -> str | None:
    for name, value in scope.get("headers") or ():
        if name != b"cookie":
            continue
        jar: SimpleCookie = SimpleCookie()
        try:
            jar.load(value.decode("latin-1"))
        except Exception:
            return None
        morsel = jar.get(ACCESS_COOKIE_NAME)
        return morsel.value.strip() or None if morsel is not None else None
    return None
