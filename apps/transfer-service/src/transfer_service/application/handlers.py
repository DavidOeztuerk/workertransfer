"""Commands, Queries und ihre Handler."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from transfer_service.domain.market_status import Availability, MarketStatus

__all__ = [
    "GetMarketStatusQuery",
    "SaveMarketStatusCommand",
    "StatusNotVisible",
    "handle_get_my_status",
    "handle_get_visible_status",
    "handle_save_status",
]


class StatusNotVisible(DomainError):
    """Nicht vorhanden ODER nicht freigegeben — von außen dasselbe.

    Hier zählt das doppelt: schon die Existenz der Aussage „diese Person hört
    zu" kann Schaden anrichten.
    """

    def __init__(self) -> None:
        super().__init__("status_not_visible", "No such market status")


@dataclass(frozen=True, slots=True)
class SaveMarketStatusCommand:
    subject_id: UUID
    availability: str
    employed: bool
    note: str


async def handle_save_status(
    cmd: SaveMarketStatusCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[MarketStatus]:
    now = deps["clock"].now()
    availability = Availability(cmd.availability)
    try:
        existing: MarketStatus | None = await repos["market"].get(cmd.subject_id)
        if existing is None:
            status = MarketStatus.create(
                cmd.subject_id,
                availability=availability,
                employed=cmd.employed,
                note=cmd.note,
                now=now,
            )
        else:
            existing.update(
                availability=availability, employed=cmd.employed, note=cmd.note, now=now
            )
            status = existing
        await repos["market"].save(status)
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(status)


async def handle_get_my_status(
    subject_id: UUID, *, deps: dict[str, Any], repos: dict[str, Any]
) -> MarketStatus:
    """Nie `null`.

    Anders als beim Profil: dort ist „noch keins" ein leeres Formular, hier ist
    „nichts gesagt" ein echter Zustand mit einer Bedeutung — nicht verfügbar.
    Ein `null` würde die Oberfläche zwingen, sich eine Voreinstellung
    auszudenken, und die Gefahr ist, dass sie sich die falsche ausdenkt.
    """
    existing: MarketStatus | None = await repos["market"].get(subject_id)
    if existing is not None:
        return existing
    return MarketStatus.default_for(subject_id, now=deps["clock"].now())


@dataclass(frozen=True, slots=True)
class GetMarketStatusQuery:
    subject_id: UUID
    tenant_id: UUID
    bearer: str


async def handle_get_visible_status(
    query: GetMarketStatusQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[MarketStatus]:
    status: MarketStatus | None = await repos["market"].get(query.subject_id)
    if status is None:
        # Kein Ledger-Aufruf für etwas, das es nicht gibt: unnötiger Round-Trip,
        # und er meldete dem Ledger geratene Subject-IDs.
        return Result.fail(StatusNotVisible())
    # ConsentUnavailable fliegt bewusst durch: der Router macht daraus 503.
    if not await deps["consent"].may_see(
        query.subject_id, tenant_id=query.tenant_id, bearer=query.bearer
    ):
        return Result.fail(StatusNotVisible())
    return Result.ok(status)
