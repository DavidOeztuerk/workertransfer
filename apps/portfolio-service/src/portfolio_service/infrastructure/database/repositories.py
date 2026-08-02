"""SQLAlchemy-Umsetzung der Portfolio-Ports."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession

from portfolio_service.domain.portfolio import Portfolio, PortfolioItem
from portfolio_service.infrastructure.database.models import PortfolioModel

__all__ = ["SqlAlchemyPortfolioRepository"]


def _to_json(entry: PortfolioItem) -> dict[str, Any]:
    return {
        "title": entry.title,
        "summary": entry.summary,
        "url": entry.url,
        "role": entry.role,
        "year": entry.year,
    }


def _from_json(raw: dict[str, Any]) -> PortfolioItem:
    return PortfolioItem(
        title=raw["title"],
        summary=raw.get("summary", ""),
        url=raw.get("url"),
        role=raw.get("role", ""),
        year=raw.get("year"),
    )


def _to_domain(row: PortfolioModel) -> Portfolio:
    # Geht durch `create`, nicht am Konstruktor vorbei: die Regeln sollen auch
    # für gespeicherte Zeilen gelten, damit eine von Hand veränderte Zeile nicht
    # unbemerkt durchrutscht.
    portfolio = Portfolio.create(
        row.id, items=[_from_json(entry) for entry in row.items], now=row.updated_at
    )
    portfolio.created_at = row.created_at
    return portfolio


class SqlAlchemyPortfolioRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, subject_id: UUID) -> Portfolio | None:
        row = await self._session.get(PortfolioModel, subject_id)
        return None if row is None else _to_domain(row)

    async def save(self, portfolio: Portfolio) -> None:
        row = await self._session.get(PortfolioModel, portfolio.subject_id)
        if row is None:
            row = PortfolioModel(
                id=portfolio.subject_id,
                created_at=portfolio.created_at,
                updated_at=portfolio.updated_at,
                items=[],
            )
            self._session.add(row)
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.items = [_to_json(entry) for entry in portfolio.items]
        row.updated_at = portfolio.updated_at
