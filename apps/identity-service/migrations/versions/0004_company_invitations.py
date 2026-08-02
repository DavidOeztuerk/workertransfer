"""company invitations

Revision ID: 0004_company_invitations
Revises: 0003_verification_and_companies
Create Date: 2026-08-02
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision: str = "0004_company_invitations"
down_revision: str | None = "0003_verification_and_companies"
branch_labels: None = None
depends_on: None = None


def upgrade() -> None:
    # ADD VALUE ist auf PG 12+ innerhalb einer Transaktion erlaubt, solange der
    # neue Wert in derselben Transaktion nicht benutzt wird — hier wird er nur
    # deklariert.
    for value in ("member_invited", "member_joined", "invitation_withdrawn"):
        op.execute(f"ALTER TYPE audit_action ADD VALUE IF NOT EXISTS '{value}'")

    op.create_table(
        "company_invitations",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column(
            "tenant_id",
            postgresql.UUID(as_uuid=True),
            sa.ForeignKey("tenants.id", ondelete="CASCADE"),
            nullable=False,
        ),
        # CITEXT wie users.email: die Einladung an Anna@Firma.example und die an
        # anna@firma.example sind dieselbe Einladung.
        sa.Column("email", postgresql.CITEXT(), nullable=False),
        sa.Column("role", sa.String(16), nullable=False),
        sa.Column(
            "invited_by",
            postgresql.UUID(as_uuid=True),
            sa.ForeignKey("users.id", ondelete="CASCADE"),
            nullable=False,
        ),
        sa.Column("status", sa.String(16), nullable=False),
        # Nur der Hash. Der Klartext geht per Mail raus und steht nirgends in
        # der Datenbank — wer sie liest, kann damit keine Einladung annehmen.
        sa.Column("token_hash", sa.String(64), nullable=False, unique=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("accepted_at", sa.DateTime(timezone=True), nullable=True),
    )
    op.create_index("ix_company_invitations_tenant_id", "company_invitations", ["tenant_id"])
    # Höchstens eine offene Einladung je (Unternehmen, Adresse). Erneutes
    # Einladen ersetzt die alte, statt eine zweite anzulegen — sonst hätte eine
    # zurückgezogene Einladung einen noch gültigen Zwilling.
    op.create_index(
        "uq_open_invitation_per_email",
        "company_invitations",
        ["tenant_id", "email"],
        unique=True,
        postgresql_where=sa.text("status = 'pending'"),
    )


def downgrade() -> None:
    op.drop_index("uq_open_invitation_per_email", table_name="company_invitations")
    op.drop_index("ix_company_invitations_tenant_id", table_name="company_invitations")
    op.drop_table("company_invitations")
