"""Die Löschung im github-service — an der Datenbank belegt (ADR-0027 §2).

`github_connections.id` **ist** die `subject_id`. Die Zeile fällt vollständig,
und zwar samt `login`: der GitHub-Name ist öffentlich, hier aber mit einem
Menschen verknüpft — und genau die Verknüpfung ist das Personendatum, nicht der
Name für sich.
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

        from github_service.configuration import GithubServiceSettings
        from github_service.presentation.compose_api import build_app

        yield build_app(GithubServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE github_connections"))
        await s.commit()
        yield s
    await engine.dispose()


async def _insert(session: AsyncSession, subject: UUID) -> None:
    await session.execute(
        text(
            "INSERT INTO github_connections (id, login, challenge, repositories, "
            "created_at, updated_at) VALUES (:id, 'oktokatze', 'abc', '[]', now(), now())"
        ),
        {"id": str(subject)},
    )
    await session.commit()


async def _connections(session: AsyncSession) -> set[UUID]:
    rows = await session.execute(text("SELECT id FROM github_connections"))
    return {row[0] for row in rows}


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def test_the_connection_row_is_really_gone(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _insert(session, subject)

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    assert await _connections(session) == set()


async def test_it_touches_nobody_else(app: Any, session: AsyncSession) -> None:
    mine, theirs = uuid4(), uuid4()
    await _insert(session, mine)
    await _insert(session, theirs)

    await _erase(app, mine)

    assert await _connections(session) == {theirs}


async def test_delivering_twice_changes_nothing(app: Any, session: AsyncSession) -> None:
    """ADR-0027 §4.2 — und beim zweiten Mal 2xx, nicht 404."""
    subject = uuid4()
    await _insert(session, subject)

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    assert await _connections(session) == set()


async def test_a_wrong_secret_looks_like_no_endpoint(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _insert(session, subject)

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    assert await _connections(session) == {subject}
