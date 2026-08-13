"""HTTP-Adapter für den Consent-Ledger.

Synchron und ohne Cache — ADR-0013, Ansatz A: „Ein Cache ist hier kein
Performance-Detail, sondern ein Regelbruch." Ein Widerruf muss beim nächsten
Abruf wirken, nicht beim übernächsten.
"""

from __future__ import annotations

import logging
from collections.abc import Sequence
from typing import Any
from uuid import UUID

import httpx
from worker_contracts import MAX_CHECK_BATCH, ConsentCheckBatchV1, ConsentCheckV1

from profile_service.application.ports import VISIBILITY_CAPABILITY, tenant_capability

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

    async def may_see(self, subject_id: UUID, *, tenant_id: UUID, bearer: str) -> bool:
        """Öffentlich freigegeben ODER diesem Unternehmen (durch eine Bewerbung).

        Zwei Abfragen statt einer, und nacheinander statt parallel: die
        öffentliche Freigabe ist der häufige Fall, und wer sie hat, braucht die
        zweite Frage nicht. `tenant_id` kommt aus dem Token des Aufrufers, nie
        aus einem Request — sie stammt aus einer geprüften Mitgliedschaft
        (ADR-0018).
        """
        if await self._granted(subject_id, VISIBILITY_CAPABILITY, bearer=bearer):
            return True
        return await self._granted(subject_id, tenant_capability(tenant_id), bearer=bearer)

    async def may_see_many(
        self, subject_ids: Sequence[UUID], *, tenant_id: UUID, bearer: str
    ) -> list[bool]:
        """Wie `may_see`, aber für eine ganze Seite in EINER Runde.

        Gemessen war das der Grund: eine Seite von 20 Profilen kostete bis zu 40
        einzelne Aufrufe — zwei Fähigkeiten je Person, jeder Aufruf mit eigenem
        `httpx.AsyncClient` und damit eigenem Verbindungsaufbau. 1,7 bis 8,8
        Sekunden für eine Seite, und unter Last mehr als die Oberfläche abwartet;
        sie zeigte dann endlos „Profile werden geladen…".

        Anders als `may_see` wird hier **immer** nach beiden Fähigkeiten gefragt,
        nicht erst die öffentliche und die zweite nur bei Bedarf. In einer
        gemeinsamen Anfrage kostet die zweite Frage nichts mehr, und die
        Alternative wäre eine zweite Runde für genau die Personen, die nicht
        öffentlich freigegeben haben — also ein Aufwand, der mit der Anzahl der
        *nicht* Freigegebenen steigt und damit selbst eine Auskunft wäre.

        Nichts wird zwischengespeichert (ADR-0013). Es ist eine Frage in einer
        Runde, keine Vorratsantwort.
        """
        if not subject_ids:
            return []
        public = VISIBILITY_CAPABILITY
        for_tenant = tenant_capability(tenant_id)
        pairs = [
            ConsentCheckV1(subject_id=subject_id, capability=capability)
            for subject_id in subject_ids
            for capability in (public, for_tenant)
        ]
        if len(pairs) > MAX_CHECK_BATCH:
            # Der Aufrufer fragt mehr, als der Vertrag trägt. Das ist ein
            # Programmierfehler und keine Auskunft über eine Person — deshalb
            # laut, nicht als „nicht freigegeben".
            raise ConsentUnavailable(
                f"{len(pairs)} Paare überschreiten MAX_CHECK_BATCH={MAX_CHECK_BATCH}"
            )

        body = ConsentCheckBatchV1(pairs=pairs)
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.post(
                    f"{self._base_url}/consent/check-batch",
                    json=body.model_dump(mode="json"),
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
            results = payload["results"]
            if len(results) != len(pairs):
                # Eine Antwort, die nicht zu den Fragen passt, lässt sich nicht
                # zuordnen — und falsch zuzuordnen hieße, das Profil der falschen
                # Person zu zeigen.
                raise ValueError(f"{len(results)} Antworten auf {len(pairs)} Fragen")
            verdicts = [
                bool(entry["granted"]) and not bool(entry.get("deleted", False))
                for entry in results
            ]
        except Exception as exc:
            _logger.warning("Consent-Ledger antwortete unverständlich")
            raise ConsentUnavailable("consent-service sent an unusable answer") from exc

        # Zwei Antworten je Person, in der Reihenfolge der Fragen: öffentlich
        # ODER für dieses Unternehmen.
        return [verdicts[i] or verdicts[i + 1] for i in range(0, len(verdicts), 2)]

    async def _granted(self, subject_id: UUID, capability: str, *, bearer: str) -> bool:
        body = ConsentCheckV1(subject_id=subject_id, capability=capability)
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
