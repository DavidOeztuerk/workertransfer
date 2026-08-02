"""Der Anfrage- und Freigabefluss, an echten Diensten.

Der Consent-Ledger läuft als echte ASGI-App mit eigener Datenbank; der
Resume-Service spricht ihn über seinen normalen HTTP-Client an, nur zeigt der
Transport in den Prozess statt ins Netz.

Die tragende Aussage: eine Freigabe gilt für **ein** Unternehmen. Ein zweites
sieht denselben Lebenslauf nicht — und nach einem Widerruf sieht ihn auch das
erste nicht mehr, ohne Wartezeit dazwischen.
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

_RESUME_DIR = Path(__file__).resolve().parents[2]
_CONSENT_DIR = _RESUME_DIR.parent / "consent-service"
_CONSENT_DB = "consent_for_resume_test"
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"
PROFILE_CAPABILITY = "profile.visibility:public"

pytestmark = pytest.mark.asyncio(loop_scope="module")


def _sync(url: str) -> str:
    return url.replace("postgresql+asyncpg://", "postgresql+psycopg://")


def _sibling(url: str, name: str) -> str:
    return url.rsplit("/", 1)[0] + "/" + name


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
    """Beide Dienste einmal je Modul, je eine Datenbank (ADR-0004).

    Eine Event-Schleife fürs ganze Modul: die Apps halten asyncpg-Pools, die an
    die Schleife ihrer Erzeugung gebunden sind. Isolation kommt stattdessen aus
    TRUNCATE zwischen den Tests.
    """
    admin_url = _sync(postgres_url)
    consent_url = _sibling(postgres_url, _CONSENT_DB)
    _drop_database(admin_url, _CONSENT_DB)
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        conn.execute(text(f'CREATE DATABASE "{_CONSENT_DB}"'))
    admin.dispose()

    patch = pytest.MonkeyPatch()
    try:
        for service_dir, url in ((_CONSENT_DIR, consent_url), (_RESUME_DIR, postgres_url)):
            cfg = Config()
            cfg.set_main_option("script_location", str(service_dir / "migrations"))
            patch.setenv("WORKER_DATABASE_URL", url)
            command.upgrade(cfg, "head")
        patch.setenv("WORKER_JWT_SECRET", SECRET)

        from consent_service.configuration import ConsentServiceSettings
        from consent_service.presentation.compose_api import build_app as build_consent

        patch.setenv("WORKER_DATABASE_URL", consent_url)
        consent_app = build_consent(ConsentServiceSettings())

        import resume_service.infrastructure.compose as compose_module
        from resume_service.infrastructure.consent import HttpConsentGate

        original = HttpConsentGate

        def _in_process_gate(*, base_url: str) -> HttpConsentGate:
            return original(base_url=base_url, transport=ASGITransport(app=consent_app))

        patch.setattr(compose_module, "HttpConsentGate", _in_process_gate)

        from resume_service.configuration import ResumeServiceSettings
        from resume_service.presentation.compose_api import build_app as build_resume

        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        yield build_resume(ResumeServiceSettings()), consent_app
    finally:
        patch.undo()
        _drop_database(admin_url, _CONSENT_DB)


@pytest.fixture
def apps(stack: tuple[Any, Any], postgres_url: str) -> tuple[Any, Any]:
    _truncate_all(postgres_url)
    _truncate_all(_sibling(postgres_url, _CONSENT_DB))
    return stack


def _token(user_id: UUID, *, tenant_id: UUID | None) -> str:
    return TokenManager(secret=SECRET).create_access_token(user_id, tenant_id, ["user"], [])


async def _call(app: Any, method: str, path: str, token: str, **kwargs: Any) -> httpx.Response:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.request(
            method, path, headers={"Authorization": f"Bearer {token}"}, **kwargs
        )


async def _release_profile(consent_app: Any, token: str, subject: UUID) -> None:
    response = await _call(
        consent_app,
        "POST",
        "/consent/grant",
        token,
        json={"subject_id": str(subject), "capability": PROFILE_CAPABILITY},
    )
    assert response.status_code == 200, response.text


async def _save_resume(resume_app: Any, token: str, employer: str = "Acme GmbH") -> None:
    response = await _call(
        resume_app,
        "PUT",
        "/resumes/me",
        token,
        json={
            "positions": [
                {
                    "employer": employer,
                    "title": "Backend-Entwicklerin",
                    "started_on": "2020-01",
                    "ended_on": None,
                    "description": "",
                }
            ],
            "education": [],
        },
    )
    assert response.status_code == 200, response.text


class Candidate:
    def __init__(self) -> None:
        self.id = uuid4()
        self.token = _token(self.id, tenant_id=None)


class Company:
    def __init__(self) -> None:
        self.tenant_id = uuid4()
        self.token = _token(uuid4(), tenant_id=self.tenant_id)


async def _prepared(apps: tuple[Any, Any]) -> tuple[Any, Any, Candidate, Company]:
    resume_app, consent_app = apps
    candidate, company = Candidate(), Company()
    await _save_resume(resume_app, candidate.token)
    await _release_profile(consent_app, candidate.token, candidate.id)
    return resume_app, consent_app, candidate, company


async def test_the_whole_journey_and_the_immediate_effect_of_a_withdrawal(
    apps: tuple[Any, Any],
) -> None:
    resume_app, _consent, candidate, company = await _prepared(apps)

    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)
    assert asked.status_code == 201, asked.text
    request_id = asked.json()["id"]

    # Vor der Freigabe: dieselbe Antwort wie für einen Lebenslauf, den es nicht gibt.
    blocked = await _call(resume_app, "GET", f"/resumes/{candidate.id}", company.token)
    assert blocked.status_code == 404

    granted = await _call(
        resume_app, "POST", f"/resumes/requests/{request_id}/grant", candidate.token
    )
    assert granted.status_code == 200, granted.text

    visible = await _call(resume_app, "GET", f"/resumes/{candidate.id}", company.token)
    assert visible.status_code == 200, visible.text
    assert visible.json()["positions"][0]["employer"] == "Acme GmbH"

    revoked = await _call(
        resume_app, "POST", f"/resumes/requests/{request_id}/revoke", candidate.token
    )
    assert revoked.status_code == 200, revoked.text

    gone = await _call(resume_app, "GET", f"/resumes/{candidate.id}", company.token)
    assert gone.status_code == 404, gone.text


async def test_a_release_reaches_exactly_one_company(apps: tuple[Any, Any]) -> None:
    """Der Kern des Entwurfs: der Lebenslauf wird nicht veröffentlicht."""
    resume_app, consent_app, candidate, first = await _prepared(apps)
    second = Company()

    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", first.token)
    await _call(
        resume_app, "POST", f"/resumes/requests/{asked.json()['id']}/grant", candidate.token
    )

    assert (
        await _call(resume_app, "GET", f"/resumes/{candidate.id}", first.token)
    ).status_code == 200
    # Das zweite Unternehmen sieht das Profil (öffentlich freigegeben), aber
    # nicht den Lebenslauf.
    assert (
        await _call(resume_app, "GET", f"/resumes/{candidate.id}", second.token)
    ).status_code == 404
    assert consent_app is not None


async def test_the_request_stays_granted_after_a_withdrawal_but_access_does_not(
    apps: tuple[Any, Any],
) -> None:
    """`GRANTED` heißt „wurde erteilt", `active` heißt „gilt gerade"."""
    resume_app, _consent, candidate, company = await _prepared(apps)
    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)
    request_id = asked.json()["id"]
    await _call(resume_app, "POST", f"/resumes/requests/{request_id}/grant", candidate.token)

    before = (await _call(resume_app, "GET", "/resumes/me/requests", candidate.token)).json()
    assert before[0]["status"] == "GRANTED"
    assert before[0]["active"] is True

    await _call(resume_app, "POST", f"/resumes/requests/{request_id}/revoke", candidate.token)

    after = (await _call(resume_app, "GET", "/resumes/me/requests", candidate.token)).json()
    assert after[0]["status"] == "GRANTED"
    assert after[0]["active"] is False


async def test_a_company_may_not_ask_twice(apps: tuple[Any, Any]) -> None:
    resume_app, _consent, candidate, company = await _prepared(apps)
    await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)

    again = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)

    assert again.status_code == 409


async def test_a_declined_request_cannot_be_reopened_by_asking_again(
    apps: tuple[Any, Any],
) -> None:
    """Wer dreimal fragen darf, hat kein Nein bekommen, sondern eine Verzögerung."""
    resume_app, _consent, candidate, company = await _prepared(apps)
    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)
    await _call(
        resume_app, "POST", f"/resumes/requests/{asked.json()['id']}/decline", candidate.token
    )

    again = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)

    assert again.status_code == 409


async def test_without_the_profile_release_a_request_is_indistinguishable_from_a_stranger(
    apps: tuple[Any, Any],
) -> None:
    resume_app, _consent_app = apps
    hidden = Candidate()
    await _save_resume(resume_app, hidden.token)
    company = Company()

    withheld = await _call(resume_app, "POST", f"/resumes/{hidden.id}/requests", company.token)
    never_existed = await _call(resume_app, "POST", f"/resumes/{uuid4()}/requests", company.token)

    assert withheld.status_code == never_existed.status_code == 404
    assert withheld.json()["detail"] == never_existed.json()["detail"]


async def test_a_person_without_a_company_cannot_ask(apps: tuple[Any, Any]) -> None:
    resume_app, _consent, candidate, _company = await _prepared(apps)
    other_person = _token(uuid4(), tenant_id=None)

    response = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", other_person)

    assert response.status_code == 403


async def test_nobody_answers_a_request_that_is_not_theirs(apps: tuple[Any, Any]) -> None:
    resume_app, _consent, candidate, company = await _prepared(apps)
    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)
    request_id = asked.json()["id"]

    stranger = _token(uuid4(), tenant_id=None)
    response = await _call(resume_app, "POST", f"/resumes/requests/{request_id}/grant", stranger)

    # Wie eine fremde Subject-ID: nicht vorhanden und nicht meins sind dasselbe.
    assert response.status_code == 404


async def test_an_answered_request_is_not_answered_again(apps: tuple[Any, Any]) -> None:
    resume_app, _consent, candidate, company = await _prepared(apps)
    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)
    request_id = asked.json()["id"]
    await _call(resume_app, "POST", f"/resumes/requests/{request_id}/decline", candidate.token)

    flipped = await _call(
        resume_app, "POST", f"/resumes/requests/{request_id}/grant", candidate.token
    )

    assert flipped.status_code == 422


async def test_a_request_may_be_made_to_a_person_without_a_resume(
    apps: tuple[Any, Any],
) -> None:
    """Sonst wäre die Anfrage ein Orakel über „hat schon einen CV gepflegt"."""
    resume_app, consent_app = apps
    candidate, company = Candidate(), Company()
    await _release_profile(consent_app, candidate.token, candidate.id)

    asked = await _call(resume_app, "POST", f"/resumes/{candidate.id}/requests", company.token)

    assert asked.status_code == 201
