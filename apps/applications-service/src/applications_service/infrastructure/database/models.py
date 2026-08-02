"""SQLAlchemy-Modelle für applications-service."""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import Boolean, DateTime, String, Text, UniqueConstraint
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from applications_service.infrastructure.database.base import Base

__all__ = ["ApplicationModel"]


class ApplicationModel(Base):
    """Eine Bewerbung. Enthält KEINE Profildaten — nur einen Verweis."""

    __tablename__ = "applications"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    job_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    subject_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    message: Mapped[str] = mapped_column(Text, nullable=False, default="")
    shares_resume: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    shares_portfolio: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    answered_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)

    __table_args__ = (
        # Genau eine Bewerbung je (Person, Stelle). Zweimal auf dieselbe Stelle
        # zu bewerben ist kein Ausdruck von Interesse, sondern ein Versehen —
        # und in der Datenbank, nicht nur im Handler, damit zwei gleichzeitige
        # Absendungen nicht beide durchkommen.
        UniqueConstraint("job_id", "subject_id", name="uq_application_job_subject"),
    )
