"""HTTP endpoint: GET /me — echoes the authenticated principal from the JWT claim.

The tenant for /me comes ONLY from the JWT claim (never a browser header in
prod — ADR-0008 / ADR-0009-preview). This sub-step still uses the platform's
default tenant resolver for the contextvar; the claim-vs-contextvar
consolidation is Sub-step 2.6.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Request, status

from identity_service.application.ports import AuthPrincipal
from identity_service.presentation.auth_middleware import get_request_user


def build_me_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["auth"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    @router.get("/me")
    async def me(request: Request) -> dict[str, object]:
        # Starlette exposes scope["state"] as request.state.
        principal: AuthPrincipal | None = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        # Die Adresse steht bewusst NICHT im Token — ein JWT ist kein Ort für
        # PII, und es wird bei jedem Request mitgeschickt. Sie hier zu laden
        # kostet einen Lesezugriff und gibt der Oberfläche, was sie für die
        # abgeleitete Firmendomain braucht. Zurück geht sie nur an die
        # angemeldete Person selbst.
        async with request_scope(session_factory) as (_uow, repos):
            user = await repos["users"].get_by_id(principal.user_id)
        return {
            "user_id": str(principal.user_id),
            "email": user.email.value if user is not None else None,
            # null while acting as a person; a company is only active after
            # POST /auth/company/{id} (ADR-0017). str(None) would have shipped
            # the literal "None" to the browser.
            "tenant_id": str(principal.tenant_id) if principal.tenant_id is not None else None,
            "roles": list(principal.roles),
        }

    return router


__all__ = ["build_me_router"]
