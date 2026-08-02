"""HTTP endpoints for the Consent-Ledger.

Bodies are versioned `worker-contracts` DTOs, never domain types (ADR-0004 §1),
so a consumer never has to import this service to talk to it.

Grant/revoke/delete are POSTs even though "delete" sounds like DELETE: every one
of them *appends a fact*. Mapping them onto HTTP verbs that imply mutation of a
resource would misrepresent an append-only ledger.
"""

from __future__ import annotations

from typing import Any, Literal, cast

from fastapi import APIRouter, HTTPException, Request, status
from worker_auth import get_request_user
from worker_contracts import (
    ConsentCheckResultV1,
    ConsentCheckV1,
    ConsentGrantedV1,
    ConsentGrantV1,
    ConsentHistoryEntryV1,
    ConsentRevokeV1,
    ConsentStateV1,
)
from worker_core import DomainError

from consent_service.application.commands import (
    CheckConsentQuery,
    ConsentSubjectMismatch,
    DeleteConsentCommand,
    GrantConsentCommand,
    RevokeConsentCommand,
    handle_check,
    handle_delete,
    handle_grant,
    handle_list_mine,
    handle_my_history,
    handle_revoke,
)
from consent_service.domain.consent_event import ConsentMetadataError, ReasonRequired
from consent_service.domain.services import ConsentState
from consent_service.domain.value_objects import InvalidCapability, InvalidReason

__all__ = ["build_consent_router"]


def _actor_id(request: Request) -> Any:
    principal = get_request_user(request.scope)
    if principal is None:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
    return principal.sub


def _to_http(error: DomainError | None) -> HTTPException:
    if isinstance(error, ConsentSubjectMismatch):
        return HTTPException(status.HTTP_403_FORBIDDEN, "subject mismatch")
    if isinstance(error, ConsentMetadataError):
        return HTTPException(status.HTTP_400_BAD_REQUEST, "complementary metadata not allowed")
    if isinstance(error, (InvalidCapability, InvalidReason, ReasonRequired)):
        return HTTPException(status.HTTP_400_BAD_REQUEST, error.message)
    return HTTPException(
        status.HTTP_400_BAD_REQUEST, error.message if error is not None else "invalid request"
    )


def _state_dto(subject_id: Any, capability: str, state: ConsentState) -> ConsentStateV1:
    """Full state including the reason — only for a caller that IS the subject."""
    return ConsentStateV1(
        subject_id=subject_id,
        capability=capability,
        granted=state.granted,
        deleted=state.deleted,
        reason=state.reason,
    )


def _check_result_dto(
    subject_id: Any, capability: str, state: ConsentState
) -> ConsentCheckResultV1:
    """State without the reason — for the cross-subject read (see `/check`)."""
    return ConsentCheckResultV1(
        subject_id=subject_id,
        capability=capability,
        granted=state.granted,
        deleted=state.deleted,
    )


def build_consent_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(prefix="/consent", tags=["consent"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    @router.post("/grant")
    async def grant(body: ConsentGrantV1, request: Request) -> ConsentStateV1:
        cmd = GrantConsentCommand(
            subject_id=body.subject_id,
            capability=body.capability,
            actor_id=_actor_id(request),
            reason=body.reason,
        )
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_grant(cmd, deps=deps, repos=repos)
        if not result.is_success:
            raise _to_http(result.error)
        return _state_dto(body.subject_id, body.capability, result.value)

    @router.post("/revoke")
    async def revoke(body: ConsentRevokeV1, request: Request) -> ConsentStateV1:
        cmd = RevokeConsentCommand(
            subject_id=body.subject_id,
            capability=body.capability,
            actor_id=_actor_id(request),
            reason=body.reason,
        )
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_revoke(cmd, deps=deps, repos=repos)
        if not result.is_success:
            raise _to_http(result.error)
        return _state_dto(body.subject_id, body.capability, result.value)

    @router.post("/delete")
    async def delete(body: ConsentRevokeV1, request: Request) -> ConsentStateV1:
        cmd = DeleteConsentCommand(
            subject_id=body.subject_id,
            capability=body.capability,
            actor_id=_actor_id(request),
            reason=body.reason,
        )
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_delete(cmd, deps=deps, repos=repos)
        if not result.is_success:
            raise _to_http(result.error)
        return _state_dto(body.subject_id, body.capability, result.value)

    @router.get("/me")
    async def mine(request: Request) -> list[ConsentGrantedV1]:
        """Was gerade gilt — und zwar nur die eigenen Freigaben.

        Kein `subject_id`-Parameter, weder im Pfad noch in der Abfrage: die
        Person kann nur ihre eigene Liste holen, weil sie nichts anderes
        angeben kann. Eine fremde Liste enthielte, welche ANDEREN Unternehmen
        Zugriff haben — eine Aussage über einen Menschen, die niemand außer ihm
        treffen darf.

        Anders als `/check`, das jedem authentifizierten Aufrufer über jeden
        offensteht: dort ist die Antwort ein einzelnes Ja/Nein zu einer Frage,
        die der Aufrufer schon gestellt hat. Hier wäre sie eine Übersicht.
        """
        subject_id = _actor_id(request)
        async with request_scope(session_factory) as (_uow, repos):
            granted = await handle_list_mine(subject_id, repos=repos)
        return [
            ConsentGrantedV1(capability=capability.value, granted_at=event.recorded_at)
            for capability, event in granted
        ]

    @router.get("/me/history")
    async def my_history(request: Request) -> list[ConsentHistoryEntryV1]:
        """Die eigene Geschichte — für die Auskunft, nicht für die Übersicht.

        `/consent/me` zeigt bewusst nur, was GILT: eine Historie verrät, wer
        einmal gefragt hat, und das ist mehr, als eine Übersichtsseite
        verspricht. Hier ist es genau richtig — die Auskunft geht an die
        betroffene Person selbst.
        """
        subject_id = _actor_id(request)
        async with request_scope(session_factory) as (_uow, repos):
            events = await handle_my_history(subject_id, repos=repos)
        return [
            ConsentHistoryEntryV1(
                capability=event.capability.value,
                # `ConsentAction` ist eine StrEnum mit genau diesen drei Werten;
                # der Vertrag hält sie als Literal fest, damit ein vierter
                # Zustand hier auffällt und nicht stillschweigend durchrutscht.
                action=cast('Literal["GRANT", "REVOKE", "DELETE"]', str(event.action)),
                recorded_at=event.recorded_at,
                reason=event.reason.value if event.reason is not None else None,
            )
            for event in events
        ]

    @router.post("/check")
    async def check(body: ConsentCheckV1, request: Request) -> ConsentCheckResultV1:
        # Any authenticated caller may ask about any subject: that is what makes
        # the ledger usable as an enabler by consuming services. The answer only
        # reveals whether a capability is granted, never the data behind it —
        # hence ConsentCheckResultV1, which has no `reason`. The withdrawal
        # reason is free text the subject wrote and belongs to the subject alone.
        _actor_id(request)
        query = CheckConsentQuery(subject_id=body.subject_id, capability=body.capability)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_check(query, deps=deps, repos=repos)
        if not result.is_success:
            raise _to_http(result.error)
        return _check_result_dto(body.subject_id, body.capability, result.value)

    return router
