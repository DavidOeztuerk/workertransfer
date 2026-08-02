"""`GET /consent/me` — die Seite, auf der eine Person sieht, was sie erteilt hat.

Der tragende Test ist `test_the_list_and_the_check_never_disagree`: die Liste
darf keine zweite Auslegung dessen sein, was „gilt" heißt. Der Tag, an dem sie
etwas anderes behauptet als `/check`, ist der Tag, an dem niemand mehr weiß, was
gilt.
"""

from __future__ import annotations

import os
from pathlib import Path
from uuid import uuid4

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from worker_auth import TokenManager

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]
SECRET = "integration-secret-with-at-least-thirty-two-bytes"


@pytest.fixture(scope="module")
def migrated_schema(postgres_url: str) -> None:
    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    command.upgrade(cfg, "head")


def _client(postgres_url: str) -> tuple[AsyncClient, str, dict[str, str]]:
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    os.environ["WORKER_JWT_SECRET"] = SECRET

    from consent_service.configuration import ConsentServiceSettings
    from consent_service.presentation.compose_api import build_app

    app = build_app(ConsentServiceSettings())
    subject = uuid4()
    token = TokenManager(SECRET).create_access_token(subject, uuid4(), ["user"], [])
    client = AsyncClient(transport=ASGITransport(app=app), base_url="http://test")
    return client, str(subject), {"Authorization": f"Bearer {token}"}


async def test_it_lists_what_holds_and_nothing_else(
    postgres_url: str, migrated_schema: None
) -> None:
    client, subject, auth = _client(postgres_url)
    tenant = uuid4()
    async with client:
        for capability in (
            "profile.visibility:public",
            f"resume.visibility:tenant:{tenant}",
            "portfolio.visibility:public",
        ):
            await client.post(
                "/consent/grant",
                json={"subject_id": subject, "capability": capability},
                headers=auth,
            )
        await client.post(
            "/consent/revoke",
            json={
                "subject_id": subject,
                "capability": "portfolio.visibility:public",
                "reason": "moechte es nicht mehr zeigen",
            },
            headers=auth,
        )

        listed = await client.get("/consent/me", headers=auth)

    assert listed.status_code == 200, listed.text
    capabilities = [entry["capability"] for entry in listed.json()]
    assert capabilities == sorted(
        ["profile.visibility:public", f"resume.visibility:tenant:{tenant}"]
    )


async def test_the_list_carries_no_withdrawal_reason(
    postgres_url: str, migrated_schema: None
) -> None:
    """Er ist Freitext, den ein Mensch über sich selbst geschrieben hat.

    Hier kommt er ohnehin nicht vor — widerrufene Einträge fehlen. Der Test
    hält fest, dass auch kein Feld dafür existiert, in das ihn später jemand
    hineinschreibt.
    """
    client, subject, auth = _client(postgres_url)
    async with client:
        await client.post(
            "/consent/grant",
            json={"subject_id": subject, "capability": "profile.visibility:public"},
            headers=auth,
        )
        listed = await client.get("/consent/me", headers=auth)

    assert set(listed.json()[0]) == {"capability", "granted_at"}


async def test_a_re_grant_shows_the_second_time_not_the_first(
    postgres_url: str, migrated_schema: None
) -> None:
    """`granted_at` ist der Zeitpunkt der WIRKSAMEN Erteilung."""
    client, subject, auth = _client(postgres_url)
    body = {"subject_id": subject, "capability": "profile.visibility:public"}
    async with client:
        await client.post("/consent/grant", json=body, headers=auth)
        first = (await client.get("/consent/me", headers=auth)).json()[0]["granted_at"]
        await client.post(
            "/consent/revoke", json={**body, "reason": "kurz ueberlegt"}, headers=auth
        )
        assert (await client.get("/consent/me", headers=auth)).json() == []
        await client.post("/consent/grant", json=body, headers=auth)
        second = (await client.get("/consent/me", headers=auth)).json()[0]["granted_at"]

    assert second >= first


async def test_a_deleted_capability_is_not_listed(postgres_url: str, migrated_schema: None) -> None:
    client, subject, auth = _client(postgres_url)
    body = {"subject_id": subject, "capability": "profile.visibility:public"}
    async with client:
        await client.post("/consent/grant", json=body, headers=auth)
        await client.post(
            "/consent/delete", json={**body, "reason": "loeschung verlangt"}, headers=auth
        )
        listed = await client.get("/consent/me", headers=auth)

    assert listed.json() == []


async def test_the_list_and_the_check_never_disagree(
    postgres_url: str, migrated_schema: None
) -> None:
    """Zwei Wege an dieselbe Auskunft, die sich uneinig werden können, sind
    schlimmer als kein zweiter Weg."""
    client, subject, auth = _client(postgres_url)
    tenant = uuid4()
    capabilities = [
        "profile.visibility:public",
        "portfolio.visibility:public",
        f"resume.visibility:tenant:{tenant}",
        f"market.visibility:tenant:{tenant}",
    ]
    async with client:
        for capability in capabilities:
            await client.post(
                "/consent/grant",
                json={"subject_id": subject, "capability": capability},
                headers=auth,
            )
        # Zwei davon wieder zurücknehmen, auf beiden Wegen.
        await client.post(
            "/consent/revoke",
            json={
                "subject_id": subject,
                "capability": "portfolio.visibility:public",
                "reason": "nicht mehr",
            },
            headers=auth,
        )
        await client.post(
            "/consent/delete",
            json={
                "subject_id": subject,
                "capability": f"market.visibility:tenant:{tenant}",
                "reason": "loeschung",
            },
            headers=auth,
        )

        listed = {
            entry["capability"] for entry in (await client.get("/consent/me", headers=auth)).json()
        }
        checked = set()
        for capability in capabilities:
            answer = await client.post(
                "/consent/check",
                json={"subject_id": subject, "capability": capability},
                headers=auth,
            )
            if answer.json()["granted"] and not answer.json().get("deleted", False):
                checked.add(capability)

    assert listed == checked


async def test_nobody_can_ask_for_someone_elses_list(
    postgres_url: str, migrated_schema: None
) -> None:
    """Es gibt keinen Parameter dafür — und auch kein Schlupfloch.

    Eine fremde Liste enthielte, welche ANDEREN Unternehmen Zugriff haben.
    """
    client, subject, auth = _client(postgres_url)
    other_client, other_subject, other_auth = _client(postgres_url)
    async with client:
        await client.post(
            "/consent/grant",
            json={"subject_id": subject, "capability": "profile.visibility:public"},
            headers=auth,
        )
    async with other_client:
        # Der Versuch, es doch anzugeben — in Pfad und Abfrage.
        by_query = await other_client.get(f"/consent/me?subject_id={subject}", headers=other_auth)
        by_path = await other_client.get(f"/consent/me/{subject}", headers=other_auth)

    assert by_query.json() == [], "ein Query-Parameter darf nichts ausrichten"
    assert by_path.status_code == 404, "es gibt keinen Pfad für fremde Listen"
    assert other_subject != subject


async def test_without_a_token_there_is_no_list(postgres_url: str, migrated_schema: None) -> None:
    client, _subject, _auth = _client(postgres_url)
    async with client:
        response = await client.get("/consent/me")

    assert response.status_code == 401


async def test_the_history_keeps_what_the_list_drops(
    postgres_url: str, migrated_schema: None
) -> None:
    """`/consent/me` sagt, was gilt. Die Geschichte sagt, was war.

    Beides ist richtig — an verschiedenen Orten. Eine Übersichtsseite, die
    zeigt, wer EINMAL gefragt hat, verspricht mehr, als sie soll; eine
    Auskunft, die es verschweigt, verspricht weniger.
    """
    client, subject, auth = _client(postgres_url)
    body = {"subject_id": subject, "capability": "profile.visibility:public"}
    async with client:
        await client.post("/consent/grant", json=body, headers=auth)
        await client.post("/consent/revoke", json={**body, "reason": "doch nicht"}, headers=auth)
        listed = (await client.get("/consent/me", headers=auth)).json()
        history = (await client.get("/consent/me/history", headers=auth)).json()

    assert listed == []
    assert [entry["action"] for entry in history] == ["GRANT", "REVOKE"]


async def test_the_history_carries_the_reason_the_others_withhold(
    postgres_url: str, migrated_schema: None
) -> None:
    """Freitext, den die Person über sich selbst geschrieben hat.

    Ihr gegenüber gibt es keinen Grund, ihn zurückzuhalten — `/check` und
    `/consent/me` kennen ihn trotzdem nicht. Das ist der Unterschied zwischen
    „gehört ihr" und „geht andere an".
    """
    client, subject, auth = _client(postgres_url)
    body = {"subject_id": subject, "capability": "profile.visibility:public"}
    async with client:
        await client.post("/consent/grant", json=body, headers=auth)
        await client.post(
            "/consent/revoke",
            json={**body, "reason": "moechte nicht mehr gefunden werden"},
            headers=auth,
        )
        history = (await client.get("/consent/me/history", headers=auth)).json()
        checked = await client.post("/consent/check", json=body, headers=auth)

    assert history[-1]["reason"] == "moechte nicht mehr gefunden werden"
    assert "reason" not in checked.json()


async def test_the_history_is_oldest_first(postgres_url: str, migrated_schema: None) -> None:
    """Eine Geschichte liest man von vorn."""
    client, subject, auth = _client(postgres_url)
    body = {"subject_id": subject, "capability": "profile.visibility:public"}
    async with client:
        await client.post("/consent/grant", json=body, headers=auth)
        await client.post("/consent/revoke", json={**body, "reason": "eins"}, headers=auth)
        await client.post("/consent/grant", json=body, headers=auth)
        history = (await client.get("/consent/me/history", headers=auth)).json()

    stamps = [entry["recorded_at"] for entry in history]
    assert stamps == sorted(stamps)
    assert [entry["action"] for entry in history] == ["GRANT", "REVOKE", "GRANT"]


async def test_nobody_gets_someone_elses_history(postgres_url: str, migrated_schema: None) -> None:
    client, subject, auth = _client(postgres_url)
    other_client, _other, other_auth = _client(postgres_url)
    async with client:
        await client.post(
            "/consent/grant",
            json={"subject_id": subject, "capability": "profile.visibility:public"},
            headers=auth,
        )
    async with other_client:
        by_query = await other_client.get(
            f"/consent/me/history?subject_id={subject}", headers=other_auth
        )

    assert by_query.json() == []


async def test_without_a_token_there_is_no_history(
    postgres_url: str, migrated_schema: None
) -> None:
    client, _subject, _auth = _client(postgres_url)
    async with client:
        response = await client.get("/consent/me/history")

    assert response.status_code == 401
