"""SQLAlchemy-Modelle für resume-service."""

from __future__ import annotations

from datetime import datetime
from typing import Any
from uuid import UUID

from sqlalchemy import DateTime, Text, UniqueConstraint
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column
from worker_outbox import build_outbox_table

from resume_service.infrastructure.database.base import Base

__all__ = ["OUTBOX", "ResumeModel", "ResumeRequestModel"]

#: Die Outbox — an DIESE `Base`, damit sie in DIESEN Migrationen auftaucht.
#: Es gibt keine gemeinsame Datenbank, also auch keine gemeinsame Outbox
#: (ADR-0004/0025).
OUTBOX = build_outbox_table(Base)


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


class ResumeRequestModel(Base):
    """Die Anfrage eines Unternehmens nach einem Lebenslauf.

    Der Unique-Index auf (subject_id, tenant_id) ist die Regel „einmal fragen":
    eine Ablehnung wäre wirkungslos, wenn dasselbe Unternehmen danach erneut
    fragen dürfte. Wer dreimal fragen darf, hat kein Nein bekommen, sondern eine
    Verzögerung. In der Datenbank statt nur im Handler, weil zwei gleichzeitige
    Anfragen sonst beide durch die Prüfung kämen.
    """

    __tablename__ = "resume_requests"
    __table_args__ = (
        UniqueConstraint("subject_id", "tenant_id", name="uq_request_subject_tenant"),
    )

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    subject_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    tenant_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False, index=True)
    requested_by: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False)
    status: Mapped[str] = mapped_column(Text, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    answered_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
