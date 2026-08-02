"""SQLAlchemy-Umsetzung der Marktstatus-Ports."""

from __future__ import annotations

from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession

from transfer_service.domain.market_status import Availability, MarketStatus
from transfer_service.infrastructure.database.models import MarketStatusModel

__all__ = ["SqlAlchemyMarketStatusRepository"]


def _to_domain(row: MarketStatusModel) -> MarketStatus:
    return MarketStatus(
        subject_id=row.id,
        availability=Availability(row.availability),
        employed=row.employed,
        note=row.note,
        created_at=row.created_at,
        updated_at=row.updated_at,
    )


class SqlAlchemyMarketStatusRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, subject_id: UUID) -> MarketStatus | None:
        row = await self._session.get(MarketStatusModel, subject_id)
        return None if row is None else _to_domain(row)

    async def save(self, status: MarketStatus) -> None:
        row = await self._session.get(MarketStatusModel, status.subject_id)
        if row is None:
            row = MarketStatusModel(
                id=status.subject_id,
                availability=str(status.availability),
                created_at=status.created_at,
                updated_at=status.updated_at,
            )
            self._session.add(row)
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.availability = str(status.availability)
        row.employed = status.employed
        row.note = status.note
        row.updated_at = status.updated_at
