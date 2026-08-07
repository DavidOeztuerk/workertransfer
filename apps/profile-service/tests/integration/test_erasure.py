"""Die Löschung im profile-service — belegt an der Datenbank, nicht an einem 200.

Der Unterschied ist der Punkt der ganzen ADR-0027: ein Endpunkt, der „erledigt"
sagt, ohne etwas zu tun, ist genau der Zustand, den ROADMAP 10.5 als Täuschung
beschreibt. Deshalb fragt jeder Test hier die Zeile selbst ab.

`profiles.id` **ist** die `subject_id` — die Zeile fällt vollständig. Es gibt in
diesem Dienst nichts, was einem anderen gehört (ADR-0027 §2).
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

#: Eine Schleife fürs ganze Modul. Die App wird einmal gebaut und hält einen
#: asyncpg-Pool; der bindet an die Schleife seiner Erzeugung. Mit einer
#: Schleife je Test spräche der zweite Test über die Verbindungen des ersten.
pytestmark = pytest.mark.asyncio(loop_scope="module")


@pytest.fixture(scope="module")
def apps(postgres_url: str) -> Iterator[tuple[Any, Any]]:
    """Einmal migrieren, zwei Apps: eine mit Geheimnis, eine ohne."""
    patch = pytest.MonkeyPatch()
    try:
        cfg = Config()
        cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        patch.setenv("WORKER_JWT_SECRET", JWT_SECRET)
        command.downgrade(cfg, "base")
        command.upgrade(cfg, "head")

        from profile_service.configuration import ProfileServiceSettings
        from profile_service.presentation.compose_api import build_app

        patch.setenv("WORKER_ERASURE_SECRET", SECRET)
        guarded = build_app(ProfileServiceSettings())
        patch.setenv("WORKER_ERASURE_SECRET", "")
        shut = build_app(ProfileServiceSettings())
        yield guarded, shut
    finally:
        patch.undo()


@pytest.fixture
def app(apps: tuple[Any, Any]) -> Any:
    return apps[0]


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, apps: tuple[Any, Any]) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE profiles"))
        await s.commit()
        yield s
    await engine.dispose()


async def _insert_profile(session: AsyncSession, subject: UUID) -> None:
    await session.execute(
        text(
            "INSERT INTO profiles (id, headline, bio, location, remote_ok, skills, "
            "created_at, updated_at) VALUES (:id, 'Entwicklerin', 'Über mich', "
            "'Berlin', true, '[\"Python\"]', now(), now())"
        ),
        {"id": str(subject)},
    )
    await session.commit()


async def _profiles(session: AsyncSession) -> set[UUID]:
    rows = await session.execute(text("SELECT id FROM profiles"))
    return {row[0] for row in rows}


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def test_the_profile_row_is_really_gone(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _insert_profile(session, subject)
    assert await _profiles(session) == {subject}

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    assert await _profiles(session) == set()


async def test_it_touches_nobody_else(app: Any, session: AsyncSession) -> None:
    """Eine Kaskade, die zu viel mitnimmt, ist schlimmer als eine, die zu wenig
    tut: das Zuwenig lässt sich nachholen."""
    mine, theirs = uuid4(), uuid4()
    await _insert_profile(session, mine)
    await _insert_profile(session, theirs)

    await _erase(app, mine)

    assert await _profiles(session) == {theirs}


async def test_delivering_twice_changes_nothing(app: Any, session: AsyncSession) -> None:
    """„Mindestens einmal" heißt hier: zweimal löschen (ADR-0027 §4.2).

    Und die zweite Zustellung MUSS 2xx sein, nicht 404 — ein 404 sähe für den
    Zusteller wie ein Fehlschlag aus, und er würde ewig wiederholen, was längst
    erledigt ist.
    """
    subject = uuid4()
    await _insert_profile(session, subject)

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    assert await _profiles(session) == set()


async def test_nothing_is_retained_by_default(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _insert_profile(session, subject)

    response = await _erase(app, subject)

    assert response.json()["retained"] == 0


async def test_a_wrong_secret_looks_like_no_endpoint(app: Any, session: AsyncSession) -> None:
    """404 statt 401 — ein 401 bestätigt, dass es den Endpunkt gibt.

    Dasselbe Muster wie bei `/notifications`, aber mit einem **anderen**
    Schlüssel: „darf eine Mail anstoßen" und „darf alles über einen Menschen
    löschen" dürfen nicht dasselbe Papier sein (ADR-0027 §4.4).
    """
    subject = uuid4()
    await _insert_profile(session, subject)

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    assert await _profiles(session) == {subject}


async def test_without_a_configured_secret_the_endpoint_is_shut(
    apps: tuple[Any, Any], session: AsyncSession
) -> None:
    """Leer heißt zu. Eine Voreinstellung, die im Zweifel öffnet, wäre hier die
    denkbar schlechteste: der Endpunkt löscht alles über einen Menschen."""
    _guarded, shut = apps
    subject = uuid4()
    await _insert_profile(session, subject)

    response = await _erase(shut, subject, secret="")

    assert response.status_code == 404
    assert await _profiles(session) == {subject}
