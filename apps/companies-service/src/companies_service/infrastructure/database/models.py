"""SQLAlchemy-Modelle für companies-service."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import DateTime, Text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from companies_service.infrastructure.database.base import Base

__all__ = ["CompanyProfileModel"]


class CompanyProfileModel(Base):
    """Ein Profil je Unternehmen; `id` IST die tenant_id."""

    __tablename__ = "company_profiles"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    display_name: Mapped[str] = mapped_column(Text, nullable=False)
    about: Mapped[str] = mapped_column(Text, nullable=False, default="")
    website: Mapped[str | None] = mapped_column(Text, nullable=True)
    # Listen als JSONB: sie werden nur als Ganzes gelesen und geschrieben, und
    # es gibt keine Abfrage über einzelne Einträge.
    locations: Mapped[list[str]] = mapped_column(JSONB, nullable=False, default=list)
    benefits: Mapped[list[str]] = mapped_column(JSONB, nullable=False, default=list)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
