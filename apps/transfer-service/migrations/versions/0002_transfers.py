"""transfers

Revision ID: 0002_transfers
Revises: 0001_init_market_status
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0002_transfers"
down_revision = "0001_init_market_status"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "transfers",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("subject_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("tenant_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("status", sa.String(16), nullable=False),
        sa.Column("requires_release", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("release_confirmed", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("message", sa.Text(), nullable=False, server_default=""),
        sa.Column("offer_note", sa.Text(), nullable=False, server_default=""),
        sa.Column("offer_start_on", sa.String(7), nullable=True),
        # BigInteger: Ablösen können groß werden, und ein Überlauf mitten in
        # einer Verhandlung wäre ein teurer Fehler.
        sa.Column("offer_fee_cents", sa.BigInteger(), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index("ix_transfers_subject_id", "transfers", ["subject_id"])
    op.create_index("ix_transfers_tenant_id", "transfers", ["tenant_id"])
    # Genau EIN laufender Vorgang je (Person, Unternehmen). Ein zweiter wäre
    # Nachfassen an der Ablehnung vorbei. Teilindex, weil abgeschlossene
    # Vorgänge beliebig oft nebeneinander stehen dürfen.
    op.create_index(
        "uq_running_transfer",
        "transfers",
        ["subject_id", "tenant_id"],
        unique=True,
        postgresql_where=sa.text("status IN ('interested', 'talking', 'offered', 'accepted')"),
    )


def downgrade() -> None:
    op.drop_index("uq_running_transfer", table_name="transfers")
    op.drop_index("ix_transfers_tenant_id", table_name="transfers")
    op.drop_index("ix_transfers_subject_id", table_name="transfers")
    op.drop_table("transfers")
