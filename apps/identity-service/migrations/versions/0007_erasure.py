"""Die Voraussetzungen der Kontolöschung (ADR-0027 V2, V3, V5)

Drei Dinge, und jedes hat seinen eigenen Grund:

**V2 — die Outbox.** identity-service hatte keine; `build_outbox_table` stand
bisher nur in transfer-, applications- und resume-service. Sie trägt hier keine
Benachrichtigungen (dieser Dienst ist deren Empfänger), sondern die
Löschabsichten — je Empfänger eine Zeile, alle in derselben Transaktion wie die
Zustandsänderung des Kontos.

**V3 — `company_invitations.invited_by` wird nullbar, CASCADE wird SET NULL.**
Bisher verschwanden die offenen Einladungen eines Unternehmens, sobald ein
Recruiter sein **privates** Konto löschte. Die Einladung gehört aber dem
Unternehmen; was fällt, ist der Name daran.

**V5 — `tenants.status`.** Die Stilllegung beim letzten Admin (§7) braucht
einen Zustand; die Tabelle trug bisher nur `id`, `name`, `domain`,
`created_at`.

Revision ID: 0007_erasure
Revises: 0006_notification_preferences
Create Date: 2026-08-06
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0007_erasure"
down_revision = "0006_notification_preferences"
branch_labels = None
depends_on = None

_INVITED_BY_FK = "company_invitations_invited_by_fkey"


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
        # NULL heißt „steht noch aus" — und für eine Löschung ist genau diese
        # Spalte der Nachweis: fertig ist sie, wenn für eine `user_id` keine
        # Zeile mehr ohne `delivered_at` steht (ADR-0027 §4).
        sa.Column("delivered_at", sa.DateTime(timezone=True), nullable=True),
        # Nur die ART eines Fehlers, nie die Antwort des Gegenübers: diese
        # Tabelle steht danach in jedem Backup.
        sa.Column("last_error", sa.String(120), nullable=False),
    )
    op.create_index("ix_outbox_created_at", "outbox", ["created_at"])
    op.create_index("ix_outbox_delivered_at", "outbox", ["delivered_at"])

    op.alter_column("company_invitations", "invited_by", existing_type=sa.Uuid, nullable=True)
    op.drop_constraint(_INVITED_BY_FK, "company_invitations", type_="foreignkey")
    op.create_foreign_key(
        _INVITED_BY_FK,
        "company_invitations",
        "users",
        ["invited_by"],
        ["id"],
        ondelete="SET NULL",
    )

    op.add_column(
        "tenants",
        sa.Column("status", sa.String(16), nullable=False, server_default="active"),
    )


def downgrade() -> None:
    op.drop_column("tenants", "status")

    op.drop_constraint(_INVITED_BY_FK, "company_invitations", type_="foreignkey")
    op.create_foreign_key(
        _INVITED_BY_FK,
        "company_invitations",
        "users",
        ["invited_by"],
        ["id"],
        ondelete="CASCADE",
    )
    # Zurück geht es nur, solange niemand gelöscht hat: NULL-Zeilen ließen sich
    # nicht wiederherstellen, und ein erfundener Wert wäre eine Behauptung
    # darüber, wer eingeladen hat.
    op.alter_column("company_invitations", "invited_by", existing_type=sa.Uuid, nullable=False)

    op.drop_index("ix_outbox_delivered_at", table_name="outbox")
    op.drop_index("ix_outbox_created_at", table_name="outbox")
    op.drop_table("outbox")
