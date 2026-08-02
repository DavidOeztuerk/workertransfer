"""Commands, Queries und ihre Handler.

Das eigene Portfolio gehört einem ohne Rückfrage; ein fremdes nur mit
Einwilligung — und die wird bei jedem Abruf frisch geholt (ADR-0013).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from portfolio_service.domain.portfolio import Portfolio, PortfolioItem

__all__ = [
    "GetPortfolioQuery",
    "PortfolioNotVisible",
    "SaveMyPortfolioCommand",
    "handle_get_my_portfolio",
    "handle_get_visible_portfolio",
    "handle_save_my_portfolio",
]


class PortfolioNotVisible(DomainError):
    """Nicht vorhanden ODER nicht freigegeben — von außen dasselbe (ADR-0020 §1)."""

    def __init__(self) -> None:
        super().__init__("portfolio_not_visible", "No such portfolio")


@dataclass(frozen=True, slots=True)
class SaveMyPortfolioCommand:
    subject_id: UUID
    items: list[PortfolioItem]


async def handle_save_my_portfolio(
    cmd: SaveMyPortfolioCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Portfolio]:
    now = deps["clock"].now()
    try:
        existing: Portfolio | None = await repos["portfolios"].get(cmd.subject_id)
        if existing is None:
            portfolio = Portfolio.create(cmd.subject_id, items=cmd.items, now=now)
        else:
            existing.update(items=cmd.items, now=now)
            portfolio = existing
        await repos["portfolios"].save(portfolio)
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(portfolio)


async def handle_get_my_portfolio(subject_id: UUID, *, repos: dict[str, Any]) -> Portfolio | None:
    """Kein `Result`: „noch keines angelegt" ist ein Zustand, kein Fehler."""
    portfolio: Portfolio | None = await repos["portfolios"].get(subject_id)
    return portfolio


@dataclass(frozen=True, slots=True)
class GetPortfolioQuery:
    subject_id: UUID
    #: Das Unternehmen des Aufrufers — aus dem Token, nie aus dem Request.
    tenant_id: UUID
    bearer: str


async def handle_get_visible_portfolio(
    query: GetPortfolioQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Portfolio]:
    portfolio: Portfolio | None = await repos["portfolios"].get(query.subject_id)
    if portfolio is None:
        # Kein Ledger-Aufruf für etwas, das es nicht gibt: unnötiger Round-Trip,
        # und er meldete dem Ledger geratene Subject-IDs.
        return Result.fail(PortfolioNotVisible())
    # ConsentUnavailable fliegt bewusst durch: der Router macht daraus 503.
    if not await deps["consent"].may_see(
        query.subject_id, tenant_id=query.tenant_id, bearer=query.bearer
    ):
        return Result.fail(PortfolioNotVisible())
    return Result.ok(portfolio)
