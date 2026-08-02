"""HTTP-Endpunkte für Portfolios.

Das eigene steht jeder angemeldeten Person offen. Ein fremdes verlangt einen
aktiven Tenant (nur Unternehmen lesen) und die Einwilligung der Person.
"""

from __future__ import annotations

import logging
from typing import Annotated, Any
from uuid import UUID, uuid4

from fastapi import APIRouter, File, HTTPException, Request, UploadFile, status
from fastapi.responses import Response
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
from worker_contracts import AttachmentV1, PortfolioItemV1, PortfolioV1, SavePortfolioV1
from worker_core import DomainError
from worker_storage import ContentTooLarge, UnsupportedContentType, sniff_content_type
from worker_storage.content import assert_within

__all__ = ["build_router"]

#: Eine Antwort für „gibt es nicht" und „ist nicht freigegeben". Sie darf sich
#: zwischen den Fällen nicht unterscheiden — sonst wäre der Statuscode ein
#: Orakel über jede geratene UUID (ADR-0020 §1).
_logger = logging.getLogger("workertransfer.portfolio.attachments")

_NOT_VISIBLE = "No such portfolio"

#: Die Endung folgt dem erkannten Typ, nicht dem hochgeladenen Dateinamen.
_SUFFIXES = {"image/png": ".png", "image/jpeg": ".jpg", "application/pdf": ".pdf"}


def _to_domain(dto: PortfolioItemV1) -> PortfolioItem:
    return PortfolioItem(
        title=dto.title,
        summary=dto.summary,
        url=dto.url,
        role=dto.role,
        year=dto.year,
        attachment=dto.attachment,
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
                attachment=entry.attachment,
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
            saved = result.value

        # ERST committen, DANN aufräumen. Andersherum wären bei einem
        # fehlgeschlagenen Commit Dateien gelöscht, auf die die gespeicherten
        # Einträge weiterhin zeigen — aus einem Aufräumen würde Datenverlust.
        # Scheitert stattdessen das Aufräumen, bleibt eine verwaiste Datei
        # liegen: sie kostet Platz und sonst nichts.
        await _remove_orphans(subject_id, saved)
        return _dto(saved)

    async def _remove_orphans(subject_id: Any, portfolio: Portfolio) -> None:
        referenced = {item.attachment for item in portfolio.items if item.attachment is not None}
        try:
            stored = await deps["storage"].list_names(str(subject_id))
            for name in stored:
                if name not in referenced:
                    await deps["storage"].delete(_storage_key(subject_id, name))
        except OSError:
            # Ein Anhang, dessen Eintrag gelöscht wurde, ist nicht mehr
            # erreichbar — er wird nirgends mehr referenziert. Das Aufräumen
            # spart Platz; es scheitern zu lassen wäre eine Fehlermeldung für
            # etwas, das die Person gerade erfolgreich getan hat.
            _logger.warning("Aufräumen der Anhänge fehlgeschlagen", exc_info=True)

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

    def _storage_key(subject_id: Any, name: str) -> str:
        """Schlüssel = Person + Name.

        Die Zusammensetzung passiert hier und nur hier: der Client nennt einen
        Namen, nie einen Schlüssel. Damit kann er mit einem fremden Namen
        höchstens ins eigene Verzeichnis greifen — die Trennung ist strukturell
        und hängt nicht daran, dass jemand eine Prüfung nicht vergisst.
        """
        return f"{subject_id}/{name}"

    @router.post("/portfolios/me/attachments", status_code=status.HTTP_201_CREATED)
    async def upload_attachment(
        request: Request, file: Annotated[UploadFile, File()]
    ) -> AttachmentV1:
        """Nimmt eine Datei entgegen und gibt ihren Namen zurück.

        Der Typ kommt aus den ersten Bytes, nicht aus dem, was der Client
        behauptet: `Content-Type` und Dateiendung sind beide frei wählbar, die
        Signatur nicht (ADR-0021).

        Der Name wird vom Server vergeben. Den vom Client zu übernehmen hieße,
        einen fremden Text zu einem Teil eines Pfades zu machen — und die
        Dateiendung mit ihm.
        """
        subject_id = _principal(request).sub
        data = await file.read()
        try:
            assert_within(data, deps["max_attachment_bytes"])
            content_type = sniff_content_type(data)
        except (ContentTooLarge, UnsupportedContentType) as exc:
            raise HTTPException(status.HTTP_422_UNPROCESSABLE_CONTENT, exc.message) from exc

        name = f"{uuid4().hex}{_SUFFIXES[content_type]}"
        stored = await deps["storage"].put(
            _storage_key(subject_id, name), data, content_type=content_type
        )
        return AttachmentV1(name=name, content_type=stored.content_type, size=stored.size)

    @router.get("/portfolios/{subject_id}/attachments/{name}")
    async def get_attachment(subject_id: UUID, name: str, request: Request) -> Response:
        """Liefert eine Datei aus — mit derselben Prüfung wie das Portfolio.

        Wer das Portfolio nicht sehen darf, bekommt auch die Datei nicht: sonst
        wäre der Anhang ein zweiter Weg an dieselben Daten, mit einem eigenen
        Filter, der irgendwann vom ersten abweicht.
        """
        principal = _principal(request)
        own = principal.sub == subject_id
        if not own:
            if principal.tenant_id is None:
                raise HTTPException(
                    status.HTTP_403_FORBIDDEN,
                    "reading other portfolios requires an active company",
                )
            try:
                allowed = await deps["consent"].may_see(subject_id, bearer=_bearer(request))
            except ConsentUnavailable as exc:
                raise HTTPException(
                    status.HTTP_503_SERVICE_UNAVAILABLE, "consent ledger unavailable"
                ) from exc
            if not allowed:
                raise HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)

        data = await deps["storage"].get(_storage_key(subject_id, name))
        if data is None:
            raise HTTPException(status.HTTP_404_NOT_FOUND, _NOT_VISIBLE)
        # Erneut aus den Bytes bestimmt statt aus einer gespeicherten Angabe:
        # eine zweite Wahrheit über denselben Sachverhalt läuft irgendwann
        # auseinander, und hier hinge ein Content-Type daran.
        content_type = sniff_content_type(data)
        return Response(
            content=data,
            media_type=content_type,
            # Als Download, nicht im Rahmen der Seite: ein PDF kann Skripte
            # enthalten, und inline ausgeliefert liefe es in unserem Ursprung.
            headers={"Content-Disposition": f'attachment; filename="{name}"'},
        )

    return router
