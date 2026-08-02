"""SQLAlchemy-Modelle für jobs-service."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import DateTime, Index, String, Text
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from jobs_service.infrastructure.database.base import Base

__all__ = ["JobModel"]


class JobModel(Base):
    """Eine Stellenausschreibung. Der Tenant ist hier die Achse (ADR-0017)."""

    __tablename__ = "jobs"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    title: Mapped[str] = mapped_column(Text, nullable=False)
    description: Mapped[str] = mapped_column(Text, nullable=False)
    location: Mapped[str] = mapped_column(Text, nullable=False, default="")
    remote: Mapped[str] = mapped_column(String(16), nullable=False)
    employment: Mapped[str] = mapped_column(String(16), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    published_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)

    __table_args__ = (
        # Der Index der öffentlichen Suche: Status zuerst (fast alle Zeilen
        # fallen darüber weg), dann die Sortierung, die der Cursor benutzt.
        Index("ix_jobs_public", "status", "published_at", "id"),
    )
