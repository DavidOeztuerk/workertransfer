"""Migration und Repository gegen echtes Postgres (ADR-0011: skippt ohne Docker)."""

from __future__ import annotations

from collections.abc import AsyncIterator
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import UUID, uuid4

import pytest
from alembic import command
from alembic.config import Config
from profile_service.domain.profile import Profile, Skills
from profile_service.infrastructure.database.repositories import (
    SqlAlchemyProfileRepository,
    decode_cursor,
)
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

_SERVICE_DIR = Path(__file__).resolve().parents[2]
NOW = datetime(2026, 8, 2, 12, 0, tzinfo=UTC)


@pytest.fixture
def migrated(postgres_url: str, monkeypatch: pytest.MonkeyPatch) -> None:
    """Frisches Schema je Test, unabhängig von der Reihenfolge im Lauf."""
    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    monkeypatch.setenv("WORKER_DATABASE_URL", postgres_url)
    command.downgrade(cfg, "base")
    command.upgrade(cfg, "head")


@pytest.fixture
async def session(postgres_url: str, migrated: None) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        yield s
    await engine.dispose()


def _profile(subject_id: UUID, *, headline: str = "Dev", when: datetime = NOW) -> Profile:
    return Profile.create(
        subject_id=subject_id,
        headline=headline,
        bio="Über mich",
        location="Berlin",
        remote_ok=True,
        skills=Skills(["Python", "Rust"]),
        now=when,
    )


async def test_save_then_get_roundtrips_every_field(session: AsyncSession) -> None:
    repo = SqlAlchemyProfileRepository(session)
    subject = uuid4()

    await repo.save(_profile(subject))
    await session.commit()

    found = await repo.get(subject)
    assert found is not None
    assert found.headline == "Dev"
    assert found.remote_ok is True
    # JSONB kommt als Liste zurück; das Wertobjekt macht wieder ein Tupel daraus.
    assert found.skills.value == ("Python", "Rust")


async def test_saving_twice_updates_instead_of_duplicating(session: AsyncSession) -> None:
    repo = SqlAlchemyProfileRepository(session)
    subject = uuid4()
    profile = _profile(subject)
    await repo.save(profile)
    await session.commit()

    later = NOW + timedelta(days=1)
    profile.update(
        headline="Staff",
        bio="neu",
        location="Hamburg",
        remote_ok=False,
        skills=Skills(["Go"]),
        now=later,
    )
    await repo.save(profile)
    await session.commit()

    found = await repo.get(subject)
    assert found is not None
    assert found.headline == "Staff"
    assert found.skills.value == ("Go",)
    # created_at bleibt der Anlagezeitpunkt — ein Update ist kein Neuanlegen.
    assert found.created_at == NOW


async def test_unknown_subject_returns_none(session: AsyncSession) -> None:
    repo = SqlAlchemyProfileRepository(session)

    assert await repo.get(uuid4()) is None


async def test_page_orders_by_last_change_and_pages_without_gaps(session: AsyncSession) -> None:
    repo = SqlAlchemyProfileRepository(session)
    subjects = [uuid4() for _ in range(5)]
    for index, subject in enumerate(subjects):
        await repo.save(
            _profile(subject, headline=f"P{index}", when=NOW + timedelta(minutes=index))
        )
    await session.commit()

    first, cursor = await repo.page(limit=2, cursor=None)
    assert [p.headline for p in first] == ["P4", "P3"]
    assert cursor is not None

    second, cursor2 = await repo.page(limit=2, cursor=cursor)
    assert [p.headline for p in second] == ["P2", "P1"]

    third, cursor3 = await repo.page(limit=2, cursor=cursor2)
    assert [p.headline for p in third] == ["P0"]
    # Letzte Seite: kein Cursor mehr, sonst blätterte der Aufrufer ins Leere.
    assert cursor3 is None


async def test_profiles_sharing_a_timestamp_are_not_skipped(session: AsyncSession) -> None:
    """Der Cursor trägt beide Sortierschlüssel — sonst überspringt er.

    Mit `updated_at` allein würde bei gleichem Zeitstempel entweder ein Profil
    verschluckt oder doppelt geliefert.
    """
    repo = SqlAlchemyProfileRepository(session)
    subjects = [uuid4() for _ in range(3)]
    for index, subject in enumerate(subjects):
        await repo.save(_profile(subject, headline=f"S{index}", when=NOW))
    await session.commit()

    seen: list[str] = []
    cursor: str | None = None
    for _ in range(3):
        page, cursor = await repo.page(limit=1, cursor=cursor)
        seen.extend(p.headline for p in page)
        if cursor is None:
            break

    assert sorted(seen) == ["S0", "S1", "S2"]


def test_a_broken_cursor_is_treated_as_the_beginning() -> None:
    # Cursor stehen in URLs und werden gekürzt und weitergereicht; ein 400 wäre
    # eine harte Antwort auf ein weiches Problem.
    assert decode_cursor("nicht-base64!!") is None
    assert decode_cursor("") is None
