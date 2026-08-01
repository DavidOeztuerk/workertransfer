"""email verification tokens, tenants table, membership role + fk

Revision ID: 0003_verification_and_companies
Revises: 0002_tenant_optional_memberships
Create Date: 2026-08-01
"""

from __future__ import annotations

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects.postgresql import CITEXT
from sqlalchemy.dialects.postgresql import UUID as PG_UUID

revision: str = "0003_verification_and_companies"
down_revision: str | None = "0002_tenant_optional_memberships"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # Verifying an email or creating a company is audited, so the enum needs the
    # labels first. ADD VALUE is allowed inside a transaction on PG 12+ as long as
    # the value is not *used* in the same one — it is not (see 0002 for precedent).
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'email_verified'")
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'company_created'")

    op.create_table(
        "tenants",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("name", sa.Text, nullable=False),
        # Die Domain IST der Nachweis: eindeutig, damit sie nur einmal
        # beansprucht werden kann. CITEXT, weil Domains case-insensitiv sind.
        sa.Column("domain", CITEXT, nullable=False, unique=True),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )

    op.create_table(
        "email_verification_tokens",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column(
            "user_id",
            PG_UUID(as_uuid=True),
            sa.ForeignKey("users.id", ondelete="CASCADE"),
            nullable=False,
        ),
        # sha256 hex — nie der Klartext.
        sa.Column("token_hash", sa.String(64), nullable=False, unique=True),
        sa.Column("purpose", sa.String(32), nullable=False),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("consumed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )
    op.create_index(
        "ix_verification_user_purpose", "email_verification_tokens", ["user_id", "purpose"]
    )

    op.add_column(
        "user_tenant_memberships",
        sa.Column("role", sa.String(16), nullable=False, server_default="member"),
    )

    # Vor dem Constraint: für jede verwaiste tenant_id eine Platzhalter-Zeile.
    # .invalid ist per RFC 2606 garantiert nicht auflösbar und kollidiert daher
    # nie mit einer echten Domain. Nichts wird stillschweigend gelöscht.
    op.execute(
        """
        INSERT INTO tenants (id, name, domain, created_at)
        SELECT DISTINCT m.tenant_id, 'Unbekannt (migriert)', m.tenant_id || '.invalid', now()
        FROM user_tenant_memberships m
        WHERE NOT EXISTS (SELECT 1 FROM tenants t WHERE t.id = m.tenant_id)
        """
    )
    op.create_foreign_key(
        "fk_membership_tenant",
        "user_tenant_memberships",
        "tenants",
        ["tenant_id"],
        ["id"],
        ondelete="CASCADE",
    )


def downgrade() -> None:
    op.drop_constraint("fk_membership_tenant", "user_tenant_memberships", type_="foreignkey")
    op.drop_column("user_tenant_memberships", "role")
    op.drop_index("ix_verification_user_purpose", table_name="email_verification_tokens")
    op.drop_table("email_verification_tokens")
    op.drop_table("tenants")
    # audit_action behält die neuen Labels: PostgreSQL kann Enum-Werte nicht
    # entfernen, ohne den Typ neu zu bauen, und ungenutzte Labels schaden nicht.
