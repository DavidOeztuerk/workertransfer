"""HTTP-Endpunkte für Unternehmen: anlegen und die eigenen auflisten.

`company`, nicht `tenant`: an der öffentlichen Grenze steht das Domänenwort.
Datenbank und JWT-Claim behalten `tenant`, weil das Wort dort in drei Tabellen
und in einem serviceübergreifenden Vertrag steckt (ADR-0017, Spec §2).
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Request, status
from worker_contracts import CompanyV1, CreateCompanyV1, MembershipV1

from identity_service.application.commands import (
    CreateCompanyCommand,
    ListMembershipsQuery,
    handle_create_company,
    handle_list_memberships,
)
from identity_service.domain.company import (
    AccountNotConfirmed,
    DomainAlreadyClaimed,
    PublicEmailDomain,
)
from identity_service.presentation.auth_middleware import get_request_user

__all__ = ["build_company_router"]


def build_company_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["companies"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _require_user(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    @router.post("/companies", status_code=status.HTTP_201_CREATED)
    async def create_company(body: CreateCompanyV1, request: Request) -> CompanyV1:
        """Legt ein Unternehmen an. Die Domain steht NICHT im Body.

        Sie wird aus der bestätigten Adresse des Erstellers abgeleitet — der
        Client benennt nur den Namen. Was er nicht senden kann, kann er nicht
        fälschen (ADR-0017/0018).
        """
        principal = _require_user(request)
        cmd = CreateCompanyCommand(user_id=principal.user_id, name=body.name)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_create_company(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err, AccountNotConfirmed):
                raise HTTPException(status.HTTP_403_FORBIDDEN, err.message)
            if isinstance(err, PublicEmailDomain):
                raise HTTPException(status.HTTP_422_UNPROCESSABLE_ENTITY, err.message)
            if isinstance(err, DomainAlreadyClaimed):
                raise HTTPException(status.HTTP_409_CONFLICT, err.message)
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        company = result.value
        return CompanyV1(id=company.id, name=company.name, domain=company.domain.value)

    @router.get("/me/companies")
    async def my_companies(request: Request) -> list[MembershipV1]:
        principal = _require_user(request)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_list_memberships(
                ListMembershipsQuery(user_id=principal.user_id), deps=deps, repos=repos
            )
        if not result.is_success:
            raise HTTPException(status.HTTP_400_BAD_REQUEST, "invalid request")
        return [
            MembershipV1(id=view.tenant_id, name=view.name, domain=view.domain, role=str(view.role))
            for view in result.value
        ]

    return router
