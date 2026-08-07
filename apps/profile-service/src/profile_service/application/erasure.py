"""Was „löschen" in diesem Dienst heißt (ADR-0027 §2).

`profiles.id` **ist** die `subject_id` — ein Profil je Person. Die Zeile fällt
vollständig: Überschrift, Text, Ort, Fähigkeiten. Es gibt hier nichts, was einem
anderen gehört, also auch nichts zu erhalten und nichts zu anonymisieren.

Kein Sichtbarkeitsfeld, das man stattdessen umlegen könnte: ob ein Profil
gezeigt werden darf, lebt allein im Ledger (ADR-0020). Ein „unsichtbar
schalten" wäre hier also nicht die schonendere Löschung, sondern gar keine.
"""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import delete
from sqlalchemy.ext.asyncio import AsyncSession

from profile_service.infrastructure.database.models import ProfileModel

__all__ = ["erase_subject"]


async def erase_subject(session: AsyncSession, subject_id: UUID) -> int:
    """Löscht das Profil und gibt zurück, wie viele Zeilen stehen blieben.

    Immer 0: dieser Dienst kennt keinen Aufbewahrungsfall. Der Rückgabewert ist
    trotzdem da, weil jeder Empfänger dieselbe Quittung gibt — der Ursprung soll
    erfahren, was blieb, statt es zu vermuten (ADR-0027 §3.4).

    `DELETE` auf eine Zeile, die schon weg ist, ist von Natur aus idempotent.
    Das ist keine Nachlässigkeit, sondern die Voraussetzung dafür, dass die
    Zustellung „mindestens einmal" sein darf (ADR-0027 §4.2).
    """
    await session.execute(delete(ProfileModel).where(ProfileModel.id == subject_id))
    return 0
