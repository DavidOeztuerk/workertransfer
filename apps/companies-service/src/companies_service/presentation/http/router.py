"""HTTP-Endpunkte für Arbeitgeberprofile.

Ein Profil ist die Selbstdarstellung eines Unternehmens. Es öffentlich zu
machen ist sein Zweck; es hinter eine Anmeldung zu legen wäre das Gegenteil —
genau wie bei den Stellen.
"""

from __future__ import annotations

from typing import Any
from uuid import UUID

from companies_service.application.handlers import (
    SaveCompanyProfileCommand,
    handle_get_own_profile,
    handle_get_public_profile,
    handle_save_profile,
)
from companies_service.domain.company_profile import CompanyProfile
from fastapi import APIRouter, HTTPException, Request, status
from worker_auth import get_request_user
from worker_contracts import CompanyProfileV1, SaveCompanyProfileV1

__all__ = ["build_router"]


def _dto(profile: CompanyProfile) -> CompanyProfileV1:
    return CompanyProfileV1(
        tenant_id=profile.tenant_id,
        display_name=profile.display_name,
        about=profile.about,
        website=profile.website,
        locations=list(profile.locations),
        benefits=list(profile.benefits),
        updated_at=profile.updated_at,
    )


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["companies"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _company(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        if principal.tenant_id is None:
            # Aussage über den Aufrufer, nicht über ein fremdes Unternehmen.
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "editing a company profile requires an active company",
            )
        return principal.tenant_id

    @router.put("/companies/me/profile")
    async def save_profile(body: SaveCompanyProfileV1, request: Request) -> CompanyProfileV1:
        """Jedes MITGLIED darf das — dieselbe Abwägung wie bei den Stellen.

        Das Rollensystem kennt zwei Rollen, und `admin` heißt „verwaltet die
        Mannschaft". Inhalte sind die Arbeit der Mitglieder.
        """
        command = SaveCompanyProfileCommand(
            tenant_id=_company(request),
            display_name=body.display_name,
            about=body.about,
            website=body.website,
            locations=body.locations,
            benefits=body.benefits,
        )
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_save_profile(command, deps=deps, repos=repos)
            if not result.is_success:
                error = result.error
                message = error.message if error is not None else "invalid profile"
                raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)
            await uow.commit()
            return _dto(result.value)

    @router.get("/companies/me/profile")
    async def own_profile(request: Request) -> CompanyProfileV1 | None:
        """`null` statt 404: „noch keins" ist ein Zustand, kein Fehler.

        Die Oberfläche zeigt darauf ein leeres Formular.
        """
        tenant_id = _company(request)
        async with request_scope(session_factory) as (_uow, repos):
            profile = await handle_get_own_profile(tenant_id, repos=repos)
            return None if profile is None else _dto(profile)

    @router.get("/companies/{tenant_id}/profile")
    async def public_profile(tenant_id: UUID) -> CompanyProfileV1:
        """Öffentlich, ohne Anmeldung.

        `404`, solange nichts angelegt wurde — dann bleibt eine Stelle anonym.
        Ein Profil zu erzwingen, bevor jemand ausschreiben darf, wäre eine
        Kopplung zwischen zwei Diensten für eine Regel, die niemand verlangt
        hat.
        """
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_get_public_profile(tenant_id, repos=repos)
            if not result.is_success:
                raise HTTPException(status.HTTP_404_NOT_FOUND, "No such company profile")
            return _dto(result.value)

    return router
