"""HTTP-Adapter für den Jobs-Service.

Eine Bewerbung braucht zwei Dinge von der Stelle: dass es sie öffentlich gibt,
und zu welchem Unternehmen sie gehört. Beides steht in `GET /jobs/{id}` — dem
Endpunkt, der bewusst ohne Anmeldung auskommt.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from uuid import UUID

import httpx

__all__ = ["HttpJobLookup", "JobsUnavailable", "PublicJob"]

_logger = logging.getLogger("workertransfer.applications.jobs")


@dataclass(frozen=True, slots=True)
class PublicJob:
    id: UUID
    tenant_id: UUID
    title: str


class JobsUnavailable(RuntimeError):
    """Der Jobs-Service schweigt — wir wissen nicht, ob es die Stelle gibt.

    Nicht als „gibt es nicht" behandeln: das wäre eine Behauptung über eine
    Ausschreibung, die vielleicht offen ist, und die Person bekäme eine
    Absage, die niemand ausgesprochen hat.
    """


class HttpJobLookup:
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

    async def public_job(self, job_id: UUID) -> PublicJob | None:
        """Die Stelle, oder `None` wenn es sie öffentlich nicht gibt.

        `None` deckt „existiert nicht", „ist noch ein Entwurf" und „ist
        geschlossen" ab — der Jobs-Service hält die drei ohnehin
        ununterscheidbar, und das ist hier genau richtig.
        """
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.get(f"{self._base_url}/jobs/{job_id}")
        except httpx.HTTPError as exc:
            _logger.warning("Jobs-Service nicht erreichbar: %s", exc)
            raise JobsUnavailable("jobs-service unreachable") from exc

        if response.status_code == 404:
            return None
        if response.status_code != 200:
            raise JobsUnavailable(f"jobs-service returned {response.status_code}")
        try:
            payload = response.json()
            return PublicJob(
                id=UUID(payload["id"]),
                tenant_id=UUID(payload["tenant_id"]),
                title=payload["title"],
            )
        except Exception as exc:
            raise JobsUnavailable("jobs-service sent an unusable answer") from exc
