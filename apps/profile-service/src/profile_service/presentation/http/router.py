"""HTTP-Endpunkte für Profile.

Das eigene Profil steht jeder angemeldeten Person offen. Ein fremdes verlangt
zweierlei: einen aktiven Tenant (nur Unternehmen lesen Profile) und die
Einwilligung der betroffenen Person.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Query, Request, status
from profile_service.application.handlers import (
    GetProfileQuery,
    ListProfilesQuery,
    SaveMyProfileCommand,
    handle_get_my_profile,
    handle_get_visible_profile,
    handle_list_visible_profiles,
    handle_save_my_profile,
)
from profile_service.domain.profile import Profile
from profile_service.infrastructure.consent import ConsentUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import ProfilePageV1, ProfileV1, SaveProfileV1

__all__ = ["build_router"]


def _dto(profile: Profile) -> ProfileV1:
    return ProfileV1(
        subject_id=profile.subject_id,
        headline=profile.headline,
        bio=profile.bio,
        location=profile.location,
        remote_ok=profile.remote_ok,
        skills=list(profile.skills.value),
        updated_at=profile.updated_at,
    )


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["profiles"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _principal(request: Request) -> Any:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    def _bearer(request: Request) -> str:
        """Das Token des Aufrufers, für die Weitergabe an den Ledger.

        Bevorzugt der Header; sonst das Cookie, das der Browser schickt — die
        Oberfläche sieht das httpOnly-Token nie und kann es nur so zurückgeben.
        """
        token = resolve_token(request.scope)
        if token is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return token

    def _require_company(request: Request) -> None:
        principal = _principal(request)
        if principal.tenant_id is None:
            # Aussage über den Aufrufer, nicht über das Ziel — verrät nichts.
            raise HTTPException(
                status.HTTP_403_FORBIDDEN,
                "reading other profiles requires an active company",
            )

    @router.put("/profiles/me")
    async def save_my_profile(body: SaveProfileV1, request: Request) -> ProfileV1:
        principal = _principal(request)
        cmd = SaveMyProfileCommand(
            subject_id=principal.sub,
            headline=body.headline,
            bio=body.bio,
            location=body.location,
            remote_ok=body.remote_ok,
            skills=body.skills,
        )
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_save_my_profile(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            raise HTTPException(
                status.HTTP_422_UNPROCESSABLE_CONTENT,
                err.message if err is not None else "invalid",
            )
        return _dto(result.value)

    @router.get("/profiles/me")
    async def my_profile(request: Request) -> ProfileV1 | None:
        principal = _principal(request)
        async with request_scope(session_factory) as (_uow, repos):
            profile = await handle_get_my_profile(principal.sub, repos=repos)
        # null statt 404: „noch keins angelegt" ist ein Zustand, den die
        # Oberfläche als leeres Formular zeigt, kein Fehler.
        return _dto(profile) if profile is not None else None

    @router.get("/profiles/{subject_id}")
    async def foreign_profile(subject_id: str, request: Request) -> ProfileV1:
        from uuid import UUID

        _require_company(request)
        bearer = _bearer(request)
        try:
            parsed = UUID(subject_id)
        except ValueError:
            # Gleiche Antwort wie für ein unbekanntes Profil — eine eigene
            # Fehlermeldung für „keine gültige UUID" hilft nur beim Raten.
            raise HTTPException(status.HTTP_404_NOT_FOUND, "no such profile") from None
        try:
            async with request_scope(session_factory) as (_uow, repos):
                result = await handle_get_visible_profile(
                    GetProfileQuery(subject_id=parsed, bearer=bearer), deps=deps, repos=repos
                )
        except ConsentUnavailable:
            # Weder zeigen noch 404: wir wissen es nicht, und beides wäre eine
            # Behauptung über die Person.
            raise HTTPException(
                status.HTTP_503_SERVICE_UNAVAILABLE, "consent service unavailable"
            ) from None
        if not result.is_success:
            raise HTTPException(status.HTTP_404_NOT_FOUND, "no such profile")
        return _dto(result.value)

    @router.get("/profiles")
    async def list_profiles(
        request: Request,
        limit: int = Query(default=20, ge=1, le=50),
        cursor: str | None = Query(default=None),
    ) -> ProfilePageV1:
        _require_company(request)
        bearer = _bearer(request)
        try:
            async with request_scope(session_factory) as (_uow, repos):
                result = await handle_list_visible_profiles(
                    ListProfilesQuery(limit=limit, cursor=cursor, bearer=bearer),
                    deps=deps,
                    repos=repos,
                )
        except ConsentUnavailable:
            raise HTTPException(
                status.HTTP_503_SERVICE_UNAVAILABLE, "consent service unavailable"
            ) from None
        profiles, next_cursor = result.value
        return ProfilePageV1(items=[_dto(p) for p in profiles], next_cursor=next_cursor)

    return router
