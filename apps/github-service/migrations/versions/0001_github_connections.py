"""github connections

Revision ID: 0001_github_connections
Revises:
Create Date: 2026-08-03
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0001_github_connections"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "github_connections",
        # `id` IST die subject_id: eine Verbindung je Person. Zwei gleichzeitig
        # wären eine Liste, und eine Liste wirft die Frage auf, welche „die
        # echte" ist.
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("login", sa.String(39), nullable=False),
        sa.Column("challenge", sa.String(64), nullable=False),
        sa.Column("verified_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("fetched_at", sa.DateTime(timezone=True), nullable=True),
        # Der Abzug als Ganzes: er wird immer zusammen geschrieben und
        # zusammen gelesen, nie einzeln abgefragt. Eine zweite Tabelle wäre ein
        # Join für einen Wert, der nur zusammen Sinn ergibt.
        sa.Column(
            "repositories",
            postgresql.JSONB(),
            nullable=False,
            server_default=sa.text("'[]'::jsonb"),
        ),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    op.drop_table("github_connections")
