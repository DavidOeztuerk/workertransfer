"""HTTP-Adapter für den Consent-Ledger.

Nur empfängerbezogen: es gibt kein `market.visibility:public`, und dieser
Adapter kann deshalb gar nicht danach fragen.

Hier wird auch geschrieben: die Person erteilt und widerruft über diesen
Dienst, damit der Capability-String an genau einer Stelle entsteht. Würde ihn
die Oberfläche bauen, gäbe es zwei Stellen, die sich über das Format einig sein
müssten — und eine davon im Browser.
"""

from __future__ import annotations

import logging
from typing import Any
from uuid import UUID

import httpx
from worker_contracts import ConsentCheckV1, ConsentGrantV1, ConsentRevokeV1

from transfer_service.domain.market_status import tenant_capability

__all__ = ["PROFILE_VISIBILITY", "ConsentUnavailable", "HttpConsentGate"]

#: Wer das Profil nicht sehen darf, darf auch nicht nach dem Marktstatus
#: fragen — sonst wäre die Anfrage ein Kanal, um die Existenz einer Person
#: zu erfahren.
PROFILE_VISIBILITY = "profile.visibility:public"

WITHDRAWAL_REASON = "Freigabe des Marktstatus zurückgezogen"

_logger = logging.getLogger("workertransfer.transfer.consent")


class ConsentUnavailable(RuntimeError):
    """Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.

    Kein DomainError: ein Systemzustand. Der Router bildet ihn auf 503 ab.
    """


class HttpConsentGate:
    def __init__(
        self,
        *,
        base_url: str,
        transport: httpx.AsyncBaseTransport | None = None,
        timeout: float = 5.0,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._transport = transport
        self._timeout = timeout

    async def _post(self, path: str, body: dict[str, Any], *, bearer: str) -> dict[str, Any]:
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.post(
                    f"{self._base_url}{path}",
                    json=body,
                    # Im Auftrag des Aufrufers: so steht im Protokoll des
                    # Ledgers, wer wirklich gefragt hat.
                    headers={"Authorization": f"Bearer {bearer}"},
                )
        except httpx.HTTPError as exc:
            _logger.warning("Consent-Ledger nicht erreichbar: %s", exc)
            raise ConsentUnavailable("consent-service unreachable") from exc

        if response.status_code != 200:
            # Auch 401/403: unser Token taugt nicht — ein Systemproblem, keine
            # Aussage darüber, ob die Person eingewilligt hat.
            _logger.warning("Consent-Ledger antwortete mit %s", response.status_code)
            raise ConsentUnavailable(f"consent-service returned {response.status_code}")

        try:
            payload: dict[str, Any] = response.json()
        except Exception as exc:
            raise ConsentUnavailable("consent-service sent an unusable answer") from exc
        return payload

    async def _granted(self, subject_id: UUID, capability: str, *, bearer: str) -> bool:
        body = ConsentCheckV1(subject_id=subject_id, capability=capability)
        payload = await self._post("/consent/check", body.model_dump(mode="json"), bearer=bearer)
        try:
            granted = bool(payload["granted"])
            deleted = bool(payload.get("deleted", False))
        except Exception as exc:
            raise ConsentUnavailable("consent-service sent an unusable answer") from exc
        return granted and not deleted

    async def may_see(self, subject_id: UUID, *, tenant_id: UUID, bearer: str) -> bool:
        return await self._granted(subject_id, tenant_capability(tenant_id), bearer=bearer)

    async def may_see_profile(self, subject_id: UUID, *, bearer: str) -> bool:
        return await self._granted(subject_id, PROFILE_VISIBILITY, bearer=bearer)

    async def grant_market(self, subject_id: UUID, tenant_id: UUID, *, bearer: str) -> None:
        body = ConsentGrantV1(subject_id=subject_id, capability=tenant_capability(tenant_id))
        await self._post("/consent/grant", body.model_dump(mode="json"), bearer=bearer)

    async def revoke_market(self, subject_id: UUID, tenant_id: UUID, *, bearer: str) -> None:
        body = ConsentRevokeV1(
            subject_id=subject_id,
            capability=tenant_capability(tenant_id),
            # Der Vertrag verlangt eine Begründung: eine Entziehung muss
            # erklärbar sein, eine Erteilung nicht.
            reason=WITHDRAWAL_REASON,
        )
        await self._post("/consent/revoke", body.model_dump(mode="json"), bearer=bearer)
