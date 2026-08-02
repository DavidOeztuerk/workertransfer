"""Consent CQRS commands + handlers (run inside a UoW).

Per ADR-0003 the router drives the per-request UoW explicitly:

    async with request_scope(session_factory) as (uow, repos):
        result = await handle_grant(cmd, deps=deps, repos=repos)

Write handlers append the consent fact AND its audit row in the same UoW
transaction (ADR-0012): both land or neither does. There is no "consent recorded
but audit lost" state, and no orphaned audit row for a write that failed.

The check handler is read-only and opens no transaction — a negative answer is a
state, not an error.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from consent_service.domain.audit import AuditAction, AuditEvent
from consent_service.domain.consent_event import ConsentEvent, ReasonRequired
from consent_service.domain.services import ConsentState, project_state
from consent_service.domain.value_objects import Capability, ConsentAction, Reason, SubjectId

__all__ = [
    "CheckConsentQuery",
    "ConsentSubjectMismatch",
    "DeleteConsentCommand",
    "GrantConsentCommand",
    "RevokeConsentCommand",
    "handle_check",
    "handle_delete",
    "handle_grant",
    "handle_list_mine",
    "handle_my_history",
    "handle_revoke",
]

_AUDIT_ACTION: dict[ConsentAction, AuditAction] = {
    ConsentAction.GRANT: AuditAction.CONSENT_GRANT,
    ConsentAction.REVOKE: AuditAction.CONSENT_REVOKE,
    ConsentAction.DELETE: AuditAction.CONSENT_DELETE,
}


class ConsentSubjectMismatch(DomainError):
    """The caller tried to change someone else's consent."""

    def __init__(self) -> None:
        super().__init__(
            "consent_subject_mismatch",
            "A subject may only manage its own consent",
        )


def _correlation_id() -> str | None:
    # Local import keeps the application layer free of a top-level worker_platform
    # dependency at import time (same convention as identity-service).
    from worker_platform.context import get_correlation_id

    return get_correlation_id()


def _tenant_id() -> UUID | None:
    """The tenant the auth middleware resolved from the JWT claim (ADR-0009).

    None outside a request scope (CLI, tests) or when the claim is not a UUID.
    A missing tenant must not stop the consent fact from being recorded: losing
    an attribution is bad, losing the fact itself is worse.
    """
    from worker_platform.context import get_tenant_id

    raw = get_tenant_id()
    if raw is None:
        return None
    try:
        return UUID(raw)
    except ValueError:
        return None


@dataclass(frozen=True, slots=True)
class GrantConsentCommand:
    subject_id: UUID
    capability: str
    actor_id: UUID
    reason: str | None = None


@dataclass(frozen=True, slots=True)
class RevokeConsentCommand:
    subject_id: UUID
    capability: str
    actor_id: UUID
    reason: str


@dataclass(frozen=True, slots=True)
class DeleteConsentCommand:
    subject_id: UUID
    capability: str
    actor_id: UUID
    reason: str


@dataclass(frozen=True, slots=True)
class CheckConsentQuery:
    subject_id: UUID
    capability: str


async def _record(
    *,
    action: ConsentAction,
    subject_id: UUID,
    capability: str,
    actor_id: UUID,
    reason: str | None,
    deps: dict[str, Any],
    repos: dict[str, Any],
) -> Result[ConsentState]:
    """Append one fact plus its audit row, then project the resulting state."""
    try:
        # Phase 3 is strict self-management: no delegation model exists yet, so
        # an actor may only act on its own subject. Admin/guardian consent is a
        # later, deliberate decision — not something to allow by omission.
        if actor_id != subject_id:
            raise ConsentSubjectMismatch()

        subject = SubjectId(subject_id)
        cap = Capability(capability)
        parsed_reason = Reason(reason) if reason is not None else None
        now = deps["clock"].now()

        if action is ConsentAction.GRANT:
            event = ConsentEvent.grant(
                subject_id=subject,
                capability=cap,
                recorded_at=now,
                actor_id=actor_id,
                reason=parsed_reason,
            )
        else:
            # REVOKE and DELETE require a reason. The command types make it
            # non-optional, but this is checked rather than asserted: `assert` is
            # stripped under python -O, and losing the guarantee in production is
            # exactly where it matters.
            if parsed_reason is None:
                raise ReasonRequired(action)
            factory = ConsentEvent.revoke if action is ConsentAction.REVOKE else ConsentEvent.delete
            event = factory(
                subject_id=subject,
                capability=cap,
                recorded_at=now,
                actor_id=actor_id,
                reason=parsed_reason,
            )

        await repos["consent"].append(event)

        # Same UoW as the fact above — atomicity, ADR-0012. The capability name
        # is allowlisted metadata; the data it governs never is.
        await repos["audit"].append(
            AuditEvent(
                actor_id=actor_id,
                tenant_id=_tenant_id(),
                action=_AUDIT_ACTION[action],
                target_id=subject_id,
                correlation_id=_correlation_id(),
                metadata={"capability": capability},
            )
        )

        latest = await repos["consent"].latest_effective(subject, cap)
        return Result.ok(project_state([latest] if latest is not None else []))
    except DomainError as error:
        return Result.fail(error)


async def handle_grant(
    cmd: GrantConsentCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ConsentState]:
    return await _record(
        action=ConsentAction.GRANT,
        subject_id=cmd.subject_id,
        capability=cmd.capability,
        actor_id=cmd.actor_id,
        reason=cmd.reason,
        deps=deps,
        repos=repos,
    )


async def handle_revoke(
    cmd: RevokeConsentCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ConsentState]:
    return await _record(
        action=ConsentAction.REVOKE,
        subject_id=cmd.subject_id,
        capability=cmd.capability,
        actor_id=cmd.actor_id,
        reason=cmd.reason,
        deps=deps,
        repos=repos,
    )


async def handle_delete(
    cmd: DeleteConsentCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ConsentState]:
    return await _record(
        action=ConsentAction.DELETE,
        subject_id=cmd.subject_id,
        capability=cmd.capability,
        actor_id=cmd.actor_id,
        reason=cmd.reason,
        deps=deps,
        repos=repos,
    )


async def handle_check(
    query: CheckConsentQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[ConsentState]:
    """Read-only projection. Any authenticated caller may ask about any subject.

    That is the point of a ledger: a consuming service has to be able to check
    whether it may act on someone's data. Answering only reveals whether a
    capability is granted, never the data behind it.
    """
    try:
        subject = SubjectId(query.subject_id)
        cap = Capability(query.capability)
    except DomainError as error:
        return Result.fail(error)

    latest = await repos["consent"].latest_effective(subject, cap)
    return Result.ok(project_state([latest] if latest is not None else []))


async def handle_list_mine(
    subject_id: UUID, *, repos: dict[str, Any]
) -> list[tuple[Capability, ConsentEvent]]:
    """Was gerade gilt — nur für die Person selbst.

    Kein `subject_id`-Parameter von außen: der Aufrufer kann nur die eigene
    Liste holen, weil er nichts anderes angeben kann. Was man nicht angeben
    kann, kann man nicht fälschen — dieselbe Regel wie bei `tenant_id`
    (ADR-0018) und der Firmendomain (ADR-0019).

    Widerrufene und gelöschte Fähigkeiten fallen heraus: die Seite beantwortet
    „was gilt", nicht „was war". Eine Historie zeigt, wer EINMAL gefragt hat,
    und das ist mehr, als hier versprochen wird.
    """
    subject = SubjectId(subject_id)
    events: list[ConsentEvent] = list(await repos["consent"].latest_per_capability(subject))
    effective: list[tuple[Capability, ConsentEvent]] = []
    for event in events:
        # Dieselbe Reduktion wie bei `/check`, nur auf einen Strom der Länge 1
        # angewandt: die Liste darf keine zweite Auslegung dessen sein, was
        # „gilt" heißt.
        if project_state([event]).granted:
            effective.append((event.capability, event))
    effective.sort(key=lambda pair: pair[0].value)
    return effective


async def handle_my_history(subject_id: UUID, *, repos: dict[str, Any]) -> list[ConsentEvent]:
    """Jedes Ereignis der eigenen Geschichte, älteste zuerst.

    Kein `subject_id` von außen — wie bei `handle_list_mine`. Und anders als
    dort ohne Filterung auf Wirksames: eine Auskunft ist der Ort, an dem die
    Vergangenheit hingehört.
    """
    events: list[ConsentEvent] = list(await repos["consent"].stream(SubjectId(subject_id)))
    return events
