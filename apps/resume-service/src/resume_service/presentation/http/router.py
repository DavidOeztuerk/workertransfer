"""HTTP-Endpunkte für den eigenen Lebenslauf.

Scheibe A: nur der Eigentümer. Fremdzugriff kommt in Scheibe B und geht dann
durch den Consent-Ledger, je Unternehmen einzeln.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Request, status
from resume_service.application.handlers import (
    SaveMyResumeCommand,
    handle_get_my_resume,
    handle_save_my_resume,
)
from resume_service.domain.resume import Education, MonthDate, Position, Resume
from worker_auth import get_request_user
from worker_contracts import EducationV1, PositionV1, ResumeV1, SaveResumeV1
from worker_core import DomainError

__all__ = ["build_router"]


def _to_domain_position(dto: PositionV1) -> Position:
    return Position(
        employer=dto.employer,
        title=dto.title,
        started_on=MonthDate.parse(dto.started_on),
        ended_on=None if dto.ended_on is None else MonthDate.parse(dto.ended_on),
        description=dto.description,
    )


def _to_domain_education(dto: EducationV1) -> Education:
    return Education(
        institution=dto.institution,
        qualification=dto.qualification,
        started_on=MonthDate.parse(dto.started_on),
        ended_on=None if dto.ended_on is None else MonthDate.parse(dto.ended_on),
    )


def _dto(resume: Resume) -> ResumeV1:
    return ResumeV1(
        subject_id=resume.subject_id,
        positions=[
            PositionV1(
                employer=entry.employer,
                title=entry.title,
                started_on=str(entry.started_on),
                ended_on=None if entry.ended_on is None else str(entry.ended_on),
                description=entry.description,
            )
            for entry in resume.positions
        ],
        education=[
            EducationV1(
                institution=entry.institution,
                qualification=entry.qualification,
                started_on=str(entry.started_on),
                ended_on=None if entry.ended_on is None else str(entry.ended_on),
            )
            for entry in resume.education
        ],
        updated_at=resume.updated_at,
    )


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["resumes"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _subject(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal.sub

    @router.put("/resumes/me")
    async def save_my_resume(body: SaveResumeV1, request: Request) -> ResumeV1:
        subject_id = _subject(request)
        try:
            # Die Umwandlung wirft dieselben DomainErrors wie das Aggregat —
            # ein Monat, den es nicht gibt, kommt hier heraus, nicht erst tiefer.
            command = SaveMyResumeCommand(
                subject_id=subject_id,
                positions=[_to_domain_position(entry) for entry in body.positions],
                education=[_to_domain_education(entry) for entry in body.education],
            )
        except DomainError as exc:
            raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, exc.message) from exc

        async with request_scope(session_factory) as (uow, repos):
            result = await handle_save_my_resume(command, deps=deps, repos=repos)
            if not result.is_success:
                error = result.error
                message = error.message if error is not None else "invalid resume"
                raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)
            await uow.commit()
            return _dto(result.value)

    @router.get("/resumes/me")
    async def get_my_resume(request: Request) -> ResumeV1 | None:
        """`null` statt 404: „noch keinen angelegt" ist ein Zustand.

        Die Oberfläche zeigt darauf ein leeres Formular — ein 404 würde sie
        zwingen, einen Fehler in einen Normalfall zurückzuübersetzen.
        """
        subject_id = _subject(request)
        async with request_scope(session_factory) as (_uow, repos):
            resume = await handle_get_my_resume(subject_id, repos=repos)
            return None if resume is None else _dto(resume)

    return router
