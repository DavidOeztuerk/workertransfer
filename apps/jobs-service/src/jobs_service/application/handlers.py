"""Commands, Queries und ihre Handler.

Alles Schreibende gehört einem Unternehmen; gelesen wird öffentlich, wenn es
veröffentlicht ist. Der Consent-Ledger kommt nicht vor — eine Ausschreibung ist
eine Aussage des Unternehmens über sich selbst.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from jobs_service.domain.job import EmploymentType, Job, RemoteMode

__all__ = [
    "MAX_PAGE_SIZE",
    "CreateJobCommand",
    "JobNotFound",
    "PublishJobCommand",
    "SearchJobsQuery",
    "UpdateJobCommand",
    "handle_close_job",
    "handle_create_job",
    "handle_get_public_job",
    "handle_list_own_jobs",
    "handle_publish_job",
    "handle_search_jobs",
    "handle_update_job",
]

MAX_PAGE_SIZE = 50
DEFAULT_PAGE_SIZE = 20


class JobNotFound(DomainError):
    """Fremdes Unternehmen ODER nicht vorhanden — von außen dasselbe.

    Ein eigener Fehler für „gibt es, gehört aber jemand anderem" wäre ein
    Orakel darüber, welche Unternehmen wie viele Stellen ausschreiben — im
    Wettbewerb eine Information, die etwas wert ist.
    """

    def __init__(self) -> None:
        super().__init__("job_not_found", "No such job")


@dataclass(frozen=True, slots=True)
class CreateJobCommand:
    tenant_id: UUID
    title: str
    description: str
    location: str
    remote: str
    employment: str


@dataclass(frozen=True, slots=True)
class UpdateJobCommand:
    """Wie `CreateJobCommand`, aber mit Pflicht-`job_id`.

    Ein gemeinsamer Typ mit `job_id: UUID | None` wäre kürzer und würde einen
    `assert` im Handler erzwingen — und `assert` fällt unter `python -O` weg,
    also genau dort, wo die Zusicherung zählt. Zwei Typen sagen dasselbe, ohne
    dass jemand sie glauben muss.
    """

    job_id: UUID
    tenant_id: UUID
    title: str
    description: str
    location: str
    remote: str
    employment: str


async def handle_create_job(
    cmd: CreateJobCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Job]:
    try:
        job = Job.draft(
            tenant_id=cmd.tenant_id,
            title=cmd.title,
            description=cmd.description,
            location=cmd.location,
            remote=RemoteMode(cmd.remote),
            employment=EmploymentType(cmd.employment),
            now=deps["clock"].now(),
        )
    except DomainError as exc:
        return Result.fail(exc)
    await repos["jobs"].add(job)
    return Result.ok(job)


async def _owned(job_id: UUID, tenant_id: UUID, repos: dict[str, Any]) -> Job | None:
    job: Job | None = await repos["jobs"].get(job_id)
    # Nicht vorhanden und nicht meins sind von außen dasselbe.
    if job is None or job.tenant_id != tenant_id:
        return None
    return job


async def handle_update_job(
    cmd: UpdateJobCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Job]:
    job = await _owned(cmd.job_id, cmd.tenant_id, repos)
    if job is None:
        return Result.fail(JobNotFound())
    try:
        job.update(
            title=cmd.title,
            description=cmd.description,
            location=cmd.location,
            remote=RemoteMode(cmd.remote),
            employment=EmploymentType(cmd.employment),
            now=deps["clock"].now(),
        )
    except DomainError as exc:
        return Result.fail(exc)
    await repos["jobs"].save(job)
    return Result.ok(job)


@dataclass(frozen=True, slots=True)
class PublishJobCommand:
    job_id: UUID
    tenant_id: UUID


async def handle_publish_job(
    cmd: PublishJobCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Job]:
    return await _transition(cmd, deps=deps, repos=repos, publish=True)


async def handle_close_job(
    cmd: PublishJobCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Job]:
    return await _transition(cmd, deps=deps, repos=repos, publish=False)


async def _transition(
    cmd: PublishJobCommand, *, deps: dict[str, Any], repos: dict[str, Any], publish: bool
) -> Result[Job]:
    job = await _owned(cmd.job_id, cmd.tenant_id, repos)
    if job is None:
        return Result.fail(JobNotFound())
    now = deps["clock"].now()
    try:
        if publish:
            job.publish(now=now)
        else:
            job.close(now=now)
    except DomainError as exc:
        return Result.fail(exc)
    await repos["jobs"].save(job)
    return Result.ok(job)


async def handle_list_own_jobs(tenant_id: UUID, *, repos: dict[str, Any]) -> list[Job]:
    jobs: list[Job] = await repos["jobs"].for_tenant(tenant_id)
    return jobs


async def handle_get_public_job(job_id: UUID, *, repos: dict[str, Any]) -> Result[Job]:
    job: Job | None = await repos["jobs"].get(job_id)
    # Ein Entwurf und eine geschlossene sind für die Öffentlichkeit dasselbe wie
    # eine, die es nicht gibt.
    if job is None or not job.is_public:
        return Result.fail(JobNotFound())
    return Result.ok(job)


@dataclass(frozen=True, slots=True)
class SearchJobsQuery:
    query: str | None = None
    location: str | None = None
    remote: str | None = None
    employment: str | None = None
    limit: int = DEFAULT_PAGE_SIZE
    cursor: str | None = None


async def handle_search_jobs(
    query: SearchJobsQuery, *, repos: dict[str, Any]
) -> tuple[list[Job], str | None]:
    limit = max(1, min(query.limit or DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE))
    result: tuple[list[Job], str | None] = await repos["jobs"].search(
        query=query.query,
        location=query.location,
        remote=query.remote,
        employment=query.employment,
        limit=limit,
        cursor=query.cursor,
    )
    return result
