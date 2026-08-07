"""HTTP-Adapter für Benachrichtigungen.

**Feuern und vergessen.** Ein Fehlschlag hier darf niemals den Vorgang scheitern
lassen, der ihn ausgelöst hat — die genaue Umkehrung der Consent-Regel, und aus
demselben Grund richtig: beim Ledger geht es um Erlaubnis (im Zweifel nein),
hier um Höflichkeit. Einen Widerruf zurückzurollen, weil eine Mail nicht rausging,
wäre grotesk.

Kopiert statt geteilt, wie der Consent-Adapter auch: ein gemeinsames Paket für
vierzig Zeilen HTTP wäre ein Kopplungspunkt über eine Dienstgrenze hinweg, und
sein Preis ist höher als der der Kopie (ADR-0004).
"""

from __future__ import annotations

import logging
from uuid import UUID

import httpx

__all__ = ["HttpNotifier", "NullNotifier"]

_logger = logging.getLogger("workertransfer.resume.notify")


class NullNotifier:
    """Tut nichts — für Tests und für Läufe ohne identity-service."""

    async def notify(self, user_id: UUID, kind: str) -> None:
        return None


class HttpNotifier:
    def __init__(
        self,
        *,
        base_url: str,
        secret: str,
        transport: httpx.AsyncBaseTransport | None = None,
        timeout: float = 5.0,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._secret = secret
        self._transport = transport
        self._timeout = timeout

    async def notify(self, user_id: UUID, kind: str) -> None:
        """Schluckt jeden Fehler. Das ist der Zweck, nicht eine Nachlässigkeit."""
        if self._secret == "":
            # Kein Geheimnis, kein Versuch: der Endpunkt wäre ohnehin zu, und
            # ein Aufruf ins Leere kostet bei jedem Vorgang eine Zeitüberschreitung.
            return
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                await client.post(
                    f"{self._base_url}/notifications",
                    json={"user_id": str(user_id), "kind": kind},
                    headers={"X-Notify-Secret": self._secret},
                )
        except Exception:
            # Auch die Antwort wird nicht geprüft: der Endpunkt antwortet
            # absichtlich immer 202, und selbst ein 500 darf hier nichts kippen.
            _logger.warning("Benachrichtigung konnte nicht abgesetzt werden", exc_info=True)
