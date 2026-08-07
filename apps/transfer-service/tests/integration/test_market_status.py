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


async def test_saying_nothing_means_unavailable(apps: tuple[Any, Any]) -> None:
    """Die Voreinstellung darf nie zugunsten des Marktes ausfallen."""
    transfer_app, _consent = apps
    token = _token(uuid4(), tenant_id=None)

    response = await _call(transfer_app, "GET", "/market/me", token)

    assert response.status_code == 200, response.text
    # Nie null: „nichts gesagt" IST ein Zustand.
    assert response.json()["availability"] == "unavailable"
    assert response.json()["is_approachable"] is False


async def test_revocation_takes_effect_on_the_very_next_read(apps: tuple[Any, Any]) -> None:
    """Kein Cache, keine Verzoegerung — hier zaehlt es am meisten."""
    transfer_app, consent_app = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    tenant = uuid4()
    company_token = _token(uuid4(), tenant_id=tenant)

    assert (await _set_status(transfer_app, person_token, "listening")).status_code == 200
    await _release(consent_app, person_token, person, tenant, grant=True)

    visible = await _call(transfer_app, "GET", f"/market/{person}", company_token)
    assert visible.status_code == 200, visible.text
    assert visible.json()["availability"] == "listening"
    assert visible.json()["is_approachable"] is True

    await _release(consent_app, person_token, person, tenant, grant=False)

    gone = await _call(transfer_app, "GET", f"/market/{person}", company_token)
    assert gone.status_code == 404, gone.text


async def test_a_release_reaches_exactly_one_company(apps: tuple[Any, Any]) -> None:
    """Es gibt kein `market.visibility:public` — die Freigabe nennt immer einen.

    „Diese Person hoert zu" ist die gefaehrlichste Angabe im System; ein
    Schalter „fuer alle Unternehmen" waere einer, dessen Folgen niemand
    ueberblickt — darunter der eigene Arbeitgeber.
    """
    transfer_app, consent_app = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    first, second = uuid4(), uuid4()
    await _set_status(transfer_app, person_token, "open")
    await _release(consent_app, person_token, person, first, grant=True)

    assert (
        await _call(transfer_app, "GET", f"/market/{person}", _token(uuid4(), tenant_id=first))
    ).status_code == 200
    assert (
        await _call(transfer_app, "GET", f"/market/{person}", _token(uuid4(), tenant_id=second))
    ).status_code == 404


async def test_withheld_and_absent_are_indistinguishable(apps: tuple[Any, Any]) -> None:
    transfer_app, _consent = apps
    person = uuid4()
    await _set_status(transfer_app, _token(person, tenant_id=None), "open")
    company_token = _token(uuid4(), tenant_id=uuid4())

    withheld = await _call(transfer_app, "GET", f"/market/{person}", company_token)
    never_existed = await _call(transfer_app, "GET", f"/market/{uuid4()}", company_token)

    assert withheld.status_code == never_existed.status_code == 404

    def _without_correlation(body: dict[str, Any]) -> dict[str, Any]:
        return {key: value for key, value in body.items() if key != "correlationId"}

    assert _without_correlation(withheld.json()) == _without_correlation(never_existed.json())


async def test_unavailable_is_an_answer_not_a_hidden_state(apps: tuple[Any, Any]) -> None:
    """Wer freigegeben hat und dann `unavailable` wird, bleibt sichtbar.

    Das Unternehmen sieht „gerade nicht" — und das ist die richtige Auskunft,
    nicht 404. Wer auch das nicht will, widerruft die Freigabe.
    """
    transfer_app, consent_app = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    tenant = uuid4()
    company_token = _token(uuid4(), tenant_id=tenant)
    await _set_status(transfer_app, person_token, "open")
    await _release(consent_app, person_token, person, tenant, grant=True)

    await _set_status(transfer_app, person_token, "unavailable")

    response = await _call(transfer_app, "GET", f"/market/{person}", company_token)
    assert response.status_code == 200
    assert response.json()["is_approachable"] is False


async def test_a_private_person_may_not_read_a_foreign_status(apps: tuple[Any, Any]) -> None:
    transfer_app, consent_app = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    tenant = uuid4()
    await _set_status(transfer_app, person_token, "open")
    await _release(consent_app, person_token, person, tenant, grant=True)

    response = await _call(
        transfer_app, "GET", f"/market/{person}", _token(uuid4(), tenant_id=None)
    )

    assert response.status_code == 403, response.text


async def test_employed_and_open_at_the_same_time(apps: tuple[Any, Any]) -> None:
    """Der Normalfall auf einem Transfermarkt."""
    transfer_app, _consent = apps
    token = _token(uuid4(), tenant_id=None)

    await _set_status(transfer_app, token, "open", employed=True)

    body = (await _call(transfer_app, "GET", "/market/me", token)).json()
    assert body["employed"] is True
    assert body["is_approachable"] is True
