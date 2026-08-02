"""SQLAlchemy-Modelle für transfer-service."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import Boolean, DateTime, String, Text
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from transfer_service.infrastructure.database.base import Base

__all__ = ["MarketStatusModel"]


class MarketStatusModel(Base):
    """Ein Marktstatus je Person; `id` IST die subject_id."""

    __tablename__ = "market_status"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    availability: Mapped[str] = mapped_column(String(16), nullable=False)
    employed: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    note: Mapped[str] = mapped_column(Text, nullable=False, default="")
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
