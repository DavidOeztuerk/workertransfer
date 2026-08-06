"""Was „löschen" hier heißt (ADR-0027 §2).

`resumes.id` **ist** die `subject_id`; Stationen und Ausbildung liegen als JSONB
in derselben Zeile und fallen mit ihr. Ein Lebenslauf nennt echte Arbeitgeber
mit Zeiträumen — genau das, was ein aktueller Arbeitgeber nicht sehen darf, und
genau das, was nach einer Löschung nirgends liegen bleiben darf.

Die Anfragen laufen in zwei Richtungen, und der Unterschied ist tragend:

* `subject_id` = Person: **fällt.** Die Zeile IST die Aussage „Unternehmen X hat
  nach diesem Menschen gefragt" — eine Aussage über ihn.
* `requested_by` = Person: **bleibt, ohne ihren Namen.** Ein Recruiter löscht
  sein privates Konto; die Anfrage gehört dem Unternehmen und handelt von einem
  *Dritten*, dessen Löschung niemand verlangt hat.

**Kein Aufbewahrungsschalter.** Für keine Zeilenklasse dieses Dienstes wurde je
eine Aufbewahrungspflicht behauptet — einen Schalter „für alle Fälle" anzulegen
wäre genau die Vorsichtsannahme, die ADR-0027 §3 abschafft.
"""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import delete, update
from sqlalchemy.ext.asyncio import AsyncSession

from resume_service.infrastructure.database.models import (
    OUTBOX,
    ResumeModel,
    ResumeRequestModel,
)

__all__ = ["erase_subject"]


async def erase_subject(session: AsyncSession, subject_id: UUID) -> int:
    """Löscht Lebenslauf und Anfragen. Gibt zurück, was stehen blieb: nichts."""
    await session.execute(delete(ResumeModel).where(ResumeModel.id == subject_id))

    await session.execute(
        delete(ResumeRequestModel).where(ResumeRequestModel.subject_id == subject_id)
    )
    await session.execute(
        update(ResumeRequestModel)
        .where(ResumeRequestModel.requested_by == subject_id)
        .values(requested_by=None)
    )

    # Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.
    await session.execute(delete(OUTBOX).where(OUTBOX.c.user_id == subject_id))
    return 0
