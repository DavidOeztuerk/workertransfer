"""HTTP-Endpunkte für Unternehmen: anlegen und die eigenen auflisten.

`company`, nicht `tenant`: an der öffentlichen Grenze steht das Domänenwort.
Datenbank und JWT-Claim behalten `tenant`, weil das Wort dort in drei Tabellen
und in einem serviceübergreifenden Vertrag steckt (ADR-0017, Spec §2).
"""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from worker_contracts import (
    AcceptInvitationV1,
    CompanyMemberV1,
    CompanyV1,
    CreateCompanyV1,
    InvitationV1,
    InviteMemberV1,
    MembershipV1,
)

from identity_service.application.commands import (
    AcceptInvitationCommand,
    CreateCompanyCommand,
    InviteMemberCommand,
    ListMembershipsQuery,
    OutgoingMail,
    WithdrawInvitationCommand,
    dispatch_all,
    handle_accept_invitation,
    handle_create_company,
    handle_invite_member,
    handle_list_memberships,
    handle_withdraw_invitation,
)
from identity_service.domain.company import (
    AccountNotConfirmed,
    DomainAlreadyClaimed,
    PublicEmailDomain,
)
from identity_service.domain.invitation import (
    InvitationExpired,
    InvitationInvalid,
    NotYourInvitation,
    OnlyAdminsMayInvite,
)
from identity_service.domain.membership import NotAMember
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
                raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, err.message)
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

    def _invitation_dto(invitation: Any) -> InvitationV1:
        return InvitationV1(
            id=invitation.id,
            email=invitation.email.value,
            role=str(invitation.role),
            status=str(invitation.status),
            created_at=invitation.created_at,
            expires_at=invitation.expires_at,
        )

    def _to_http(err: Any) -> HTTPException:
        """Fachliche Fehler auf Statuscodes — an einer Stelle, damit sie nicht driften."""
        if isinstance(err, NotAMember):
            # Wie eine fremde Ressource: wer nicht Mitglied ist, soll nicht
            # unterscheiden können, ob es das Unternehmen gibt.
            return HTTPException(status.HTTP_404_NOT_FOUND, "no such company")
        if isinstance(err, OnlyAdminsMayInvite):
            # Aussage über den Aufrufer, nicht über die Ressource — 403 verrät
            # hier nichts, was er nicht schon weiß.
            return HTTPException(status.HTTP_403_FORBIDDEN, err.message)
        if isinstance(err, InvitationExpired | NotYourInvitation):
            return HTTPException(status.HTTP_400_BAD_REQUEST, err.message)
        if isinstance(err, InvitationInvalid):
            return HTTPException(status.HTTP_404_NOT_FOUND, err.message)
        return HTTPException(
            status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
        )

    @router.post("/companies/{tenant_id}/invitations", status_code=status.HTTP_201_CREATED)
    async def invite_member(
        tenant_id: UUID, body: InviteMemberV1, request: Request
    ) -> InvitationV1:
        """Lädt eine Adresse ein. Nur ein Administrator darf das.

        Die Rolle des Einladenden wird aus der Datenbank gelesen, nicht aus dem
        Token: dort steht nur, FÜR welches Unternehmen jemand handelt, nicht mit
        welcher Berechtigung.

        Die Antwort ist dieselbe, ob die Adresse ein Konto hat oder nicht — sonst
        wäre der Endpunkt ein Weg, Plattformmitgliedschaft zu erfragen, ohne den
        Consent-Ledger zu fragen (product-scope.md).
        """
        principal = _require_user(request)
        cmd = InviteMemberCommand(
            tenant_id=tenant_id,
            inviter_id=principal.user_id,
            email=body.email,
            role=body.role,
        )
        outbox: list[OutgoingMail] = []
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_invite_member(cmd, deps=deps, repos=repos, outbox=outbox)
        if not result.is_success:
            raise _to_http(result.error)
        # Erst nach dem Commit: sonst ginge der Einladungslink raus, bevor die
        # Zeile existiert, auf die er zeigt.
        await dispatch_all(outbox, deps)
        return _invitation_dto(result.value)

    @router.get("/companies/{tenant_id}/invitations")
    async def open_invitations(tenant_id: UUID, request: Request) -> list[InvitationV1]:
        principal = _require_user(request)
        async with request_scope(session_factory) as (_uow, repos):
            role = await repos["memberships"].role_of(principal.user_id, tenant_id)
            if role is None:
                raise _to_http(NotAMember())
            invitations = await repos["invitations"].list_open(tenant_id)
        return [_invitation_dto(invitation) for invitation in invitations]

    @router.delete(
        "/companies/{tenant_id}/invitations/{invitation_id}",
        status_code=status.HTTP_204_NO_CONTENT,
    )
    async def withdraw_invitation(tenant_id: UUID, invitation_id: UUID, request: Request) -> None:
        principal = _require_user(request)
        cmd = WithdrawInvitationCommand(
            tenant_id=tenant_id, invitation_id=invitation_id, actor_id=principal.user_id
        )
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_withdraw_invitation(cmd, deps=deps, repos=repos)
        if not result.is_success:
            raise _to_http(result.error)

    @router.get("/companies/{tenant_id}/members")
    async def company_members(tenant_id: UUID, request: Request) -> list[CompanyMemberV1]:
        principal = _require_user(request)
        async with request_scope(session_factory) as (_uow, repos):
            role = await repos["memberships"].role_of(principal.user_id, tenant_id)
            if role is None:
                raise _to_http(NotAMember())
            members = await repos["memberships"].list_members(tenant_id)
        return [
            CompanyMemberV1(user_id=user_id, display_name=name, role=str(member_role))
            for user_id, name, member_role in members
        ]

    @router.post("/invitations/accept")
    async def accept_invitation(body: AcceptInvitationV1, request: Request) -> MembershipV1:
        """Nimmt eine Einladung an — mit dem Token UND der eingeladenen Adresse.

        Das Token allein reicht nicht: Tokens werden weitergeleitet, und wer den
        Link hat, ist nicht, wer eingeladen wurde. Die Adresse kommt aus der
        Datenbank, nicht aus dem Request.

        Der Beitritt ändert die Mitgliedschaften, nicht die laufende Sitzung: um
        für das neue Unternehmen zu handeln, wechselt man danach bewusst über
        `POST /auth/company/{id}` (ADR-0018). Ein automatischer Wechsel würde
        jemanden ungefragt aus dem Unternehmen herausbefördern, in dem er gerade
        arbeitet.
        """
        principal = _require_user(request)
        cmd = AcceptInvitationCommand(token=body.token, user_id=principal.user_id)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_accept_invitation(cmd, deps=deps, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            invitation = result.value
            company = await repos["companies"].get_by_id(invitation.tenant_id)
        return MembershipV1(
            id=invitation.tenant_id,
            name=company.name if company is not None else "",
            domain=company.domain.value if company is not None else "",
            role=str(invitation.role),
        )

    return router
