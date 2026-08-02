"""market requests

Revision ID: 0003_market_requests
Revises: 0002_transfers
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0003_market_requests"
down_revision = "0002_transfers"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "market_requests",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("subject_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("tenant_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("requested_by", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("status", sa.String(16), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("answered_at", sa.DateTime(timezone=True), nullable=True),
    )
    op.create_index("ix_market_requests_subject_id", "market_requests", ["subject_id"])
    op.create_index("ix_market_requests_tenant_id", "market_requests", ["tenant_id"])
    # Einmal fragen — und zwar in der Datenbank, nicht nur im Handler. Zwei
    # gleichzeitige Anfragen desselben Unternehmens würden die Prüfung im Code
    # beide passieren; hier scheitert die zweite.
    op.create_unique_constraint(
        "uq_market_requests_subject_tenant", "market_requests", ["subject_id", "tenant_id"]
    )


def downgrade() -> None:
    op.drop_constraint("uq_market_requests_subject_tenant", "market_requests", type_="unique")
    op.drop_index("ix_market_requests_tenant_id", table_name="market_requests")
    op.drop_index("ix_market_requests_subject_id", table_name="market_requests")
    op.drop_table("market_requests")
