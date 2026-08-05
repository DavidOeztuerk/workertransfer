"""job skills

Revision ID: 0002_job_skills
Revises: 0001_init_jobs
Create Date: 2026-08-05
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0002_job_skills"
down_revision = "0001_init_jobs"
branch_labels = None
depends_on = None


def upgrade() -> None:
    # `server_default` ist hier nicht nur Bequemlichkeit für bestehende Zeilen:
    # ohne ihn wäre die Spalte für jede Ausschreibung, die vor diesem Schnitt
    # geschrieben wurde, NULL — und `Skills(None)` bricht. Eine leere Liste ist
    # die richtige Aussage: die Stelle hat nichts aufgezählt.
    op.add_column(
        "jobs",
        sa.Column(
            "skills",
            postgresql.JSONB(),
            nullable=False,
            server_default=sa.text("'[]'::jsonb"),
        ),
    )


def downgrade() -> None:
    op.drop_column("jobs", "skills")
