"""Was eine Löschzustellung anders braucht als eine Benachrichtigung (ADR-0027 §4).

Drei Zusagen, und jede kehrt eine Voreinstellung um, die für eine Mail richtig
ist:

1. **Aufgeben ist nicht erlaubt.** `MAX_ATTEMPTS` lässt die Zeile nach zehn
   Fehlversuchen liegen — für eine Benachrichtigung richtig, für eine Löschung
   das stille Scheitern, gegen das die ganze Konstruktion antritt.
2. **„Noch nicht dran" ist kein Fehlschlag.** Die Reihenfolge aus ADR-0027 §6
   (erst die Empfänger, dann die Abschlussnachricht, dann das Konto) ist
   *erzwungen*, nicht empfohlen. Ein Zusteller, der eine Zeile zurückstellt,
   darf dafür keinen Versuch verbrauchen und keinen Fehler eintragen — sonst
   sähe geordnetes Warten wie ein kaputter Empfänger aus.
3. **Ein toter Empfänger darf nicht zum Dauerfeuer werden.** Ohne
   Versuchsobergrenze braucht es wachsenden Abstand, sonst hämmert ein Dienst,
   der seit Tagen nicht antwortet, im Sekundentakt gegen die Wand.
"""

from __future__ import annotations

from uuid import UUID

import pytest
from sqlalchemy import select
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase
from worker_outbox import (
    MAX_ATTEMPTS,
    Deferred,
    OutboxDispatcher,
    build_outbox_table,
    record,
    run_with_backoff,
)

USER = UUID("22222222-2222-2222-2222-222222222222")


class Base(DeclarativeBase):
    pass


OUTBOX = build_outbox_table(Base)


@pytest.fixture
async def sessions():  # type: ignore[no-untyped-def]
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as connection:
        await connection.run_sync(Base.metadata.create_all)
    yield async_sessionmaker(engine, expire_on_commit=False)
    await engine.dispose()


class Dead:
    """Ein Empfänger, der nie antwortet. Genau der Fall aus ADR-0027 §4."""

    def __init__(self) -> None:
        self.attempts = 0

    async def notify(self, user_id: UUID, kind: str) -> None:
        self.attempts += 1
        raise ConnectionError("Dienst antwortet dauerhaft nicht")


class NotYet:
    """Stellt zurück, statt zu scheitern — bis die Vorbedingung erfüllt ist."""

    def __init__(self, *, defer_times: int) -> None:
        self.calls = 0
        self._defer_times = defer_times

    async def notify(self, user_id: UUID, kind: str) -> None:
        self.calls += 1
        if self.calls <= self._defer_times:
            raise Deferred("die Empfänger haben noch nicht quittiert")


async def _row(sessions):  # type: ignore[no-untyped-def]
    async with sessions() as session:
        return (
            await session.execute(
                select(OUTBOX.c.attempts, OUTBOX.c.last_error, OUTBOX.c.delivered_at)
            )
        ).one()


async def _one_pending(sessions) -> None:  # type: ignore[no-untyped-def]
    async with sessions() as session:
        await record(session, OUTBOX, user_id=USER, kind="erasure:profile")
        await session.commit()


class TestGivingUpIsNotAllowed:
    async def test_without_a_ceiling_it_keeps_trying_past_the_normal_limit(
        self,
        sessions,  # type: ignore[no-untyped-def]
    ) -> None:
        """Die Zeile bleibt fällig, auch nach dem zwanzigsten Versuch.

        Mit der Voreinstellung filtert `pending()` sie nach zehn Versuchen
        heraus: sie steht noch da, wird aber **nie wieder versucht**. Bei einer
        Löschung wäre das eine Zusage, die niemand mehr einlöst.
        """
        await _one_pending(sessions)
        dead = Dead()
        dispatcher = OutboxDispatcher(
            session_factory=sessions, table=OUTBOX, delivery=dead, max_attempts=None
        )

        for _ in range(MAX_ATTEMPTS + 5):
            assert await dispatcher.drain_once() == 0

        assert dead.attempts == MAX_ATTEMPTS + 5
        row = await _row(sessions)
        assert row.attempts == MAX_ATTEMPTS + 5
        # Und niemals zugestellt — das ist die Zusage, auf der der ganze
        # Nachweis aus ADR-0027 §4 steht.
        assert row.delivered_at is None

    async def test_the_default_still_gives_up(self, sessions) -> None:  # type: ignore[no-untyped-def]
        """Die Umkehrung gilt nur, wo sie ausdrücklich verlangt wurde.

        Für eine Mail bleibt „liegenlassen statt ewig wiederholen" richtig
        (ADR-0025). Ohne diesen Test wäre nicht zu sehen, ob die neue Fassung
        die alte Voreinstellung mitgerissen hat.
        """
        await _one_pending(sessions)
        dead = Dead()
        dispatcher = OutboxDispatcher(session_factory=sessions, table=OUTBOX, delivery=dead)

        for _ in range(MAX_ATTEMPTS + 5):
            await dispatcher.drain_once()

        assert dead.attempts == MAX_ATTEMPTS


class TestDeferredIsNotAFailure:
    async def test_a_deferred_row_spends_no_attempt_and_records_no_error(
        self,
        sessions,  # type: ignore[no-untyped-def]
    ) -> None:
        """Sonst wäre geordnetes Warten von einem kaputten Empfänger nicht zu
        unterscheiden — und mit Obergrenze würde die Reihenfolge die Löschung
        selbst abwürgen."""
        await _one_pending(sessions)
        waiting = NotYet(defer_times=3)
        dispatcher = OutboxDispatcher(session_factory=sessions, table=OUTBOX, delivery=waiting)

        for _ in range(3):
            assert await dispatcher.drain_once() == 0

        row = await _row(sessions)
        assert row.attempts == 0
        assert row.last_error == ""
        assert row.delivered_at is None

        # Und sobald die Vorbedingung steht, geht sie ganz normal raus.
        assert await dispatcher.drain_once() == 1
        assert (await _row(sessions)).delivered_at is not None


class TestTheIntervalGrowsOnlyAgainstAWall:
    """Der Abstand wächst gegen eine **Wand**, nicht gegen **Ruhe**.

    Der Unterschied ist keine Feinheit, und er hat diese Fassung eine
    E2E-Reise gekostet: die erste Umsetzung verlängerte den Takt, sobald ein
    Durchlauf nichts zustellte — und das ist bei einer leeren Tabelle IMMER
    der Fall. Auf einem ruhigen System stand der Zusteller damit nach wenigen
    Minuten auf der Obergrenze, und die nächste Löschung fing bis zu fünf
    Minuten lang gar nicht erst an. Gemessen, nicht vermutet: die Kaskade
    lief, sie lief nur zu spät.

    „Nichts zu tun" ist kein Fehlschlag. Zurückgehalten wird nur, wenn etwas
    fällig war und **nichts davon** durchging.
    """

    async def test_an_idle_queue_never_slows_the_next_erasure_down(self) -> None:
        """Der Fehler, den die E2E-Reise gefunden hat.

        Ein Mensch, der auf einem ruhigen System löscht, darf nicht warten,
        weil vorher niemand gelöscht hat.
        """
        slept: list[float] = []

        class Idle:
            async def drain_detailed(self) -> tuple[int, int]:
                # nichts war fällig, nichts ging raus
                return (0, 0)

        async def fake_sleep(seconds: float) -> None:
            slept.append(seconds)

        await run_with_backoff(
            Idle(),  # type: ignore[arg-type]
            interval_seconds=1.0,
            max_interval_seconds=8.0,
            sleep=fake_sleep,
            should_stop=lambda: len(slept) >= 5,
        )

        assert slept == [1.0, 1.0, 1.0, 1.0, 1.0]

    async def test_a_dead_recipient_is_retried_ever_more_slowly(self) -> None:
        slept: list[float] = []

        class Wall:
            async def drain_detailed(self) -> tuple[int, int]:
                # eine Zeile war fällig, keine ging raus
                return (1, 0)

        async def fake_sleep(seconds: float) -> None:
            slept.append(seconds)

        await run_with_backoff(
            Wall(),  # type: ignore[arg-type]
            interval_seconds=1.0,
            max_interval_seconds=8.0,
            sleep=fake_sleep,
            should_stop=lambda: len(slept) >= 6,
        )

        # Verdoppelt, bis die Obergrenze erreicht ist — und dann nicht weiter.
        assert slept == [2.0, 4.0, 8.0, 8.0, 8.0, 8.0]

    async def test_a_successful_delivery_puts_the_pace_back(self) -> None:
        """Nach einem Erfolg wieder eng: sonst zahlt die nächste Löschung den
        Preis dafür, dass die vorige auf einen toten Dienst gewartet hat."""
        slept: list[float] = []
        results = iter([(1, 0), (1, 0), (1, 1), (1, 0)])

        class Sometimes:
            async def drain_detailed(self) -> tuple[int, int]:
                return next(results, (0, 0))

        async def fake_sleep(seconds: float) -> None:
            slept.append(seconds)

        await run_with_backoff(
            Sometimes(),  # type: ignore[arg-type]
            interval_seconds=1.0,
            max_interval_seconds=8.0,
            sleep=fake_sleep,
            should_stop=lambda: len(slept) >= 4,
        )

        assert slept == [2.0, 4.0, 1.0, 2.0]
