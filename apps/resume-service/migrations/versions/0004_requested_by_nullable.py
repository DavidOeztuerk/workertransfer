"""requested_by darf NULL sein

Löscht ein Recruiter sein **privates** Konto, verschwindet damit nicht die
Anfrage seines Arbeitgebers: die gehört dem Unternehmen und handelt von einem
*Dritten*. Was fällt, ist der Name der Person daran (ADR-0027 §2, V3).

Die Gegenrichtung — Anfragen, deren `subject_id` diese Person ist — fällt
vollständig: so eine Zeile IST die Aussage „Unternehmen X hat nach diesem
Menschen gefragt".

Revision ID: 0004_requested_by_nullable
Revises: 0003_outbox
Create Date: 2026-08-06
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0004_requested_by_nullable"
down_revision = "0003_outbox"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.alter_column("resume_requests", "requested_by", existing_type=sa.Uuid, nullable=True)


def downgrade() -> None:
    # Zurück geht es nur, solange niemand gelöscht hat: NULL-Zeilen ließen sich
    # nicht wiederherstellen, und ein erfundener Wert wäre eine Behauptung
    # darüber, wer gefragt hat.
    op.alter_column("resume_requests", "requested_by", existing_type=sa.Uuid, nullable=False)
