"""Commands und Handler für den eigenen Lebenslauf.

Scheibe A kennt nur den Eigentümer: kein Fremdzugriff, kein Ledger. Was ein
Unternehmen sehen darf, kommt in Scheibe B dazu — und geht dann durch dieselben
Regeln wie beim Profil (ADR-0020).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from resume_service.domain.resume import Education, Position, Resume

__all__ = [
    "SaveMyResumeCommand",
    "handle_get_my_resume",
    "handle_save_my_resume",
]


@dataclass(frozen=True, slots=True)
class SaveMyResumeCommand:
    subject_id: UUID
    positions: list[Position]
    education: list[Education]


async def handle_save_my_resume(
    cmd: SaveMyResumeCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Resume]:
    now = deps["clock"].now()
    try:
        existing: Resume | None = await repos["resumes"].get(cmd.subject_id)
        if existing is None:
            resume = Resume.create(
                cmd.subject_id, positions=cmd.positions, education=cmd.education, now=now
            )
        else:
            existing.update(positions=cmd.positions, education=cmd.education, now=now)
            resume = existing
        await repos["resumes"].save(resume)
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(resume)


async def handle_get_my_resume(subject_id: UUID, *, repos: dict[str, Any]) -> Resume | None:
    """Kein `Result`: „noch keinen angelegt" ist ein Zustand, kein Fehler.

    Nebenbei umgeht das dieselbe Falle wie beim Profil — `worker_core.Result`
    unterscheidet „kein Wert" nicht von „der Wert ist None", `.value` würde
    werfen.
    """
    resume: Resume | None = await repos["resumes"].get(subject_id)
    return resume
