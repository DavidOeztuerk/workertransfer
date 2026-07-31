"""Transport-generic JWT authentication middleware.

Lives here rather than in the kernel because authentication is opt-in per service
(ADR-0002: the kernel stays small and always-on; `worker-*` packages are the
composable, opt-in building blocks). Services install it through
``create_api_app(..., auth_middleware=JwtAuthMiddleware, auth_middleware_kwargs=...)``.

It is shared rather than copied for a concrete reason: the original per-service
copy in identity-service read the token *only* from ``Authorization: Bearer``,
while login delivers it as an httpOnly cookie — so ``GET /me`` was unreachable
from any browser. A second hand-copied middleware would have reproduced exactly
that bug, which is the drift ADR-0014 documents for `worker-exceptions`.

Raw ASGI on purpose: ``BaseHTTPMiddleware`` runs the downstream app in a separate
task, which breaks contextvar propagation (the reason ADR-0009 deleted
`worker-middleware`).
"""

from __future__ import annotations

from collections.abc import Callable
from http.cookies import SimpleCookie
from typing import Any

from starlette.types import ASGIApp, Receive, Scope, Send

__all__ = ["JwtAuthMiddleware", "extract_bearer_token", "extract_cookie_token", "resolve_token"]

DEFAULT_COOKIE_NAME = "access"


def extract_bearer_token(scope: Scope) -> str | None:
    """Read a token from ``Authorization: Bearer <token>``."""
    for name, value in scope.get("headers") or ():
        if name == b"authorization":
            header = value.decode("latin-1")
            if header.lower().startswith("bearer "):
                return header[7:].strip() or None
            return None
    return None


def extract_cookie_token(scope: Scope, cookie_name: str = DEFAULT_COOKIE_NAME) -> str | None:
    """Read a token from the named cookie.

    The browser never sees an httpOnly token, so a cookie is the only carrier it
    can offer back (``credentials: "include"``).
    """
    for name, value in scope.get("headers") or ():
        if name != b"cookie":
            continue
        jar: SimpleCookie = SimpleCookie()
        try:
            jar.load(value.decode("latin-1"))
        except Exception:
            return None
        morsel = jar.get(cookie_name)
        return morsel.value.strip() or None if morsel is not None else None
    return None


def resolve_token(scope: Scope, cookie_name: str = DEFAULT_COOKIE_NAME) -> str | None:
    """Header first, cookie second.

    The header wins so an explicit credential on the request always beats an
    ambient one from the jar — a caller passing a token deliberately should not
    be silently overridden by a stale session cookie.
    """
    return extract_bearer_token(scope) or extract_cookie_token(scope, cookie_name)


class JwtAuthMiddleware:
    """Attach the verified principal to ``scope["state"][state_key]``.

    ``verify`` is any callable turning a token into a principal and raising on
    failure — typically ``TokenManager.verify_token`` bound to
    ``expected_type="access"``, or a service adapter returning its own principal
    type. Keeping it a plain callable means this package never learns a service's
    principal shape (ADR-0004 boundary).

    On any failure — no token, malformed, wrong type, expired — the principal is
    ``None`` rather than a 401, so public endpoints keep working and each route
    decides for itself.
    """

    def __init__(
        self,
        app: ASGIApp,
        *,
        verify: Callable[[str], Any],
        cookie_name: str = DEFAULT_COOKIE_NAME,
        state_key: str = "user",
    ) -> None:
        self.app = app
        self._verify = verify
        self._cookie_name = cookie_name
        self._state_key = state_key

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        scope.setdefault("state", {})
        scope["state"][self._state_key] = self._principal(scope)
        await self.app(scope, receive, send)

    def _principal(self, scope: Scope) -> Any:
        token = resolve_token(scope, self._cookie_name)
        if token is None:
            return None
        try:
            return self._verify(token)
        except Exception:
            return None
