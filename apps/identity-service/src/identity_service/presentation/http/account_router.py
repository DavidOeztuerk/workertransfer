"""Die Löschung des eigenen Kontos — ein Vorgang, der genau einmal existiert.

`POST /consent/delete` ist dafür zurückgezogen (ADR-0027 §1): es war
kapabilitätsbezogen, nicht kontobezogen, und jede Capability in diesem System
ist eine *Sichtbarkeit*. „Lösche `profile.visibility:public`" konnte deshalb nie
„lösche das Profil" heißen.

**Ohne Begründungsfeld.** Von einem Menschen, der sein Konto löschen will, eine
Begründung zu verlangen, ist ein Hebel gegen ihn — und der Freitext wäre
ausgerechnet das Einzige im Ledger, das danach wieder gelöscht werden müsste.

**Nur für sich selbst.** Es gibt keinen Parameter für jemand anderen, also auch
kein Schlupfloch. Ein Löschknopf für Fremde wäre die mächtigste Delegation im
System und braucht eine eigene Entscheidung, keine Nebenwirkung dieser hier.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Request, Response, status

from identity_service.application.erasure import request_erasure
from identity_service.application.ports import AuthPrincipal
from identity_service.presentation.auth_middleware import get_request_user

__all__ = ["build_account_router"]


def build_account_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["account"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]
    clock = deps["clock"]

    @router.post("/account/erasure", status_code=status.HTTP_202_ACCEPTED)
    async def erase_my_account(request: Request) -> Response:
        """202, nicht 200: fertig ist sie erst, wenn alle Empfänger quittiert
        haben — und das dauert länger als diese Antwort.

        Was sofort passiert: alle Sitzungen sind widerrufen und das Konto ist
        gesperrt. Ab hier passiert nichts mehr unter diesem Namen.

        Kein Fortschrittsbalken und keine Statusseite (§6): wer sich noch
        anmelden könnte, um zuzusehen, hätte ein Konto, das noch funktioniert —
        und genau das soll nicht mehr stimmen. Die Auskunft kommt in **einer**
        Nachricht am Ende.
        """
        principal: AuthPrincipal | None = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")

        async with request_scope(session_factory) as (uow, _repos):
            # Die Absichten entstehen VOR dem Commit, in DERSELBEN Transaktion
            # wie die Sperrung des Kontos (ADR-0025). Es gibt keinen Zustand
            # „gesperrt, aber niemand wurde beauftragt".
            await request_erasure(uow.session, user_id=principal.user_id, now=clock.now())
            await uow.commit()

        # Auch beim zweiten Mal 202: zweimal zu drücken ist kein Fehler, und
        # ein 409 wäre eine Auskunft darüber, dass schon etwas läuft, ohne
        # dass sie irgendjemandem hilft.
        return Response(status_code=status.HTTP_202_ACCEPTED)

    return router
