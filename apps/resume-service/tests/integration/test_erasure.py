"""Die Löschung im resume-service (ADR-0027 §2).

`resumes.id` **ist** die `subject_id`; Stationen und Ausbildung liegen als JSONB
in derselben Zeile und fallen mit ihr. Interessanter sind die Anfragen, und sie
gehen in zwei verschiedene Richtungen:

* `resume_requests` mit `subject_id` = Person **fällt** — die Zeile IST die
  Aussage „Unternehmen X hat nach diesem Menschen gefragt".
* `resume_requests` mit `requested_by` = Person **bleibt ohne ihren Namen** —
  ein Recruiter löscht sein privates Konto, die Anfrage gehört dem Unternehmen
  und handelt von einem Dritten.

Dieser Dienst hat **keinen** Aufbewahrungsschalter: es gibt hier keine
Zeilenklasse, für die je eine Aufbewahrungspflicht behauptet wurde.
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest
import pytest_asyncio
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

_SERVICE_DIR = Path(__file__).resolve().parents[2]
SECRET = "erasure-secret-with-at-least-thirty-two-bytes"
JWT_SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"

pytestmark = pytest.mark.asyncio(loop_scope="module")


@pytest.fixture(scope="module")
def app(postgres_url: str) -> Iterator[Any]:
    patch = pytest.MonkeyPatch()
    try:
        cfg = Config()
        cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        patch.setenv("WORKER_JWT_SECRET", JWT_SECRET)
        patch.setenv("WORKER_ERASURE_SECRET", SECRET)
        command.downgrade(cfg, "base")
        command.upgrade(cfg, "head")

        from resume_service.configuration import ResumeServiceSettings
        from resume_service.presentation.compose_api import build_app

        yield build_app(ResumeServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE resumes, resume_requests, outbox"))
        await s.commit()
        yield s
    await engine.dispose()


async def _resume(session: AsyncSession, subject: UUID) -> None:
    await session.execute(
        text(
            "INSERT INTO resumes (id, positions, education, created_at, updated_at) VALUES "
            "(:id, CAST(:p AS jsonb), '[]', now(), now())"
        ),
        {"id": str(subject), "p": '[{"employer": "Ein Arbeitgeber", "title": "Entwicklerin"}]'},
    )
    await session.commit()


async def _request(session: AsyncSession, *, subject: UUID, requested_by: UUID) -> UUID:
    request_id = uuid4()
    await session.execute(
        text(
            "INSERT INTO resume_requests (id, subject_id, tenant_id, requested_by, status, "
            "created_at) VALUES (:id, :subject, :tenant, :by, 'PENDING', now())"
        ),
        {
            "id": str(request_id),
            "subject": str(subject),
            "tenant": str(uuid4()),
            "by": str(requested_by),
        },
    )
    await session.commit()
    return request_id


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def _count(session: AsyncSession, sql: str, subject: UUID) -> int:
    rows = await session.execute(text(sql), {"s": str(subject)})
    return int(rows.scalar_one())


async def test_the_resume_falls_with_every_employer_in_it(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _resume(session, subject)

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    assert await _count(session, "SELECT count(*) FROM resumes WHERE id = :s", subject) == 0


async def test_a_request_about_this_person_falls(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _request(session, subject=subject, requested_by=uuid4())

    await _erase(app, subject)

    assert (
        await _count(session, "SELECT count(*) FROM resume_requests WHERE subject_id = :s", subject)
        == 0
    )


async def test_a_request_this_person_sent_stays_but_loses_their_name(
    app: Any, session: AsyncSession
) -> None:
    recruiter, other_person = uuid4(), uuid4()
    request_id = await _request(session, subject=other_person, requested_by=recruiter)

    await _erase(app, recruiter)

    row = (
        await session.execute(
            text("SELECT subject_id, requested_by FROM resume_requests WHERE id = :i"),
            {"i": str(request_id)},
        )
    ).one()
    assert row[0] == other_person
    assert row[1] is None


async def test_the_pending_notification_falls_too(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await session.execute(
        text(
            "INSERT INTO outbox (id, user_id, kind, created_at, attempts, last_error) "
            "VALUES (:id, :user, 'resume_request', now(), 0, '')"
        ),
        {"id": str(uuid4()), "user": str(subject)},
    )
    await session.commit()

    await _erase(app, subject)

    assert await _count(session, "SELECT count(*) FROM outbox WHERE user_id = :s", subject) == 0


async def test_nothing_is_retained_here(app: Any, session: AsyncSession) -> None:
    """Dieser Dienst kennt keinen Aufbewahrungsfall — und soll auch keinen
    bekommen, ohne dass jemand es entscheidet."""
    subject = uuid4()
    await _resume(session, subject)

    response = await _erase(app, subject)

    assert response.json()["retained"] == 0


async def test_delivering_twice_changes_nothing(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _resume(session, subject)

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    assert await _count(session, "SELECT count(*) FROM resumes WHERE id = :s", subject) == 0


async def test_a_wrong_secret_looks_like_no_endpoint(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _resume(session, subject)

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    assert await _count(session, "SELECT count(*) FROM resumes WHERE id = :s", subject) == 1
