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

from resume_service.domain.request import ResumeRequest
from resume_service.domain.resume import Education, Position, Resume

__all__ = [
    "AlreadyRequested",
    "AnswerRequestCommand",
    "GetResumeQuery",
    "RequestResumeCommand",
    "ResumeNotVisible",
    "RevokeAccessCommand",
    "SaveMyResumeCommand",
    "handle_answer_request",
    "handle_get_my_resume",
    "handle_get_visible_resume",
    "handle_list_requests_for_subject",
    "handle_list_requests_for_tenant",
    "handle_request_resume",
    "handle_revoke_access",
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


class ResumeNotVisible(DomainError):
    """Nicht vorhanden ODER nicht freigegeben — von außen dasselbe (ADR-0020 §1)."""

    def __init__(self) -> None:
        super().__init__("resume_not_visible", "No such resume")


class AlreadyRequested(DomainError):
    """Einmal fragen.

    Ohne diese Regel wäre eine Ablehnung wirkungslos: wer dreimal fragen darf,
    hat kein Nein bekommen, sondern eine Verzögerung. Gilt auch nach einem
    Widerruf — der ist eine stärkere Aussage als die Ablehnung, nicht eine
    schwächere.
    """

    def __init__(self) -> None:
        super().__init__("already_requested", "This company has already asked")


@dataclass(frozen=True, slots=True)
class RequestResumeCommand:
    subject_id: UUID
    tenant_id: UUID
    requested_by: UUID
    bearer: str


async def handle_request_resume(
    cmd: RequestResumeCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ResumeRequest]:
    """Ein Unternehmen fragt nach einem Lebenslauf.

    Voraussetzung ist die Profilfreigabe, nicht die Existenz eines Lebenslaufs.
    Beides zu prüfen wäre ein Orakel: „hat schon einen CV gepflegt" ist eine
    Information über die Person, die niemand erfragen können soll.
    """
    if not await deps["consent"].may_see_profile(cmd.subject_id, bearer=cmd.bearer):
        return Result.fail(ResumeNotVisible())
    if await repos["requests"].find(cmd.subject_id, cmd.tenant_id) is not None:
        return Result.fail(AlreadyRequested())

    request = ResumeRequest.open(
        subject_id=cmd.subject_id,
        tenant_id=cmd.tenant_id,
        requested_by=cmd.requested_by,
        now=deps["clock"].now(),
    )
    await repos["requests"].add(request)
    return Result.ok(request)


@dataclass(frozen=True, slots=True)
class AnswerRequestCommand:
    request_id: UUID
    actor_id: UUID
    bearer: str
    grant: bool


async def handle_answer_request(
    cmd: AnswerRequestCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ResumeRequest]:
    request: ResumeRequest | None = await repos["requests"].get(cmd.request_id)
    # Eine fremde Anfrage-ID verhält sich wie eine fremde Subject-ID: nicht
    # vorhanden und nicht meins sind von außen dasselbe.
    if request is None or request.subject_id != cmd.actor_id:
        return Result.fail(ResumeNotVisible())

    now = deps["clock"].now()
    try:
        if cmd.grant:
            request.grant(by=cmd.actor_id, now=now)
        else:
            request.decline(by=cmd.actor_id, now=now)
    except DomainError as exc:
        return Result.fail(exc)

    if cmd.grant:
        # Erst der Ledger, dann der Vorgang: schlägt der Ledger fehl, fliegt
        # ConsentUnavailable durch und die Transaktion wird nie committet. Der
        # umgekehrte Weg könnte einen Vorgang auf GRANTED setzen, dem keine
        # Berechtigung entspricht.
        await deps["consent"].grant_resume(request.subject_id, request.tenant_id, bearer=cmd.bearer)
    await repos["requests"].save(request)
    return Result.ok(request)


@dataclass(frozen=True, slots=True)
class RevokeAccessCommand:
    request_id: UUID
    actor_id: UUID
    bearer: str


async def handle_revoke_access(
    cmd: RevokeAccessCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ResumeRequest]:
    """Widerruf — nur im Ledger, der Vorgang bleibt unangetastet.

    `GRANTED` heißt „wurde einmal erteilt". Diesen Zustand beim Widerruf zu
    ändern würde die Geschichte umschreiben; ob der Zugriff gilt, sagt ohnehin
    nur der Ledger.
    """
    request: ResumeRequest | None = await repos["requests"].get(cmd.request_id)
    if request is None or request.subject_id != cmd.actor_id:
        return Result.fail(ResumeNotVisible())
    await deps["consent"].revoke_resume(request.subject_id, request.tenant_id, bearer=cmd.bearer)
    return Result.ok(request)


@dataclass(frozen=True, slots=True)
class GetResumeQuery:
    subject_id: UUID
    tenant_id: UUID
    bearer: str


async def handle_get_visible_resume(
    query: GetResumeQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Resume]:
    resume: Resume | None = await repos["resumes"].get(query.subject_id)
    if resume is None:
        # Kein Ledger-Aufruf für einen Lebenslauf, den es nicht gibt: unnötiger
        # Round-Trip, und er meldete dem Ledger geratene Subject-IDs.
        return Result.fail(ResumeNotVisible())
    # ConsentUnavailable fliegt bewusst durch: der Router macht daraus 503.
    if not await deps["consent"].may_read_resume(
        query.subject_id, query.tenant_id, bearer=query.bearer
    ):
        return Result.fail(ResumeNotVisible())
    return Result.ok(resume)


async def handle_list_requests_for_subject(
    subject_id: UUID, *, repos: dict[str, Any]
) -> list[ResumeRequest]:
    requests: list[ResumeRequest] = await repos["requests"].for_subject(subject_id)
    return requests


async def handle_list_requests_for_tenant(
    tenant_id: UUID, *, repos: dict[str, Any]
) -> list[ResumeRequest]:
    requests: list[ResumeRequest] = await repos["requests"].for_tenant(tenant_id)
    return requests
