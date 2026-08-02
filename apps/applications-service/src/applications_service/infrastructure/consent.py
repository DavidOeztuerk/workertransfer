"""HTTP-Adapter für den Consent-Ledger.

Hier wird nur geschrieben: eine Bewerbung erteilt beim Absenden
empfängerbezogene Einwilligungen und nimmt sie beim Zurückziehen zurück.
Gelesen wird nirgends — die Bewerbung enthält keine Profildaten, und wer sie
sehen will, fragt die zuständigen Dienste, wo der Ledger greift.
"""

from __future__ import annotations

import logging
from typing import Any
from uuid import UUID

import httpx
from worker_contracts import ConsentGrantV1, ConsentRevokeV1

__all__ = ["ConsentUnavailable", "HttpConsentWriter", "capabilities_for"]

_logger = logging.getLogger("workertransfer.applications.consent")

WITHDRAWAL_REASON = "Bewerbung zurückgezogen"


def capabilities_for(tenant_id: UUID, *, resume: bool, portfolio: bool) -> list[str]:
    """Welche Einwilligungen eine Bewerbung erteilt.

    Das Profil ist immer dabei — eine Bewerbung ohne jede Angabe zur Person ist
    keine. Lebenslauf und Portfolio nur, wenn die Person sie mitschickt.
    """
    capabilities = [f"profile.visibility:tenant:{tenant_id}"]
    if resume:
        capabilities.append(f"resume.visibility:tenant:{tenant_id}")
    if portfolio:
        capabilities.append(f"portfolio.visibility:tenant:{tenant_id}")
    return capabilities


class ConsentUnavailable(RuntimeError):
    """Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.

    Kein DomainError: ein Systemzustand. Der Router bildet ihn auf 503 ab.
    """


class HttpConsentWriter:
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

    async def _post(self, path: str, body: dict[str, Any], *, bearer: str) -> None:
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.post(
                    f"{self._base_url}{path}",
                    json=body,
                    # Im Auftrag der Person: sie erteilt die Einwilligung, nicht
                    # dieser Dienst. Der Ledger weist einen fremden Akteur ab.
                    headers={"Authorization": f"Bearer {bearer}"},
                )
        except httpx.HTTPError as exc:
            _logger.warning("Consent-Ledger nicht erreichbar: %s", exc)
            raise ConsentUnavailable("consent-service unreachable") from exc
        if response.status_code != 200:
            _logger.warning("Consent-Ledger antwortete mit %s", response.status_code)
            raise ConsentUnavailable(f"consent-service returned {response.status_code}")

    async def grant_all(self, subject_id: UUID, capabilities: list[str], *, bearer: str) -> None:
        for capability in capabilities:
            body = ConsentGrantV1(subject_id=subject_id, capability=capability)
            await self._post("/consent/grant", body.model_dump(mode="json"), bearer=bearer)

    async def revoke_all(self, subject_id: UUID, capabilities: list[str], *, bearer: str) -> None:
        for capability in capabilities:
            body = ConsentRevokeV1(
                subject_id=subject_id,
                capability=capability,
                # Der Vertrag verlangt eine Begründung: eine Entziehung muss
                # erklärbar sein, eine Erteilung nicht.
                reason=WITHDRAWAL_REASON,
            )
            await self._post("/consent/revoke", body.model_dump(mode="json"), bearer=bearer)
