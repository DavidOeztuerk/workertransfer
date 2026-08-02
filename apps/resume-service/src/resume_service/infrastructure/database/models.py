"""SQLAlchemy-Modelle für resume-service."""

from __future__ import annotations

from datetime import datetime
from typing import Any
from uuid import UUID

from sqlalchemy import DateTime
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from resume_service.infrastructure.database.base import Base

__all__ = ["ResumeModel"]


class ResumeModel(Base):
    """Ein Lebenslauf je Person.

    Stationen und Ausbildung liegen als JSONB, nicht als eigene Tabellen: sie
    werden ausschließlich als Ganzes gelesen und als Ganzes geschrieben, es gibt
    keine Abfrage über einzelne Stationen, und das Aggregat garantiert seine
    Invarianten (höchstens eine laufende Station, Reihenfolge) nur, wenn es
    vollständig durch die Domäne geht. Zwei Kindtabellen brächten Joins und
    Teil-Updates für einen Zugriff, den es nicht gibt.
    """

    __tablename__ = "resumes"

    # id IST die subject_id aus dem Token.
    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    positions: Mapped[list[dict[str, Any]]] = mapped_column(JSONB, nullable=False, default=list)
    education: Mapped[list[dict[str, Any]]] = mapped_column(JSONB, nullable=False, default=list)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
