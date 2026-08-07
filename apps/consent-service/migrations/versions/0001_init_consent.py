"""init consent_events audit_events

Revision ID: 0001_init_consent
Revises:
Create Date: 2026-07-31
"""

from __future__ import annotations

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from consent_service.domain.audit import AuditAction
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID

revision: str = "0001_init_consent"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "consent_events",
        sa.Column("id", sa.BigInteger(), primary_key=True, autoincrement=True),
        # Domain-side identity and the idempotency key: a retried write carrying
        # the same event_id hits this constraint instead of recording a duplicate
        # fact.
        sa.Column("event_id", PG_UUID(as_uuid=True), nullable=False, unique=True),
        sa.Column("subject_id", PG_UUID(as_uuid=True), nullable=False),
        sa.Column("capability", sa.Text(), nullable=False),
        sa.Column("action", sa.String(16), nullable=False),
        sa.Column("actor_id", PG_UUID(as_uuid=True), nullable=True),
        sa.Column("reason", sa.Text(), nullable=True),
        sa.Column("metadata", JSONB(), nullable=False, server_default=sa.text("'{}'::jsonb")),
        sa.Column("recorded_at", sa.DateTime(timezone=True), nullable=False),
        # Text + CHECK rather than a PG enum: adding an action later is a
        # constraint change, not an ALTER TYPE that locks the table.
        sa.CheckConstraint(
            "action IN ('GRANT','REVOKE','DELETE')", name="ck_consent_events_action"
        ),
    )
    # Serves the projection query verbatim: filter on (subject, capability),
    # newest row first, deterministic tie-break on event_id.
    op.create_index(
        "ix_consent_events_lookup",
        "consent_events",
        [
            "subject_id",
            "capability",
            sa.text("recorded_at DESC"),
            sa.text("event_id DESC"),
        ],
    )

    audit_action = sa.Enum(
        AuditAction,
        name="consent_audit_action",
        values_callable=lambda e: [m.value for m in e],
    )
    op.create_table(
        "audit_events",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("actor_id", PG_UUID(as_uuid=True), nullable=True, index=True),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=True, index=True),
        sa.Column("action", audit_action, nullable=False),
        sa.Column("target_id", PG_UUID(as_uuid=True), nullable=True),
        sa.Column("correlation_id", sa.String(64), nullable=True),
        sa.Column("occurred_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("metadata", JSONB(), nullable=False, server_default=sa.text("'{}'::jsonb")),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            nullable=False,
            server_default=sa.text("now()"),
        ),
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            nullable=False,
            server_default=sa.text("now()"),
        ),
    )


def downgrade() -> None:
    op.drop_table("audit_events")
    sa.Enum(name="consent_audit_action").drop(op.get_bind(), checkfirst=True)
    op.drop_index("ix_consent_events_lookup", table_name="consent_events")
    op.drop_table("consent_events")
