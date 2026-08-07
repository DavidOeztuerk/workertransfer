"""Was die Outbox verspricht — und was sie ausdrücklich nicht verspricht.

Gegen SQLite im Speicher, nicht gegen Postgres: die interessanten Zusagen
(dieselbe Transaktion, Wiederholung, kein Inhalt in der Tabelle) hängen an
SQLAlchemy und nicht am Dialekt. Der Dialekt-Teil wird im Dienst geprüft, wo
die Migration wirklich läuft.
"""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import UUID

import pytest
from sqlalchemy import select
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase
from worker_outbox import (
    MAX_ATTEMPTS,
    OutboxDispatcher,
    build_outbox_table,
    record,
    run_forever,
)

USER = UUID("11111111-1111-1111-1111-111111111111")


class Base(DeclarativeBase):
    pass


OUTBOX = build_outbox_table(Base)


@pytest.fixture
async def sessions():  # type: ignore[no-untyped-def]
    # Geht nur, weil die Tabelle `sqlalchemy.Uuid` benutzt statt des
    # Postgres-Typs: mit `postgresql.UUID` kam der Wert hier als Rohwert
    # zurück und `UUID(...)` scheiterte. Der dialektfreie Typ rendert auf
    # Postgres denselben nativen Typ — und lässt sich zusätzlich hier prüfen.
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as connection:
        await connection.run_sync(Base.metadata.create_all)
    yield async_sessionmaker(engine, expire_on_commit=False)
    await engine.dispose()


class Recorder:
    """Ein Zusteller, der mitschreibt — und auf Wunsch scheitert."""

    def __init__(self, *, fail_times: int = 0) -> None:
        self.calls: list[tuple[UUID, str]] = []
        self._fail_times = fail_times

    async def notify(self, user_id: UUID, kind: str) -> None:
        if len(self.calls) < self._fail_times:
            self.calls.append((user_id, kind))
            raise ConnectionError("provider unreachable")
        self.calls.append((user_id, kind))


async def _pending_count(sessions) -> int:  # type: ignore[no-untyped-def]
    async with sessions() as session:
        rows = await session.execute(select(OUTBOX.c.id).where(OUTBOX.c.delivered_at.is_(None)))
        return len(rows.all())


class TestTheSameTransaction:
    async def test_a_rolled_back_change_takes_the_intent_with_it(self, sessions) -> None:  # type: ignore[no-untyped-def]
        """Der Kern. Ohne diese Zusage wäre die Outbox schlimmer als der Status quo.

        Jemand bekäme die Nachricht „dein Transfer wurde angenommen", während
        in der Datenbank nichts davon steht — eine Lüge, die aus einem
        Optimierungsversuch entsteht.
        """
        async with sessions() as session:
            await record(session, OUTBOX, user_id=USER, kind="transfer.accepted")
            await session.rollback()

        assert await _pending_count(sessions) == 0

    async def test_a_committed_change_keeps_the_intent(self, sessions) -> None:  # type: ignore[no-untyped-def]
        async with sessions() as session:
            await record(session, OUTBOX, user_id=USER, kind="transfer.accepted")
            await session.commit()

        assert await _pending_count(sessions) == 1


class TestDelivery:
    async def test_it_delivers_and_stops_offering_the_same_row(self, sessions) -> None:  # type: ignore[no-untyped-def]
        async with sessions() as session:
            await record(session, OUTBOX, user_id=USER, kind="transfer.accepted")
            await session.commit()

        recorder = Recorder()
        dispatcher = OutboxDispatcher(session_factory=sessions, table=OUTBOX, delivery=recorder)

        assert await dispatcher.drain_once() == 1
        assert recorder.calls == [(USER, "transfer.accepted")]
        # Der zweite Durchlauf darf sie NICHT noch einmal anfassen.
        assert await dispatcher.drain_once() == 0
        assert len(recorder.calls) == 1

    async def test_a_failure_keeps_the_row_for_the_next_run(self, sessions) -> None:  # type: ignore[no-untyped-def]
        """Genau das, was die heutige „feuern und vergessen"-Fassung nicht kann.

        Dort ist die Benachrichtigung nach einem Netzzucken für immer weg.
        """
        async with sessions() as session:
            await record(session, OUTBOX, user_id=USER, kind="transfer.accepted")
            await session.commit()

        recorder = Recorder(fail_times=1)
        dispatcher = OutboxDispatcher(session_factory=sessions, table=OUTBOX, delivery=recorder)

        assert await dispatcher.drain_once() == 0
        assert await _pending_count(sessions) == 1
        # Beim nächsten Lauf klappt es — ohne dass jemand etwas tun musste.
        assert await dispatcher.drain_once() == 1
        assert await _pending_count(sessions) == 0

    async def test_it_gives_up_eventually_but_never_deletes(self, sessions) -> None:  # type: ignore[no-untyped-def]
        """Aufgeben heißt liegenlassen, nicht wegräumen.

        Eine Zeile, die still verschwindet, ist der Zustand, den diese Tabelle
        abschaffen soll — dann wüsste wieder niemand, dass etwas fehlt.
        """
        async with sessions() as session:
            await record(session, OUTBOX, user_id=USER, kind="transfer.accepted")
            await session.commit()

        dispatcher = OutboxDispatcher(
            session_factory=sessions, table=OUTBOX, delivery=Recorder(fail_times=999)
        )
        for _ in range(MAX_ATTEMPTS + 3):
            await dispatcher.drain_once()

        async with sessions() as session:
            row = (await session.execute(select(OUTBOX.c.attempts, OUTBOX.c.last_error))).one()
        assert row.attempts == MAX_ATTEMPTS
        # Nur die ART des Fehlers, nie eine Antwort des Gegenübers.
        assert row.last_error == "ConnectionError"
        assert "unreachable" not in row.last_error

    async def test_the_oldest_goes_first(self, sessions) -> None:  # type: ignore[no-untyped-def]
        # Eine überholte Benachrichtigung kommt in der falschen Reihenfolge an:
        # „Angebot zurückgezogen" vor „Angebot gemacht".
        old = datetime(2026, 1, 1, tzinfo=UTC)
        new = datetime(2026, 6, 1, tzinfo=UTC)
        async with sessions() as session:
            await record(session, OUTBOX, user_id=USER, kind="zweitens", now=new)
            await record(session, OUTBOX, user_id=USER, kind="erstens", now=old)
            await session.commit()

        recorder = Recorder()
        await OutboxDispatcher(
            session_factory=sessions, table=OUTBOX, delivery=recorder
        ).drain_once()

        assert [kind for _, kind in recorder.calls] == ["erstens", "zweitens"]


class TestTheTableHoldsNoContent:
    def test_there_is_no_column_a_message_could_slip_into(self) -> None:
        """Eine Outbox ist ein DAUERHAFTER Speicher — sie steht danach in jedem Backup.

        Deshalb dieselbe Strenge wie bei `DraftContext`: was es nicht gibt,
        kann nicht hineingeraten. Ein `payload`- oder `body`-Feld wäre die
        Einladung, beim nächsten Feature den Nachrichtentext mitzuschreiben.
        """
        assert set(OUTBOX.c.keys()) == {
            "id",
            "user_id",
            "kind",
            "created_at",
            "attempts",
            "delivered_at",
            "last_error",
        }


class TestTheLoopSurvives:
    async def test_a_broken_run_does_not_kill_the_dispatcher(self, sessions) -> None:  # type: ignore[no-untyped-def]
        """Stirbt die Schleife, bleibt die Tabelle liegen — und niemand merkt es."""
        ticks = 0

        class Exploding(OutboxDispatcher):
            async def drain_once(self) -> int:
                raise RuntimeError("Datenbank weg")

        async def fake_sleep(_seconds: float) -> None:
            nonlocal ticks
            ticks += 1

        await run_forever(
            Exploding(session_factory=sessions, table=OUTBOX, delivery=Recorder()),
            interval_seconds=0.0,
            sleep=fake_sleep,
            should_stop=lambda: ticks >= 3,
        )

        assert ticks == 3
