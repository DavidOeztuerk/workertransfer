"""Was `TokenManager` von einem Token aus dem .NET-Dienst annehmen muss.

Girder setzt `iss` und `aud` in jeden Token und kann es nicht lassen
(`bugs/jwtservice-kann-nicht-ohne-aud-ausstellen.md`). PyJWT weist einen Token
mit `aud` ab, solange keine Zielgruppe erwartet wird — ohne die Nachsicht hier
läse kein Python-Dienst einen einzigen .NET-Token.

Die Nachsicht ist eine Schuld und keine Dauerlösung: Ü-1 in
`docs/uebergang-python-dotnet.md`.
"""

from __future__ import annotations

import time
from typing import Any
from uuid import uuid4

import jwt as pyjwt
import pytest
from worker_auth import InvalidToken, TokenManager

SECRET = "a" * 40

#: Was der .NET-Dienst schreibt — Girders eigene Ansprüche plus die zwei, die
#: die Python-Dienste brauchen.
NET_CLAIMS: dict[str, Any] = {
    "iss": "workertransfer-identity",
    "aud": "workertransfer",
    "email": "anna@example.com",
    "session_id": "1f46520e-796a-4abf-9502-835a42046737",
    "role": "user",
    "email_verified": "True",
    "account_status": "Active",
}


def _net_token(**overrides: Any) -> str:
    now = int(time.time())
    claims: dict[str, Any] = {
        "sub": str(uuid4()),
        "tenant_id": str(uuid4()),
        "type": "access",
        "jti": str(uuid4()),
        "iat": now,
        "exp": now + 900,
        **NET_CLAIMS,
    }
    claims.update(overrides)
    return str(pyjwt.encode(claims, SECRET, algorithm="HS256"))


def test_ein_net_token_wird_angenommen() -> None:
    payload = TokenManager(secret=SECRET).verify_token(_net_token(), expected_type="access")

    assert payload.type == "access"
    # Ohne `roles` im Token bleibt die Liste leer — die Rollen stehen in
    # `user_tenant_memberships`, und dort werden sie auch gelesen.
    assert payload.roles == []
    assert payload.permissions == []


def test_der_mandant_kommt_aus_tenant_id_nicht_aus_girders_tenant() -> None:
    """Ein Firmen-Token darf hier nicht als Personen-Token ankommen."""
    firma = uuid4()

    payload = TokenManager(secret=SECRET).verify_token(
        _net_token(tenant_id=str(firma), tenant=str(firma)), expected_type="access"
    )

    assert payload.tenant_id == firma


def test_ein_python_token_ohne_zielgruppe_wird_weiterhin_angenommen() -> None:
    """Beim Umstieg liegen im Browser noch Token ohne `aud`."""
    manager = TokenManager(secret=SECRET)
    alt = manager.create_access_token(uuid4(), None, roles=["user"], permissions=[])

    assert manager.verify_token(alt, expected_type="access").tenant_id is None


class TestWasTrotzdemAbgelehntWird:
    """Die Nachsicht gilt der Zielgruppe und sonst nichts."""

    def test_eine_falsche_signatur(self) -> None:
        fremd = _net_token()

        with pytest.raises(InvalidToken):
            TokenManager(secret="b" * 40).verify_token(fremd, expected_type="access")

    def test_ein_abgelaufener_token(self) -> None:
        now = int(time.time())

        with pytest.raises(InvalidToken):
            TokenManager(secret=SECRET).verify_token(
                _net_token(iat=now - 1800, exp=now - 900), expected_type="access"
            )

    def test_ein_erneuerungstoken_an_der_stelle_eines_zugriffstokens(self) -> None:
        with pytest.raises(InvalidToken):
            TokenManager(secret=SECRET).verify_token(
                _net_token(type="refresh"), expected_type="access"
            )

    def test_ein_token_ohne_typ(self) -> None:
        ohne_typ = {k: v for k, v in NET_CLAIMS.items()}
        now = int(time.time())
        token = pyjwt.encode(
            {"sub": str(uuid4()), "jti": "x", "iat": now, "exp": now + 900, **ohne_typ},
            SECRET,
            algorithm="HS256",
        )

        with pytest.raises(InvalidToken):
            TokenManager(secret=SECRET).verify_token(str(token), expected_type="access")
