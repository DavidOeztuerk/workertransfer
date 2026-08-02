"""SQLAlchemy-Modelle für transfer-service."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import BigInteger, Boolean, DateTime, String, Text
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from transfer_service.infrastructure.database.base import Base

__all__ = ["MarketRequestModel", "MarketStatusModel", "TransferModel"]


class MarketStatusModel(Base):
    """Ein Marktstatus je Person; `id` IST die subject_id."""

    __tablename__ = "market_status"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    availability: Mapped[str] = mapped_column(String(16), nullable=False)
    employed: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    note: Mapped[str] = mapped_column(Text, nullable=False, default="")
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)


class TransferModel(Base):
    """Ein Transfer-Vorgang zwischen einer Person und einem Unternehmen."""

    __tablename__ = "transfers"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    subject_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    requires_release: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    release_confirmed: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    message: Mapped[str] = mapped_column(Text, nullable=False, default="")
    offer_note: Mapped[str] = mapped_column(Text, nullable=False, default="")
    offer_start_on: Mapped[str | None] = mapped_column(String(7), nullable=True)
    offer_fee_cents: Mapped[int | None] = mapped_column(BigInteger, nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)


class MarketRequestModel(Base):
    """Die Anfrage eines Unternehmens nach einem Marktstatus.

    Kein `revoked_at`: der Widerruf lebt im Ledger. Ihn hier zu spiegeln hieße,
    zwei Wahrheiten über dieselbe Frage zu führen.
    """

    __tablename__ = "market_requests"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    subject_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    requested_by: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    answered_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
