"""HTTP-Endpunkte für die GitHub-Verbindung."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, Response, status
from github_service.application.handlers import (
    ConnectCommand,
    NoConnection,
    NotProven,
    VisibleQuery,
    handle_connect,
    handle_disconnect,
    handle_get_mine,
    handle_get_visible,
    handle_refresh,
    handle_verify,
)
from github_service.domain.connection import GitHubConnection, challenge_description
from github_service.infrastructure.consent import ConsentUnavailable
from github_service.infrastructure.github import GitHubUnavailable
from worker_auth import get_request_user, resolve_token
from worker_contracts import ConnectGitHubV1, GitHubConnectionV1, RepositoryV1

__all__ = ["build_router"]

#: Eine Antwort für „gibt es nicht" und „ist nicht freigegeben".
_NOT_VISIBLE = "No such connection"


def _dto(connection: GitHubConnection, *, own: bool) -> GitHubConnectionV1:
    return GitHubConnectionV1(
        subject_id=connection.subject_id,
        login=connection.login,
        verified=connection.is_verified,
        # Nur in der eigenen Ansicht: die Einmalzeichenfolge nützt allein der
        # Person, die den Gist anlegt.
        challenge_description=(
            challenge_description(connection.challenge)
            if own and not connection.is_verified
            else None
        ),
        fetched_at=connection.fetched_at,
        repositories=[
            RepositoryV1(
                name=r.name,
                description=r.description,
                language=r.language,
                stars=r.stars,
                url=r.url,
                pushed_at=r.pushed_at,
            )
            for r in connection.repositories
        ],
    )


def build_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["github"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _subject(request: Request) -> UUID:
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        subject: UUID = principal.sub
        return subject

    def _bearer(request: Request) -> str:
        token = resolve_token(request.scope)
        if token is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return token

    def _to_http(error: Any) -> HTTPException:
        if isinstance(error, NotProven):
            # 422, nicht 404: die Anfrage war in Ordnung, der Nachweis fehlte.
            return HTTPException(
                status.HTTP_422_UNPROCESSABLE_CONTENT,
                "no public gist with that description was found",
            )
        if isinstance(error, NoConnection):
            return HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
        message = error.message if error is not None else "invalid request"
        return HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, message)

    @router.post("/github/me")
    async def connect(body: ConnectGitHubV1, request: Request) -> GitHubConnectionV1:
        """Den Benutzernamen nennen und die Einmalzeichenfolge bekommen.

        Hier wird NICHT bei GitHub angefragt: solange nichts bewiesen ist, gibt
        es nichts zu holen — und ein Abruf verriete nur, dass jemand nach
        diesem Konto gefragt hat.
        """
        command = ConnectCommand(subject_id=_subject(request), login=body.login)
        async with request_scope(session_factory) as (uow, repos):
            result = await handle_connect(command, deps=deps, repos=repos)
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value, own=True)

    @router.post("/github/me/verify")
    async def verify(request: Request) -> GitHubConnectionV1:
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_verify(_subject(request), deps=deps, repos=repos)
            except GitHubUnavailable as exc:
                # Nicht „nicht bewiesen": das hieße, jemandem den Nachweis
                # abzusprechen, weil WIR gerade nicht fragen konnten.
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "github unavailable"
                ) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value, own=True)

    @router.post("/github/me/refresh")
    async def refresh(request: Request) -> GitHubConnectionV1:
        async with request_scope(session_factory) as (uow, repos):
            try:
                result = await handle_refresh(_subject(request), deps=deps, repos=repos)
            except GitHubUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "github unavailable"
                ) from exc
            if not result.is_success:
                raise _to_http(result.error)
            await uow.commit()
            return _dto(result.value, own=True)

    @router.get("/github/me")
    async def mine(request: Request) -> GitHubConnectionV1 | None:
        """Auch die unbewiesene Verbindung — sonst sähe die Person nichts."""
        async with request_scope(session_factory) as (_uow, repos):
            connection = await handle_get_mine(_subject(request), repos=repos)
        return None if connection is None else _dto(connection, own=True)

    @router.delete("/github/me", status_code=status.HTTP_204_NO_CONTENT)
    async def disconnect(request: Request) -> Response:
        """Trennen heißt löschen — der Abzug verschwindet mit."""
        async with request_scope(session_factory) as (uow, repos):
            await handle_disconnect(_subject(request), repos=repos)
            await uow.commit()
        return Response(status_code=status.HTTP_204_NO_CONTENT)

    @router.get("/github/{subject_id}")
    async def visible(subject_id: UUID, request: Request) -> GitHubConnectionV1:
        _subject(request)
        query = VisibleQuery(subject_id=subject_id, bearer=_bearer(request))
        async with request_scope(session_factory) as (_uow, repos):
            try:
                result = await handle_get_visible(query, deps=deps, repos=repos)
            except ConsentUnavailable as exc:
                # Weder zeigen noch leugnen: beides wäre eine Behauptung über
                # die Person, die niemand treffen kann.
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not result.is_success:
                raise HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
            return _dto(result.value, own=False)

    return router
