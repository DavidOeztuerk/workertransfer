"""Commands, Queries und ihre Handler.

Die Reihenfolge beim Absenden ist die eigentliche Entscheidung: erst der
Ledger, dann der Vorgang, dann der Commit (wie in 3.3). Schlägt der Ledger
fehl, wird nichts committet. Gelingt er und scheitert der Commit, hätte das
Unternehmen Zugriff ohne sichtbare Bewerbung — deshalb widerruft das
Zurückziehen bedingungslos, und jeder Ausgang führt in einen sauberen Zustand.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from applications_service.domain.application import (
    Application,
    ApplicationStatus,
    SharedArtifacts,
)
from applications_service.infrastructure.consent import capabilities_for

__all__ = [
    "AdvanceApplicationCommand",
    "ApplicationNotFound",
    "JobNotOpen",
    "SubmitApplicationCommand",
    "UnknownStatus",
    "WithdrawApplicationCommand",
    "handle_advance",
    "handle_list_for_job",
    "handle_list_mine",
    "handle_submit",
    "handle_withdraw",
]


class ApplicationNotFound(DomainError):
    """Nicht vorhanden ODER nicht deins — von außen dasselbe."""

    def __init__(self) -> None:
        super().__init__("application_not_found", "No such application")


class UnknownStatus(DomainError):
    def __init__(self) -> None:
        super().__init__("unknown_status", "That is not a status an application can have")


class JobNotOpen(DomainError):
    """Existiert nicht, ist ein Entwurf, oder ist geschlossen.

    Der Jobs-Service hält die drei ununterscheidbar; hier bleibt das so.
    """

    def __init__(self) -> None:
        super().__init__("job_not_open", "No such job")


@dataclass(frozen=True, slots=True)
class SubmitApplicationCommand:
    job_id: UUID
    subject_id: UUID
    message: str
    shares_resume: bool
    shares_portfolio: bool
    bearer: str


async def handle_submit(
    cmd: SubmitApplicationCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Application]:
    job = await deps["jobs"].public_job(cmd.job_id)
    if job is None:
        return Result.fail(JobNotOpen())

    now = deps["clock"].now()
    shared = SharedArtifacts(resume=cmd.shares_resume, portfolio=cmd.shares_portfolio)
    existing: Application | None = await repos["applications"].find(cmd.job_id, cmd.subject_id)

    try:
        if existing is None:
            application = Application.submit(
                job_id=job.id,
                # Einmal aus der Stelle übernommen und danach nicht mehr
                # angefasst — eine Stelle wechselt nicht das Unternehmen.
                tenant_id=job.tenant_id,
                subject_id=cmd.subject_id,
                message=cmd.message,
                shared=shared,
                now=now,
            )
        else:
            existing.resubmit(message=cmd.message, shared=shared, now=now)
            application = existing
    except DomainError as exc:
        return Result.fail(exc)

    # Erst der Ledger: schlägt er fehl, fliegt ConsentUnavailable durch und es
    # wird nichts committet. Ein Vorgang ohne die zugehörige Freigabe wäre eine
    # Bewerbung, die das Unternehmen nicht lesen kann — und andersherum wäre es
    # schlimmer.
    await deps["consent"].grant_all(
        cmd.subject_id,
        capabilities_for(application.tenant_id, resume=shared.resume, portfolio=shared.portfolio),
        bearer=cmd.bearer,
    )
    if existing is None:
        await repos["applications"].add(application)
    else:
        await repos["applications"].save(application)
    return Result.ok(application)


@dataclass(frozen=True, slots=True)
class WithdrawApplicationCommand:
    application_id: UUID
    subject_id: UUID
    bearer: str


async def handle_withdraw(
    cmd: WithdrawApplicationCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Application]:
    application: Application | None = await repos["applications"].get(cmd.application_id)
    # Nicht vorhanden und nicht meins sind von außen dasselbe.
    if application is None or application.subject_id != cmd.subject_id:
        return Result.fail(ApplicationNotFound())
    try:
        application.withdraw(by=cmd.subject_id, now=deps["clock"].now())
    except DomainError as exc:
        return Result.fail(exc)

    # Bedingungslos widerrufen — auch was vielleicht gar nicht erteilt wurde.
    # Das schließt die Lücke, die ein geglückter Ledger-Aufruf mit
    # fehlgeschlagenem Commit hinterlassen hätte, und der Ledger verträgt einen
    # Widerruf ohne vorherige Erteilung.
    await deps["consent"].revoke_all(
        cmd.subject_id,
        capabilities_for(application.tenant_id, resume=True, portfolio=True),
        bearer=cmd.bearer,
    )
    await repos["applications"].save(application)
    return Result.ok(application)


@dataclass(frozen=True, slots=True)
class AdvanceApplicationCommand:
    application_id: UUID
    tenant_id: UUID
    status: str


async def handle_advance(
    cmd: AdvanceApplicationCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Application]:
    application: Application | None = await repos["applications"].get(cmd.application_id)
    if application is None or application.tenant_id != cmd.tenant_id:
        return Result.fail(ApplicationNotFound())
    try:
        # Ein Statuswort, das es nicht gibt, lässt der Vertrag nicht durch —
        # aber ein Handler soll auch ohne ihn nicht abstürzen.
        wanted = ApplicationStatus(cmd.status)
    except ValueError:
        return Result.fail(UnknownStatus())
    try:
        application.advance(to=wanted, now=deps["clock"].now())
    except DomainError as exc:
        return Result.fail(exc)
    await repos["applications"].save(application)
    return Result.ok(application)


async def handle_list_mine(subject_id: UUID, *, repos: dict[str, Any]) -> list[Application]:
    applications: list[Application] = await repos["applications"].for_subject(subject_id)
    return applications


async def handle_list_for_job(
    job_id: UUID, tenant_id: UUID, *, repos: dict[str, Any]
) -> list[Application]:
    applications: list[Application] = await repos["applications"].for_job(job_id)
    # Fremde Stellen liefern nichts, statt zu verraten, dass es sie gibt.
    return [entry for entry in applications if entry.tenant_id == tenant_id]


async def handle_company_stats(tenant_id: UUID, *, repos: dict[str, Any]) -> dict[str, int]:
    """Kennzahlen über die EIGENEN Vorgänge (ADR-0026).

    Kein Consent-Aufruf, und das ist kein Versehen: gezählt wird, was das
    Unternehmen ohnehin schon einzeln sieht. Eine Einwilligung abzufragen, um
    etwas zu zählen, das bereits sichtbar ist, wäre Theater — und würde den
    Ledger mit einer Frage belasten, die er nicht beantworten soll.
    """
    counts: dict[str, int] = await repos["applications"].count_by_status(tenant_id)
    return counts
