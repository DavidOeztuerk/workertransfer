"""HTTP-Endpunkte für den Marktstatus."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from transfer_service.application.handlers import (
    GetMarketStatusQuery,
    SaveMarketStatusCommand,
    handle_get_my_status,
    handle_get_visible_status,
    handle_save_status,
)
from transfer_service.domain.market_status import MarketStatus
from transfer_service.infrastructure.consent import ConsentUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import MarketStatusV1, SaveMarketStatusV1

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

    return router
