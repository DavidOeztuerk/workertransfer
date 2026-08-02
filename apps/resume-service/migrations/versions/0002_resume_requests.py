"""resume requests

Revision ID: 0002_resume_requests
Revises: 0001_init_resumes
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision = "0002_resume_requests"
down_revision = "0001_init_resumes"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "resume_requests",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("subject_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("tenant_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("requested_by", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("status", sa.Text(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("answered_at", sa.DateTime(timezone=True), nullable=True),
        # "Einmal fragen" in der Datenbank, nicht nur im Handler: zwei
        # gleichzeitige Anfragen kämen sonst beide durch die Prüfung, und die
        # Ablehnung wäre umgehbar.
        sa.UniqueConstraint("subject_id", "tenant_id", name="uq_request_subject_tenant"),
    )
    op.create_index("ix_resume_requests_subject_id", "resume_requests", ["subject_id"])
    op.create_index("ix_resume_requests_tenant_id", "resume_requests", ["tenant_id"])


def downgrade() -> None:
    op.drop_index("ix_resume_requests_tenant_id", table_name="resume_requests")
    op.drop_index("ix_resume_requests_subject_id", table_name="resume_requests")
    op.drop_table("resume_requests")
