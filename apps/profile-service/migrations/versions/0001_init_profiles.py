"""init profiles

Revision ID: 0001_init_profiles
Revises:
Create Date: 2026-08-02
"""

from __future__ import annotations

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID

revision: str = "0001_init_profiles"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "profiles",
        # Kein eigener Schlüssel: die Person IST das Profil. Ein separater
        # Identifikator erzwänge eine Zuordnungstabelle und verkomplizierte die
        # Consent-Abfrage, die ohnehin über subject_id läuft.
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("headline", sa.Text, nullable=False),
        sa.Column("bio", sa.Text, nullable=False, server_default=""),
        sa.Column("location", sa.Text, nullable=False, server_default=""),
        sa.Column("remote_ok", sa.Boolean, nullable=False, server_default=sa.false()),
        sa.Column("skills", JSONB, nullable=False, server_default=sa.text("'[]'::jsonb")),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
        sa.Column(
            "updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )
    # Die Liste sortiert nach zuletzt geändert; ohne Index wird daraus ein
    # Full-Table-Sort, sobald mehr als eine Handvoll Profile existieren.
    op.create_index("ix_profiles_updated_at", "profiles", [sa.text("updated_at DESC"), "id"])


def downgrade() -> None:
    op.drop_index("ix_profiles_updated_at", table_name="profiles")
    op.drop_table("profiles")
