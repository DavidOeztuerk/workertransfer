"""Die Tür zum Transfermarkt, an echten Diensten belegt.

Der Consent-Ledger läuft als echte ASGI-App mit eigener Datenbank; der
Transfer-Service spricht ihn über seinen normalen HTTP-Client an, nur dass der
Transport in den Prozess statt ins Netz zeigt.

Der tragende Test ist `test_the_door_opens_the_market`: vorher kann ein
Unternehmen keinen Vorgang beginnen, nachher schon — und zwar ohne dass
irgendwo im Test von Hand eine Capability erteilt wurde. Vor diesem Slice war
genau das der einzige Weg, und damit war der Transfermarkt praktisch tot.
"""

from __future__ import annotations

from collections.abc import Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx
import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import create_engine, text
from worker_auth import TokenManager

_TRANSFER_DIR = Path(__file__).resolve().parents[2]
_CONSENT_DIR = _TRANSFER_DIR.parent / "consent-service"
_CONSENT_DB = "consent_for_market_request_test"
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"


class RecordingNotifier:
    """Merkt sich, wer benachrichtigt worden wäre — und kann scheitern.

    `fail` ist der Punkt: eine misslungene Benachrichtigung darf den Vorgang
    nicht kippen, der sie ausgelöst hat.
    """

    def __init__(self) -> None:
        self.sent: list[tuple[UUID, str]] = []
        self.fail = False

    async def notify(self, user_id: UUID, kind: str) -> None:
        if self.fail:
            raise RuntimeError("identity-service nicht erreichbar")
        self.sent.append((user_id, kind))


NOTIFIER = RecordingNotifier()

#: Eine Schleife fürs ganze Modul — asyncpg-Pools binden an die Schleife ihrer
#: Erzeugung.
pytestmark = pytest.mark.asyncio(loop_scope="module")


def _sync(url: str) -> str:
    return url.replace("postgresql+asyncpg://", "postgresql+psycopg://")


def _sibling(url: str, name: str) -> str:
    return url.rsplit("/", 1)[0] + "/" + name


def _migrate(service_dir: Path, url: str, patch: pytest.MonkeyPatch) -> None:
    cfg = Config()
    cfg.set_main_option("script_location", str(service_dir / "migrations"))
    patch.setenv("WORKER_DATABASE_URL", url)
    command.upgrade(cfg, "head")


def _drop_database(admin_url: str, name: str) -> None:
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        conn.execute(
            text(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
                "WHERE datname = :name AND pid <> pg_backend_pid()"
            ),
            {"name": name},
        )
        conn.execute(text(f'DROP DATABASE IF EXISTS "{name}"'))
    admin.dispose()


def _truncate_all(url: str) -> None:
    engine = create_engine(_sync(url), isolation_level="AUTOCOMMIT")
    with engine.connect() as conn:
        names = [
            row[0]
            for row in conn.execute(
                text(
                    "SELECT tablename FROM pg_tables WHERE schemaname = 'public' "
                    "AND tablename <> 'alembic_version'"
                )
            )
        ]
        if names:
            joined = ", ".join(f'"{name}"' for name in names)
            conn.execute(text(f"TRUNCATE {joined} RESTART IDENTITY CASCADE"))
    engine.dispose()


@pytest.fixture(scope="module")
def stack(postgres_url: str) -> Iterator[tuple[Any, Any]]:
    admin_url = _sync(postgres_url)
    consent_url = _sibling(postgres_url, _CONSENT_DB)
    _drop_database(admin_url, _CONSENT_DB)
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        conn.execute(text(f'CREATE DATABASE "{_CONSENT_DB}"'))
    admin.dispose()

    patch = pytest.MonkeyPatch()
    try:
        _migrate(_CONSENT_DIR, consent_url, patch)
        _migrate(_TRANSFER_DIR, postgres_url, patch)
        patch.setenv("WORKER_JWT_SECRET", SECRET)

        from consent_service.configuration import ConsentServiceSettings
        from consent_service.presentation.compose_api import build_app as build_consent

        patch.setenv("WORKER_DATABASE_URL", consent_url)
        consent_app = build_consent(ConsentServiceSettings())

        import transfer_service.infrastructure.compose as compose_module
        from transfer_service.infrastructure.consent import HttpConsentGate

        original = HttpConsentGate

        def _in_process_gate(*, base_url: str) -> HttpConsentGate:
            return original(base_url=base_url, transport=ASGITransport(app=consent_app))

        patch.setattr(compose_module, "HttpConsentGate", _in_process_gate)

        def _recording_notifier(*, base_url: str, secret: str) -> Any:
            _ = (base_url, secret)
            return NOTIFIER

        patch.setattr(compose_module, "HttpNotifier", _recording_notifier)

        from transfer_service.configuration import TransferServiceSettings
        from transfer_service.presentation.compose_api import build_app as build_transfer

        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        transfer_app = build_transfer(TransferServiceSettings())
        yield transfer_app, consent_app
    finally:
        patch.undo()
        _drop_database(admin_url, _CONSENT_DB)


@pytest.fixture
def apps(stack: tuple[Any, Any], postgres_url: str) -> tuple[Any, Any]:
    _truncate_all(postgres_url)
    _truncate_all(_sibling(postgres_url, _CONSENT_DB))
    NOTIFIER.sent.clear()
    NOTIFIER.fail = False
    return stack


def _token(user_id: UUID, *, tenant_id: UUID | None) -> str:
    return TokenManager(secret=SECRET).create_access_token(user_id, tenant_id, ["user"], [])


async def _call(app: Any, method: str, path: str, token: str, **kwargs: Any) -> httpx.Response:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.request(
            method, path, headers={"Authorization": f"Bearer {token}"}, **kwargs
        )


async def _publish_profile(consent_app: Any, token: str, subject: UUID) -> None:
    """Die Voraussetzung der Anfrage: das Profil ist öffentlich.

    Nicht die Existenz eines Marktstatus — die zu prüfen wäre ein Orakel.
    """
    response = await _call(
        consent_app,
        "POST",
        "/consent/grant",
        token,
        json={"subject_id": str(subject), "capability": "profile.visibility:public"},
    )
    assert response.status_code == 200, response.text


async def _prepare(apps: tuple[Any, Any], *, availability: str = "listening"):
    transfer_app, consent_app = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    tenant = uuid4()
    company_token = _token(uuid4(), tenant_id=tenant)
    await _call(
        transfer_app,
        "PUT",
        "/market/me",
        person_token,
        json={"availability": availability, "employed": False, "note": ""},
    )
    await _publish_profile(consent_app, person_token, person)
    return transfer_app, person, person_token, tenant, company_token


async def test_the_door_opens_the_market(apps: tuple[Any, Any]) -> None:
    """Vorher kein Vorgang möglich, nachher schon — ohne Freigabe von Hand.

    Der eigentliche Beleg dieses Slices. Nirgends in diesem Test wird
    `market.visibility:tenant:<id>` direkt erteilt; die Freigabe entsteht
    ausschließlich dadurch, dass die Person die Anfrage beantwortet.
    """
    app, person, person_token, _tenant, company_token = await _prepare(apps)

    blocked = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    assert blocked.status_code == 404, blocked.text

    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    assert asked.status_code == 201, asked.text
    request_id = asked.json()["id"]
    # Das fragende Unternehmen erfährt nichts über die Person — auch nicht
    # darüber, ob die Freigabe gilt.
    assert asked.json()["active"] is None
    assert asked.json()["status"] == "PENDING"

    # Die Anfrage allein öffnet nichts.
    still_blocked = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    assert still_blocked.status_code == 404

    granted = await _call(app, "POST", f"/market/requests/{request_id}/grant", person_token)
    assert granted.status_code == 200, granted.text

    seen = await _call(app, "GET", f"/market/{person}", company_token)
    assert seen.status_code == 200
    assert seen.json()["is_approachable"] is True

    started = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    assert started.status_code == 201, started.text


async def test_declining_leaves_the_market_shut(apps: tuple[Any, Any]) -> None:
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    request_id = asked.json()["id"]

    declined = await _call(app, "POST", f"/market/requests/{request_id}/decline", person_token)
    assert declined.status_code == 200
    assert declined.json()["status"] == "DECLINED"
    assert declined.json()["active"] is False

    blocked = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    assert blocked.status_code == 404


async def test_asking_twice_is_refused_even_after_a_withdrawal(apps: tuple[Any, Any]) -> None:
    """Wer dreimal fragen darf, hat kein Nein bekommen, sondern eine Verzögerung."""
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    request_id = asked.json()["id"]
    await _call(app, "POST", f"/market/requests/{request_id}/grant", person_token)
    await _call(app, "POST", f"/market/requests/{request_id}/revoke", person_token)

    again = await _call(app, "POST", f"/market/{person}/requests", company_token)
    assert again.status_code == 409


async def test_a_withdrawal_shuts_the_market_on_the_next_read(apps: tuple[Any, Any]) -> None:
    """ADR-0013 ohne Cache: der Widerruf wirkt beim nächsten Zugriff, nicht beim übernächsten."""
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    request_id = asked.json()["id"]
    await _call(app, "POST", f"/market/requests/{request_id}/grant", person_token)
    assert (await _call(app, "GET", f"/market/{person}", company_token)).status_code == 200

    revoked = await _call(app, "POST", f"/market/requests/{request_id}/revoke", person_token)
    assert revoked.status_code == 200
    # Der Vorgang bleibt GRANTED — er hält fest, was geschehen ist.
    assert revoked.json()["status"] == "GRANTED"
    assert revoked.json()["active"] is False

    assert (await _call(app, "GET", f"/market/{person}", company_token)).status_code == 404


async def test_a_running_transfer_survives_the_withdrawal(apps: tuple[Any, Any]) -> None:
    """Der Vorgang hat seine eigene Tür — und seine eigene Absage.

    Ihn mit dem Widerruf zu beenden wäre bequem und falsch: ein laufendes
    Gespräch abzubrechen ist eine Entscheidung, die die Person treffen soll,
    nicht eine Nebenwirkung, die sie an anderer Stelle auslöst.
    """
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    request_id = asked.json()["id"]
    await _call(app, "POST", f"/market/requests/{request_id}/grant", person_token)
    started = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    transfer_id = started.json()["id"]

    await _call(app, "POST", f"/market/requests/{request_id}/revoke", person_token)

    mine = await _call(app, "GET", "/transfers/me", person_token)
    assert [t["id"] for t in mine.json()] == [transfer_id]
    # Und sie kann ihn beenden, wenn sie will.
    declined = await _call(app, "POST", f"/transfers/{transfer_id}/decline", person_token)
    assert declined.status_code == 200


async def test_asking_needs_a_public_profile(apps: tuple[Any, Any]) -> None:
    """Sonst wäre die Anfrage ein Kanal, um die Existenz einer Person zu erfahren."""
    transfer_app, _consent_app = apps
    unknown = uuid4()
    company_token = _token(uuid4(), tenant_id=uuid4())
    asked = await _call(transfer_app, "POST", f"/market/{unknown}/requests", company_token)
    assert asked.status_code == 404


async def test_a_person_without_a_company_cannot_ask(apps: tuple[Any, Any]) -> None:
    """403 ist eine Aussage über den Aufrufer, nicht über das Ziel."""
    app, person, _person_token, _tenant, _company_token = await _prepare(apps)
    lone = _token(uuid4(), tenant_id=None)
    asked = await _call(app, "POST", f"/market/{person}/requests", lone)
    assert asked.status_code == 403


async def test_only_the_person_asked_may_answer(apps: tuple[Any, Any]) -> None:
    app, person, _person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    request_id = asked.json()["id"]

    stranger = _token(uuid4(), tenant_id=None)
    answered = await _call(app, "POST", f"/market/requests/{request_id}/grant", stranger)
    # Nicht 403: „nicht vorhanden" und „nicht meins" sind von außen dasselbe.
    assert answered.status_code == 404


async def test_both_sides_see_the_request_but_only_one_sees_what_holds(
    apps: tuple[Any, Any],
) -> None:
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    request_id = asked.json()["id"]
    await _call(app, "POST", f"/market/requests/{request_id}/grant", person_token)

    mine = await _call(app, "GET", "/market/me/requests", person_token)
    assert mine.status_code == 200
    assert [(r["status"], r["active"]) for r in mine.json()] == [("GRANTED", True)]

    theirs = await _call(app, "GET", "/market/requests", company_token)
    assert theirs.status_code == 200
    assert [(r["status"], r["active"]) for r in theirs.json()] == [("GRANTED", None)]


async def test_a_declined_request_stays_in_the_company_list(apps: tuple[Any, Any]) -> None:
    """Sonst sähen „abgelehnt" und „nie gefragt" gleich aus."""
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    await _call(app, "POST", f"/market/requests/{asked.json()['id']}/decline", person_token)

    theirs = await _call(app, "GET", "/market/requests", company_token)
    assert [r["status"] for r in theirs.json()] == ["DECLINED"]


async def test_unavailable_means_no_even_with_a_granted_request(apps: tuple[Any, Any]) -> None:
    """Die Freigabe erlaubt zu sehen, nicht zu stören."""
    app, person, person_token, _tenant, company_token = await _prepare(
        apps, availability="unavailable"
    )
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    await _call(app, "POST", f"/market/requests/{asked.json()['id']}/grant", person_token)

    seen = await _call(app, "GET", f"/market/{person}", company_token)
    assert seen.status_code == 200
    assert seen.json()["is_approachable"] is False

    blocked = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    assert blocked.status_code == 404


async def test_the_person_is_told_that_someone_asked(apps: tuple[Any, Any]) -> None:
    """Ohne diesen Ruf erfährt sie es nur, wenn sie zufällig vorbeischaut."""
    app, person, _person_token, _tenant, company_token = await _prepare(apps)

    await _call(app, "POST", f"/market/{person}/requests", company_token)

    assert NOTIFIER.sent == [(person, "market_request")]


async def test_a_broken_notifier_does_not_break_the_request(apps: tuple[Any, Any]) -> None:
    """Die genaue Umkehrung der Consent-Regel, und aus demselben Grund richtig.

    Beim Ledger geht es um Erlaubnis — im Zweifel nein. Hier geht es um
    Höflichkeit, und einen Vorgang scheitern zu lassen, weil eine Mail nicht
    rausging, wäre grotesk.
    """
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    NOTIFIER.fail = True

    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)

    assert asked.status_code == 201, asked.text
    # Und der Vorgang steht wirklich in der Datenbank, nicht nur in der Antwort.
    mine = await _call(app, "GET", "/market/me/requests", person_token)
    assert [r["status"] for r in mine.json()] == ["PENDING"]


async def test_the_company_is_never_mailed(apps: tuple[Any, Any]) -> None:
    """Eine Mail an einen Firmenverteiler mit dem Namen eines Menschen wäre
    derselbe Leck-Kanal, nur andersherum."""
    app, person, person_token, _tenant, company_token = await _prepare(apps)
    asked = await _call(app, "POST", f"/market/{person}/requests", company_token)
    NOTIFIER.sent.clear()

    await _call(app, "POST", f"/market/requests/{asked.json()['id']}/grant", person_token)
    started = await _call(
        app, "POST", "/transfers", company_token, json={"subject_id": str(person), "message": "Hi"}
    )
    await _call(app, "POST", f"/transfers/{started.json()['id']}/accept-talk", person_token)

    # Nur der Zug des Unternehmens (der Transfer) erreicht jemanden — und zwar
    # die Person. Ihre eigenen Züge lösen nichts aus.
    assert NOTIFIER.sent == [(person, "transfer_update")]
