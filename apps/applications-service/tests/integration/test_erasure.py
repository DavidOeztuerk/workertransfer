"""Die Löschung im applications-service — hier liegt einer der beiden Konflikte.

**Die tragende Entscheidung der ADR-0027 steht in §3: die Voreinstellung löscht
vollständig, auch `status = 'hired'`.** Das ist die Umkehrung eines früheren
Entwurfs, und es ist die Stelle, an der ein stillschweigendes Zurückrutschen in
die Vorsichtsannahme auffallen muss. Deshalb steht der Test dafür hier zuerst.

Die Begründung, kurz: die Plattform ist nicht der Arbeitgeber. Wird jemand über
sie eingestellt, liegt der Vertrag beim Arbeitgeber — die Bewerbungszeile bei
einem Vermittler ist keine Unterlage über ein Arbeitsverhältnis. Wer sie als
solche braucht, führt sie am falschen Ort.
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
from applications_service.application import erasure
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

_SERVICE_DIR = Path(__file__).resolve().parents[2]
SECRET = "erasure-secret-with-at-least-thirty-two-bytes"
JWT_SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"

ALL_STATUSES = ("submitted", "reviewing", "rejected", "withdrawn", "hired")

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

        from applications_service.configuration import ApplicationsServiceSettings
        from applications_service.presentation.compose_api import build_app

        yield build_app(ApplicationsServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE applications, outbox"))
        await s.commit()
        yield s
    await engine.dispose()


async def _insert(session: AsyncSession, subject: UUID, *, status: str) -> UUID:
    application_id = uuid4()
    await session.execute(
        text(
            "INSERT INTO applications (id, job_id, tenant_id, subject_id, message, "
            "shares_resume, shares_portfolio, status, created_at, updated_at) VALUES "
            "(:id, :job, :tenant, :subject, 'Mein Anschreiben', false, false, :status, "
            "now(), now())"
        ),
        {
            "id": str(application_id),
            "job": str(uuid4()),
            "tenant": str(uuid4()),
            "subject": str(subject),
            "status": status,
        },
    )
    await session.commit()
    return application_id


async def _statuses(session: AsyncSession, subject: UUID) -> set[str]:
    rows = await session.execute(
        text("SELECT status FROM applications WHERE subject_id = :s"), {"s": str(subject)}
    )
    return {row[0] for row in rows}


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def test_by_default_even_a_hired_application_falls(app: Any, session: AsyncSession) -> None:
    """Die Zeilenklasse, um die es geht. Fällt sie nicht, ist §3 gebrochen —
    und ein Mensch, der sein Konto gelöscht hat, steht weiter in der Liste
    eines Unternehmens."""
    subject = uuid4()
    await _insert(session, subject, status="hired")

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    assert await _statuses(session, subject) == set()
    # Und der Ursprung erfährt: es blieb nichts.
    assert response.json()["retained"] == 0


async def test_by_default_every_status_falls(app: Any, session: AsyncSession) -> None:
    """Auch das Anschreiben — den Text hat dieser Mensch selbst verfasst."""
    subject = uuid4()
    for status in ALL_STATUSES:
        await _insert(session, subject, status=status)

    await _erase(app, subject)

    assert await _statuses(session, subject) == set()
    rows = await session.execute(
        text("SELECT count(*) FROM applications WHERE message <> ''"),
    )
    assert rows.scalar_one() == 0


async def test_the_pending_notification_falls_too(app: Any, session: AsyncSession) -> None:
    """Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt."""
    subject = uuid4()
    await session.execute(
        text(
            "INSERT INTO outbox (id, user_id, kind, created_at, attempts, last_error) "
            "VALUES (:id, :user, 'application_update', now(), 0, '')"
        ),
        {"id": str(uuid4()), "user": str(subject)},
    )
    await session.commit()

    await _erase(app, subject)

    rows = await session.execute(
        text("SELECT count(*) FROM outbox WHERE user_id = :u"), {"u": str(subject)}
    )
    assert rows.scalar_one() == 0


async def test_it_touches_nobody_else(app: Any, session: AsyncSession) -> None:
    mine, theirs = uuid4(), uuid4()
    await _insert(session, mine, status="hired")
    await _insert(session, theirs, status="hired")

    await _erase(app, mine)

    assert await _statuses(session, theirs) == {"hired"}


async def test_delivering_twice_changes_nothing(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _insert(session, subject, status="hired")

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    assert await _statuses(session, subject) == set()


async def test_a_wrong_secret_looks_like_no_endpoint(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _insert(session, subject, status="submitted")

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    assert await _statuses(session, subject) == {"submitted"}


class TestTheFlippedSwitchHoldsExactlyOneRowClass:
    """Umgelegt wird er **nur hier**, nie in der Voreinstellung (ADR-0027 §3.2).

    Sonst prüft niemand, ob die Abgrenzung hält, und der Schalter wäre
    unbenutzter Code mit einer Behauptung daran. Keine Ausdehnung auf
    `rejected` — eine abgelehnte Bewerbung begründet nichts.
    """

    @pytest.fixture(autouse=True)
    def flipped(self, monkeypatch: pytest.MonkeyPatch) -> None:
        monkeypatch.setattr(erasure, "RETAIN_HIRED_APPLICATIONS", True)

    async def test_only_hired_survives(self, app: Any, session: AsyncSession) -> None:
        subject = uuid4()
        for status in ALL_STATUSES:
            await _insert(session, subject, status=status)

        response = await _erase(app, subject)

        assert await _statuses(session, subject) == {"hired"}
        # Ausgesetzt ist nicht übersprungen: der Ursprung erfährt, dass etwas
        # geblieben ist (ADR-0027 §3.4).
        assert response.json()["retained"] == 1

    async def test_a_rejected_application_still_falls(
        self, app: Any, session: AsyncSession
    ) -> None:
        subject = uuid4()
        await _insert(session, subject, status="rejected")

        await _erase(app, subject)

        assert await _statuses(session, subject) == set()
