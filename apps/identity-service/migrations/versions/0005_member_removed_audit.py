"""member removed audit action

Revision ID: 0005_member_removed_audit
Revises: 0004_company_invitations
Create Date: 2026-08-02
"""

from __future__ import annotations

from alembic import op

revision: str = "0005_member_removed_audit"
down_revision: str | None = "0004_company_invitations"
branch_labels: None = None
depends_on: None = None


def upgrade() -> None:
    # Eigene Revision, nicht an 0004 angehängt: 0004 ist bereits gelaufen, und
    # eine nachträglich geänderte Migration wäre auf jeder Umgebung, die sie
    # schon hat, wirkungslos.
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'member_removed'")


def downgrade() -> None:
    # PostgreSQL kann keinen Enum-Wert entfernen, ohne den Typ neu zu bauen.
    # Ein zusätzlicher, unbenutzter Wert schadet nicht.
    pass
