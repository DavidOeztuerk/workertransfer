"""requested_by darf NULL sein

Wie bei `resume_requests`: die Anfrage gehört dem Unternehmen, der Name der
Person daran nicht mehr (ADR-0027 §2, V3).

Revision ID: 0005_requested_by_nullable
Revises: 0004_outbox
Create Date: 2026-08-06
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0005_requested_by_nullable"
down_revision = "0004_outbox"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.alter_column("market_requests", "requested_by", existing_type=sa.Uuid, nullable=True)


def downgrade() -> None:
    op.alter_column("market_requests", "requested_by", existing_type=sa.Uuid, nullable=False)
