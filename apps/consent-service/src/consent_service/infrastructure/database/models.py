"""SQLAlchemy 2 models for consent-service (Postgres-native types).

`consent_events` is append-only. No mixin adds an `updated_at` column, because
nothing updates a row — the absence is the point, not an oversight.
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID, uuid4

from sqlalchemy import BigInteger, CheckConstraint, DateTime, Enum, Index, String, Text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column
from worker_database import TimestampMixin

from consent_service.domain.audit import AuditAction
from consent_service.infrastructure.database.base import Base

__all__ = ["AuditEventModel", "ConsentEventModel"]


class ConsentEventModel(Base):
    __tablename__ = "consent_events"

    # A monotonic surrogate key makes the append order readable at a glance;
    # event_id is the domain identity and the idempotency key.
    id: Mapped[int] = mapped_column(BigInteger, primary_key=True, autoincrement=True)
    event_id: Mapped[UUID] = mapped_column(
        PG_UUID(as_uuid=True), nullable=False, unique=True, default=uuid4
    )
    subject_id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), nullable=False)
    capability: Mapped[str] = mapped_column(Text, nullable=False)
    # Plain text plus a CHECK constraint rather than a PG enum: adding an action
    # later is a constraint change, not an ALTER TYPE that locks the table.
    action: Mapped[str] = mapped_column(String(16), nullable=False)
    actor_id: Mapped[UUID | None] = mapped_column(PG_UUID(as_uuid=True), nullable=True)
    reason: Mapped[str | None] = mapped_column(Text, nullable=True)
    # `metadata` is reserved on DeclarativeBase (Base.metadata is the MetaData
    # object), so the Python attribute is `meta` and the column keeps the name.
    meta: Mapped[dict[str, str]] = mapped_column("metadata", JSONB, nullable=False, default=dict)
    recorded_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)

    __table_args__ = (
        CheckConstraint("action IN ('GRANT','REVOKE','DELETE')", name="ck_consent_events_action"),
        # Serves the projection query verbatim: filter by (subject, capability),
        # take the newest row, tie-break on event_id.
        Index(
            "ix_consent_events_lookup",
            "subject_id",
            "capability",
            recorded_at.desc(),
            event_id.desc(),
        ),
    )


class AuditEventModel(Base, TimestampMixin):
    __tablename__ = "audit_events"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True, default=uuid4)
    actor_id: Mapped[UUID | None] = mapped_column(PG_UUID(as_uuid=True), nullable=True, index=True)
    tenant_id: Mapped[UUID | None] = mapped_column(PG_UUID(as_uuid=True), nullable=True, index=True)
    action: Mapped[AuditAction] = mapped_column(
        Enum(
            AuditAction,
            name="consent_audit_action",
            values_callable=lambda e: [m.value for m in e],
        ),
        nullable=False,
    )
    target_id: Mapped[UUID | None] = mapped_column(PG_UUID(as_uuid=True), nullable=True)
    correlation_id: Mapped[str | None] = mapped_column(String(64), nullable=True)
    occurred_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    meta: Mapped[dict[str, str]] = mapped_column("metadata", JSONB, nullable=False, default=dict)
