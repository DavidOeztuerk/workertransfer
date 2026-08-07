"""Benachrichtigungen: der Dienst-Endpunkt und die Einstellungen der Person.

Warum hier und nicht in einem eigenen Dienst: ein `notifications-service`
bräuchte die E-Mail-Adresse. Die liegt hier, und sie dorthin zu kopieren oder
über einen Lookup `subject_id → E-Mail` herauszureichen hieße, das
empfindlichste Datum des Systems zu vervielfachen — für eine Textmail.
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, HTTPException, Request, Response, status
from worker_contracts import NotificationPreferencesV1, NotifyV1

from identity_service.application.commands import (
    NotifyCommand,
    deliver_notification,
    handle_notify,
)
from identity_service.application.ports import AuthPrincipal
from identity_service.domain.notification import NotificationKind
from identity_service.presentation.auth_middleware import get_request_user

__all__ = ["build_notification_router"]

_logger = logging.getLogger("workertransfer.identity.notifications")

#: Der Header, den ein Dienst mitschickt. Ein Browser kann ihn nicht setzen,
#: ohne das Geheimnis zu kennen, und er kennt es nicht.
SECRET_HEADER = "X-Notify-Secret"  # noqa: S105 - ein Headername, kein Geheimnis


def build_notification_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["notifications"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]
    settings = deps["settings"]

    def _principal(request: Request) -> AuthPrincipal:
        principal: AuthPrincipal | None = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        return principal

    @router.post("/notifications", status_code=status.HTTP_202_ACCEPTED)
    async def notify(body: NotifyV1, request: Request) -> Response:
        """Immer 202 — auch wenn nichts verschickt wurde.

        Abbestellt, gedrosselt, Konto unbestätigt, Person unbekannt: der
        Aufrufer erfährt nichts darüber, ob und warum. Sonst wäre der Endpunkt
        ein Orakel darüber, ob es diese Person gibt und ob sie Mails will.
        """
        secret = settings.notify_secret.get_secret_value()
        # Leeres Geheimnis heißt: zu. Eine Voreinstellung, die im Zweifel
        # öffnet, wäre hier die falsche.
        if secret == "" or request.headers.get(SECRET_HEADER) != secret:
            # 404 statt 401: ein 401 bestätigt, dass es den Endpunkt gibt.
            raise HTTPException(status.HTTP_404_NOT_FOUND, "Not Found")

        kind = NotificationKind(body.kind)
        async with request_scope(session_factory) as (uow, repos):
            should_send = await handle_notify(
                NotifyCommand(user_id=body.user_id, kind=kind), deps=deps, repos=repos
            )
            user = await repos["users"].get_by_id(body.user_id) if should_send else None
            await uow.commit()

        # Nach dem Commit: vorher zu senden hieße, über etwas zu
        # benachrichtigen, das gleich zurückgerollt wird.
        if should_send and user is not None:
            try:
                await deliver_notification(user.email.value, kind, deps=deps)
            except Exception:  # Zustellung darf niemals etwas kippen
                # Die Drossel ist schon gesetzt, also geht diese Nachricht
                # verloren. Das ist die richtige Richtung: lieber eine Mail zu
                # wenig als eine Schleife, die es bei jedem Ereignis erneut
                # versucht.
                _logger.warning("Benachrichtigung konnte nicht zugestellt werden", exc_info=True)
        return Response(status_code=status.HTTP_202_ACCEPTED)

    @router.get("/me/notification-preferences")
    async def read_preferences(request: Request) -> NotificationPreferencesV1:
        user_id = _principal(request).user_id
        async with request_scope(session_factory) as (_uow, repos):
            preference = await repos["notifications"].get(user_id)
        return NotificationPreferencesV1(
            resume_request=preference.wants(NotificationKind.RESUME_REQUEST),
            market_request=preference.wants(NotificationKind.MARKET_REQUEST),
            application_update=preference.wants(NotificationKind.APPLICATION_UPDATE),
            transfer_update=preference.wants(NotificationKind.TRANSFER_UPDATE),
        )

    @router.put("/me/notification-preferences")
    async def write_preferences(
        body: NotificationPreferencesV1, request: Request
    ) -> NotificationPreferencesV1:
        user_id = _principal(request).user_id
        async with request_scope(session_factory) as (uow, repos):
            preference = await repos["notifications"].get(user_id)
            preference.set(NotificationKind.RESUME_REQUEST, body.resume_request)
            preference.set(NotificationKind.MARKET_REQUEST, body.market_request)
            preference.set(NotificationKind.APPLICATION_UPDATE, body.application_update)
            preference.set(NotificationKind.TRANSFER_UPDATE, body.transfer_update)
            await repos["notifications"].save(preference)
            await uow.commit()
        return body

    return router
