"""Die Löschung im Ledger — der einzige Empfänger, bei dem etwas STEHEN BLEIBT.

Das Ledger ist der **Beleg**, dass gelöscht wurde. Es mitzulöschen hieße, die
Löschung unbeweisbar zu machen — und die Behauptung „wir haben gelöscht" gegen
nichts mehr prüfbar (ADR-0027 §5).

Was bleibt: `event_id`, `subject_id`, `capability`, `action`, `recorded_at`,
`actor_id` — die vollständige Kette aus Erteilungen, Widerrufen und, am Ende, je
einer `DELETE`-Zeile.

Was herausfällt: `reason` (→ NULL) und `metadata` (→ `{}`). Der Grund ist
Freitext, den ein Mensch über sich selbst geschrieben hat — das einzige wirklich
personenbezogene Feld hier, und der Beleg braucht es nicht: *dass* widerrufen
wurde, steht in `action`.

Das tragende Argument ist nicht „wir dürfen aufbewahren", sondern: **was
übrigbleibt, ist keine Auskunft über einen Menschen mehr.** Nach der Löschung
gibt es im ganzen System keine Abbildung `subject_id → Mensch`; zurück bleiben
UUIDs, Capability-Namen und Zeitstempel.
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
from worker_auth import TokenManager

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

        from consent_service.configuration import ConsentServiceSettings
        from consent_service.presentation.compose_api import build_app

        yield build_app(ConsentServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE consent_events, audit_events RESTART IDENTITY"))
        await s.commit()
        yield s
    await engine.dispose()


def _auth(subject: UUID) -> dict[str, str]:
    token = TokenManager(secret=JWT_SECRET).create_access_token(subject, None, ["user"], [])
    return {"Authorization": f"Bearer {token}"}


async def _grant(app: Any, subject: UUID, capability: str) -> None:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        response = await client.post(
            "/consent/grant",
            json={"subject_id": str(subject), "capability": capability},
            headers=_auth(subject),
        )
        assert response.status_code == 200, response.text


async def _revoke(app: Any, subject: UUID, capability: str, reason: str) -> None:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        response = await client.post(
            "/consent/revoke",
            json={"subject_id": str(subject), "capability": capability, "reason": reason},
            headers=_auth(subject),
        )
        assert response.status_code == 200, response.text


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def _events(session: AsyncSession, subject: UUID) -> list[Any]:
    rows = await session.execute(
        text(
            "SELECT capability, action, reason, metadata FROM consent_events "
            "WHERE subject_id = :s ORDER BY id"
        ),
        {"s": str(subject)},
    )
    return list(rows)


async def test_the_chain_of_facts_survives(app: Any, session: AsyncSession) -> None:
    """Ohne sie wäre die Behauptung „wir haben gelöscht" gegen nichts prüfbar."""
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")
    await _revoke(app, subject, "profile.visibility:public", "doch nicht")

    await _erase(app, subject)

    actions = [row.action for row in await _events(session, subject)]
    assert actions == ["GRANT", "REVOKE", "DELETE"]


async def test_the_free_text_reason_is_gone(app: Any, session: AsyncSession) -> None:
    """Der Grund ist das einzige wirklich personenbezogene Feld hier — und der
    Beleg braucht ihn nicht: *dass* widerrufen wurde, steht in `action`."""
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")
    await _revoke(app, subject, "profile.visibility:public", "mein Arbeitgeber liest mit")

    await _erase(app, subject)

    for row in await _events(session, subject):
        assert row.reason is None, row
        assert row.metadata == {}, row


async def test_one_delete_row_per_capability_ever_held(app: Any, session: AsyncSession) -> None:
    """Je Capability eine — auch für die, die längst widerrufen war.

    Die Kette soll für jede Erlaubnis, die dieser Mensch je hielt, mit demselben
    Satz enden.
    """
    subject = uuid4()
    tenant = uuid4()
    held = [
        "profile.visibility:public",
        "portfolio.visibility:public",
        f"resume.visibility:tenant:{tenant}",
    ]
    for capability in held:
        await _grant(app, subject, capability)
    await _revoke(app, subject, "portfolio.visibility:public", "nicht mehr")

    await _erase(app, subject)

    deleted = [row.capability for row in await _events(session, subject) if row.action == "DELETE"]
    assert sorted(deleted) == sorted(held)


async def test_a_delete_row_carries_no_reason(app: Any, session: AsyncSession) -> None:
    """Von einem Menschen, der sein Konto löschen will, eine Begründung zu
    verlangen, ist ein Hebel gegen ihn (ADR-0027 §Kontext 5)."""
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")

    await _erase(app, subject)

    rows = [row for row in await _events(session, subject) if row.action == "DELETE"]
    assert rows and all(row.reason is None for row in rows)


async def test_the_audit_metadata_is_emptied(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")
    before = await session.execute(
        text("SELECT metadata FROM audit_events WHERE target_id = :s"), {"s": str(subject)}
    )
    assert [row[0] for row in before] != [{}], "sonst prüft der Test nichts"

    await _erase(app, subject)

    rows = await session.execute(
        text("SELECT metadata FROM audit_events WHERE target_id = :s"), {"s": str(subject)}
    )
    entries = [row[0] for row in rows]
    assert entries and all(entry == {} for entry in entries)


async def test_the_audit_row_itself_stays(app: Any, session: AsyncSession) -> None:
    """Keine Kaskade mit `users` — das ist eine ausdrückliche Entscheidung
    (ADR-0012). Was bleibt: `action`, `occurred_at`, `correlation_id` und die
    Kennungen als Pseudonym."""
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")

    await _erase(app, subject)

    rows = await session.execute(
        text("SELECT count(*) FROM audit_events WHERE target_id = :s"), {"s": str(subject)}
    )
    assert rows.scalar_one() >= 1


async def test_delivering_twice_adds_no_second_round(app: Any, session: AsyncSession) -> None:
    """„Mindestens einmal" trifft hier auf einen Vorgang, der ANHÄNGT.

    Ein zweites `DELETE` je Capability wäre kein Schaden, aber eine Unwahrheit:
    die Person hat einmal gelöscht, nicht zweimal.
    """
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    deleted = [row for row in await _events(session, subject) if row.action == "DELETE"]
    assert len(deleted) == 1


async def test_it_touches_nobody_else(app: Any, session: AsyncSession) -> None:
    mine, theirs = uuid4(), uuid4()
    await _grant(app, mine, "profile.visibility:public")
    await _grant(app, theirs, "profile.visibility:public")
    await _revoke(app, theirs, "profile.visibility:public", "mein guter Grund")

    await _erase(app, mine)

    others = await _events(session, theirs)
    assert [row.action for row in others] == ["GRANT", "REVOKE"]
    assert others[1].reason == "mein guter Grund"


async def test_a_wrong_secret_looks_like_no_endpoint(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _grant(app, subject, "profile.visibility:public")

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    assert [row.action for row in await _events(session, subject)] == ["GRANT"]
