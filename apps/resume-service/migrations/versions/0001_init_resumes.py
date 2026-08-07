"""init resumes

Revision ID: 0001_init_resumes
Revises:
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0001_init_resumes"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "resumes",
        # Kein eigener Schlüssel: ein Lebenslauf je Person, die subject_id
        # genügt. Ein Surrogatschlüssel bräuchte zusätzlich einen Unique-Index
        # auf subject_id, um dieselbe Regel durchzusetzen.
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("positions", postgresql.JSONB(), nullable=False, server_default="[]"),
        sa.Column("education", postgresql.JSONB(), nullable=False, server_default="[]"),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    op.drop_table("resumes")
