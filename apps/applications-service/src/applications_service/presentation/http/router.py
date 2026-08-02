"""HTTP-Endpunkte für Bewerbungen.

Die Bewerbung enthält keine Profildaten. Sie nennt eine `subject_id`; wer
Profil, Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste,
und dort greift der Consent-Ledger.
"""

from __future__ import annotations

from typing import Any
from uuid import UUID

from applications_service.application.handlers import (
    AdvanceApplicationCommand,
    ApplicationNotFound,
    JobNotOpen,
    SubmitApplicationCommand,
    WithdrawApplicationCommand,
    handle_advance,
    handle_list_for_job,
    handle_list_mine,
    handle_submit,
    handle_withdraw,
)
from applications_service.domain.application import Application, NotYours, TransitionNotAllowed
from applications_service.infrastructure.consent import ConsentUnavailable
from applications_service.infrastructure.jobs import JobsUnavailable
from fastapi import APIRouter, HTTPException, Request, status
from worker_auth import get_request_user, resolve_token
from worker_contracts import AdvanceApplicationV1, ApplicationV1, SubmitApplicationV1
from worker_core import DomainError

__all__ = ["build_router"]

_NOT_FOUND = "No such application"


def _dto(application: Application) -> ApplicationV1:
    return ApplicationV1(
        id=application.id,
        job_id=application.job_id,
        tenant_id=application.tenant_id,
        subject_id=application.subject_id,
        message=application.message,
        shares_resume=application.shared.resume,
        shares_portfolio=application.shared.portfolio,
        status=str(application.status),  # type: ignore[arg-type]
        created_at=application.created_at,
        updated_at=application.updated_at,
    )


def _to_http(error: Any) -> HTTPException:
    if isinstance(error, ApplicationNotFound):
        return HTTPException(status.HTTP_404_NOT_FOUND, _NOT_FOUND)
    if isinstance(error, JobNotOpen):
        return HTTPException(status.HTTP_404_NOT_FOUND, "No such job")
    if isinstance(error, NotYours):
        # Wie eine fremde Ressource: nicht meins und nicht vorhanden sind
        # von außen dasselbe.
        return HTTPException(status.HTTP_404_NOT_FOUND, _NOT_FOUND)
    if isinstance(error, TransitionNotAllowed):
        # Die Eingabe ist in Ordnung, der Zustand passt nicht.
        return HTTPException(status.HTTP_409_CONFLICT, error.message)
    message = error.message if isinstance(error, DomainError) else "invalid application"
    return HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["applications"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _principal(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    def _company(request: Request) -> Any:
        principal = _principal(request)
        if principal.tenant_id is None:
            # Aussage über den Aufrufer, nicht über eine fremde Stelle.
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "reading applications requires an active company",
            )
        return principal.tenant_id

    def _bearer(request: Request) -> str:
        token = resolve_token(request.scope)
        if token is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return token

    def _unavailable(exc: Exception) -> HTTPException:
        """Weder ablehnen noch annehmen: wir wissen es gerade nicht."""
        return HTTPException(status.HTTP_503_SERVICE_UNAVAILABLE, "a dependency is unavailable")

    @router.post("/applications", status_code=status.HTTP_201_CREATED)
    async def submit(body: SubmitApplicationV1, request: Request) -> ApplicationV1:
        """Bewerben — und damit die Daten für dieses eine Unternehmen freigeben.

        Die Freigabe entsteht im Ledger, nicht in dieser Datenbank, und sie
        nennt den Empfänger. Sie ist derselbe Mechanismus wie beim Lebenslauf
        (3.3), nur ausgelöst durch eine andere Handlung.
        """
        command = SubmitApplicationCommand(
            job_id=body.job_id,
            subject_id=_principal(request).sub,
            message=body.message,
            shares_resume=body.shares_resume,
            shares_portfolio=body.shares_portfolio,
            bearer=_bearer(request),
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_submit(command, deps=deps, repos=repos)
            except (ConsentUnavailable, JobsUnavailable) as exc:
                raise _unavailable(exc) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value)

    @router.get("/applications/me")
    async def my_applications(request: Request) -> list[ApplicationV1]:
        subject_id = _principal(request).sub
        async with request_scope(session_factory) as (_uow, repos):
            applications = await handle_list_mine(subject_id, repos=repos)
        return [_dto(entry) for entry in applications]

    @router.post("/applications/{application_id}/withdraw")
    async def withdraw(application_id: UUID, request: Request) -> ApplicationV1:
        """Zurückziehen — der Vorgang bleibt, die Daten sind weg.

        Dass jemand sich beworben und zurückgezogen hat, gehört zur Geschichte
        des Verfahrens im Unternehmen. Die Person dahinter ist danach nicht
        mehr einsehbar.
        """
        command = WithdrawApplicationCommand(
            application_id=application_id,
            subject_id=_principal(request).sub,
            bearer=_bearer(request),
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_withdraw(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise _unavailable(exc) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value)

    @router.get("/jobs/{job_id}/applications")
    async def for_job(job_id: UUID, request: Request) -> list[ApplicationV1]:
        tenant_id = _company(request)
        async with request_scope(session_factory) as (_uow, repos):
            applications = await handle_list_for_job(job_id, tenant_id, repos=repos)
        return [_dto(entry) for entry in applications]

    @router.post("/applications/{application_id}/status")
    async def advance(
        application_id: UUID, body: AdvanceApplicationV1, request: Request
    ) -> ApplicationV1:
        command = AdvanceApplicationCommand(
            application_id=application_id, tenant_id=_company(request), status=body.status
        )
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_advance(command, deps=deps, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value)

    return router
