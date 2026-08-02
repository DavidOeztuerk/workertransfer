"""HTTP-Endpunkte für den Marktstatus, seine Freigabe und die Vorgänge."""

from __future__ import annotations

import asyncio
from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from transfer_service.application.handlers import (
    AlreadyRequested,
    AlreadyRunning,
    AnswerRequestCommand,
    ExpressInterestCommand,
    GetMarketStatusQuery,
    MakeOfferCommand,
    NotApproachable,
    RequestMarketStatusCommand,
    RevokeMarketAccessCommand,
    SaveMarketStatusCommand,
    StatusNotVisible,
    TransferNotFound,
    handle_answer_request,
    handle_company_action,
    handle_express_interest,
    handle_get_my_status,
    handle_get_visible_status,
    handle_list_for_subject,
    handle_list_for_tenant,
    handle_list_requests_for_subject,
    handle_list_requests_for_tenant,
    handle_make_offer,
    handle_person_action,
    handle_request_market_status,
    handle_revoke_market_access,
    handle_save_status,
)
from transfer_service.domain.market_status import MarketStatus
from transfer_service.domain.request import AlreadyAnswered, MarketRequest
from transfer_service.domain.transfer import NotYours, Transfer, TransitionNotAllowed
from transfer_service.infrastructure.consent import ConsentUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import (
    ExpressInterestV1,
    MakeOfferV1,
    MarketRequestV1,
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


def _request_dto(request: MarketRequest, *, active: bool | None) -> MarketRequestV1:
    return MarketRequestV1(
        id=request.id,
        subject_id=request.subject_id,
        tenant_id=request.tenant_id,
        status=str(request.status),  # type: ignore[arg-type]
        created_at=request.created_at,
        answered_at=request.answered_at,
        active=active,
    )


def _request_http(error: Any) -> HTTPException:
    if isinstance(error, StatusNotVisible):
        # Nicht vorhanden, nicht freigegeben, nicht meins — alles dasselbe.
        raise HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
    if isinstance(error, AlreadyRequested | AlreadyAnswered):
        return HTTPException(status.HTTP_409_CONFLICT, error.message)
    message = error.message if error is not None else "invalid request"
    return HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)


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

    def _company(request: Request) -> Any:
        principal = _principal(request)
        if principal.tenant_id is None:
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "this action requires an active company",
            )
        return principal.tenant_id

    @router.post("/market/{subject_id}/requests", status_code=status.HTTP_201_CREATED)
    async def request_status(subject_id: UUID, request: Request) -> MarketRequestV1:
        """„Darf ich sehen, ob du gerade zuhörst?"

        Die leichtere der beiden Fragen — die schwerere ist der Vorgang selbst.
        Sie zu beantworten kostet nichts: wer `unavailable` ist und freigibt,
        zeigt genau das, und niemand wurde gestört.
        """
        principal = _principal(request)
        command = RequestMarketStatusCommand(
            subject_id=subject_id,
            tenant_id=_company(request),
            requested_by=principal.sub,
            bearer=_bearer(request),
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_request_market_status(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _request_http(result.error)
            await uow.commit()
            # Das anfragende Unternehmen bekommt kein `active`: es hat die
            # Antwort schon in Form des Status, den es sieht oder nicht sieht.
            return _request_dto(result.value, active=None)

    @router.get("/market/me/requests")
    async def my_requests(request: Request) -> list[MarketRequestV1]:
        """Wer hat gefragt — und was gilt gerade.

        `active` kommt frisch aus dem Ledger und kann von `status` abweichen:
        nach einem Widerruf bleibt `GRANTED` stehen, `active` fällt auf `false`.
        Genau deshalb steht die Berechtigung nicht im Vorgang.
        """
        subject_id = _principal(request).sub
        bearer = _bearer(request)
        async with request_scope(session_factory) as (_uow, repos):
            requests = await handle_list_requests_for_subject(subject_id, repos=repos)
        granted = [r for r in requests if str(r.status) == "GRANTED"]
        try:
            # Nur für erteilte Anfragen fragen: für PENDING und DECLINED steht
            # die Antwort fest, und jeder Aufruf kostet einen Round-Trip.
            verdicts = await asyncio.gather(
                *(
                    deps["consent"].may_see(r.subject_id, tenant_id=r.tenant_id, bearer=bearer)
                    for r in granted
                )
            )
        except ConsentUnavailable as exc:
            raise HTTPException(
                status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
            ) from exc
        active_by_id = dict(zip((r.id for r in granted), verdicts, strict=True))
        return [_request_dto(r, active=active_by_id.get(r.id, False)) for r in requests]

    @router.get("/market/requests")
    async def company_requests(request: Request) -> list[MarketRequestV1]:
        """Auch abgelehnte bleiben sichtbar.

        Sonst sähen „abgelehnt" und „nie gefragt" gleich aus — und dann fragt
        jemand erneut, im guten Glauben.
        """
        tenant_id = _company(request)
        async with request_scope(session_factory) as (_uow, repos):
            requests = await handle_list_requests_for_tenant(tenant_id, repos=repos)
        return [_request_dto(r, active=None) for r in requests]

    async def _answer(request_id: UUID, request: Request, *, grant: bool) -> MarketRequestV1:
        command = AnswerRequestCommand(
            request_id=request_id,
            actor_id=_principal(request).sub,
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
                raise _request_http(result.error)
            await uow.commit()
            return _request_dto(result.value, active=grant)

    @router.post("/market/requests/{request_id}/grant")
    async def grant_request(request_id: UUID, request: Request) -> MarketRequestV1:
        return await _answer(request_id, request, grant=True)

    @router.post("/market/requests/{request_id}/decline")
    async def decline_request(request_id: UUID, request: Request) -> MarketRequestV1:
        return await _answer(request_id, request, grant=False)

    @router.post("/market/requests/{request_id}/revoke")
    async def revoke_access(request_id: UUID, request: Request) -> MarketRequestV1:
        """Der Widerruf wirkt im Ledger, nicht im Vorgang.

        Ein laufender Transfer-Vorgang bleibt bestehen: er hat seine eigene Tür
        und seine eigene Absage.
        """
        command = RevokeMarketAccessCommand(
            request_id=request_id,
            actor_id=_principal(request).sub,
            bearer=_bearer(request),
        )
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_revoke_market_access(command, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise _request_http(result.error)
            await uow.commit()
            return _request_dto(result.value, active=False)

    # Reihenfolge ist hier tragend: `/market/requests` muss VOR
    # `/market/{subject_id}` stehen, sonst schluckt der Platzhalter den
    # festen Pfad und „requests" landet als UUID im Validator.
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
