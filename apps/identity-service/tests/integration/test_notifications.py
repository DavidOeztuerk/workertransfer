"""Der Benachrichtigungsweg an echten Migrationen und dem echten Versandpfad.

Der tragende Test ist `test_the_mail_never_says_what_it_is_about`: er liest die
Mail, die wirklich verschickt wurde, und prüft, dass sie nichts über den Vorgang
verrät. Eine Mail landet womöglich im Postfach beim aktuellen Arbeitgeber — auf
dessen Servern, in dessen Backups. Eine Zeile mit „Marktstatus" darin wäre genau
die Auskunft, gegen die diese Plattform gebaut ist.

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
SECRET = "test-notify-secret"


@pytest.fixture
def migrated_schema(postgres_url: str, monkeypatch: pytest.MonkeyPatch) -> None:
    from sqlalchemy import create_engine, text

    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    monkeypatch.setenv("WORKER_DATABASE_URL", postgres_url)

    sync_url = postgres_url.replace("postgresql+asyncpg://", "postgresql+psycopg://")
    reset_engine = create_engine(sync_url)
    with reset_engine.connect() as conn:
        Base.metadata.drop_all(conn, checkfirst=True)
        conn.execute(text("DROP TABLE IF EXISTS alembic_version"))
        conn.commit()
    reset_engine.dispose()

    command.upgrade(cfg, "head")


@pytest.fixture
def outbox(monkeypatch: pytest.MonkeyPatch) -> list[tuple[str, str, str]]:
    sent: list[tuple[str, str, str]] = []

    async def _capture(self: Any, *, to: str, subject: str, body: str) -> None:
        sent.append((to, subject, body))

    from identity_service.infrastructure.mail import SmtpMailer

    monkeypatch.setattr(SmtpMailer, "send", _capture)
    return sent


def _app(monkeypatch: pytest.MonkeyPatch, *, secret: str = SECRET) -> Any:
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")
    monkeypatch.setenv("WORKER_NOTIFY_SECRET", secret)
    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    return build_app(IdentityServiceSettings())


async def _confirmed_person(
    client: AsyncClient, outbox: list[tuple[str, str, str]], email: str
) -> str:
    """Registrieren und bestätigen — ein unbestätigtes Konto bekommt keine Post."""
    await client.post(
        "/auth/register",
        json={"email": email, "password": "strongpassword1", "display_name": "Anna"},
    )
    match = re.search(r"/verify\?token=(\S+)", outbox[-1][2])
    assert match is not None
    await client.post("/auth/verify-email", json={"token": match.group(1)})
    login = await client.post("/auth/login", json={"email": email, "password": "strongpassword1"})
    assert login.status_code == 200, login.text
    me = await client.get("/me")
    return str(me.json()["user_id"])


async def test_a_notification_reaches_the_person(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        user_id = await _confirmed_person(client, outbox, "anna@notify-test.de")
        outbox.clear()

        response = await client.post(
            "/notifications",
            json={"user_id": user_id, "kind": "market_request"},
            headers={"X-Notify-Secret": SECRET},
        )

    assert response.status_code == 202
    assert len(outbox) == 1
    assert outbox[0][0] == "anna@notify-test.de"


@pytest.mark.parametrize(
    "kind", ["resume_request", "market_request", "application_update", "transfer_update"]
)
async def test_the_mail_never_says_what_it_is_about(
    kind: str,
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Der tragende Test dieses Schnitts, an der wirklich versandten Mail."""
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        user_id = await _confirmed_person(client, outbox, f"anna-{kind}@notify-test.de")
        outbox.clear()
        await client.post(
            "/notifications",
            json={"user_id": user_id, "kind": kind},
            headers={"X-Notify-Secret": SECRET},
        )

    _to, subject, body = outbox[0]
    # Der Markenname raus: „WorkerTransfer" enthält „transfer", und der Name
    # steht schon auf der Bestätigungsmail.
    text = f"{subject}\n{body}".lower().replace("workertransfer", "")
    for leak in ("marktstatus", "lebenslauf", "bewerbung", "transfer", "anfrage", "angebot", kind):
        assert leak not in text, f"{leak!r} steht in der Mail"


async def test_the_throttle_swallows_the_second_within_the_hour(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Auch eine inhaltsfreie Mail verrät ihren Zeitpunkt."""
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        user_id = await _confirmed_person(client, outbox, "anna-throttle@notify-test.de")
        outbox.clear()
        headers = {"X-Notify-Secret": SECRET}
        first = await client.post(
            "/notifications", json={"user_id": user_id, "kind": "market_request"}, headers=headers
        )
        # Eine andere Art — die Drossel greift trotzdem, sonst wären vier Arten
        # vier Kanäle und die Frequenz verriete wieder etwas.
        second = await client.post(
            "/notifications", json={"user_id": user_id, "kind": "resume_request"}, headers=headers
        )

    # Beide 202: der Aufrufer erfährt nicht, dass gedrosselt wurde.
    assert (first.status_code, second.status_code) == (202, 202)
    assert len(outbox) == 1


async def test_a_switched_off_kind_stays_quiet_and_the_others_do_not(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        user_id = await _confirmed_person(client, outbox, "anna-off@notify-test.de")
        saved = await client.put(
            "/me/notification-preferences",
            json={
                "resume_request": True,
                "market_request": False,
                "application_update": True,
                "transfer_update": True,
            },
        )
        assert saved.status_code == 200
        # Wirklich gespeichert, nicht nur zurückgespiegelt.
        assert (await client.get("/me/notification-preferences")).json()["market_request"] is False

        outbox.clear()
        headers = {"X-Notify-Secret": SECRET}
        await client.post(
            "/notifications", json={"user_id": user_id, "kind": "market_request"}, headers=headers
        )
        assert outbox == []

        await client.post(
            "/notifications", json={"user_id": user_id, "kind": "resume_request"}, headers=headers
        )
        assert len(outbox) == 1


async def test_every_kind_is_on_before_anyone_touched_the_settings(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Ohne Zeile in der Tabelle gilt die Voreinstellung — sie wird nicht angelegt."""
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        await _confirmed_person(client, outbox, "anna-default@notify-test.de")
        body = (await client.get("/me/notification-preferences")).json()

    assert body == {
        "resume_request": True,
        "market_request": True,
        "application_update": True,
        "transfer_update": True,
    }


async def test_an_unconfirmed_account_gets_no_post(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Die Adresse ist noch nicht als seine erwiesen."""
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        await client.post(
            "/auth/register",
            json={
                "email": "pending@notify-test.de",
                "password": "strongpassword1",
                "display_name": "Pending",
            },
        )
        # Die user_id ist von außen nicht zu erfahren — deshalb über die
        # Datenbank, was hier ausnahmsweise der Punkt des Tests ist.
        from sqlalchemy import create_engine, text

        sync = create_engine(postgres_url.replace("postgresql+asyncpg://", "postgresql+psycopg://"))
        with sync.connect() as conn:
            row = conn.execute(
                text("SELECT id FROM users WHERE email = 'pending@notify-test.de'")
            ).one()
        sync.dispose()

        outbox.clear()
        response = await client.post(
            "/notifications",
            json={"user_id": str(row[0]), "kind": "market_request"},
            headers={"X-Notify-Secret": SECRET},
        )

    # 202 trotzdem: der Aufrufer erfährt nicht, dass das Konto unbestätigt ist.
    assert response.status_code == 202
    assert outbox == []


async def test_an_unknown_person_answers_exactly_like_a_known_one(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Sonst wäre der Endpunkt ein Orakel darüber, wer auf der Plattform ist."""
    from uuid import uuid4

    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        known = await _confirmed_person(client, outbox, "anna-known@notify-test.de")
        headers = {"X-Notify-Secret": SECRET}
        for_known = await client.post(
            "/notifications", json={"user_id": known, "kind": "market_request"}, headers=headers
        )
        for_stranger = await client.post(
            "/notifications",
            json={"user_id": str(uuid4()), "kind": "market_request"},
            headers=headers,
        )

    assert for_known.status_code == for_stranger.status_code == 202
    assert for_known.content == for_stranger.content


@pytest.mark.parametrize("headers", [{}, {"X-Notify-Secret": "wrong"}])
async def test_without_the_secret_the_endpoint_does_not_exist(
    headers: dict[str, str],
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """404, nicht 401: ein 401 bestätigt, dass es den Endpunkt gibt."""
    from uuid import uuid4

    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/notifications",
            json={"user_id": str(uuid4()), "kind": "market_request"},
            headers=headers,
        )

    assert response.status_code == 404
    assert outbox == []


async def test_an_empty_secret_closes_the_endpoint(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Eine Voreinstellung, die im Zweifel öffnet, wäre hier die falsche.

    Ohne diese Regel wäre jede Umgebung, in der die Variable vergessen wurde,
    ein offener Mailversender — und zwar lautlos.
    """
    from uuid import uuid4

    transport = ASGITransport(app=_app(monkeypatch, secret=""))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/notifications",
            json={"user_id": str(uuid4()), "kind": "market_request"},
            headers={"X-Notify-Secret": ""},
        )

    assert response.status_code == 404
    assert outbox == []


async def test_the_settings_belong_to_the_person(
    postgres_url: str,
    migrated_schema: None,
    outbox: list[tuple[str, str, str]],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = ASGITransport(app=_app(monkeypatch))
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/me/notification-preferences")

    assert response.status_code == 401
