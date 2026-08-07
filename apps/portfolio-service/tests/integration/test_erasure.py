"""Die Löschung im portfolio-service — dem einzigen Dienst mit Dateien.

Geprüft wird **am Speicher**, nicht an der Portfolio-Antwort (ADR-0027
Verifikation): eine leere Antwort beweist nur, dass die Zeile weg ist. Die
Datei, auf die sie zeigte, liegt davon unberührt weiter im Dateisystem — und
genau das ist der Fehler, den man hier nicht sieht, wenn man ihn nicht sucht.

**Reihenfolge umgekehrt zum Hochladen.** Der Upload committet zuerst und räumt
danach auf, weil ein fehlgeschlagener Commit sonst Dateien löscht, auf die
gültige Einträge zeigen. Beim Löschen gilt das Gegenteil: erst die Dateien, dann
die Zeile. Bricht es dazwischen ab, zeigen Einträge ins Leere und der nächste
Zustellversuch räumt sie weg — andersherum bliebe der Inhalt liegen, den
niemand mehr referenziert und deshalb auch niemand mehr findet.
"""

from __future__ import annotations

import json
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
def storage_root(tmp_path_factory: pytest.TempPathFactory) -> Path:
    return tmp_path_factory.mktemp("attachments")


@pytest.fixture(scope="module")
def app(postgres_url: str, storage_root: Path) -> Iterator[Any]:
    patch = pytest.MonkeyPatch()
    try:
        cfg = Config()
        cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        patch.setenv("WORKER_JWT_SECRET", JWT_SECRET)
        patch.setenv("WORKER_ERASURE_SECRET", SECRET)
        patch.setenv("WORKER_STORAGE_ROOT", str(storage_root))
        command.downgrade(cfg, "base")
        command.upgrade(cfg, "head")

        from portfolio_service.configuration import PortfolioServiceSettings
        from portfolio_service.presentation.compose_api import build_app

        yield build_app(PortfolioServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE portfolios"))
        await s.commit()
        yield s
    await engine.dispose()


async def _insert(session: AsyncSession, subject: UUID, *, attachment: str | None) -> None:
    items = [{"title": "Ein Projekt", "description": "", "url": None, "attachment": attachment}]
    await session.execute(
        text(
            "INSERT INTO portfolios (id, items, created_at, updated_at) "
            "VALUES (:id, CAST(:items AS jsonb), now(), now())"
        ),
        {"id": str(subject), "items": json.dumps(items)},
    )
    await session.commit()


def _store(root: Path, subject: UUID, name: str) -> Path:
    directory = root / str(subject)
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / name
    path.write_bytes(b"ein Anhang")
    return path


async def _portfolios(session: AsyncSession) -> set[UUID]:
    rows = await session.execute(text("SELECT id FROM portfolios"))
    return {row[0] for row in rows}


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def test_the_row_and_every_attachment_are_gone(
    app: Any, session: AsyncSession, storage_root: Path
) -> None:
    subject = uuid4()
    await _insert(session, subject, attachment="bild.png")
    first = _store(storage_root, subject, "bild.png")
    second = _store(storage_root, subject, "zweites.png")

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    assert await _portfolios(session) == set()
    assert not first.exists()
    # Auch der Anhang, auf den KEIN Eintrag zeigt: eine Waise unter dem Namen
    # dieses Menschen ist genauso seine Datei.
    assert not second.exists()


async def test_the_empty_directory_goes_too(
    app: Any, session: AsyncSession, storage_root: Path
) -> None:
    """Die Spur, die man übersieht: ein leeres Verzeichnis, das nach einer
    `subject_id` heißt, sagt immer noch „diesen Menschen gab es hier"."""
    subject = uuid4()
    await _insert(session, subject, attachment="bild.png")
    _store(storage_root, subject, "bild.png")

    await _erase(app, subject)

    assert not (storage_root / str(subject)).exists()


async def test_it_touches_nobody_elses_files(
    app: Any, session: AsyncSession, storage_root: Path
) -> None:
    mine, theirs = uuid4(), uuid4()
    await _insert(session, mine, attachment="bild.png")
    await _insert(session, theirs, attachment="bild.png")
    _store(storage_root, mine, "bild.png")
    ours = _store(storage_root, theirs, "bild.png")

    await _erase(app, mine)

    assert await _portfolios(session) == {theirs}
    assert ours.exists()


async def test_a_person_without_attachments_erases_cleanly(app: Any, session: AsyncSession) -> None:
    """Kein Anhang ist kein Sonderfall — und darf kein Fehler sein."""
    subject = uuid4()
    await _insert(session, subject, attachment=None)

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    assert await _portfolios(session) == set()


async def test_delivering_twice_changes_nothing(
    app: Any, session: AsyncSession, storage_root: Path
) -> None:
    subject = uuid4()
    await _insert(session, subject, attachment="bild.png")
    _store(storage_root, subject, "bild.png")

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    assert await _portfolios(session) == set()


async def test_a_wrong_secret_looks_like_no_endpoint(
    app: Any, session: AsyncSession, storage_root: Path
) -> None:
    subject = uuid4()
    await _insert(session, subject, attachment="bild.png")
    kept = _store(storage_root, subject, "bild.png")

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    assert await _portfolios(session) == {subject}
    assert kept.exists()
