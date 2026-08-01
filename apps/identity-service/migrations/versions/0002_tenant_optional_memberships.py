"""tenant becomes optional; company membership moves to its own table

A tenant is a company and a natural person has none (ADR-0017), so `users` stops
carrying one. Membership becomes a relation, because one person may act for
several companies.

Email uniqueness has to move with it. `uq_users_tenant_email` cannot simply be
relaxed: with a nullable `tenant_id` Postgres treats NULLs as distinct, so the
same address could register without limit. Email is therefore globally unique
now — one person, one account.

Revision ID: 0002_tenant_optional_memberships
Revises: 0001_init_users_sessions_audit
Create Date: 2026-08-01
"""

from __future__ import annotations

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects.postgresql import UUID as PG_UUID

revision: str = "0002_tenant_optional_memberships"
down_revision: str | None = "0001_init_users_sessions_audit"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # Switching into a company is a privilege change and is audited, so the enum
    # needs the labels first. ADD VALUE is allowed inside a transaction on PG 12+
    # as long as the value is not *used* in the same one — it is not.
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'tenant_switch'")
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'tenant_switch_denied'")

    op.create_table(
        "user_tenant_memberships",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column(
            "user_id",
            PG_UUID(as_uuid=True),
            sa.ForeignKey("users.id", ondelete="CASCADE"),
            nullable=False,
            index=True,
        ),
        sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=False, index=True),
        sa.Column(
            "granted_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
        sa.UniqueConstraint("user_id", "tenant_id", name="uq_membership_user_tenant"),
    )

    # Carry existing rows over before the column disappears. Every user today has
    # a NOT NULL tenant_id, so this preserves exactly the associations that were
    # implied by the old model — nothing is invented and nothing is dropped.
    op.execute(
        """
        INSERT INTO user_tenant_memberships (id, user_id, tenant_id, granted_at)
        SELECT gen_random_uuid(), id, tenant_id, created_at FROM users
        """
    )

    # Must precede dropping the composite constraint: if two tenants share an
    # address, this fails loudly here rather than silently keeping duplicates
    # that the new model cannot express.
    op.create_unique_constraint("uq_users_email", "users", ["email"])
    op.drop_constraint("uq_users_tenant_email", "users", type_="unique")
    op.drop_column("users", "tenant_id")

    # A session belongs to a person; a tenant is only active when one was
    # switched to (POST /auth/tenant/{id}).
    op.alter_column("sessions", "tenant_id", existing_type=PG_UUID(as_uuid=True), nullable=True)
    # Actions by a person carry no tenant, and that is the honest value.
    op.alter_column("audit_events", "tenant_id", existing_type=PG_UUID(as_uuid=True), nullable=True)


def downgrade() -> None:
    # Rebuilding users.tenant_id needs a single tenant per user. Anyone with
    # several memberships has no correct answer, so the pick is arbitrary — this
    # direction is lossy by nature, not by oversight.
    op.add_column("users", sa.Column("tenant_id", PG_UUID(as_uuid=True), nullable=True))
    op.execute(
        """
        UPDATE users SET tenant_id = sub.tenant_id
        FROM (
            SELECT DISTINCT ON (user_id) user_id, tenant_id
            FROM user_tenant_memberships ORDER BY user_id, granted_at
        ) AS sub
        WHERE users.id = sub.user_id
        """
    )
    # Users who never had a membership cannot be restored to a NOT NULL column.
    op.execute("DELETE FROM users WHERE tenant_id IS NULL")
    op.alter_column("users", "tenant_id", existing_type=PG_UUID(as_uuid=True), nullable=False)
    op.create_index("ix_users_tenant_id", "users", ["tenant_id"])

    op.drop_constraint("uq_users_email", "users", type_="unique")
    op.create_unique_constraint("uq_users_tenant_email", "users", ["tenant_id", "email"])

    op.execute("DELETE FROM sessions WHERE tenant_id IS NULL")
    op.alter_column("sessions", "tenant_id", existing_type=PG_UUID(as_uuid=True), nullable=False)
    op.execute("DELETE FROM audit_events WHERE tenant_id IS NULL")
    op.alter_column(
        "audit_events", "tenant_id", existing_type=PG_UUID(as_uuid=True), nullable=False
    )

    op.drop_table("user_tenant_memberships")
