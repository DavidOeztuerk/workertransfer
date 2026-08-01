"""SQLAlchemy-Modelle für profile-service."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import Boolean, DateTime, Text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from profile_service.infrastructure.database.base import Base

__all__ = ["ProfileModel"]


class ProfileModel(Base):
    __tablename__ = "profiles"

    # id IST die subject_id aus dem Token — ein Profil je Person.
    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    headline: Mapped[str] = mapped_column(Text, nullable=False)
    bio: Mapped[str] = mapped_column(Text, nullable=False, default="")
    location: Mapped[str] = mapped_column(Text, nullable=False, default="")
    remote_ok: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    skills: Mapped[list[str]] = mapped_column(JSONB, nullable=False, default=list)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
