"""JWT auth middleware — sets request.state.user = AuthPrincipal | None.

Reads the ``Authorization: Bearer <token>`` header from the raw ASGI scope
and, on a valid access token, sets ``request.state.user`` to the
``AuthPrincipal``. On any failure (no header, malformed, invalid/expired
token) it sets ``user = None`` so endpoints can decide (``/me`` returns 401;
the auth endpoints are public and ignore it). Starlette's ``request.state``
maps from ``scope["state"]``.
"""

from __future__ import annotations

from starlette.types import ASGIApp, Receive, Scope, Send

from identity_service.application.ports import AuthPrincipal
from identity_service.infrastructure.auth.jwt_service import JwTokenService


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
    headers = scope.get("headers") or ()
    auth: str | None = None
    for name, value in headers:
        if name == b"authorization":
            auth = value.decode("latin-1")
            break
    if auth is None or not auth.lower().startswith("bearer "):
        return None
    token = auth[7:].strip()
    try:
        return tokens.verify_access_token(token)
    except Exception:
        return None
