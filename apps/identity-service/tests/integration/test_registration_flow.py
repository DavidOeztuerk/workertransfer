"""Der ganze Onboarding-Weg ohne Handarbeit an der Datenbank.

Bisher ließ sich eine Mitgliedschaft nur per SQL-INSERT anlegen — jeder
bestehende Integrationstest musste an dieser Stelle in die Datenbank greifen.
Dieser Test schließt die Lücke: registrieren, den Token aus der VERSANDTEN MAIL
holen, bestätigen, anmelden, Unternehmen anlegen, hineinwechseln.

Er hat außerdem einen Bug gefunden, den keine Unit-Test-Ebene sehen konnte:
`handle_verify_email` mutierte ein losgelöstes Aggregat, ohne es zurückzu-
schreiben. `verify-email` meldete 200, der nächste Login scheiterte trotzdem mit
403. Die Fakes gaben dieselbe Instanz zurück und verdeckten das —
`test_confirming_survives_a_fresh_request` pinnt es fest.

Skips ohne Docker (ADR-0011 offline-skip).
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from worker_database import Base

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]


@pytest.fixture
def migrated_schema(postgres_url: str, monkeypatch: pytest.MonkeyPatch) -> None:
    """Frisches Schema, unabhängig von der Reihenfolge im Suite-Lauf.

    Base.metadata.drop_all lässt alembic_version stehen (alembic besitzt die
    Tabelle, sie steht nicht in Base.metadata), ein späteres upgrade head wäre
    also ein No-op. Deshalb wird beides zurückgesetzt.
    """
    from sqlalchemy import create_engine, text

    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    monkeypatch.setenv("WORKER_DATABASE_URL", postgres_url)

    sync_url = postgres_url.replace("postgresql+asyncpg://", "postgresql+psycopg://")
    try:
        reset_engine = create_engine(sync_url)
    except Exception:
        reset_engine = create_engine(postgres_url.replace("postgresql+asyncpg://", "postgresql://"))
    with reset_engine.connect() as conn:
        Base.metadata.drop_all(conn, checkfirst=True)
        conn.execute(text("DROP TABLE IF EXISTS alembic_version"))
        conn.commit()
    reset_engine.dispose()

    command.upgrade(cfg, "head")


@pytest.fixture
def outbox(monkeypatch: pytest.MonkeyPatch) -> list[tuple[str, str, str]]:
    """Fängt den echten Versandweg ab, ohne den Rest zu ersetzen.

    Der Klartext-Token existiert nur in der Mail — genau wie in Produktion. Ihn
    stattdessen aus der Datenbank zu lesen wäre unmöglich (dort steht nur der
    Hash) und würde den Zweck dieses Tests aufheben.
    """
    sent: list[tuple[str, str, str]] = []

    async def _capture(self: Any, *, to: str, subject: str, body: str) -> None:
        sent.append((to, subject, body))

    from identity_service.infrastructure.mail import SmtpMailer

    monkeypatch.setattr(SmtpMailer, "send", _capture)
    return sent


def _token_from(outbox: list[tuple[str, str, str]]) -> str:
    match = re.search(r"/verify\?token=(\S+)", outbox[-1][2])
    assert match is not None, f"kein Bestätigungslink in der Mail: {outbox[-1][2]!r}"
    return match.group(1)


def _app(monkeypatch: pytest.MonkeyPatch) -> Any:
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")
    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    return build_app(IdentityServiceSettings())


async def test_person_registers_confirms_creates_company_and_switches(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = ASGITransport(app=_app(monkeypatch))

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        reg = await client.post(
            "/auth/register",
            json={
                "email": "anna@flow-firma.de",
                "password": "strongpassword1",
                "display_name": "Anna",
            },
        )
        assert reg.status_code == 201, reg.text

        # Unbestätigt: 403, nicht 401 — sonst wäre das Konto eine Sackgasse.
        blocked = await client.post(
            "/auth/login",
            json={"email": "anna@flow-firma.de", "password": "strongpassword1"},
        )
        assert blocked.status_code == 403, blocked.text

        confirmed = await client.post("/auth/verify-email", json={"token": _token_from(outbox)})
        assert confirmed.status_code == 200, confirmed.text

        login = await client.post(
            "/auth/login",
            json={"email": "anna@flow-firma.de", "password": "strongpassword1"},
        )
        assert login.status_code == 200, login.text
        access = login.cookies.get("access")

        created = await client.post(
            "/companies",
            json={"name": "Flow Firma GmbH"},
            headers={"Authorization": f"Bearer {access}"},
        )
        assert created.status_code == 201, created.text
        company_id = created.json()["id"]
        # Die Domain stammt aus der bestätigten Adresse, nicht aus dem Request.
        assert created.json()["domain"] == "flow-firma.de"

        mine = await client.get("/me/companies", headers={"Authorization": f"Bearer {access}"})
        assert [m["role"] for m in mine.json()] == ["admin"]

        switched = await client.post(
            f"/auth/company/{company_id}", headers={"Authorization": f"Bearer {access}"}
        )
        assert switched.status_code == 200, switched.text

        me = await client.get(
            "/me", headers={"Authorization": f"Bearer {switched.cookies.get('access')}"}
        )
        assert me.json()["tenant_id"] == company_id


async def test_confirming_survives_a_fresh_request(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Regression: die Freischaltung muss in der Datenbank landen.

    handle_verify_email mutierte ein losgelöstes Aggregat ohne Rückschreiben.
    Der Endpunkt meldete 200, und erst der nächste Request — mit einer neuen
    Session — sah wieder ein PENDING-Konto.
    """
    transport = ASGITransport(app=_app(monkeypatch))

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        await client.post(
            "/auth/register",
            json={
                "email": "persist@flow-firma.de",
                "password": "strongpassword1",
                "display_name": "P",
            },
        )
        await client.post("/auth/verify-email", json={"token": _token_from(outbox)})

        login = await client.post(
            "/auth/login",
            json={"email": "persist@flow-firma.de", "password": "strongpassword1"},
        )

    assert login.status_code == 200, login.text


async def test_a_private_address_cannot_claim_a_company_but_lives_normally(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Registrierung ist für private Adressen offen — nur die Domain nicht."""
    transport = ASGITransport(app=_app(monkeypatch))

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        reg = await client.post(
            "/auth/register",
            json={"email": "max@gmail.com", "password": "strongpassword1", "display_name": "Max"},
        )
        assert reg.status_code == 201, reg.text
        await client.post("/auth/verify-email", json={"token": _token_from(outbox)})
        login = await client.post(
            "/auth/login", json={"email": "max@gmail.com", "password": "strongpassword1"}
        )
        assert login.status_code == 200, login.text

        refused = await client.post(
            "/companies",
            json={"name": "Nicht Google"},
            headers={"Authorization": f"Bearer {login.cookies.get('access')}"},
        )

    assert refused.status_code == 422, refused.text


async def test_a_second_person_cannot_claim_the_same_domain(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = ASGITransport(app=_app(monkeypatch))

    async def _confirmed_login(client: AsyncClient, email: str) -> str:
        await client.post(
            "/auth/register",
            json={"email": email, "password": "strongpassword1", "display_name": "X"},
        )
        await client.post("/auth/verify-email", json={"token": _token_from(outbox)})
        login = await client.post(
            "/auth/login", json={"email": email, "password": "strongpassword1"}
        )
        token: str = login.cookies.get("access") or ""
        return token

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        first = await _confirmed_login(client, "erste@dieselbe-firma.de")
        ok = await client.post(
            "/companies", json={"name": "Erste"}, headers={"Authorization": f"Bearer {first}"}
        )
        assert ok.status_code == 201, ok.text

        second = await _confirmed_login(client, "zweite@dieselbe-firma.de")
        clash = await client.post(
            "/companies", json={"name": "Zweite"}, headers={"Authorization": f"Bearer {second}"}
        )

    assert clash.status_code == 409, clash.text


async def test_registering_a_known_address_answers_the_same_and_warns_the_owner(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Kein Enumerationskanal über den echten HTTP-Weg."""
    transport = ASGITransport(app=_app(monkeypatch))
    body = {
        "email": "doppelt@flow-firma.de",
        "password": "strongpassword1",
        "display_name": "D",
    }

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        first = await client.post("/auth/register", json=body)
        outbox.clear()
        second = await client.post("/auth/register", json=body)

    assert first.status_code == second.status_code == 201
    assert first.json() == second.json()
    assert len(outbox) == 1
    assert "versucht" in outbox[0][2].lower()


async def test_logging_out_actually_ends_the_session(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Regression: /auth/logout löschte nur das refresh-Cookie.

    Das access-Cookie blieb stehen, und verify_access_token prüft nur Signatur
    und Ablauf — nie die sessions-Tabelle. /me antwortete danach weiter mit 200,
    der Browser zeigte weiter "angemeldet", und niemand bekam einen Fehler zu
    sehen. Auf einem geteilten Rechner hieß das: bis zu 15 Minuten offen.
    """
    transport = ASGITransport(app=_app(monkeypatch))

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        await client.post(
            "/auth/register",
            json={
                "email": "bye@flow-firma.de",
                "password": "strongpassword1",
                "display_name": "B",
            },
        )
        await client.post("/auth/verify-email", json={"token": _token_from(outbox)})
        await client.post(
            "/auth/login", json={"email": "bye@flow-firma.de", "password": "strongpassword1"}
        )
        before = await client.get("/me")
        assert before.status_code == 200, before.text

        logout = await client.post("/auth/logout")
        assert logout.status_code == 204, logout.text

        # Derselbe Client, derselbe Cookie-Jar — genau das, was der Browser tut.
        after = await client.get("/me")

    assert after.status_code == 401, after.text


async def test_clicking_the_confirmation_link_twice_stays_successful(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Ein Reload der Bestätigungsseite darf keine rote Fehlermeldung zeigen."""
    transport = ASGITransport(app=_app(monkeypatch))

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        await client.post(
            "/auth/register",
            json={
                "email": "zweimal@flow-firma.de",
                "password": "strongpassword1",
                "display_name": "Z",
            },
        )
        token = _token_from(outbox)
        first = await client.post("/auth/verify-email", json={"token": token})
        second = await client.post("/auth/verify-email", json={"token": token})

    assert first.status_code == 200, first.text
    assert second.status_code == 200, second.text
