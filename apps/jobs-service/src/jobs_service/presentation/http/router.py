"""HTTP-Endpunkte für Stellenausschreibungen.

Schreiben gehört einem Unternehmen; Lesen ist öffentlich, sobald veröffentlicht.
`GET /jobs` und `GET /jobs/{id}` sind die ersten Endpunkte im System **ohne
Authentifizierung** — eine Ausschreibung, die man nur angemeldet sieht, ist
keine Ausschreibung.
"""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Query, Request, status
from jobs_service.application.handlers import (
    MAX_PAGE_SIZE,
    CreateJobCommand,
    JobNotFound,
    PublishJobCommand,
    SearchJobsQuery,
    UpdateJobCommand,
    handle_close_job,
    handle_create_job,
    handle_get_public_job,
    handle_list_own_jobs,
    handle_publish_job,
    handle_search_jobs,
    handle_update_job,
)
from jobs_service.domain.job import Job, TransitionNotAllowed
from worker_ai import DrafterUnavailable, JobDraftContext
from worker_auth import get_request_user
from worker_contracts import (
    DraftJobTextV1,
    JobPageV1,
    JobTextDraftV1,
    JobV1,
    SaveJobV1,
)
from worker_core import DomainError

__all__ = ["build_router"]

#: Eine Antwort für „gibt es nicht" und „gehört jemand anderem". Ein
#: Unterschied wäre ein Orakel darüber, welche Unternehmen wie viele Stellen
#: ausschreiben — im Wettbewerb etwas wert.
_NOT_FOUND = "No such job"


def _dto(job: Job) -> JobV1:
    return JobV1(
        id=job.id,
        tenant_id=job.tenant_id,
        title=job.title,
        description=job.description,
        location=job.location,
        remote=str(job.remote),  # type: ignore[arg-type]
        employment=str(job.employment),  # type: ignore[arg-type]
        skills=list(job.skills.value),
        status=str(job.status),  # type: ignore[arg-type]
        published_at=job.published_at,
        updated_at=job.updated_at,
    )


def _to_http(error: Any) -> HTTPException:
    if isinstance(error, JobNotFound):
        return HTTPException(status.HTTP_404_NOT_FOUND, _NOT_FOUND)
    if isinstance(error, TransitionNotAllowed):
        # 409, nicht 422: die Eingabe ist in Ordnung, der Zustand passt nicht.
        return HTTPException(status.HTTP_409_CONFLICT, error.message)
    message = error.message if isinstance(error, DomainError) else "invalid job"
    return HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["jobs"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _company(request: Request) -> Any:
        """Das aktive Unternehmen — Aussage über den Aufrufer, nicht über das Ziel.

        Deshalb darf dieser Fall 403 heißen, während eine fremde Ausschreibung
        404 bekommt: hier wird nichts über ein anderes Unternehmen verraten.
        """
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        if principal.tenant_id is None:
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "posting a job requires an active company",
            )
        return principal.tenant_id

    @router.post("/jobs/draft")
    async def draft_job_text(body: DraftJobTextV1, request: Request) -> JobTextDraftV1:
        """Die eigene Anzeige verständlicher formulieren lassen.

        Der einzige Unternehmens-Agent aus dem ULTRAPLAN, der ohne eigene
        Abwägung baubar ist: Scout, Candidate Ranking, Salary Recommendation
        und Team Analyzer richten sich alle auf **Personen**. Dieser richtet
        sich auf einen Text, den das Unternehmen selbst verfasst hat — er sagt
        über niemanden etwas (ADR-0022/0024).

        `_company(request)` steht auch hier: ausschreiben darf, wer für ein
        Unternehmen handelt. Ohne die Prüfung wäre der Endpunkt ein
        Textgenerator für jeden Angemeldeten.

        Der Zusammenhang kommt aus dem Request und nicht aus der Datenbank —
        beim Schreiben gibt es die Anzeige dort noch nicht. Unbedenklich, weil
        es Angaben des Unternehmens über sich selbst sind.
        """
        _ = _company(request)
        context = JobDraftContext(
            title=body.title,
            description=body.description,
            skills=tuple(body.skills),
            location=body.location,
            wish=body.wish,
        )
        try:
            draft = await deps["drafter"].draft(context)
        except DrafterUnavailable as exc:
            raise HTTPException(
                status.HTTP_503_SERVICE_UNAVAILABLE,
                "Der Entwurfsdienst ist gerade nicht verfügbar.",
            ) from exc
        return JobTextDraftV1(draft=draft)

    @router.post("/jobs", status_code=status.HTTP_201_CREATED)
    async def create_job(body: SaveJobV1, request: Request) -> JobV1:
        """Legt einen Entwurf an. Jedes MITGLIED darf das.

        Ausschreiben ist die Arbeit, für die jemand ins Unternehmen geholt
        wurde; sie an die Administratorenrolle zu binden würde die Rolle
        „Mitglied" bedeutungslos machen.
        """
        command = CreateJobCommand(
            tenant_id=_company(request),
            title=body.title,
            description=body.description,
            location=body.location,
            remote=body.remote,
            employment=body.employment,
            skills=tuple(body.skills),
        )
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_create_job(command, deps=deps, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value)

    @router.put("/jobs/{job_id}")
    async def update_job(job_id: UUID, body: SaveJobV1, request: Request) -> JobV1:
        command = UpdateJobCommand(
            job_id=job_id,
            tenant_id=_company(request),
            title=body.title,
            description=body.description,
            location=body.location,
            remote=body.remote,
            employment=body.employment,
            skills=tuple(body.skills),
        )
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_update_job(command, deps=deps, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value)

    @router.post("/jobs/{job_id}/publish")
    async def publish_job(job_id: UUID, request: Request) -> JobV1:
        return await _transition(job_id, request, publish=True)

    @router.post("/jobs/{job_id}/close")
    async def close_job(job_id: UUID, request: Request) -> JobV1:
        return await _transition(job_id, request, publish=False)

    async def _transition(job_id: UUID, request: Request, *, publish: bool) -> JobV1:
        command = PublishJobCommand(job_id=job_id, tenant_id=_company(request))
        handler = handle_publish_job if publish else handle_close_job
        async with request_scope(session_factory) as (uow, repos):
            result = await handler(command, deps=deps, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value)

    @router.get("/companies/me/jobs")
    async def own_jobs(request: Request) -> list[JobV1]:
        """Alle eigenen, jeden Status — auch Entwürfe und geschlossene."""
        tenant_id = _company(request)
        async with request_scope(session_factory) as (_uow, repos):
            jobs = await handle_list_own_jobs(tenant_id, repos=repos)
        return [_dto(job) for job in jobs]

    @router.get("/jobs")
    async def search_jobs(
        q: str | None = None,
        location: str | None = None,
        remote: str | None = None,
        employment: str | None = None,
        company: UUID | None = None,
        limit: int = Query(default=20, ge=1, le=MAX_PAGE_SIZE),
        cursor: str | None = None,
    ) -> JobPageV1:
        """Öffentlich, ohne Anmeldung — bewusst.

        Eine Stellenausschreibung, die man nur angemeldet sieht, ist keine
        Ausschreibung. Personenbezogene Daten kommen erst mit der Bewerbung ins
        Spiel, und dort greift der Consent-Ledger wieder.
        """
        query = SearchJobsQuery(
            query=q,
            location=location,
            remote=remote,
            employment=employment,
            company=company,
            limit=limit,
            cursor=cursor,
        )
        async with request_scope(session_factory) as (_uow, repos):
            jobs, next_cursor = await handle_search_jobs(query, repos=repos)
        return JobPageV1(items=[_dto(job) for job in jobs], next_cursor=next_cursor)

    @router.get("/jobs/{job_id}")
    async def public_job(job_id: UUID) -> JobV1:
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_get_public_job(job_id, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            return _dto(result.value)

    return router
