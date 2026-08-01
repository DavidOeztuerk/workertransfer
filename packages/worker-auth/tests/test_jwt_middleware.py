"""The shared JWT middleware accepts both token carriers.

These pin the contract every service inherits, so the header-only blindness that
made identity-service's `GET /me` unreachable from a browser cannot come back via
a hand-copied middleware.
"""

from __future__ import annotations

from typing import Any

import pytest
from starlette.types import Receive, Scope, Send
from worker_auth import (
    JwtAuthMiddleware,
    extract_bearer_token,
    extract_cookie_token,
    resolve_token,
)

TOKEN = "a.valid.token"  # opaque fixture; the verifier below is a stub


def _scope(headers: list[tuple[bytes, bytes]], scope_type: str = "http") -> Scope:
    return {"type": scope_type, "headers": headers, "path": "/x", "method": "GET"}


def _verify_ok(token: str) -> dict[str, str]:
    if token != TOKEN:
        raise ValueError("bad token")
    return {"principal": token}


async def _run(scope: Scope, **kwargs: Any) -> Scope:
    seen: dict[str, Scope] = {}

    async def downstream(inner: Scope, receive: Receive, send: Send) -> None:
        seen["scope"] = inner

    async def receive() -> dict[str, str]:  # pragma: no cover - never awaited
        return {"type": "http.request"}

    async def send(_message: object) -> None:  # pragma: no cover - never awaited
        return None

    kwargs.setdefault("verify", _verify_ok)
    await JwtAuthMiddleware(downstream, **kwargs)(scope, receive, send)
    return seen["scope"]


class TestExtraction:
    def test_bearer_header(self) -> None:
        assert extract_bearer_token(_scope([(b"authorization", b"Bearer abc")])) == "abc"

    @pytest.mark.parametrize(
        "header",
        [b"Basic abc", b"Bearer ", b"bearer", b"abc"],
    )
    def test_non_bearer_headers_yield_none(self, header: bytes) -> None:
        assert extract_bearer_token(_scope([(b"authorization", header)])) is None

    def test_bearer_is_case_insensitive(self) -> None:
        assert extract_bearer_token(_scope([(b"authorization", b"bearer abc")])) == "abc"

    def test_cookie(self) -> None:
        assert extract_cookie_token(_scope([(b"cookie", b"access=abc")])) == "abc"

    def test_cookie_among_others(self) -> None:
        jar = b"theme=dark; access=abc; refresh=zzz"
        assert extract_cookie_token(_scope([(b"cookie", jar)])) == "abc"

    def test_cookie_name_is_configurable(self) -> None:
        scope = _scope([(b"cookie", b"session_token=abc")])
        assert extract_cookie_token(scope, "session_token") == "abc"
        assert extract_cookie_token(scope) is None

    def test_empty_cookie_value_is_none(self) -> None:
        assert extract_cookie_token(_scope([(b"cookie", b"access=")])) is None

    def test_header_takes_precedence_over_cookie(self) -> None:
        # An explicit credential on the request must beat an ambient one from the
        # jar, or a stale session cookie could silently override the caller.
        scope = _scope([(b"authorization", b"Bearer from-header"), (b"cookie", b"access=from-jar")])
        assert resolve_token(scope) == "from-header"

    def test_cookie_is_used_when_no_header(self) -> None:
        assert resolve_token(_scope([(b"cookie", b"access=from-jar")])) == "from-jar"

    def test_no_carrier_is_none(self) -> None:
        assert resolve_token(_scope([])) is None


class TestMiddleware:
    async def test_sets_principal_from_header(self) -> None:
        scope = await _run(_scope([(b"authorization", f"Bearer {TOKEN}".encode())]))
        assert scope["state"]["user"] == {"principal": TOKEN}

    async def test_sets_principal_from_cookie(self) -> None:
        scope = await _run(_scope([(b"cookie", f"access={TOKEN}".encode())]))
        assert scope["state"]["user"] == {"principal": TOKEN}

    async def test_verification_failure_yields_none_not_an_error(self) -> None:
        # A rejected token is an anonymous request, not a 500: public endpoints
        # keep working and each route decides for itself.
        scope = await _run(_scope([(b"authorization", b"Bearer wrong")]))
        assert scope["state"]["user"] is None

    async def test_absent_token_yields_none(self) -> None:
        assert (await _run(_scope([])))["state"]["user"] is None

    async def test_state_key_is_configurable(self) -> None:
        scope = await _run(
            _scope([(b"authorization", f"Bearer {TOKEN}".encode())]), state_key="principal"
        )
        assert scope["state"]["principal"] == {"principal": TOKEN}

    async def test_existing_scope_state_is_preserved(self) -> None:
        scope = _scope([])
        scope["state"] = {"unrelated": 1}
        result = await _run(scope)
        assert result["state"]["unrelated"] == 1
        assert result["state"]["user"] is None

    async def test_non_http_scopes_pass_straight_through(self) -> None:
        # Websocket/lifespan scopes have no headers to authenticate.
        seen: dict[str, Scope] = {}

        async def downstream(inner: Scope, receive: Receive, send: Send) -> None:
            seen["scope"] = inner

        async def receive() -> dict[str, str]:  # pragma: no cover
            return {"type": "lifespan.startup"}

        async def send(_message: object) -> None:  # pragma: no cover
            return None

        scope = _scope([], scope_type="lifespan")
        await JwtAuthMiddleware(downstream, verify=_verify_ok)(scope, receive, send)
        assert "state" not in seen["scope"]
