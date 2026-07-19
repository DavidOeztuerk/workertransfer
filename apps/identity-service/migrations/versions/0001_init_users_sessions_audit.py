"""init users sessions audit_events

Revision ID: 0001_init_users_sessions_audit
Revises:
Create Date: 2026-07-16
"""

from __future__ import annotations

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from identity_service.domain.audit import AuditAction
from identity_service.domain.user import AccountStatus
from sqlalchemy.dialects.postgresql import CITEXT, JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID

revision: str = "0001_init_users_sessions_audit"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    account_status = sa.Enum(
        AccountStatus, name="account_status", values_callable=lambda e: [m.value for m in e]
    )
    audit_action = sa.Enum(
        AuditAction, name="audit_action", values_callable=lambda e: [m.value for m in e]
    )
    op.create_table(
        "users",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("email", CITEXT, nullable=False),
        sa.Column("password_hash", sa.Text, nullable=False),
        sa.Column("display_name", sa.String(255), nullable=False),
        sa.Column(
            "status", account_status, nullable=False, server_default=AccountStatus.ACTIVE.value
        ),
        sa.Column("roles", JSONB, nullable=False, server_default=sa.text("'[]'::jsonb")),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
        sa.Column(
            "updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
        sa.Column("version", sa.Integer, nullable=False, server_default="1"),
        sa.UniqueConstraint("tenant_id", "email", name="uq_users_tenant_email"),
    )

    op.create_table(
        "sessions",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column(
            "user_id",
            PG_UUID(as_uuid=True),
            sa.ForeignKey("users.id", ondelete="CASCADE"),
            nullable=False,
            index=True,
        ),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("refresh_jti", sa.String(128), nullable=False, unique=True),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("revoked_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
        sa.Column(
            "updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )

    op.create_table(
        "audit_events",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("actor_id", PG_UUID(as_uuid=True), nullable=True, index=True),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("action", audit_action, nullable=False),
        sa.Column("target_id", PG_UUID(as_uuid=True), nullable=True),
        sa.Column("correlation_id", sa.String(64), nullable=True),
        sa.Column("occurred_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("metadata", JSONB, nullable=False, server_default=sa.text("'{}'::jsonb")),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
        sa.Column(
            "updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )


def downgrade() -> None:
    op.drop_table("audit_events")
    op.drop_table("sessions")
    op.drop_table("users")
    sa.Enum(name="audit_action").drop(op.get_bind(), checkfirst=True)
    sa.Enum(name="account_status").drop(op.get_bind(), checkfirst=True)
