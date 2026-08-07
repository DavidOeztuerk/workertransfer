"""HTTP-Adapter für den Consent-Ledger.

Synchron und ohne Cache (ADR-0013): ein Widerruf muss beim nächsten Abruf
wirken, nicht beim übernächsten.
"""

from __future__ import annotations

import logging
from typing import Any
from uuid import UUID

import httpx
from worker_contracts import ConsentCheckV1

from portfolio_service.application.ports import VISIBILITY_CAPABILITY, tenant_capability

__all__ = ["ConsentUnavailable", "HttpConsentGate"]

_logger = logging.getLogger("workertransfer.portfolio.consent")


class ConsentUnavailable(RuntimeError):
    """Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.

    Kein DomainError: ein Systemzustand, kein fachlicher Ausgang. Der Router
    bildet ihn auf 503 ab.
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

    async def may_see(self, subject_id: UUID, *, tenant_id: UUID, bearer: str) -> bool:
        """Öffentlich freigegeben ODER diesem Unternehmen (durch eine Bewerbung).

        Nacheinander statt parallel: die öffentliche Freigabe ist der häufige
        Fall, und wer sie hat, braucht die zweite Frage nicht. `tenant_id`
        kommt aus dem Token des Aufrufers, nie aus einem Request.
        """
        if await self._granted(subject_id, VISIBILITY_CAPABILITY, bearer=bearer):
            return True
        return await self._granted(subject_id, tenant_capability(tenant_id), bearer=bearer)

    async def _granted(self, subject_id: UUID, capability: str, *, bearer: str) -> bool:
        body = ConsentCheckV1(subject_id=subject_id, capability=capability)
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.post(
                    f"{self._base_url}/consent/check",
                    json=body.model_dump(mode="json"),
                    # Im Auftrag des Aufrufers: so steht im Protokoll des
                    # Ledgers, wer wirklich gefragt hat.
                    headers={"Authorization": f"Bearer {bearer}"},
                )
        except httpx.HTTPError as exc:
            _logger.warning("Consent-Ledger nicht erreichbar: %s", exc)
            raise ConsentUnavailable("consent-service unreachable") from exc

        if response.status_code != 200:
            _logger.warning("Consent-Ledger antwortete mit %s", response.status_code)
            raise ConsentUnavailable(f"consent-service returned {response.status_code}")

        try:
            payload: dict[str, Any] = response.json()
            granted = bool(payload["granted"])
            deleted = bool(payload.get("deleted", False))
        except Exception as exc:
            raise ConsentUnavailable("consent-service sent an unusable answer") from exc
        return granted and not deleted
