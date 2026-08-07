"""HTTP-Adapter für den Consent-Ledger.

Nur lesend. Erteilt und widerrufen wird wie beim Profil und beim Portfolio
direkt beim Ledger — dieser Dienst schreibt keine Einwilligung, er hält sich an
eine.

Öffentlich, weil die Repositories ohnehin öffentlich sind. **Das Heikle ist
nicht der Code, sondern die Verbindung**: wer hier unter einem anderen Namen
auftritt als auf GitHub, wird durch sie identifizierbar. Deshalb liegt sie im
Ledger wie alles andere und wirkt ein Widerruf sofort (ADR-0013).
"""

from __future__ import annotations

import logging
from typing import Any
from uuid import UUID

import httpx
from worker_contracts import ConsentCheckV1

__all__ = ["VISIBILITY", "ConsentUnavailable", "HttpConsentGate"]

_logger = logging.getLogger("workertransfer.github.consent")

VISIBILITY = "github.visibility:public"


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

    async def may_see(self, subject_id: UUID, *, bearer: str) -> bool:
        body = ConsentCheckV1(subject_id=subject_id, capability=VISIBILITY)
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
