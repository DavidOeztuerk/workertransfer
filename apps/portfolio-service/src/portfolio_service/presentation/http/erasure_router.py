"""Der Löschbefehl von identity-service (ADR-0027 §4).

Kopiert statt geteilt, wie der Consent- und der Notify-Adapter auch: dreißig
Zeilen HTTP in ein gemeinsames Paket zu heben wäre ein Kopplungspunkt über eine
Dienstgrenze hinweg, und sein Preis ist höher als der der Kopie (ADR-0004).
Was NICHT kopiert wird, ist die Bedeutung: was gelöscht wird, weiß allein dieser
Dienst (`application/erasure.py`).

Drei Eigenschaften, die alle drei tragend sind:

* **Eigenes Geheimnis, nicht das der Benachrichtigung.** „Darf eine Mail
  anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht dasselbe
  Papier sein (ADR-0027 §4.4).
* **404 statt 401** bei falschem oder fehlendem Wert — ein 401 bestätigt, dass
  es den Endpunkt gibt.
* **2xx auch beim zweiten Mal.** Ein 404 für „schon gelöscht" sähe für den
  Zusteller wie ein Fehlschlag aus, und er würde ewig wiederholen, was längst
  erledigt ist (ADR-0027 §4.2).
"""

from __future__ import annotations

import secrets
from typing import Any

from fastapi import APIRouter, HTTPException, Request, status
from portfolio_service.application.erasure import erase_subject
from worker_contracts import ErasureResultV1, ErasureV1

__all__ = ["ERASURE_SECRET_HEADER", "build_erasure_router"]

#: Ein Browser kann ihn nicht setzen, ohne das Geheimnis zu kennen — und er
#: kennt es nicht.
ERASURE_SECRET_HEADER = "X-Erasure-Secret"  # noqa: S105 - ein Headername, kein Geheimnis


def build_erasure_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["erasure"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]
    settings = deps["settings"]

    @router.post("/internal/erasure")
    async def erase(body: ErasureV1, request: Request) -> ErasureResultV1:
        secret = settings.erasure_secret.get_secret_value()
        presented = request.headers.get(ERASURE_SECRET_HEADER, "")
        # Leeres Geheimnis heißt: zu. Bei einem Endpunkt, der alles über einen
        # Menschen löscht, wäre eine Voreinstellung, die im Zweifel öffnet, die
        # denkbar schlechteste.
        if secret == "" or not secrets.compare_digest(presented, secret):
            raise HTTPException(status.HTTP_404_NOT_FOUND, "Not Found")

        async with request_scope(session_factory) as (uow, _repos):
            retained = await erase_subject(uow.session, deps["storage"], body.user_id)
            await uow.commit()
        return ErasureResultV1(retained=retained)

    return router
