"""HTTP-Endpunkte für den Marktstatus."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from transfer_service.application.handlers import (
    AlreadyRunning,
    ExpressInterestCommand,
    GetMarketStatusQuery,
    MakeOfferCommand,
    NotApproachable,
    SaveMarketStatusCommand,
    TransferNotFound,
    handle_company_action,
    handle_express_interest,
    handle_get_my_status,
    handle_get_visible_status,
    handle_list_for_subject,
    handle_list_for_tenant,
    handle_make_offer,
    handle_person_action,
    handle_save_status,
)
from transfer_service.domain.market_status import MarketStatus
from transfer_service.domain.transfer import NotYours, Transfer, TransitionNotAllowed
from transfer_service.infrastructure.consent import ConsentUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import (
    ExpressInterestV1,
    MakeOfferV1,
    MarketStatusV1,
    SaveMarketStatusV1,
    TransferV1,
)

__all__ = ["build_router"]

#: Eine Antwort für „gibt es nicht" und „ist nicht freigegeben". Der Unterschied
#: wäre hier besonders teuer: schon die Existenz der Aussage verrät etwas.
_NOT_VISIBLE = "No such market status"


def _dto(status_: MarketStatus) -> MarketStatusV1:
    return MarketStatusV1(
        subject_id=status_.subject_id,
        availability=str(status_.availability),  # type: ignore[arg-type]
        employed=status_.employed,
        note=status_.note,
        # Abgeleitet mitgeschickt: sonst reimt sich jeder Client die Regel
        # selbst zusammen, und irgendeiner reimt sie falsch.
        is_approachable=status_.is_approachable,
        updated_at=status_.updated_at,
    )


def _transfer_dto(transfer: Transfer) -> TransferV1:
    return TransferV1(
        id=transfer.id,
        subject_id=transfer.subject_id,
        tenant_id=transfer.tenant_id,
        status=str(transfer.status),  # type: ignore[arg-type]
        requires_release=transfer.requires_release,
        release_confirmed=transfer.release_confirmed,
        message=transfer.message,
        offer_note=transfer.offer_note,
        offer_start_on=transfer.offer_start_on,
        offer_fee_cents=transfer.offer_fee_cents,
        created_at=transfer.created_at,
        updated_at=transfer.updated_at,
    )


def _transfer_http(error: Any) -> HTTPException:
    if isinstance(error, TransferNotFound | NotYours):
        return HTTPException(status.HTTP_404_NOT_FOUND, "No such transfer")
    if isinstance(error, NotApproachable):
        # Kein Status, keine Freigabe, oder „gerade nicht" — alles dasselbe nach
        # außen. Sonst wäre der Endpunkt ein Orakel darüber, wer zuhört.
        return HTTPException(status.HTTP_404_NOT_FOUND, "No such person")
    if isinstance(error, AlreadyRunning | TransitionNotAllowed):
        # Die Eingabe ist in Ordnung, der Zustand passt nicht.
        return HTTPException(status.HTTP_409_CONFLICT, error.message)
    message = error.message if error is not None else "invalid transfer"
    return HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["market"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _principal(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    def _bearer(request: Request) -> str:
        token = resolve_token(request.scope)
        if token is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return token

    @router.put("/market/me")
    async def save_status(body: SaveMarketStatusV1, request: Request) -> MarketStatusV1:
        command = SaveMarketStatusCommand(
            subject_id=_principal(request).sub,
            availability=body.availability,
            employed=body.employed,
            note=body.note,
        )
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_save_status(command, deps=deps, repos=repos)
            if not result.is_success:
                error = result.error
                message = error.message if error is not None else "invalid status"
                raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)
            await uow.commit()
            return _dto(result.value)

    @router.get("/market/me")
    async def my_status(request: Request) -> MarketStatusV1:
        """Nie `null`: „nichts gesagt" IST ein Zustand, nämlich `unavailable`."""
        subject_id = _principal(request).sub
        async with request_scope(session_factory) as (_uow, repos):
            return _dto(await handle_get_my_status(subject_id, deps=deps, repos=repos))

    @router.get("/market/{subject_id}")
    async def visible_status(subject_id: UUID, request: Request) -> MarketStatusV1:
        principal = _principal(request)
        if principal.tenant_id is None:
            # Aussage über den Aufrufer, nicht über das Ziel.
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "reading a market status requires an active company",
            )
        query = GetMarketStatusQuery(
            subject_id=subject_id, tenant_id=principal.tenant_id, bearer=_bearer(request)
        )
        async with request_scope(session_factory) as (_uow, repos):
            try:
                result = await handle_get_visible_status(query, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                # Weder zeigen noch leugnen: beides wäre eine Behauptung über
                # die Person, die niemand treffen kann.
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
            return _dto(result.value)

    def _company(request: Request) -> Any:
        principal = _principal(request)
        if principal.tenant_id is None:
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "this action requires an active company",
            )
        return principal.tenant_id

    @router.post("/transfers", status_code=status.HTTP_201_CREATED)
    async def express_interest(body: ExpressInterestV1, request: Request) -> TransferV1:
        """Ein Unternehmen zeigt Interesse.

        Voraussetzung: der Marktstatus ist diesem Unternehmen freigegeben UND
        die Person ist ansprechbar. `unavailable` heißt nein, auch mit Freigabe
        — die Freigabe erlaubt zu sehen, nicht zu stören.
        """
        command = ExpressInterestCommand(
            subject_id=body.subject_id,
            tenant_id=_company(request),
            message=body.message,
            bearer=_bearer(request),
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_express_interest(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _transfer_http(result.error)
            await uow.commit()
            return _transfer_dto(result.value)

    @router.get("/transfers/me")
    async def my_transfers(request: Request) -> list[TransferV1]:
        subject_id = _principal(request).sub
        async with request_scope(session_factory) as (_uow, repos):
            transfers = await handle_list_for_subject(subject_id, repos=repos)
        return [_transfer_dto(entry) for entry in transfers]

    @router.get("/transfers")
    async def company_transfers(request: Request) -> list[TransferV1]:
        tenant_id = _company(request)
        async with request_scope(session_factory) as (_uow, repos):
            transfers = await handle_list_for_tenant(tenant_id, repos=repos)
        return [_transfer_dto(entry) for entry in transfers]

    async def _person(transfer_id: UUID, request: Request, action: str) -> TransferV1:
        subject_id = _principal(request).sub
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_person_action(
                transfer_id, subject_id, action, deps=deps, repos=repos
            )
            if not result.is_success:
                raise _transfer_http(result.error)
            await uow.commit()
            return _transfer_dto(result.value)

    async def _company_move(transfer_id: UUID, request: Request, action: str) -> TransferV1:
        tenant_id = _company(request)
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_company_action(
                transfer_id, tenant_id, action, deps=deps, repos=repos
            )
            if not result.is_success:
                raise _transfer_http(result.error)
            await uow.commit()
            return _transfer_dto(result.value)

    # Getrennte Endpunkte statt eines `PATCH status`: jeder Übergang gehört
    # einer Seite, und ein gemeinsamer müsste bei jedem Aufruf herausfinden, wer
    # gerade was darf. Getrennt steht es in der URL.

    @router.post("/transfers/{transfer_id}/accept-talk")
    async def accept_talk(transfer_id: UUID, request: Request) -> TransferV1:
        return await _person(transfer_id, request, "accept_talk")

    @router.post("/transfers/{transfer_id}/accept-offer")
    async def accept_offer(transfer_id: UUID, request: Request) -> TransferV1:
        return await _person(transfer_id, request, "accept_offer")

    @router.post("/transfers/{transfer_id}/confirm-release")
    async def confirm_release(transfer_id: UUID, request: Request) -> TransferV1:
        """Die Person bestätigt, dass ihr Arbeitgeber sie gehen lässt.

        Die Plattform prüft das nicht und kann es nicht — sie kennt weder den
        Arbeitgeber noch den Vertrag. Was der Schritt leistet, ist, die Frage zu
        stellen und die Antwort festzuhalten.
        """
        return await _person(transfer_id, request, "confirm_release")

    @router.post("/transfers/{transfer_id}/decline")
    async def decline(transfer_id: UUID, request: Request) -> TransferV1:
        """Immer möglich, aus jedem laufenden Zustand."""
        return await _person(transfer_id, request, "decline")

    @router.post("/transfers/{transfer_id}/offer")
    async def make_offer(transfer_id: UUID, body: MakeOfferV1, request: Request) -> TransferV1:
        command = MakeOfferCommand(
            transfer_id=transfer_id,
            tenant_id=_company(request),
            note=body.note,
            start_on=body.start_on,
            fee_cents=body.fee_cents,
        )
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_make_offer(command, deps=deps, repos=repos)
            if not result.is_success:
                raise _transfer_http(result.error)
            await uow.commit()
            return _transfer_dto(result.value)

    @router.post("/transfers/{transfer_id}/complete")
    async def complete(transfer_id: UUID, request: Request) -> TransferV1:
        return await _company_move(transfer_id, request, "complete")

    @router.post("/transfers/{transfer_id}/withdraw")
    async def withdraw(transfer_id: UUID, request: Request) -> TransferV1:
        return await _company_move(transfer_id, request, "withdraw")

    return router
