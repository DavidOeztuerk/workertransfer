"""init portfolios

Revision ID: 0001_init_portfolios
Revises:
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0001_init_portfolios"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "portfolios",
        # Kein Surrogatschlüssel: ein Portfolio je Person, die subject_id genügt.
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("items", postgresql.JSONB(), nullable=False, server_default="[]"),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    op.drop_table("portfolios")
