"""Das Versprechen dieses Slices, an echten Diensten belegt.

Der Consent-Ledger läuft hier als echte ASGI-App mit eigener Datenbank; der
Transfer-Service spricht ihn über seinen normalen HTTP-Client an, nur dass der
Transport in den Prozess statt ins Netz zeigt. Alles andere ist Produktion:
dieselben Handler, dieselben Migrationen, dieselben Tokens.

Der tragende Test ist `test_revocation_takes_effect_on_the_very_next_read` —
ADR-0013 hat einen Cache ausdrücklich verworfen, weil ein Widerruf sofort
wirken muss. Hier wird das nachgewiesen statt behauptet.
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
_CONSENT_DB = "consent_for_transfer_test"
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"


#: Eine Schleife fürs ganze Modul. Die Apps werden einmal gebaut und halten
#: asyncpg-Pools, die an die Schleife ihrer Erzeugung gebunden sind; mit der
#: Voreinstellung (eine Schleife je Test) bricht ab dem zweiten Test jede
#: Abfrage mit "attached to a different loop".
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
        # Die App-Pools geben ihre Verbindungen nicht her — build_app reicht die
        # Engine nicht heraus. Ohne dieses Kappen scheitert DROP DATABASE.
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
    """Beide Dienste einmal je Modul aufbauen.

    Je Test neu wäre leichter zu lesen, geht aber nicht: die Apps halten
    Verbindungspools offen, die `build_app` nicht herausgibt, und ein
    DROP DATABASE gegen eine belegte Datenbank scheitert. Statt Container zu
    wechseln, räumt `apps` die Tabellen zwischen den Tests aus — dieselbe
    Isolation, ein Bruchteil der Zeit.
    """
    admin_url = _sync(postgres_url)
    consent_url = _sibling(postgres_url, _CONSENT_DB)
    _drop_database(admin_url, _CONSENT_DB)
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        # Zwei Datenbanken in einem Container — je Service eine (ADR-0004).
        # Kein zweiter Container: die Trennung, um die es geht, ist die der
        # Daten, und die ist damit vollständig.
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

        # Der Transfer-Service benutzt seinen echten HTTP-Client; nur der
        # Transport zeigt in den Prozess statt ins Netz. Damit ist alles auf dem
        # Weg dahin — DTO, Header, Statuscodes, Fail-closed — wirklich geprüft
        # und nicht durch ein Fake ersetzt.
        import transfer_service.infrastructure.compose as compose_module
        from transfer_service.infrastructure.consent import HttpConsentGate

        original = HttpConsentGate

        def _in_process_gate(*, base_url: str) -> HttpConsentGate:
            return original(base_url=base_url, transport=ASGITransport(app=consent_app))

        patch.setattr(compose_module, "HttpConsentGate", _in_process_gate)

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
    """Jeder Test beginnt bei null — in beiden Datenbanken."""
    _truncate_all(postgres_url)
    _truncate_all(_sibling(postgres_url, _CONSENT_DB))
    return stack


def _token(user_id: UUID, *, tenant_id: UUID | None) -> str:
    return TokenManager(secret=SECRET).create_access_token(user_id, tenant_id, ["user"], [])


def _capability(tenant_id: UUID) -> str:
    return f"market.visibility:tenant:{tenant_id}"


async def _call(app: Any, method: str, path: str, token: str, **kwargs: Any) -> httpx.Response:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.request(
            method, path, headers={"Authorization": f"Bearer {token}"}, **kwargs
        )


async def _set_status(app: Any, token: str, availability: str, **extra: Any) -> httpx.Response:
    return await _call(
        app,
        "PUT",
        "/market/me",
        token,
        json={"availability": availability, "employed": True, "note": "", **extra},
    )


async def _release(
    consent_app: Any, token: str, subject: UUID, tenant: UUID, *, grant: bool
) -> None:
    path = "/consent/grant" if grant else "/consent/revoke"
    body: dict[str, Any] = {"subject_id": str(subject), "capability": _capability(tenant)}
    if not grant:
        body["reason"] = "moechte nicht mehr angesprochen werden"
    response = await _call(consent_app, "POST", path, token, json=body)
    assert response.status_code == 200, response.text


async def _interest(app: Any, token: str, subject: UUID, **extra: Any) -> httpx.Response:
    return await _call(
        app,
        "POST",
        "/transfers",
        token,
        json={"subject_id": str(subject), "message": "Hallo", **extra},
    )


async def _prepare(apps: tuple[Any, Any], *, employed: bool, availability: str = "listening"):
    """Person mit Marktstatus und Freigabe fuer ein Unternehmen."""
    transfer_app, consent_app = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    tenant = uuid4()
    company_token = _token(uuid4(), tenant_id=tenant)
    await _set_status(transfer_app, person_token, availability, employed=employed)
    await _release(consent_app, person_token, person, tenant, grant=True)
    return transfer_app, person, person_token, tenant, company_token


async def test_the_full_way_when_nobody_needs_a_release(apps: tuple[Any, Any]) -> None:
    """Der Happy-Path aus der Definition of Done."""
    app, person, person_token, _tenant, company_token = await _prepare(apps, employed=False)

    started = await _interest(app, company_token, person)
    assert started.status_code == 201, started.text
    tid = started.json()["id"]
    assert started.json()["requires_release"] is False

    assert (
        await _call(app, "POST", f"/transfers/{tid}/accept-talk", person_token)
    ).status_code == 200
    offered = await _call(
        app,
        "POST",
        f"/transfers/{tid}/offer",
        company_token,
        json={"note": "Guter Vertrag.", "start_on": "2026-11", "fee_cents": 0},
    )
    assert offered.status_code == 200, offered.text
    assert (
        await _call(app, "POST", f"/transfers/{tid}/accept-offer", person_token)
    ).status_code == 200

    done = await _call(app, "POST", f"/transfers/{tid}/complete", company_token)
    assert done.status_code == 200, done.text
    assert done.json()["status"] == "completed"


async def test_when_a_release_is_needed_the_person_closes_it(apps: tuple[Any, Any]) -> None:
    """Die Plattform kontaktiert den aktuellen Arbeitgeber NICHT.

    Sie weiss nicht, wer er ist, und soll es nicht wissen: ein Datensatz
    „arbeitet bei X" neben „hoert zu" waere genau die Auskunft, die jemanden den
    Arbeitsplatz kostet. Also bestaetigt die Person selbst.
    """
    app, person, person_token, _tenant, company_token = await _prepare(apps, employed=True)
    tid = (await _interest(app, company_token, person)).json()["id"]
    await _call(app, "POST", f"/transfers/{tid}/accept-talk", person_token)
    await _call(app, "POST", f"/transfers/{tid}/offer", company_token, json={"note": ""})
    await _call(app, "POST", f"/transfers/{tid}/accept-offer", person_token)

    # Das Unternehmen kann nicht abschliessen, solange die Freigabe fehlt.
    too_early = await _call(app, "POST", f"/transfers/{tid}/complete", company_token)
    assert too_early.status_code == 409, too_early.text

    confirmed = await _call(app, "POST", f"/transfers/{tid}/confirm-release", person_token)
    assert confirmed.status_code == 200, confirmed.text
    assert confirmed.json()["status"] == "completed"
    assert confirmed.json()["release_confirmed"] is True


async def test_an_unavailable_person_cannot_be_approached(apps: tuple[Any, Any]) -> None:
    """Die Freigabe erlaubt zu sehen, nicht zu stoeren."""
    app, person, _person_token, _tenant, company_token = await _prepare(
        apps, employed=False, availability="unavailable"
    )

    response = await _interest(app, company_token, person)

    assert response.status_code == 404, response.text


async def test_without_a_release_of_the_market_status_nobody_may_start(
    apps: tuple[Any, Any],
) -> None:
    transfer_app, _consent = apps
    person = uuid4()
    await _set_status(transfer_app, _token(person, tenant_id=None), "open")
    stranger = _token(uuid4(), tenant_id=uuid4())

    withheld = await _interest(transfer_app, stranger, person)
    never_existed = await _interest(transfer_app, stranger, uuid4())

    # Sonst waere der Endpunkt ein Orakel darueber, wer zuhoert.
    assert withheld.status_code == never_existed.status_code == 404
    assert withheld.json()["detail"] == never_existed.json()["detail"]


async def test_the_person_may_decline_at_any_point(apps: tuple[Any, Any]) -> None:
    app, person, person_token, _tenant, company_token = await _prepare(apps, employed=False)
    tid = (await _interest(app, company_token, person)).json()["id"]
    await _call(app, "POST", f"/transfers/{tid}/accept-talk", person_token)
    await _call(app, "POST", f"/transfers/{tid}/offer", company_token, json={"note": ""})

    declined = await _call(app, "POST", f"/transfers/{tid}/decline", person_token)

    assert declined.status_code == 200, declined.text
    assert declined.json()["status"] == "declined"
    # Und danach geht nichts mehr.
    assert (
        await _call(app, "POST", f"/transfers/{tid}/accept-offer", person_token)
    ).status_code == 409


async def test_only_one_running_transfer_per_pair(apps: tuple[Any, Any]) -> None:
    """Ein zweiter waere Nachfassen an der Ablehnung vorbei."""
    app, person, person_token, _tenant, company_token = await _prepare(apps, employed=False)
    await _interest(app, company_token, person)

    again = await _interest(app, company_token, person)
    assert again.status_code == 409, again.text

    # Nach einem endgueltigen Ausgang aber schon: das ist eine neue Entscheidung.
    tid = (await _call(app, "GET", "/transfers", company_token)).json()[0]["id"]
    await _call(app, "POST", f"/transfers/{tid}/withdraw", company_token)
    assert (await _interest(app, company_token, person)).status_code == 201
    assert person_token is not None


async def test_a_stranger_cannot_move_a_foreign_transfer(apps: tuple[Any, Any]) -> None:
    app, person, _person_token, _tenant, company_token = await _prepare(apps, employed=False)
    tid = (await _interest(app, company_token, person)).json()["id"]

    stranger = _token(uuid4(), tenant_id=None)
    theirs = await _call(app, "POST", f"/transfers/{tid}/accept-talk", stranger)
    invented = await _call(app, "POST", f"/transfers/{uuid4()}/accept-talk", stranger)

    assert theirs.status_code == invented.status_code == 404
    assert theirs.json()["detail"] == invented.json()["detail"]


async def test_an_offer_needs_a_conversation_first(apps: tuple[Any, Any]) -> None:
    """Ein Angebot an jemanden, der noch nicht zugestimmt hat zu reden, waere
    genau die Belaestigung, gegen die der ganze Fluss gebaut ist."""
    app, person, _person_token, _tenant, company_token = await _prepare(apps, employed=False)
    tid = (await _interest(app, company_token, person)).json()["id"]

    too_early = await _call(
        app, "POST", f"/transfers/{tid}/offer", company_token, json={"note": ""}
    )

    assert too_early.status_code == 409, too_early.text
