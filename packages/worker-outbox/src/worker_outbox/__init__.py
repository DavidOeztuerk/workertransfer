"""Die Absicht wird dort festgehalten, wo die Änderung passiert — in derselben Transaktion.

**Das Problem, das es wirklich gibt.** Benachrichtigungen laufen heute als
„feuern und vergessen": ein HTTP-Aufruf, dessen Fehler geschluckt wird. Der
Grund dafür ist richtig und bleibt — ein Widerruf darf nicht scheitern, weil
eine Mail nicht rausging. Der Preis war aber, dass die Mail dann **für immer
weg** ist: identity-service startet gerade neu, das Netz zuckt, jemand
deployt — und niemand erfährt, dass sein Transfer angenommen wurde. Im
Protokoll steht eine Warnung, die keiner liest.

Die Outbox löst genau diesen Widerspruch, ohne ihn umzudrehen: die *Absicht*
wird in **derselben Transaktion** wie die fachliche Änderung geschrieben. Geht
die Änderung durch, liegt die Absicht fest; wird sie zurückgerollt, ist auch
die Absicht weg. Danach stellt ein Zusteller sie zu und darf dabei so oft
scheitern, wie er will — der Vorgang ist längst durch.

**Kein Broker.** Kein RabbitMQ, kein Kafka, kein NATS. Die drei standen als
`worker-messaging` im Repository: 129 Zeilen, fünf schwere Abhängigkeiten, drei
Umsetzungen, **null Konsumenten** — dieselbe Geschichte wie `worker-files`
(ADR-0021), `worker-github` (ADR-0022) und `worker-ai` (ADR-0024). Ein
Postgres, das jeder Dienst ohnehin betreibt, trägt eine Outbox mit einer
Tabelle (ADR-0025).

**Was hier NICHT hineingehört: Inhalt.** Die Nutzlast trägt eine `user_id` und
eine Art (`transfer.accepted`) — dieselben zwei Angaben, die der HTTP-Aufruf
heute schon schickt, und keine mehr. Eine Outbox ist ein *dauerhafter* Speicher:
was hier hineingerät, liegt danach in einer Tabelle, in einem Backup und in
jedem Dump. Freitext einer Person, Nachrichtentexte, Lebensläufe haben dort
nichts zu suchen (`product-scope.md`), und der Fehlerfall speichert nur die
**Art** des Fehlers, nie die Antwort des Gegenübers.

**Mindestens einmal, nicht genau einmal.** Ein Zusteller, der abstürzt,
nachdem er zugestellt, aber bevor er abgehakt hat, stellt erneut zu. Das ist
die ehrliche Zusage; „genau einmal" bekommt man über eine Dienstgrenze hinweg
nicht geschenkt. Der Empfänger muss doppelte Zustellungen vertragen — bei einer
Benachrichtigung heißt das im schlimmsten Fall eine Mail zweimal, und das ist
deutlich besser als keine.
"""

from __future__ import annotations

import logging
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Protocol
from uuid import UUID, uuid4

from sqlalchemy import Column, DateTime, Integer, String, Table, Uuid, select, update
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import DeclarativeBase

__all__ = [
    "MAX_ATTEMPTS",
    "Delivery",
    "OutboxDispatcher",
    "OutboxEntry",
    "build_outbox_table",
    "record",
]

_logger = logging.getLogger("workertransfer.outbox")

#: Nach so vielen vergeblichen Versuchen bleibt die Zeile liegen, statt ewig
#: wiederholt zu werden. Aufgegeben ist nicht gelöscht: sie steht weiter da und
#: ist abfragbar. Eine Zeile, die still verschwindet, ist genau der Zustand,
#: den diese Tabelle abschaffen soll.
MAX_ATTEMPTS = 10


def build_outbox_table(base: type[DeclarativeBase], *, name: str = "outbox") -> Table:
    """Die Tabelle, **je Dienst in seiner eigenen Datenbank**.

    Als Funktion und nicht als geteiltes Modell: es gibt keine gemeinsame
    Datenbank (ADR-0004), also gibt es auch keine gemeinsame Outbox. Jeder
    Dienst hängt sie an *seine* `Base`, damit sie in *seinen* Migrationen
    auftaucht — und `tests/test_migration_metadata.py` weiter aufgeht.
    """
    # Spalten bewusst schmal: id, Empfänger, Art, Zeitstempel, Zähler. Kein
    # Textfeld, in das später eine Nachricht rutschen könnte.
    return Table(
        name,
        base.metadata,
        # `Uuid` statt `postgresql.UUID`: auf Postgres rendert es denselben
        # nativen Typ, ist aber nicht an den Dialekt gebunden. Der Unterschied
        # ist nicht theoretisch — mit dem Postgres-Typ ließ sich diese Tabelle
        # gegen keine andere Datenbank prüfen, und ein Baustein, den man nur im
        # Vollausbau testen kann, wird seltener geprüft.
        Column("id", Uuid, primary_key=True),
        Column("user_id", Uuid, nullable=False),
        Column("kind", String(64), nullable=False),
        Column("created_at", DateTime(timezone=True), nullable=False, index=True),
        Column("attempts", Integer, nullable=False, default=0),
        # NULL heißt „steht noch aus" — die Spalte, auf der der Zusteller sucht.
        Column("delivered_at", DateTime(timezone=True), nullable=True, index=True),
        # Nur die ART des Fehlers (`ConnectError`), nie die Antwort des
        # Gegenübers und nie ein Inhalt.
        Column("last_error", String(120), nullable=False, default=""),
    )


@dataclass(frozen=True, slots=True)
class OutboxEntry:
    """Eine ausstehende Absicht, so wie der Zusteller sie sieht."""

    id: UUID
    user_id: UUID
    kind: str
    attempts: int


class Delivery(Protocol):
    """Was zustellt — HTTP, SMTP, was auch immer.

    Absichtlich dieselbe Form wie der heutige `Notifier.notify`, damit der
    bestehende Adapter unverändert weiterbenutzt werden kann. Der Unterschied
    ist nicht die Zustellung, sondern dass sie **wiederholbar** geworden ist.
    """

    async def notify(self, user_id: UUID, kind: str) -> None: ...


async def record(
    session: AsyncSession,
    table: Table,
    *,
    user_id: UUID,
    kind: str,
    now: datetime | None = None,
) -> UUID:
    """Schreibt die Absicht in die **laufende** Transaktion des Aufrufers.

    Kein eigener Commit, keine eigene Session — das ist der ganze Punkt. Würde
    hier eigenständig geschrieben, könnte die Benachrichtigung existieren,
    während die Änderung zurückgerollt wurde: jemand bekäme die Nachricht, sein
    Transfer sei angenommen, und in der Datenbank stünde nichts davon.
    """
    entry_id = uuid4()
    await session.execute(
        table.insert().values(
            id=entry_id,
            user_id=user_id,
            kind=kind,
            created_at=now or datetime.now(UTC),
            attempts=0,
            delivered_at=None,
            last_error="",
        )
    )
    return entry_id


class OutboxDispatcher:
    """Holt ausstehende Zeilen und stellt sie zu.

    Läuft **im Dienst selbst** als Hintergrundaufgabe, nicht als eigener
    Prozess: ein weiterer Dienst wäre ein weiteres Deployment, ein weiterer
    Gesundheitscheck und ein weiterer Ort zum Vergessen — für eine Schleife,
    die eine Tabelle liest.
    """

    def __init__(
        self,
        *,
        session_factory: Callable[[], AsyncSession],
        table: Table,
        delivery: Delivery,
        batch_size: int = 50,
        max_attempts: int = MAX_ATTEMPTS,
    ) -> None:
        self._session_factory = session_factory
        self._table = table
        self._delivery = delivery
        self._batch_size = batch_size
        self._max_attempts = max_attempts

    async def pending(self, session: AsyncSession) -> list[OutboxEntry]:
        rows = await session.execute(
            select(
                self._table.c.id,
                self._table.c.user_id,
                self._table.c.kind,
                self._table.c.attempts,
            )
            .where(self._table.c.delivered_at.is_(None))
            .where(self._table.c.attempts < self._max_attempts)
            # Älteste zuerst: eine Benachrichtigung, die überholt wird, kommt in
            # der falschen Reihenfolge an.
            .order_by(self._table.c.created_at)
            .limit(self._batch_size)
        )
        return [
            OutboxEntry(id=row.id, user_id=row.user_id, kind=row.kind, attempts=row.attempts)
            for row in rows
        ]

    async def drain_once(self) -> int:
        """Ein Durchlauf. Gibt zurück, wie viele wirklich zugestellt wurden."""
        delivered = 0
        async with self._session_factory() as session:
            entries = await self.pending(session)
            for entry in entries:
                if await self._deliver(session, entry):
                    delivered += 1
            await session.commit()
        return delivered

    async def _deliver(self, session: AsyncSession, entry: OutboxEntry) -> bool:
        try:
            await self._delivery.notify(entry.user_id, entry.kind)
        except Exception as exc:
            # Nur die Art, nie der Inhalt — und gekürzt, damit ein
            # geschwätziger Fehler die Spalte nicht sprengt.
            await session.execute(
                update(self._table)
                .where(self._table.c.id == entry.id)
                .values(attempts=entry.attempts + 1, last_error=type(exc).__name__[:120])
            )
            _logger.warning(
                "Zustellung fehlgeschlagen (%s, Versuch %s)",
                type(exc).__name__,
                entry.attempts + 1,
            )
            return False
        await session.execute(
            update(self._table)
            .where(self._table.c.id == entry.id)
            .values(delivered_at=datetime.now(UTC), attempts=entry.attempts + 1)
        )
        return True


async def run_forever(
    dispatcher: OutboxDispatcher,
    *,
    interval_seconds: float,
    sleep: Callable[[float], Awaitable[None]],
    should_stop: Callable[[], bool] = lambda: False,
) -> None:
    """Die Schleife — mit eingereichtem `sleep`, damit ein Test sie steuern kann.

    Ein `asyncio.sleep` fest verdrahtet hieße: entweder ein Test, der wirklich
    wartet, oder gar kein Test.
    """
    while not should_stop():
        try:
            await dispatcher.drain_once()
        except Exception:
            # Der Zusteller darf nie sterben. Stirbt er, bleibt die Tabelle
            # liegen und niemand merkt es — genau der Zustand von vorher.
            _logger.warning("Outbox-Durchlauf fehlgeschlagen", exc_info=True)
        await sleep(interval_seconds)
