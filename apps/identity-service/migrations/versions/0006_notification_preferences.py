"""notification preferences

Revision ID: 0006_notification_preferences
Revises: 0005_member_removed_audit
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0006_notification_preferences"
down_revision = "0005_member_removed_audit"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "notification_preferences",
        sa.Column("user_id", postgresql.UUID(as_uuid=True), primary_key=True),
        # Alles an: eine Benachrichtigung über den eigenen Vorgang ist keine
        # Werbung, sondern die Bedingung dafür, dass „die Person entscheidet"
        # überhaupt eintreten kann.
        sa.Column("resume_request", sa.Boolean(), nullable=False, server_default=sa.true()),
        sa.Column("market_request", sa.Boolean(), nullable=False, server_default=sa.true()),
        sa.Column("application_update", sa.Boolean(), nullable=False, server_default=sa.true()),
        sa.Column("transfer_update", sa.Boolean(), nullable=False, server_default=sa.true()),
        # Die Drossel: eine je Person, über alle Arten hinweg.
        sa.Column("last_sent_at", sa.DateTime(timezone=True), nullable=True),
    )


def downgrade() -> None:
    op.drop_table("notification_preferences")
