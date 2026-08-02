"""HTTP-Endpunkte für den eigenen Lebenslauf.

Scheibe A: nur der Eigentümer. Fremdzugriff kommt in Scheibe B und geht dann
durch den Consent-Ledger, je Unternehmen einzeln.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from resume_service.application.handlers import (
    AlreadyRequested,
    AnswerRequestCommand,
    GetResumeQuery,
    RequestResumeCommand,
    ResumeNotVisible,
    RevokeAccessCommand,
    SaveMyResumeCommand,
    handle_answer_request,
    handle_get_my_resume,
    handle_get_visible_resume,
    handle_list_requests_for_subject,
    handle_list_requests_for_tenant,
    handle_request_resume,
    handle_revoke_access,
    handle_save_my_resume,
)
from resume_service.domain.request import ResumeRequest
from resume_service.domain.resume import Education, MonthDate, Position, Resume
from resume_service.infrastructure.consent import ConsentUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import EducationV1, PositionV1, ResumeRequestV1, ResumeV1, SaveResumeV1
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


def _request_dto(request: ResumeRequest, *, active: bool | None = None) -> ResumeRequestV1:
    return ResumeRequestV1(
        id=request.id,
        subject_id=request.subject_id,
        tenant_id=request.tenant_id,
        status=str(request.status),
        created_at=request.created_at,
        answered_at=request.answered_at,
        active=active,
    )


#: Eine Antwort für „gibt es nicht" und „ist nicht freigegeben". Sie darf sich
#: zwischen den Fällen nicht unterscheiden — sonst wäre der Statuscode ein
#: Orakel über jede geratene UUID (ADR-0020 §1).
_NOT_VISIBLE = "No such resume"


def _to_http(error: Any) -> HTTPException:
    """Fachliche Fehler auf Statuscodes — an einer Stelle, damit sie nicht driften."""
    if isinstance(error, ResumeNotVisible):
        return HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
    if isinstance(error, AlreadyRequested):
        return HTTPException(status.HTTP_409_CONFLICT, error.message)
    message = error.message if error is not None else "request refused"
    return HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)


async def _notify(deps: dict[str, Any], user_id: UUID, kind: str) -> None:
    """Benachrichtigen darf nichts kippen — und das steht HIER, nicht nur im Adapter.

    Der HTTP-Adapter schluckt seine Fehler bereits. Sich darauf zu verlassen
    hieße, die Zusage an der Wahl der Implementierung aufzuhängen: ein anderer
    Adapter, ein Tippfehler in den Einstellungen, ein Fake im Test — und ein
    Vorgang scheitert, weil eine Mail nicht rausging. Ein Integrationstest mit
    einem absichtlich kaputten Notifier hat genau das gezeigt.
    """
    try:
        await deps["notify"].notify(user_id, kind)
    except Exception:
        _logger.warning("Benachrichtigung konnte nicht abgesetzt werden", exc_info=True)


_logger = logging.getLogger("workertransfer.resume.notify")


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["resumes"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _principal(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    def _subject(request: Request) -> Any:
        return _principal(request).sub

    def _company(request: Request) -> Any:
        """Der aktive Tenant — Aussage über den Aufrufer, nicht über das Ziel.

        Deshalb darf dieser Fall 403 heißen, während ein verborgener Lebenslauf
        404 bekommt: hier wird nichts über eine dritte Person verraten.
        """
        principal = _principal(request)
        if principal.tenant_id is None:
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "requesting a resume requires an active company",
            )
        return principal

    def _bearer(request: Request) -> str:
        """Das Token des Aufrufers, zur Weitergabe an den Ledger.

        Header zuerst, sonst das Cookie — die Oberfläche sieht das httpOnly-Token
        nie und kann es nur so zurückgeben.
        """
        token = resolve_token(request.scope)
        if token is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return token

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

    @router.get("/resumes/me/requests")
    async def my_requests(request: Request) -> list[ResumeRequestV1]:
        """Wer hat nach meinem Lebenslauf gefragt — und was gilt gerade.

        `active` kommt frisch aus dem Ledger und kann von `status` abweichen:
        nach einem Widerruf bleibt `GRANTED` stehen, `active` fällt auf `false`.
        Genau deshalb steht die Berechtigung nicht im Vorgang.
        """
        subject_id = _subject(request)
        bearer = _bearer(request)
        async with request_scope(session_factory) as (_uow, repos):
            requests = await handle_list_requests_for_subject(subject_id, repos=repos)
        granted = [r for r in requests if str(r.status) == "GRANTED"]
        try:
            # Nur für erteilte Anfragen fragen: für PENDING und DECLINED steht
            # die Antwort fest, und jeder Aufruf kostet einen Round-Trip.
            verdicts = await asyncio.gather(
                *(
                    deps["consent"].may_read_resume(r.subject_id, r.tenant_id, bearer=bearer)
                    for r in granted
                )
            )
        except ConsentUnavailable as exc:
            raise HTTPException(
                status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
            ) from exc
        active_by_id = dict(zip((r.id for r in granted), verdicts, strict=True))
        return [
            _request_dto(
                r, active=active_by_id.get(r.id, False) if str(r.status) == "GRANTED" else False
            )
            for r in requests
        ]

    @router.post("/resumes/requests/{request_id}/grant")
    async def grant_request(request_id: UUID, request: Request) -> ResumeRequestV1:
        return await _answer(request_id, request, grant=True)

    @router.post("/resumes/requests/{request_id}/decline")
    async def decline_request(request_id: UUID, request: Request) -> ResumeRequestV1:
        return await _answer(request_id, request, grant=False)

    async def _answer(request_id: UUID, request: Request, *, grant: bool) -> ResumeRequestV1:
        command = AnswerRequestCommand(
            request_id=request_id,
            actor_id=_subject(request),
            bearer=_bearer(request),
            grant=grant,
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_answer_request(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _request_dto(result.value, active=grant)

    @router.post("/resumes/requests/{request_id}/revoke")
    async def revoke_access(request_id: UUID, request: Request) -> ResumeRequestV1:
        """Der Widerruf wirkt im Ledger, nicht im Vorgang.

        Den Vorgang zurückzusetzen würde die Geschichte umschreiben — er hält
        fest, was geschehen ist, nicht was gilt.
        """
        command = RevokeAccessCommand(
            request_id=request_id, actor_id=_subject(request), bearer=_bearer(request)
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_revoke_access(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _request_dto(result.value, active=False)

    @router.post("/resumes/{subject_id}/requests", status_code=status.HTTP_201_CREATED)
    async def request_resume(subject_id: UUID, request: Request) -> ResumeRequestV1:
        principal = _company(request)
        command = RequestResumeCommand(
            subject_id=subject_id,
            tenant_id=principal.tenant_id,
            requested_by=principal.sub,
            bearer=_bearer(request),
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_request_resume(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
        # Nach dem Commit und ohne Rückwirkung: eine misslungene Mail darf die
        # Anfrage nicht rückgängig machen.
        await _notify(deps, subject_id, "resume_request")
        return _request_dto(result.value)

    @router.get("/resumes/requests")
    async def company_requests(request: Request) -> list[ResumeRequestV1]:
        """Die eigenen Anfragen. Ohne `active`.

        Das Unternehmen hat die Antwort bereits in Form der Daten, die es bekommt
        oder nicht bekommt; ein Feld hier wäre eine zweite Auskunft über
        denselben Sachverhalt.
        """
        principal = _company(request)
        async with request_scope(session_factory) as (_uow, repos):
            requests = await handle_list_requests_for_tenant(principal.tenant_id, repos=repos)
        return [_request_dto(r) for r in requests]

    @router.get("/resumes/{subject_id}")
    async def get_visible_resume(subject_id: UUID, request: Request) -> ResumeV1:
        principal = _company(request)
        query = GetResumeQuery(
            subject_id=subject_id, tenant_id=principal.tenant_id, bearer=_bearer(request)
        )
        async with request_scope(session_factory) as (_uow, repos):
            try:
                result = await handle_get_visible_resume(query, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                # Weder 404 noch anzeigen: beides wäre eine Behauptung über die
                # Person, die in diesem Moment niemand treffen kann.
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _to_http(result.error)
            return _dto(result.value)

    return router
