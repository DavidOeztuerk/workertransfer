"""outbox

Die Absicht wird in derselben Transaktion festgehalten wie die Änderung, die
sie ausgelöst hat (ADR-0025). Die Tabelle gehört DIESEM Dienst und liegt in
SEINER Datenbank — es gibt keine gemeinsame Outbox, so wenig wie es eine
gemeinsame Datenbank gibt (ADR-0004).

Revision ID: 0003_outbox
Revises: 0002_resume_requests
Create Date: 2026-08-06
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0003_outbox"
down_revision = "0002_resume_requests"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "outbox",
        # `sa.Uuid` statt `postgresql.UUID`: auf Postgres derselbe native Typ,
        # aber nicht an den Dialekt gebunden — dieselbe Wahl wie in
        # `worker_outbox.build_outbox_table`, damit Modell und Migration nicht
        # auseinanderlaufen.
        sa.Column("id", sa.Uuid, primary_key=True),
        sa.Column("user_id", sa.Uuid, nullable=False),
        sa.Column("kind", sa.String(64), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("attempts", sa.Integer, nullable=False),
        # NULL heißt „steht noch aus" — die Spalte, auf der der Zusteller sucht.
        sa.Column("delivered_at", sa.DateTime(timezone=True), nullable=True),
        # Nur die ART eines Fehlers, nie die Antwort des Gegenübers und nie ein
        # Inhalt: diese Tabelle steht danach in jedem Backup.
        sa.Column("last_error", sa.String(120), nullable=False),
    )
    op.create_index("ix_outbox_created_at", "outbox", ["created_at"])
    op.create_index("ix_outbox_delivered_at", "outbox", ["delivered_at"])


def downgrade() -> None:
    op.drop_index("ix_outbox_delivered_at", table_name="outbox")
    op.drop_index("ix_outbox_created_at", table_name="outbox")
    op.drop_table("outbox")
