"""Anzeigen zurückziehen, wenn ein Unternehmen stillgelegt wird (ADR-0027 §7).

**jobs-service ist kein Empfänger der Löschkaskade.** `jobs` trägt `tenant_id`,
Titel, Beschreibung, Ort, Fähigkeiten, Status — und **keine Spalte, die auf eine
natürliche Person zeigt**. Kein `created_by`, keine `subject_id`. Eine
Stellenanzeige ist der Text eines Unternehmens.

Was hier ankommt, ist deshalb eine **andere Absicht**: löscht die einzige Person
mit `role='admin'` ihr Konto, wird das Unternehmen stillgelegt, und eine
unbeaufsichtigte Stellenanzeige ist schlechter als keine — Bewerbungen liefen an
niemanden.

**Und diese Absicht zählt ausdrücklich NICHT in den Vollständigkeitsnachweis der
Löschung.** Sonst könnte ein stiller jobs-service die Löschung eines Menschen
offenhalten — genau die Kopplung, die §7 ausschließt: ein persönliches Recht
darf nicht an einer Organisationsfrage hängen.
"""

from __future__ import annotations

from typing import Any, cast
from uuid import UUID

from sqlalchemy import CursorResult, update
from sqlalchemy.ext.asyncio import AsyncSession

from jobs_service.domain.job import JobStatus
from jobs_service.infrastructure.database.models import JobModel

__all__ = ["withdraw_company_jobs"]


async def withdraw_company_jobs(session: AsyncSession, tenant_id: UUID) -> int:
    """Schließt die laufenden Anzeigen. Gibt zurück, wie viele es waren.

    `CLOSED`, nicht gelöscht: die Anzeige gehört dem Unternehmen, und ein
    Unternehmen ist keine natürliche Person (ADR-0017). Was endet, ist ihre
    Sichtbarkeit.

    Idempotent — eine schon geschlossene Anzeige fällt aus der Bedingung heraus.
    """
    # `rowcount` liegt am CursorResult, nicht am generischen `Result` — der
    # `cast` sagt mypy, was SQLAlchemy bei einem UPDATE wirklich liefert.
    result = cast(
        CursorResult[Any],
        await session.execute(
            update(JobModel)
            .where(
                JobModel.tenant_id == tenant_id,
                JobModel.status == JobStatus.PUBLISHED.value,
            )
            .values(status=JobStatus.CLOSED.value)
        ),
    )
    return int(result.rowcount or 0)
