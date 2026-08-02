"""HTTP-Endpunkte für Portfolios.

Das eigene steht jeder angemeldeten Person offen. Ein fremdes verlangt einen
aktiven Tenant (nur Unternehmen lesen) und die Einwilligung der Person.
"""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from portfolio_service.application.handlers import (
    GetPortfolioQuery,
    SaveMyPortfolioCommand,
    handle_get_my_portfolio,
    handle_get_visible_portfolio,
    handle_save_my_portfolio,
)
from portfolio_service.domain.portfolio import Portfolio, PortfolioItem
from portfolio_service.infrastructure.consent import ConsentUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import PortfolioItemV1, PortfolioV1, SavePortfolioV1
from worker_core import DomainError

__all__ = ["build_router"]

#: Eine Antwort für „gibt es nicht" und „ist nicht freigegeben". Sie darf sich
#: zwischen den Fällen nicht unterscheiden — sonst wäre der Statuscode ein
#: Orakel über jede geratene UUID (ADR-0020 §1).
_NOT_VISIBLE = "No such portfolio"


def _to_domain(dto: PortfolioItemV1) -> PortfolioItem:
    return PortfolioItem(
        title=dto.title, summary=dto.summary, url=dto.url, role=dto.role, year=dto.year
    )


def _dto(portfolio: Portfolio) -> PortfolioV1:
    return PortfolioV1(
        subject_id=portfolio.subject_id,
        items=[
            PortfolioItemV1(
                title=entry.title,
                summary=entry.summary,
                url=entry.url,
                role=entry.role,
                year=entry.year,
            )
            for entry in portfolio.items
        ],
        updated_at=portfolio.updated_at,
    )


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["portfolios"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _principal(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    def _bearer(request: Request) -> str:
        """Header zuerst, sonst das Cookie — die Oberfläche sieht das
        httpOnly-Token nie und kann es nur so zurückgeben."""
        token = resolve_token(request.scope)
        if token is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return token

    @router.put("/portfolios/me")
    async def save_my_portfolio(body: SavePortfolioV1, request: Request) -> PortfolioV1:
        subject_id = _principal(request).sub
        try:
            # Die Umwandlung wirft dieselben DomainErrors wie das Aggregat — ein
            # `javascript:`-Link kommt hier heraus, nicht erst tiefer.
            command = SaveMyPortfolioCommand(
                subject_id=subject_id, items=[_to_domain(entry) for entry in body.items]
            )
        except DomainError as exc:
            raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, exc.message) from exc

        async with request_scope(session_factory) as (uow, repos):
            result = await handle_save_my_portfolio(command, deps=deps, repos=repos)
            if not result.is_success:
                error = result.error
                message = error.message if error is not None else "invalid portfolio"
                raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)
            await uow.commit()
            return _dto(result.value)

    @router.get("/portfolios/me")
    async def get_my_portfolio(request: Request) -> PortfolioV1 | None:
        """`null` statt 404: „noch keines angelegt" ist ein Zustand."""
        subject_id = _principal(request).sub
        async with request_scope(session_factory) as (_uow, repos):
            portfolio = await handle_get_my_portfolio(subject_id, repos=repos)
            return None if portfolio is None else _dto(portfolio)

    @router.get("/portfolios/{subject_id}")
    async def get_visible_portfolio(subject_id: UUID, request: Request) -> PortfolioV1:
        principal = _principal(request)
        if principal.tenant_id is None:
            # Aussage über den Aufrufer, nicht über das Ziel — verrät nichts.
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "reading other portfolios requires an active company",
            )
        query = GetPortfolioQuery(subject_id=subject_id, bearer=_bearer(request))
        async with request_scope(session_factory) as (_uow, repos):
            try:
                result = await handle_get_visible_portfolio(query, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                # Weder 404 noch anzeigen: beides wäre eine Behauptung über die
                # Person, die in diesem Moment niemand treffen kann.
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
            return _dto(result.value)

    return router
