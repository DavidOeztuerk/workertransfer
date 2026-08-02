"""init company profiles

Revision ID: 0001_init_company_profiles
Revises:
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0001_init_company_profiles"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "company_profiles",
        # Kein Surrogatschlüssel: ein Profil je Unternehmen, die tenant_id genügt.
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("display_name", sa.Text(), nullable=False),
        sa.Column("about", sa.Text(), nullable=False, server_default=""),
        sa.Column("website", sa.Text(), nullable=True),
        sa.Column("locations", postgresql.JSONB(), nullable=False, server_default="[]"),
        sa.Column("benefits", postgresql.JSONB(), nullable=False, server_default="[]"),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    op.drop_table("company_profiles")
