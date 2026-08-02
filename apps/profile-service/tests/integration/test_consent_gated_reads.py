"""Das Versprechen dieses Slices, an echten Diensten belegt.

Der Consent-Ledger läuft hier als echte ASGI-App mit eigener Datenbank; der
Profile-Service spricht ihn über seinen normalen HTTP-Client an, nur dass der
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

_PROFILE_DIR = Path(__file__).resolve().parents[2]
_CONSENT_DIR = _PROFILE_DIR.parent / "consent-service"
_CONSENT_DB = "consent_for_profile_test"
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"
CAPABILITY = "profile.visibility:public"

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
        _migrate(_PROFILE_DIR, postgres_url, patch)
        patch.setenv("WORKER_JWT_SECRET", SECRET)

        from consent_service.configuration import ConsentServiceSettings
        from consent_service.presentation.compose_api import build_app as build_consent

        patch.setenv("WORKER_DATABASE_URL", consent_url)
        consent_app = build_consent(ConsentServiceSettings())

        # Der Profile-Service benutzt seinen echten HTTP-Client; nur der
        # Transport zeigt in den Prozess statt ins Netz. Damit ist alles auf dem
        # Weg dahin — DTO, Header, Statuscodes, Fail-closed — wirklich geprüft
        # und nicht durch ein Fake ersetzt.
        import profile_service.infrastructure.compose as compose_module
        from profile_service.infrastructure.consent import HttpConsentGate

        original = HttpConsentGate

        def _in_process_gate(*, base_url: str) -> HttpConsentGate:
            return original(base_url=base_url, transport=ASGITransport(app=consent_app))

        patch.setattr(compose_module, "HttpConsentGate", _in_process_gate)

        from profile_service.configuration import ProfileServiceSettings
        from profile_service.presentation.compose_api import build_app as build_profile

        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        profile_app = build_profile(ProfileServiceSettings())
        yield profile_app, consent_app
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


async def _save_profile(profile_app: Any, token: str, headline: str = "Senior Python") -> None:
    transport = ASGITransport(app=profile_app)
    async with AsyncClient(transport=transport, base_url="http://profile") as client:
        response = await client.put(
            "/profiles/me",
            json={
                "headline": headline,
                "bio": "Hallo",
                "location": "Berlin",
                "remote_ok": True,
                "skills": ["Python"],
            },
            headers={"Authorization": f"Bearer {token}"},
        )
    assert response.status_code == 200, response.text


async def _set_consent(consent_app: Any, token: str, subject: UUID, *, grant: bool) -> None:
    transport = ASGITransport(app=consent_app)
    path = "/consent/grant" if grant else "/consent/revoke"
    body: dict[str, Any] = {"subject_id": str(subject), "capability": CAPABILITY}
    if not grant:
        body["reason"] = "möchte nicht mehr gefunden werden"
    async with AsyncClient(transport=transport, base_url="http://consent") as client:
        response = await client.post(path, json=body, headers={"Authorization": f"Bearer {token}"})
    assert response.status_code == 200, response.text


async def _read_as_company(profile_app: Any, token: str, subject: UUID) -> httpx.Response:
    transport = ASGITransport(app=profile_app)
    async with AsyncClient(transport=transport, base_url="http://profile") as client:
        return await client.get(
            f"/profiles/{subject}", headers={"Authorization": f"Bearer {token}"}
        )


async def test_revocation_takes_effect_on_the_very_next_read(apps: tuple[Any, Any]) -> None:
    """Kein Cache, keine Verzögerung — ADR-0013 an der Stelle geprüft, wo es zählt."""
    profile_app, consent_app = apps
    candidate = uuid4()
    candidate_token = _token(candidate, tenant_id=None)
    company_token = _token(uuid4(), tenant_id=uuid4())

    await _save_profile(profile_app, candidate_token)
    await _set_consent(consent_app, candidate_token, candidate, grant=True)

    visible = await _read_as_company(profile_app, company_token, candidate)
    assert visible.status_code == 200, visible.text
    assert visible.json()["headline"] == "Senior Python"

    await _set_consent(consent_app, candidate_token, candidate, grant=False)

    gone = await _read_as_company(profile_app, company_token, candidate)
    assert gone.status_code == 404, gone.text


async def test_without_consent_a_profile_is_indistinguishable_from_none(
    apps: tuple[Any, Any],
) -> None:
    profile_app, _ = apps
    candidate = uuid4()
    await _save_profile(profile_app, _token(candidate, tenant_id=None))
    company_token = _token(uuid4(), tenant_id=uuid4())

    withheld = await _read_as_company(profile_app, company_token, candidate)
    never_existed = await _read_as_company(profile_app, company_token, uuid4())

    assert withheld.status_code == never_existed.status_code == 404

    # Bis auf die Korrelations-ID, die je Anfrage neu ist, muss die Antwort
    # Zeichen für Zeichen dieselbe sein: jeder Unterschied — ein anderer Code,
    # ein anderer Titel, ein anderes Feld — wäre ein Orakel dafür, ob es die
    # Person gibt.
    def _without_correlation(body: dict[str, Any]) -> dict[str, Any]:
        return {key: value for key, value in body.items() if key != "correlationId"}

    assert _without_correlation(withheld.json()) == _without_correlation(never_existed.json())


async def test_a_private_person_may_not_read_foreign_profiles(apps: tuple[Any, Any]) -> None:
    """Kein Netzwerk unter Kandidaten — Profile lesen Unternehmen (ADR-0017)."""
    profile_app, consent_app = apps
    candidate = uuid4()
    candidate_token = _token(candidate, tenant_id=None)
    await _save_profile(profile_app, candidate_token)
    await _set_consent(consent_app, candidate_token, candidate, grant=True)

    other_person = _token(uuid4(), tenant_id=None)
    response = await _read_as_company(profile_app, other_person, candidate)

    assert response.status_code == 403, response.text


async def test_the_owner_sees_their_own_profile_without_any_consent(
    apps: tuple[Any, Any],
) -> None:
    profile_app, _ = apps
    candidate = uuid4()
    token = _token(candidate, tenant_id=None)
    await _save_profile(profile_app, token)

    transport = ASGITransport(app=profile_app)
    async with AsyncClient(transport=transport, base_url="http://profile") as client:
        response = await client.get("/profiles/me", headers={"Authorization": f"Bearer {token}"})

    assert response.status_code == 200
    assert response.json()["headline"] == "Senior Python"


async def test_the_list_shows_only_released_profiles(apps: tuple[Any, Any]) -> None:
    profile_app, consent_app = apps
    released, withheld = uuid4(), uuid4()
    released_token = _token(released, tenant_id=None)
    await _save_profile(profile_app, released_token, headline="Sichtbar")
    await _save_profile(profile_app, _token(withheld, tenant_id=None), headline="Verborgen")
    await _set_consent(consent_app, released_token, released, grant=True)

    transport = ASGITransport(app=profile_app)
    company_token = _token(uuid4(), tenant_id=uuid4())
    async with AsyncClient(transport=transport, base_url="http://profile") as client:
        response = await client.get(
            "/profiles", headers={"Authorization": f"Bearer {company_token}"}
        )

    assert response.status_code == 200, response.text
    headlines = [item["headline"] for item in response.json()["items"]]
    assert headlines == ["Sichtbar"]
