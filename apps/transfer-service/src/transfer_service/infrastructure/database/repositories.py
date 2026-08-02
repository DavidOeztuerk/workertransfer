"""SQLAlchemy-Umsetzung der Marktstatus-Ports."""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from transfer_service.domain.market_status import Availability, MarketStatus
from transfer_service.domain.request import MarketRequest, RequestStatus
from transfer_service.domain.transfer import Transfer, TransferStatus
from transfer_service.infrastructure.database.models import (
    MarketRequestModel,
    MarketStatusModel,
    TransferModel,
)

__all__ = [
    "SqlAlchemyMarketRequestRepository",
    "SqlAlchemyMarketStatusRepository",
    "SqlAlchemyTransferRepository",
]


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


def _transfer_to_domain(row: TransferModel) -> Transfer:
    return Transfer(
        id=row.id,
        subject_id=row.subject_id,
        tenant_id=row.tenant_id,
        status=TransferStatus(row.status),
        requires_release=row.requires_release,
        release_confirmed=row.release_confirmed,
        message=row.message,
        offer_note=row.offer_note,
        offer_start_on=row.offer_start_on,
        offer_fee_cents=row.offer_fee_cents,
        created_at=row.created_at,
        updated_at=row.updated_at,
    )


class SqlAlchemyTransferRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, transfer_id: UUID) -> Transfer | None:
        row = await self._session.get(TransferModel, transfer_id)
        return None if row is None else _transfer_to_domain(row)

    async def find_running(self, subject_id: UUID, tenant_id: UUID) -> Transfer | None:
        stmt = select(TransferModel).where(
            TransferModel.subject_id == subject_id,
            TransferModel.tenant_id == tenant_id,
            TransferModel.status.in_(["interested", "talking", "offered", "accepted"]),
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _transfer_to_domain(row)

    async def add(self, transfer: Transfer) -> None:
        self._session.add(
            TransferModel(
                id=transfer.id,
                subject_id=transfer.subject_id,
                tenant_id=transfer.tenant_id,
                status=str(transfer.status),
                requires_release=transfer.requires_release,
                release_confirmed=transfer.release_confirmed,
                message=transfer.message,
                offer_note=transfer.offer_note,
                offer_start_on=transfer.offer_start_on,
                offer_fee_cents=transfer.offer_fee_cents,
                created_at=transfer.created_at,
                updated_at=transfer.updated_at,
            )
        )

    async def save(self, transfer: Transfer) -> None:
        row = await self._session.get(TransferModel, transfer.id)
        if row is None:
            await self.add(transfer)
            return
        # Alle veränderlichen Felder schreiben.
        row.status = str(transfer.status)
        row.release_confirmed = transfer.release_confirmed
        row.offer_note = transfer.offer_note
        row.offer_start_on = transfer.offer_start_on
        row.offer_fee_cents = transfer.offer_fee_cents
        row.updated_at = transfer.updated_at

    async def for_subject(self, subject_id: UUID) -> list[Transfer]:
        return await self._listed(TransferModel.subject_id == subject_id)

    async def for_tenant(self, tenant_id: UUID) -> list[Transfer]:
        return await self._listed(TransferModel.tenant_id == tenant_id)

    async def _listed(self, condition: object) -> list[Transfer]:
        stmt = (
            select(TransferModel)
            .where(condition)  # type: ignore[arg-type]
            .order_by(TransferModel.created_at.desc())
        )
        return [_transfer_to_domain(row) for row in (await self._session.execute(stmt)).scalars()]


def _request_to_domain(row: MarketRequestModel) -> MarketRequest:
    return MarketRequest(
        id=row.id,
        subject_id=row.subject_id,
        tenant_id=row.tenant_id,
        requested_by=row.requested_by,
        status=RequestStatus(row.status),
        created_at=row.created_at,
        answered_at=row.answered_at,
    )


class SqlAlchemyMarketRequestRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, request_id: UUID) -> MarketRequest | None:
        row = await self._session.get(MarketRequestModel, request_id)
        return None if row is None else _request_to_domain(row)

    async def find(self, subject_id: UUID, tenant_id: UUID) -> MarketRequest | None:
        stmt = select(MarketRequestModel).where(
            MarketRequestModel.subject_id == subject_id,
            MarketRequestModel.tenant_id == tenant_id,
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _request_to_domain(row)

    async def add(self, request: MarketRequest) -> None:
        self._session.add(
            MarketRequestModel(
                id=request.id,
                subject_id=request.subject_id,
                tenant_id=request.tenant_id,
                requested_by=request.requested_by,
                status=str(request.status),
                created_at=request.created_at,
                answered_at=request.answered_at,
            )
        )

    async def save(self, request: MarketRequest) -> None:
        row = await self._session.get(MarketRequestModel, request.id)
        if row is None:
            await self.add(request)
            return
        # Alle veränderlichen Felder schreiben.
        row.status = str(request.status)
        row.answered_at = request.answered_at

    async def for_subject(self, subject_id: UUID) -> list[MarketRequest]:
        return await self._listed(MarketRequestModel.subject_id == subject_id)

    async def for_tenant(self, tenant_id: UUID) -> list[MarketRequest]:
        return await self._listed(MarketRequestModel.tenant_id == tenant_id)

    async def _listed(self, condition: object) -> list[MarketRequest]:
        stmt = (
            select(MarketRequestModel)
            .where(condition)  # type: ignore[arg-type]
            .order_by(MarketRequestModel.created_at.desc())
        )
        return [_request_to_domain(row) for row in (await self._session.execute(stmt)).scalars()]
