"""company slug

Revision ID: 0002_company_slug
Revises: 0001_init_company_profiles
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0002_company_slug"
down_revision = "0001_init_company_profiles"
branch_labels = None
depends_on = None


def upgrade() -> None:
    # Erst nullable anlegen, füllen, dann festziehen: eine NOT-NULL-Spalte ohne
    # Vorgabewert scheitert auf jeder Tabelle, die bereits Zeilen hat.
    op.add_column("company_profiles", sa.Column("slug", sa.String(60), nullable=True))
    # Vorhandene Zeilen bekommen ein Kürzel aus ihrer ID — unschön, aber
    # eindeutig, und in dieser Phase hat noch niemand eines geteilt.
    op.execute(
        "UPDATE company_profiles SET slug = 'unternehmen-' || substring(id::text, 1, 8) "
        "WHERE slug IS NULL"
    )
    op.alter_column("company_profiles", "slug", nullable=False)
    op.create_unique_constraint("uq_company_profiles_slug", "company_profiles", ["slug"])


def downgrade() -> None:
    op.drop_constraint("uq_company_profiles_slug", "company_profiles", type_="unique")
    op.drop_column("company_profiles", "slug")
