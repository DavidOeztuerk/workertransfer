"""Die gemerkte Absicht, ein Unternehmen anzulegen (E2.6)

Eine nullable Spalte auf `users`. `NULL` heißt „eine Person"; ein Name heißt
„hier registriert sich ein Unternehmen".

**Warum auf dem Server und nicht im Browser.** Ein `localStorage`-Merker wäre
billiger — genau so merkt sich `apps/web/src/jobs/intent.ts` eine Stelle über
das Anmelden hinweg. Für eine Bestätigungsmail trägt das nicht:
Bestätigungslinks werden oft auf einem **anderen Gerät** geöffnet, dort ist der
Merker weg, und aus einer Unternehmensregistrierung würde stillschweigend eine
Personenregistrierung. Niemand merkt es, weil beides gleich aussieht.

**Warum EINE Spalte und kein zusätzliches `account_type`.** Der Kontotyp ist
hieraus ableitbar, und zwei Spalten, von denen eine die andere impliziert,
können widersprüchlich werden. `NULL` ist die Aussage.

**Warum „pending".** Sie wird bei der Bestätigung eingelöst und danach geleert.
Genau das macht den zweiten Klick auf denselben Bestätigungslink idempotent: die
Absicht ist verbraucht, es entsteht kein zweites Unternehmen.

Nicht in dieser Migration und mit Grund: das Unternehmen selbst. Es entsteht
erst bei der Bestätigung, weil eine unbestätigte Adresse keine Domain beweist
(ADR-0019) — `handle_create_company` wirft dafür `AccountNotConfirmed`.

Revision ID: 0008_pending_company_intent
Revises: 0007_erasure
Create Date: 2026-08-12
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0008_pending_company_intent"
down_revision = "0007_erasure"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "users",
        sa.Column("pending_company_name", sa.String(length=200), nullable=True),
    )


def downgrade() -> None:
    op.drop_column("users", "pending_company_name")
