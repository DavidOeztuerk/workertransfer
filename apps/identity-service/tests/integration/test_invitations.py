"""Einladungen gegen echtes Postgres.

Ein Unternehmen entsteht mit genau einer Person. Ohne einen Weg hinein bleibt
es dabei — hier wird geprüft, dass dieser Weg genau einen Menschen hereinlässt:
den eingeladenen.
"""

from __future__ import annotations

import re
from collections.abc import Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import create_engine, text
from worker_database import Base

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]
PASSWORD = "strongpassword1"


@pytest.fixture
def migrated(postgres_url: str, monkeypatch: pytest.MonkeyPatch) -> None:
    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    monkeypatch.setenv("WORKER_DATABASE_URL", postgres_url)
    sync_url = postgres_url.replace("postgresql+asyncpg://", "postgresql+psycopg://")
    engine = create_engine(sync_url)
    with engine.connect() as conn:
        Base.metadata.drop_all(conn, checkfirst=True)
        conn.execute(text("DROP TABLE IF EXISTS company_invitations"))
        conn.execute(text("DROP TABLE IF EXISTS alembic_version"))
        conn.commit()
    engine.dispose()
    command.upgrade(cfg, "head")


class _CollectingMailer:
    """Fängt die Mails ab, damit der Test an den Einladungs-Token kommt.

    Der steht bewusst nirgends sonst: nicht in der Antwort, nicht in der Liste
    der offenen Einladungen, und in der Datenbank nur als Hash.
    """

    def __init__(self) -> None:
        self.sent: list[tuple[str, str, str]] = []

    async def send(self, *, to: str, subject: str, body: str) -> None:
        self.sent.append((to, subject, body))


@pytest.fixture
def stack(
    migrated: None, monkeypatch: pytest.MonkeyPatch
) -> Iterator[tuple[Any, _CollectingMailer]]:
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation import compose_api

    mailer = _CollectingMailer()
    original = compose_api.build_app

    def _build_with_mailer(settings: Any) -> Any:
        app = original(settings)
        return app

    import identity_service.infrastructure.compose as infra

    original_compose = infra.compose_infrastructure

    def _compose(settings: Any, engine: Any) -> dict[str, Any]:
        deps = original_compose(settings, engine)
        deps["mailer"] = mailer
        return deps

    monkeypatch.setattr(compose_api, "compose_infrastructure", _compose)
    yield _build_with_mailer(IdentityServiceSettings()), mailer


async def _register_and_login(client: AsyncClient, email: str, name: str, url: str) -> UUID:
    register = await client.post(
        "/auth/register", json={"email": email, "password": PASSWORD, "display_name": name}
    )
    assert register.status_code == 201, register.text
    engine = create_engine(url.replace("postgresql+asyncpg://", "postgresql+psycopg://"))
    with engine.connect() as conn:
        conn.execute(text("UPDATE users SET status = 'active' WHERE email = :e"), {"e": email})
        conn.commit()
        user_id = conn.execute(
            text("SELECT id FROM users WHERE email = :e"), {"e": email}
        ).scalar_one()
    engine.dispose()
    login = await client.post("/auth/login", json={"email": email, "password": PASSWORD})
    assert login.status_code == 200, login.text
    return UUID(str(user_id))


def _token_from(body: str) -> str:
    match = re.search(r"[?&]token=([A-Za-z0-9_-]+)", body)
    assert match is not None, body
    return match.group(1)


async def _company_for(client: AsyncClient, name: str) -> UUID:
    created = await client.post("/companies", json={"name": name})
    assert created.status_code == 201, created.text
    return UUID(created.json()["id"])


async def test_an_invited_person_joins_and_a_stranger_cannot(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    admin_email = f"chef@{domain}"
    invited_email = f"neu@{domain}"
    stranger_email = f"fremd-{uuid4().hex[:8]}@woanders.example"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        await _register_and_login(admin, admin_email, "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        switched = await admin.post(f"/auth/company/{tenant_id}")
        assert switched.status_code == 200, switched.text

        invited = await admin.post(
            f"/companies/{tenant_id}/invitations",
            json={"email": invited_email, "role": "member"},
        )
        assert invited.status_code == 201, invited.text
        # Der Token steht NICHT in der Antwort.
        assert "token" not in invited.json()

        open_list = await admin.get(f"/companies/{tenant_id}/invitations")
        assert [entry["email"] for entry in open_list.json()] == [invited_email]
        assert all("token" not in entry for entry in open_list.json())

    token = _token_from(mailer.sent[-1][2])
    assert mailer.sent[-1][0] == invited_email

    # Ein Fremder mit demselben Link kommt nicht hinein. Tokens werden
    # weitergeleitet — wer den Link hat, ist nicht, wer eingeladen wurde.
    async with AsyncClient(transport=transport, base_url="http://test") as stranger:
        await _register_and_login(stranger, stranger_email, "Fremd", postgres_url)
        refused = await stranger.post("/invitations/accept", json={"token": token})
        assert refused.status_code == 400, refused.text
        mine = await stranger.get("/me/companies")
        assert mine.json() == []

    async with AsyncClient(transport=transport, base_url="http://test") as invitee:
        await _register_and_login(invitee, invited_email, "Neu", postgres_url)
        accepted = await invitee.post("/invitations/accept", json={"token": token})
        assert accepted.status_code == 200, accepted.text
        assert accepted.json()["role"] == "member"

        # Der Beitritt wechselt die Sitzung nicht — dafür gibt es den bewussten
        # Wechsel (ADR-0018).
        me = await invitee.get("/me")
        assert me.json()["tenant_id"] is None
        switched = await invitee.post(f"/auth/company/{tenant_id}")
        assert switched.status_code == 200, switched.text

        # Und zweimal annehmen geht nicht.
        again = await invitee.post("/invitations/accept", json={"token": token})
        assert again.status_code == 404, again.text


async def test_a_member_may_not_invite(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    admin_email = f"chef@{domain}"
    member_email = f"kollege@{domain}"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        await _register_and_login(admin, admin_email, "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")
        await admin.post(
            f"/companies/{tenant_id}/invitations", json={"email": member_email, "role": "member"}
        )
    token = _token_from(mailer.sent[-1][2])

    async with AsyncClient(transport=transport, base_url="http://test") as member:
        await _register_and_login(member, member_email, "Kollege", postgres_url)
        await member.post("/invitations/accept", json={"token": token})
        await member.post(f"/auth/company/{tenant_id}")

        refused = await member.post(
            f"/companies/{tenant_id}/invitations",
            json={"email": f"noch-einer@{domain}", "role": "member"},
        )

        assert refused.status_code == 403, refused.text
        # Aber sehen darf ein Mitglied die Mannschaft.
        members = await member.get(f"/companies/{tenant_id}/members")
        assert members.status_code == 200
        assert {entry["role"] for entry in members.json()} == {"admin", "member"}


async def test_an_outsider_cannot_tell_the_company_exists(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    app, _mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")

    async with AsyncClient(transport=transport, base_url="http://test") as outsider:
        await _register_and_login(
            outsider, f"aussen-{uuid4().hex[:8]}@woanders.example", "Außen", postgres_url
        )

        real = await outsider.get(f"/companies/{tenant_id}/members")
        invented = await outsider.get(f"/companies/{uuid4()}/members")

        assert real.status_code == invented.status_code == 404
        assert real.json()["detail"] == invented.json()["detail"]


async def test_inviting_the_same_address_twice_replaces_the_open_invitation(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    """Sonst hätte eine zurückgezogene Einladung einen noch gültigen Zwilling."""
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    invited = f"neu@{domain}"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")
        await admin.post(
            f"/companies/{tenant_id}/invitations", json={"email": invited, "role": "member"}
        )
        first_token = _token_from(mailer.sent[-1][2])
        await admin.post(
            f"/companies/{tenant_id}/invitations", json={"email": invited, "role": "admin"}
        )
        second_token = _token_from(mailer.sent[-1][2])

        open_list = await admin.get(f"/companies/{tenant_id}/invitations")
        assert len(open_list.json()) == 1
        assert open_list.json()[0]["role"] == "admin"

    assert first_token != second_token
    async with AsyncClient(transport=transport, base_url="http://test") as invitee:
        await _register_and_login(invitee, invited, "Neu", postgres_url)
        stale = await invitee.post("/invitations/accept", json={"token": first_token})
        assert stale.status_code == 404, stale.text
        fresh = await invitee.post("/invitations/accept", json={"token": second_token})
        assert fresh.status_code == 200, fresh.text
        assert fresh.json()["role"] == "admin"


async def test_a_withdrawn_invitation_cannot_be_used(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    invited = f"neu@{domain}"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")
        created = await admin.post(
            f"/companies/{tenant_id}/invitations", json={"email": invited, "role": "member"}
        )
        token = _token_from(mailer.sent[-1][2])
        removed = await admin.delete(f"/companies/{tenant_id}/invitations/{created.json()['id']}")
        assert removed.status_code == 204, removed.text

    async with AsyncClient(transport=transport, base_url="http://test") as invitee:
        await _register_and_login(invitee, invited, "Neu", postgres_url)
        refused = await invitee.post("/invitations/accept", json={"token": token})
        assert refused.status_code == 404, refused.text


async def test_a_member_can_be_removed_and_loses_the_company_on_refresh(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    """Entfernen wirkt beim nächsten Refresh, nicht erst beim Ablauf.

    Das ist die Regel aus PR #8, hier zum ersten Mal auf dem Weg, für den sie
    gebaut wurde: vorher konnte niemand entfernt werden.
    """
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    member_email = f"kollege@{domain}"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")
        await admin.post(
            f"/companies/{tenant_id}/invitations", json={"email": member_email, "role": "member"}
        )
        token = _token_from(mailer.sent[-1][2])

        async with AsyncClient(transport=transport, base_url="http://test") as member:
            member_id = await _register_and_login(member, member_email, "Kollege", postgres_url)
            await member.post("/invitations/accept", json={"token": token})
            await member.post(f"/auth/company/{tenant_id}")
            assert (await member.get("/me")).json()["tenant_id"] == str(tenant_id)

            removed = await admin.delete(f"/companies/{tenant_id}/members/{member_id}")
            assert removed.status_code == 204, removed.text

            refreshed = await member.post("/auth/refresh")
            assert refreshed.status_code == 200, refreshed.text
            me = await member.get("/me")
            # Die Sitzung überlebt, das Unternehmen nicht.
            assert me.status_code == 200
            assert me.json()["tenant_id"] is None
            assert (await member.get("/me/companies")).json() == []


async def test_the_last_admin_cannot_leave(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    """Ein Unternehmen ohne Administrator wäre nicht gelöscht, sondern verwaist."""
    app, _mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        admin_id = await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")

        refused = await admin.delete(f"/companies/{tenant_id}/members/{admin_id}")

        assert refused.status_code == 409, refused.text
        members = await admin.get(f"/companies/{tenant_id}/members")
        assert len(members.json()) == 1


async def test_a_member_may_not_remove_anyone(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    member_email = f"kollege@{domain}"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        admin_id = await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")
        await admin.post(
            f"/companies/{tenant_id}/invitations", json={"email": member_email, "role": "member"}
        )
        token = _token_from(mailer.sent[-1][2])

    async with AsyncClient(transport=transport, base_url="http://test") as member:
        await _register_and_login(member, member_email, "Kollege", postgres_url)
        await member.post("/invitations/accept", json={"token": token})
        await member.post(f"/auth/company/{tenant_id}")

        refused = await member.delete(f"/companies/{tenant_id}/members/{admin_id}")

        assert refused.status_code == 403, refused.text


async def test_an_admin_may_leave_once_someone_else_is_admin(
    stack: tuple[Any, _CollectingMailer], postgres_url: str
) -> None:
    """Die Regel schützt das Unternehmen, nicht den Posten."""
    app, mailer = stack
    transport = ASGITransport(app=app)
    domain = f"firma-{uuid4().hex[:8]}.example"
    successor_email = f"nachfolge@{domain}"

    async with AsyncClient(transport=transport, base_url="http://test") as admin:
        admin_id = await _register_and_login(admin, f"chef@{domain}", "Chefin", postgres_url)
        tenant_id = await _company_for(admin, "Firma")
        await admin.post(f"/auth/company/{tenant_id}")
        await admin.post(
            f"/companies/{tenant_id}/invitations",
            json={"email": successor_email, "role": "admin"},
        )
        token = _token_from(mailer.sent[-1][2])

        async with AsyncClient(transport=transport, base_url="http://test") as successor:
            await _register_and_login(successor, successor_email, "Nachfolge", postgres_url)
            await successor.post("/invitations/accept", json={"token": token})

        left = await admin.delete(f"/companies/{tenant_id}/members/{admin_id}")

        assert left.status_code == 204, left.text
