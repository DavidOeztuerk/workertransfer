"""Der Endpunkt für die Stilllegung eines Unternehmens (ADR-0027 §7).

Bewusst **nicht** `/internal/erasure`: dieser Dienst hält nichts
Personenbezogenes und ist kein Empfänger der Löschkaskade. Ein Löschbefehl an
einen Dienst ohne zu löschende Daten wäre ein Endpunkt, der „erledigt" sagt,
ohne je etwas zu tun — und ein gleich benannter Pfad hier würde ihn früher oder
später in die Empfängerliste rutschen lassen.

Ansonsten dasselbe Muster: eigenes Geheimnis, 404 statt 401, 2xx auch beim
zweiten Mal.
"""

from __future__ import annotations

import secrets
from typing import Any

from fastapi import APIRouter, HTTPException, Request, status
from jobs_service.application.withdrawal import withdraw_company_jobs
from worker_contracts import CompanyWithdrawalV1

__all__ = ["ERASURE_SECRET_HEADER", "build_withdrawal_router"]

ERASURE_SECRET_HEADER = "X-Erasure-Secret"  # noqa: S105 - ein Headername, kein Geheimnis


def build_withdrawal_router(deps: dict[str, Any]) -> APIRouter:
    router = APIRouter(tags=["erasure"])
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]
    settings = deps["settings"]

    @router.post("/internal/company-withdrawal")
    async def withdraw(body: CompanyWithdrawalV1, request: Request) -> dict[str, int]:
        secret = settings.erasure_secret.get_secret_value()
        presented = request.headers.get(ERASURE_SECRET_HEADER, "")
        if secret == "" or not secrets.compare_digest(presented, secret):
            raise HTTPException(status.HTTP_404_NOT_FOUND, "Not Found")

        async with request_scope(session_factory) as (uow, _repos):
            withdrawn = await withdraw_company_jobs(uow.session, body.tenant_id)
            await uow.commit()
        return {"withdrawn": withdrawn}

    return router
