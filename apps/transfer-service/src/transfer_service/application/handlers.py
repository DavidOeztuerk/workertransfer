"""Commands, Queries und ihre Handler."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from transfer_service.domain.market_status import Availability, MarketStatus
from transfer_service.domain.transfer import Transfer

__all__ = [
    "AlreadyRunning",
    "ExpressInterestCommand",
    "GetMarketStatusQuery",
    "MakeOfferCommand",
    "NotApproachable",
    "SaveMarketStatusCommand",
    "StatusNotVisible",
    "TransferNotFound",
    "handle_company_action",
    "handle_express_interest",
    "handle_get_my_status",
    "handle_get_visible_status",
    "handle_list_for_subject",
    "handle_list_for_tenant",
    "handle_make_offer",
    "handle_person_action",
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


class TransferNotFound(DomainError):
    """Nicht vorhanden ODER nicht deins — von außen dasselbe."""

    def __init__(self) -> None:
        super().__init__("transfer_not_found", "No such transfer")


class NotApproachable(DomainError):
    """Kein Status, keine Freigabe, oder `unavailable` — alles dasselbe nach außen.

    Sonst wäre der Endpunkt ein Orakel darüber, wer auf der Plattform ist und
    wer gerade zuhört.
    """

    def __init__(self) -> None:
        super().__init__("not_approachable", "No such person")


class AlreadyRunning(DomainError):
    """Ein zweiter laufender Vorgang wäre Nachfassen an der Ablehnung vorbei."""

    def __init__(self) -> None:
        super().__init__("already_running", "There is already a running transfer")


@dataclass(frozen=True, slots=True)
class ExpressInterestCommand:
    subject_id: UUID
    tenant_id: UUID
    message: str
    bearer: str


async def handle_express_interest(
    cmd: ExpressInterestCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Transfer]:
    status: MarketStatus | None = await repos["market"].get(cmd.subject_id)
    if status is None:
        return Result.fail(NotApproachable())
    # ConsentUnavailable fliegt bewusst durch: der Router macht daraus 503.
    if not await deps["consent"].may_see(
        cmd.subject_id, tenant_id=cmd.tenant_id, bearer=cmd.bearer
    ):
        return Result.fail(NotApproachable())
    if not status.is_approachable:
        # Die Freigabe erlaubt zu sehen, nicht zu stören.
        return Result.fail(NotApproachable())
    if await repos["transfers"].find_running(cmd.subject_id, cmd.tenant_id) is not None:
        return Result.fail(AlreadyRunning())

    try:
        transfer = Transfer.express_interest(
            subject_id=cmd.subject_id,
            tenant_id=cmd.tenant_id,
            # Aus dem Marktstatus kopiert, damit eine spätere Änderung die
            # Bedingungen eines laufenden Vorgangs nicht rückwirkend verschiebt.
            requires_release=status.employed,
            message=cmd.message,
            now=deps["clock"].now(),
        )
    except DomainError as exc:
        return Result.fail(exc)
    await repos["transfers"].add(transfer)
    return Result.ok(transfer)


async def _owned(
    transfer_id: UUID, *, subject_id: UUID | None, tenant_id: UUID | None, repos: dict[str, Any]
) -> Transfer | None:
    transfer: Transfer | None = await repos["transfers"].get(transfer_id)
    if transfer is None:
        return None
    if subject_id is not None and transfer.subject_id != subject_id:
        return None
    if tenant_id is not None and transfer.tenant_id != tenant_id:
        return None
    return transfer


async def handle_person_action(
    transfer_id: UUID,
    subject_id: UUID,
    action: str,
    *,
    deps: dict[str, Any],
    repos: dict[str, Any],
) -> Result[Transfer]:
    transfer = await _owned(transfer_id, subject_id=subject_id, tenant_id=None, repos=repos)
    if transfer is None:
        return Result.fail(TransferNotFound())
    now = deps["clock"].now()
    try:
        getattr(transfer, action)(by=subject_id, now=now)
    except DomainError as exc:
        return Result.fail(exc)
    await repos["transfers"].save(transfer)
    return Result.ok(transfer)


@dataclass(frozen=True, slots=True)
class MakeOfferCommand:
    transfer_id: UUID
    tenant_id: UUID
    note: str
    start_on: str | None
    fee_cents: int | None


async def handle_make_offer(
    cmd: MakeOfferCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Transfer]:
    transfer = await _owned(cmd.transfer_id, subject_id=None, tenant_id=cmd.tenant_id, repos=repos)
    if transfer is None:
        return Result.fail(TransferNotFound())
    try:
        transfer.make_offer(
            note=cmd.note,
            start_on=cmd.start_on,
            fee_cents=cmd.fee_cents,
            now=deps["clock"].now(),
        )
    except DomainError as exc:
        return Result.fail(exc)
    await repos["transfers"].save(transfer)
    return Result.ok(transfer)


async def handle_company_action(
    transfer_id: UUID,
    tenant_id: UUID,
    action: str,
    *,
    deps: dict[str, Any],
    repos: dict[str, Any],
) -> Result[Transfer]:
    transfer = await _owned(transfer_id, subject_id=None, tenant_id=tenant_id, repos=repos)
    if transfer is None:
        return Result.fail(TransferNotFound())
    try:
        getattr(transfer, action)(now=deps["clock"].now())
    except DomainError as exc:
        return Result.fail(exc)
    await repos["transfers"].save(transfer)
    return Result.ok(transfer)


async def handle_list_for_subject(subject_id: UUID, *, repos: dict[str, Any]) -> list[Transfer]:
    transfers: list[Transfer] = await repos["transfers"].for_subject(subject_id)
    return transfers


async def handle_list_for_tenant(tenant_id: UUID, *, repos: dict[str, Any]) -> list[Transfer]:
    transfers: list[Transfer] = await repos["transfers"].for_tenant(tenant_id)
    return transfers
