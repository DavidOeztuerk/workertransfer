"""HTTP-Adapter für den Consent-Ledger.

Synchron und ohne Cache — ADR-0013, Ansatz A: „Ein Cache ist hier kein
Performance-Detail, sondern ein Regelbruch." Ein Widerruf muss beim nächsten
Abruf wirken, nicht beim übernächsten.
"""

from __future__ import annotations

import logging
from typing import Any
from uuid import UUID

import httpx
from worker_contracts import ConsentCheckV1

from profile_service.application.ports import VISIBILITY_CAPABILITY

__all__ = ["ConsentUnavailable", "HttpConsentGate"]

_logger = logging.getLogger("workertransfer.profile.consent")


class ConsentUnavailable(RuntimeError):
    """Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.

    Bewusst kein DomainError: es ist kein fachlicher Ausgang, sondern ein
    Systemzustand. Der Router bildet ihn auf 503 ab.
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
        body = ConsentCheckV1(subject_id=subject_id, capability=VISIBILITY_CAPABILITY)
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.post(
                    f"{self._base_url}/consent/check",
                    json=body.model_dump(mode="json"),
                    headers={"Authorization": f"Bearer {bearer}"},
                )
        except httpx.HTTPError as exc:
            _logger.warning("Consent-Ledger nicht erreichbar: %s", exc)
            raise ConsentUnavailable("consent-service unreachable") from exc

        if response.status_code != 200:
            # Auch 401/403: unser Token taugt nicht — das ist ein Systemproblem,
            # keine Aussage darüber, ob die Person eingewilligt hat.
            _logger.warning("Consent-Ledger antwortete mit %s", response.status_code)
            raise ConsentUnavailable(f"consent-service returned {response.status_code}")

        try:
            payload: dict[str, Any] = response.json()
            granted = bool(payload["granted"])
            deleted = bool(payload.get("deleted", False))
        except Exception as exc:
            _logger.warning("Consent-Ledger antwortete unverständlich")
            raise ConsentUnavailable("consent-service sent an unusable answer") from exc

        # DELETE zieht die Capability logisch zurück; beides zu prüfen macht die
        # Absicht auch dann richtig, wenn der Ledger einmal beides meldet.
        return granted and not deleted
