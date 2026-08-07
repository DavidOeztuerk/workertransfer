"""Der Beweis für die Entscheidung „verweisen statt kopieren".

Drei Dienste laufen hier als echte ASGI-Apps mit je eigener Datenbank: der
Consent-Ledger, der Profile-Service und der Applications-Service. Der
Jobs-Service ist durch ein Fake ersetzt — er liefert nur zwei Werte
(Unternehmen und Existenz), und ihn mitzustarten würde eine vierte Datenbank
und eine vierte Migration kosten, ohne eine Aussage hinzuzufügen.

Der tragende Test: bewerben → das Unternehmen sieht das Profil → zurückziehen →
es sieht es nicht mehr, der Vorgang bleibt. Eine Kopie könnte das nicht.
"""

from __future__ import annotations

import os
from collections.abc import Iterator
from dataclasses import dataclass
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

_APPLICATIONS_DIR = Path(__file__).resolve().parents[2]
_CONSENT_DIR = _APPLICATIONS_DIR.parent / "consent-service"
_PROFILE_DIR = _APPLICATIONS_DIR.parent / "profile-service"
_CONSENT_DB = "consent_for_applications_test"
_PROFILE_DB = "profile_for_applications_test"
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"

pytestmark = pytest.mark.asyncio(loop_scope="module")


def _sync(url: str) -> str:
    return url.replace("postgresql+asyncpg://", "postgresql+psycopg://")


def _sibling(url: str, name: str) -> str:
    return url.rsplit("/", 1)[0] + "/" + name


def _drop_database(admin_url: str, name: str) -> None:
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        # Die App-Pools geben ihre Verbindungen nicht her — build_app reicht die
        # Engine nicht heraus.
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


@dataclass
class FakeJobs:
    """Der Jobs-Service, auf das reduziert, was eine Bewerbung von ihm braucht.

    Ihn mitzustarten würde eine vierte Datenbank und eine vierte Migration
    kosten, ohne eine Aussage hinzuzufügen: dass eine geschlossene Stelle 404
    liefert, prüft `apps/jobs-service` selbst.
    """

    tenant_id: UUID
    open_jobs: set[UUID]

    async def public_job(self, job_id: UUID) -> Any:
        from applications_service.infrastructure.jobs import PublicJob

        if job_id not in self.open_jobs:
            return None
        return PublicJob(id=job_id, tenant_id=self.tenant_id, title="Eine Stelle")


@pytest.fixture(scope="module")
def stack(postgres_url: str) -> Iterator[tuple[Any, Any, Any, FakeJobs]]:
    admin_url = _sync(postgres_url)
    consent_url = _sibling(postgres_url, _CONSENT_DB)
    profile_url = _sibling(postgres_url, _PROFILE_DB)
    for name in (_CONSENT_DB, _PROFILE_DB):
        _drop_database(admin_url, name)
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        # Je Dienst eine Datenbank (ADR-0004), alle in einem Container: die
        # Trennung, um die es geht, ist die der Daten.
        for name in (_CONSENT_DB, _PROFILE_DB):
            conn.execute(text(f'CREATE DATABASE "{name}"'))
    admin.dispose()

    patch = pytest.MonkeyPatch()
    try:
        for service_dir, url in (
            (_CONSENT_DIR, consent_url),
            (_PROFILE_DIR, profile_url),
            (_APPLICATIONS_DIR, postgres_url),
        ):
            cfg = Config()
            cfg.set_main_option("script_location", str(service_dir / "migrations"))
            patch.setenv("WORKER_DATABASE_URL", url)
            command.upgrade(cfg, "head")
        patch.setenv("WORKER_JWT_SECRET", SECRET)

        from consent_service.configuration import ConsentServiceSettings
        from consent_service.presentation.compose_api import build_app as build_consent

        patch.setenv("WORKER_DATABASE_URL", consent_url)
        consent_app = build_consent(ConsentServiceSettings())

        # Profile- und Applications-Service sprechen den Ledger über ihre
        # echten HTTP-Clients an; nur der Transport zeigt in den Prozess.
        import profile_service.infrastructure.compose as profile_compose
        from profile_service.infrastructure.consent import HttpConsentGate

        original_gate = HttpConsentGate
        patch.setattr(
            profile_compose,
            "HttpConsentGate",
            lambda *, base_url: original_gate(
                base_url=base_url, transport=ASGITransport(app=consent_app)
            ),
        )

        from profile_service.configuration import ProfileServiceSettings
        from profile_service.presentation.compose_api import build_app as build_profile

        patch.setenv("WORKER_DATABASE_URL", profile_url)
        profile_app = build_profile(ProfileServiceSettings())

        import applications_service.infrastructure.compose as app_compose
        from applications_service.infrastructure.consent import HttpConsentWriter

        original_writer = HttpConsentWriter
        patch.setattr(
            app_compose,
            "HttpConsentWriter",
            lambda *, base_url: original_writer(
                base_url=base_url, transport=ASGITransport(app=consent_app)
            ),
        )
        jobs = FakeJobs(tenant_id=uuid4(), open_jobs=set())
        patch.setattr(app_compose, "HttpJobLookup", lambda *, base_url: jobs)

        from applications_service.configuration import ApplicationsServiceSettings
        from applications_service.presentation.compose_api import build_app as build_applications

        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        applications_app = build_applications(ApplicationsServiceSettings())
        yield applications_app, profile_app, consent_app, jobs
    finally:
        patch.undo()
        for name in (_CONSENT_DB, _PROFILE_DB):
            _drop_database(admin_url, name)


@pytest.fixture
def apps(
    stack: tuple[Any, Any, Any, FakeJobs], postgres_url: str
) -> tuple[Any, Any, Any, FakeJobs]:
    _truncate_all(postgres_url)
    _truncate_all(_sibling(postgres_url, _CONSENT_DB))
    _truncate_all(_sibling(postgres_url, _PROFILE_DB))
    stack[3].open_jobs.clear()
    return stack


def _token(user_id: UUID, *, tenant_id: UUID | None) -> str:
    return TokenManager(secret=SECRET).create_access_token(user_id, tenant_id, ["user"], [])


async def _call(app: Any, method: str, path: str, token: str, **kwargs: Any) -> httpx.Response:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.request(
            method, path, headers={"Authorization": f"Bearer {token}"}, **kwargs
        )


async def _save_profile(profile_app: Any, token: str, headline: str) -> None:
    response = await _call(
        profile_app,
        "PUT",
        "/profiles/me",
        token,
        json={
            "headline": headline,
            "bio": "",
            "location": "Berlin",
            "remote_ok": True,
            "skills": [],
        },
    )
    assert response.status_code == 200, response.text


async def test_applying_opens_the_profile_and_withdrawing_closes_it(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    """Der tragende Test. Eine Kopie könnte das nicht."""
    applications_app, profile_app, _consent, jobs = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    company_token = _token(uuid4(), tenant_id=jobs.tenant_id)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)

    await _save_profile(profile_app, person_token, "Senior Python")

    # Vorher: das Unternehmen sieht nichts. Das Profil ist nicht öffentlich
    # freigegeben, und eine Bewerbung gibt es noch nicht.
    before = await _call(profile_app, "GET", f"/profiles/{person}", company_token)
    assert before.status_code == 404

    applied = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": "Ich passe dazu."},
    )
    assert applied.status_code == 201, applied.text
    application_id = applied.json()["id"]

    # Jetzt sieht es das Profil — über den Profile-Service, nicht über die
    # Bewerbung. Die Bewerbung trägt keine Profildaten.
    visible = await _call(profile_app, "GET", f"/profiles/{person}", company_token)
    assert visible.status_code == 200, visible.text
    assert visible.json()["headline"] == "Senior Python"
    assert "headline" not in applied.json()

    withdrawn = await _call(
        applications_app, "POST", f"/applications/{application_id}/withdraw", person_token
    )
    assert withdrawn.status_code == 200, withdrawn.text
    assert withdrawn.json()["status"] == "withdrawn"

    # Und weg. Ohne Wartezeit dazwischen.
    gone = await _call(profile_app, "GET", f"/profiles/{person}", company_token)
    assert gone.status_code == 404, gone.text

    # Der Vorgang bleibt sichtbar: dass jemand sich beworben und zurückgezogen
    # hat, gehört zur Geschichte des Verfahrens im Unternehmen.
    theirs = await _call(applications_app, "GET", f"/jobs/{job_id}/applications", company_token)
    assert [entry["status"] for entry in theirs.json()] == ["withdrawn"]


async def test_a_second_company_sees_nothing(apps: tuple[Any, Any, Any, FakeJobs]) -> None:
    """Die Freigabe nennt einen Empfänger — genau einen."""
    applications_app, profile_app, _consent, jobs = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    await _save_profile(profile_app, person_token, "Senior Python")
    await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": ""},
    )

    other_company = _token(uuid4(), tenant_id=uuid4())
    response = await _call(profile_app, "GET", f"/profiles/{person}", other_company)

    assert response.status_code == 404, response.text


async def test_a_job_that_is_not_open_cannot_be_applied_to(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    applications_app, profile_app, _consent, _jobs = apps
    person_token = _token(uuid4(), tenant_id=None)
    await _save_profile(profile_app, person_token, "Senior Python")

    response = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(uuid4()), "message": ""},
    )

    assert response.status_code == 404, response.text


async def test_applying_twice_updates_instead_of_duplicating(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    applications_app, profile_app, _consent, jobs = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    await _save_profile(profile_app, person_token, "Senior Python")

    first = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": "Erste"},
    )
    assert first.status_code == 201

    # Eine laufende Bewerbung lässt sich nicht erneut absenden.
    again = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": "Zweite"},
    )
    assert again.status_code == 409, again.text

    mine = await _call(applications_app, "GET", "/applications/me", person_token)
    assert len(mine.json()) == 1


async def test_after_a_withdrawal_one_may_apply_again(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    """Eine neue Entscheidung — und die Freigabe entsteht neu."""
    applications_app, profile_app, _consent, jobs = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    company_token = _token(uuid4(), tenant_id=jobs.tenant_id)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    await _save_profile(profile_app, person_token, "Senior Python")

    applied = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": ""},
    )
    await _call(
        applications_app,
        "POST",
        f"/applications/{applied.json()['id']}/withdraw",
        person_token,
    )
    assert (
        await _call(profile_app, "GET", f"/profiles/{person}", company_token)
    ).status_code == 404

    again = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": "Doch"},
    )

    assert again.status_code == 201, again.text
    assert (
        await _call(profile_app, "GET", f"/profiles/{person}", company_token)
    ).status_code == 200


async def test_a_rejected_applicant_cannot_try_again(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    """Sonst wäre ein „nein" nur eine Verzögerung."""
    applications_app, profile_app, _consent, jobs = apps
    person_token = _token(uuid4(), tenant_id=None)
    company_token = _token(uuid4(), tenant_id=jobs.tenant_id)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    await _save_profile(profile_app, person_token, "Senior Python")
    applied = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": ""},
    )
    rejected = await _call(
        applications_app,
        "POST",
        f"/applications/{applied.json()['id']}/status",
        company_token,
        json={"status": "rejected"},
    )
    assert rejected.status_code == 200, rejected.text

    again = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": "Bitte doch"},
    )

    assert again.status_code == 409, again.text


async def test_a_foreign_company_cannot_move_an_application(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    applications_app, profile_app, _consent, jobs = apps
    person_token = _token(uuid4(), tenant_id=None)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    await _save_profile(profile_app, person_token, "Senior Python")
    applied = await _call(
        applications_app,
        "POST",
        "/applications",
        person_token,
        json={"job_id": str(job_id), "message": ""},
    )

    stranger = _token(uuid4(), tenant_id=uuid4())
    theirs = await _call(
        applications_app,
        "POST",
        f"/applications/{applied.json()['id']}/status",
        stranger,
        json={"status": "hired"},
    )
    invented = await _call(
        applications_app,
        "POST",
        f"/applications/{uuid4()}/status",
        stranger,
        json={"status": "hired"},
    )

    # Nicht meins und nicht vorhanden sind von außen dasselbe.
    assert theirs.status_code == invented.status_code == 404
    assert theirs.json()["detail"] == invented.json()["detail"]


async def test_the_notification_survives_a_broken_notifier(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    """Der Gewinn von 9.2 — und der Fall, den die alte Fassung verlor.

    Vorher lief die Benachrichtigung als „feuern und vergessen": ein
    HTTP-Aufruf nach dem Commit, dessen Fehler geschluckt wurde. Ein Neustart
    von identity-service genügte, und die Person erfuhr nie, dass ihre
    Bewerbung beantwortet wurde.

    Diese Zusage war hier vorher **gar nicht geprüft** — der Dienst hatte
    keinen einzigen Test auf den Notifier. Genau deshalb steht er jetzt da.
    """
    from applications_service.infrastructure.database.models import OUTBOX
    from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
    from worker_outbox import OutboxDispatcher

    applications_app, profile_app, _consent, jobs = apps
    person = uuid4()
    person_token = _token(person, tenant_id=None)
    company_token = _token(uuid4(), tenant_id=jobs.tenant_id)
    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    await _save_profile(profile_app, person_token, "Senior Python")
    applied = await _call(
        applications_app, "POST", "/applications", person_token, json={"job_id": str(job_id)}
    )
    assert applied.status_code == 201, applied.text

    # Der Zug des Unternehmens: DAS wird gemeldet.
    answered = await _call(
        applications_app,
        "POST",
        f"/applications/{applied.json()['id']}/status",
        company_token,
        json={"status": "hired"},
    )
    assert answered.status_code == 200, answered.text

    engine = create_async_engine(os.environ["WORKER_DATABASE_URL"])
    try:
        sessions = async_sessionmaker(engine, expire_on_commit=False)

        class Broken:
            async def notify(self, user_id: UUID, kind: str) -> None:
                raise ConnectionError("identity-service startet gerade neu")

        class Working:
            def __init__(self) -> None:
                self.sent: list[tuple[UUID, str]] = []

            async def notify(self, user_id: UUID, kind: str) -> None:
                self.sent.append((user_id, kind))

        # Erster Anlauf scheitert — die Zeile bleibt liegen, statt verloren zu
        # gehen. Das ist der ganze Unterschied zu vorher.
        broken = OutboxDispatcher(session_factory=sessions, table=OUTBOX, delivery=Broken())
        assert await broken.drain_once() == 0

        # Der Dienst ist wieder da, niemand musste etwas nachtragen.
        working = Working()
        good = OutboxDispatcher(session_factory=sessions, table=OUTBOX, delivery=working)
        assert await good.drain_once() == 1
        assert working.sent == [(person, "application_update")]
    finally:
        await engine.dispose()


async def test_the_stats_count_only_the_companys_own_applications(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    """Kennzahlen über die EIGENEN Vorgänge — und über sonst nichts (ADR-0026).

    Zulässig ist die Zahl, weil das Unternehmen dieselben Bewerbungen ohnehin
    einzeln in seiner Liste sieht. Sie ist eine Bequemlichkeit, keine neue
    Auskunft. Deshalb prüft dieser Test vor allem die Abgrenzung: ein zweites
    Unternehmen darf in derselben Zahl nicht auftauchen.
    """
    applications_app, profile_app, _consent, jobs = apps
    company_token = _token(uuid4(), tenant_id=jobs.tenant_id)

    job_id = uuid4()
    jobs.open_jobs.add(job_id)
    for _ in range(2):
        person = uuid4()
        token = _token(person, tenant_id=None)
        await _save_profile(profile_app, token, "Senior Python")
        applied = await _call(
            applications_app, "POST", "/applications", token, json={"job_id": str(job_id)}
        )
        assert applied.status_code == 201, applied.text

    # Eine davon beantworten, damit zwei Status vorkommen.
    listed = await _call(applications_app, "GET", f"/jobs/{job_id}/applications", company_token)
    await _call(
        applications_app,
        "POST",
        f"/applications/{listed.json()[0]['id']}/status",
        company_token,
        json={"status": "rejected"},
    )

    stats = await _call(applications_app, "GET", "/companies/me/application-stats", company_token)
    assert stats.status_code == 200, stats.text
    body = stats.json()
    assert body["total"] == 2
    assert body["by_status"] == {"submitted": 1, "rejected": 1}

    # Ein fremdes Unternehmen sieht seine eigene (leere) Zahl, nicht diese.
    stranger = _token(uuid4(), tenant_id=uuid4())
    theirs = await _call(applications_app, "GET", "/companies/me/application-stats", stranger)
    assert theirs.status_code == 200, theirs.text
    assert theirs.json() == {"by_status": {}, "total": 0}


async def test_the_stats_say_nothing_about_the_people_behind_them(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    """Die eigentliche Grenze, und sie liegt nicht bei der Aggregation.

    Eine Zahl, die Bewerbungen mit Marktstatus, Lebenslauf oder Vorgängen bei
    ANDEREN Firmen verrechnet, wäre eine Aussage über Menschen aus Quellen, die
    einzeln freigegeben wurden — und keine Aggregation macht das wieder gut.
    Deshalb hält dieser Test die Feldmenge der Antwort fest: was es nicht gibt,
    kann nicht herausgehen (dieselbe Strenge wie bei `DraftContext`).
    """
    applications_app, _profile, _consent, jobs = apps
    company_token = _token(uuid4(), tenant_id=jobs.tenant_id)

    stats = await _call(applications_app, "GET", "/companies/me/application-stats", company_token)

    assert set(stats.json().keys()) == {"by_status", "total"}


async def test_the_stats_need_an_active_company(
    apps: tuple[Any, Any, Any, FakeJobs],
) -> None:
    # Aussage über den Aufrufer, nicht über ein fremdes Unternehmen — 403.
    applications_app, _profile, _consent, _jobs = apps
    person_token = _token(uuid4(), tenant_id=None)

    response = await _call(applications_app, "GET", "/companies/me/application-stats", person_token)

    assert response.status_code == 403, response.text
