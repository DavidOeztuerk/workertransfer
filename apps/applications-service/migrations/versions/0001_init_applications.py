"""init applications

Revision ID: 0001_init_applications
Revises:
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0001_init_applications"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "applications",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("job_id", postgresql.UUID(as_uuid=True), nullable=False),
        # Kopiert aus der Stelle: ein Fremdschlüssel geht nicht (andere
        # Datenbank, ADR-0004), und eine Stelle wechselt nicht das Unternehmen.
        sa.Column("tenant_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("subject_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("message", sa.Text(), nullable=False, server_default=""),
        sa.Column("shares_resume", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("shares_portfolio", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("status", sa.String(16), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("answered_at", sa.DateTime(timezone=True), nullable=True),
        # Genau eine je (Person, Stelle) — in der Datenbank, damit zwei
        # gleichzeitige Absendungen nicht beide durchkommen.
        sa.UniqueConstraint("job_id", "subject_id", name="uq_application_job_subject"),
    )
    op.create_index("ix_applications_job_id", "applications", ["job_id"])
    op.create_index("ix_applications_tenant_id", "applications", ["tenant_id"])
    op.create_index("ix_applications_subject_id", "applications", ["subject_id"])


def downgrade() -> None:
    op.drop_index("ix_applications_subject_id", table_name="applications")
    op.drop_index("ix_applications_tenant_id", table_name="applications")
    op.drop_index("ix_applications_job_id", table_name="applications")
    op.drop_table("applications")
