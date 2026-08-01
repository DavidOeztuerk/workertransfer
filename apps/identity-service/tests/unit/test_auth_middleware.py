"""AuthMiddleware token carriers: Authorization header AND the `access` cookie.

The browser client never sees the httpOnly access token, so it can only replay
it as a cookie (`credentials: "include"`). Supporting only the header made
`GET /me` unreachable from apps/web. These tests pin both carriers without
needing a database or Docker.
"""

from __future__ import annotations

from uuid import uuid4

from identity_service.infrastructure.auth.jwt_service import JwTokenService
from identity_service.presentation.auth_middleware import AuthMiddleware, get_request_user
from starlette.types import Receive, Scope, Send

SECRET = "unit-test-secret-with-at-least-thirty-two-bytes"


def _tokens() -> JwTokenService:
    return JwTokenService(SECRET)


def _scope(headers: list[tuple[bytes, bytes]]) -> Scope:
    return {"type": "http", "headers": headers, "path": "/me", "method": "GET"}


async def _run(scope: Scope) -> Scope:
    """Drive the middleware over `scope` and hand back the mutated scope."""
    seen: dict[str, Scope] = {}

    async def app(inner: Scope, receive: Receive, send: Send) -> None:
        seen["scope"] = inner

    async def receive() -> dict[str, str]:  # pragma: no cover - never awaited
        return {"type": "http.request"}

    async def send(_message: object) -> None:  # pragma: no cover - never awaited
        return None

    await AuthMiddleware(app, tokens=_tokens())(scope, receive, send)
    return seen["scope"]


def _issue() -> tuple[str, str, str]:
    user_id, tenant_id = uuid4(), uuid4()
    token = _tokens().issue_access_token(user_id, tenant_id, ["user"], [])
    return token, str(user_id), str(tenant_id)


async def test_bearer_header_resolves_principal() -> None:
    token, user_id, tenant_id = _issue()
    scope = await _run(_scope([(b"authorization", f"Bearer {token}".encode())]))
    principal = get_request_user(scope)
    assert principal is not None
    assert str(principal.user_id) == user_id
    assert str(principal.tenant_id) == tenant_id


async def test_access_cookie_resolves_principal() -> None:
    """The browser path: no Authorization header, token only in the cookie."""
    token, user_id, _ = _issue()
    scope = await _run(_scope([(b"cookie", f"access={token}".encode())]))
    principal = get_request_user(scope)
    assert principal is not None
    assert str(principal.user_id) == user_id


async def test_cookie_is_read_alongside_other_cookies() -> None:
    token, user_id, _ = _issue()
    cookie = f"theme=dark; access={token}; refresh=irrelevant".encode()
    scope = await _run(_scope([(b"cookie", cookie)]))
    principal = get_request_user(scope)
    assert principal is not None
    assert str(principal.user_id) == user_id


async def test_header_wins_over_cookie() -> None:
    header_token, header_user, _ = _issue()
    cookie_token, cookie_user, _ = _issue()
    assert header_user != cookie_user
    scope = await _run(
        _scope(
            [
                (b"authorization", f"Bearer {header_token}".encode()),
                (b"cookie", f"access={cookie_token}".encode()),
            ]
        )
    )
    principal = get_request_user(scope)
    assert principal is not None
    assert str(principal.user_id) == header_user


async def test_no_token_yields_none() -> None:
    assert get_request_user(await _run(_scope([]))) is None


async def test_refresh_cookie_alone_is_not_an_access_token() -> None:
    """Only the `access` cookie authenticates; the refresh cookie must not."""
    _, _, _ = _issue()
    refresh = _tokens().issue_refresh_token(uuid4(), uuid4(), session_jti="jti-1")
    scope = await _run(_scope([(b"cookie", f"refresh={refresh}".encode())]))
    assert get_request_user(scope) is None


async def test_malformed_carriers_yield_none() -> None:
    for headers in (
        [(b"authorization", b"Bearer not-a-jwt")],
        [(b"authorization", b"Basic dXNlcjpwYXNz")],
        [(b"authorization", b"Bearer ")],
        [(b"cookie", b"access=not-a-jwt")],
        [(b"cookie", b"access=")],
    ):
        assert get_request_user(await _run(_scope(headers))) is None
