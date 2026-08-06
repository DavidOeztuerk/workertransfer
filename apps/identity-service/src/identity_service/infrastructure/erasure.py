"""Der Zusteller der Löschung — und warum er ein eigener sein muss (ADR-0027 §4.1).

Der bestehende `HttpNotifier` bleibt, wie er ist: er fängt jede Ausnahme und
prüft die Antwort nicht. Für eine Benachrichtigung ist das richtig — eine
misslungene Mail darf keinen Vorgang kippen (ADR-0025).

Für eine Löschung ist genau das tödlich. `OutboxDispatcher._deliver` setzt
`delivered_at`, sobald `notify` ohne Ausnahme zurückkehrt; mit einem
schluckenden Adapter würde ein `ConnectError` oder ein `500` als **zugestellt**
verbucht. `delivered_at` wäre dann exakt die Lüge, die ADR-0027 verhindern soll,
und der gesamte Nachweis aus §4 wertlos.

Deshalb dieser Adapter, und deshalb wirft er bei **Transportfehler** *und* bei
**Nicht-2xx**. Auch der Sonderfall „kein Geheimnis konfiguriert" ist hier ein
Fehlschlag und kein stilles Zurückkehren: eine Zeile, die als zugestellt gilt,
obwohl niemand gelöscht hat, ist schlimmer als eine, die liegen bleibt.
"""

from __future__ import annotations

from collections.abc import Mapping
from typing import Any
from uuid import UUID

import httpx

__all__ = ["ERASURE_SECRET_HEADER", "ErasureUndelivered", "HttpErasureDelivery"]

#: Derselbe Header wie bei den Empfängern — aber ausdrücklich ein **anderer
#: Schlüssel** als der Mail-Auslöser: „darf eine Mail anstoßen" und „darf alles
#: über einen Menschen löschen" dürfen nicht dasselbe Papier sein (§4.4).
ERASURE_SECRET_HEADER = "X-Erasure-Secret"  # noqa: S105 - ein Headername, kein Geheimnis


class ErasureUndelivered(Exception):
    """Der Empfänger hat NICHT bestätigt. Die Zeile bleibt offen.

    Trägt bewusst nur die Art des Fehlschlags, nie die Antwort des Gegenübers:
    was hier hineingerät, landet über `last_error` in einer dauerhaften Tabelle
    und damit in jedem Backup (ADR-0025 §5).
    """


class HttpErasureDelivery:
    def __init__(
        self,
        *,
        targets: Mapping[str, str],
        secret: str,
        transport: httpx.AsyncBaseTransport | None = None,
        timeout: float = 10.0,
    ) -> None:
        self._targets = dict(targets)
        self._secret = secret
        self._transport = transport
        self._timeout = timeout

    async def send(self, service: str, user_id: UUID) -> int:
        """Löscht bei einem Empfänger. Gibt zurück, was der stehen ließ."""
        answer = await self._post(service, "/internal/erasure", {"user_id": str(user_id)})
        try:
            retained = answer.json().get("retained", 0)
        except ValueError:
            # Eine 2xx-Antwort ohne verwertbaren Rumpf: gelöscht wurde
            # offenbar, über Zurückgebliebenes sagt sie nichts.
            return 0
        return int(retained)

    async def withdraw_company(self, tenant_id: UUID) -> None:
        """Zieht die Anzeigen eines stillgelegten Unternehmens zurück (§7).

        Eine Absicht über ein **Unternehmen**, nicht über einen Menschen —
        deshalb `tenant_id` und ein eigener Pfad. Sie zählt ausdrücklich nicht
        in den Vollständigkeitsnachweis der Löschung.
        """
        await self._post("jobs", "/internal/company-withdrawal", {"tenant_id": str(tenant_id)})

    async def _post(self, service: str, path: str, body: dict[str, Any]) -> httpx.Response:
        if self._secret == "":
            # Kein stilles Zurückkehren wie beim Notifier: dort ist „nichts zu
            # tun" richtig, hier hieße es „gelöscht", ohne dass jemand gelöscht
            # hat.
            raise ErasureUndelivered("kein Löschgeheimnis konfiguriert")
        base = self._targets.get(service)
        if base is None:
            # Ein Tippfehler in der Empfängerliste darf nicht zu einer Löschung
            # führen, die sich selbst für erledigt erklärt.
            raise ErasureUndelivered(f"unbekannter Empfänger: {service}")

        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.post(
                    f"{base.rstrip('/')}{path}",
                    json=body,
                    headers={ERASURE_SECRET_HEADER: self._secret},
                )
        except httpx.HTTPError as error:
            raise ErasureUndelivered(type(error).__name__) from error

        if response.status_code // 100 != 2:
            # Die Antwort WIRD angesehen — der Unterschied zum Notifier, und der
            # Grund, warum es diesen Adapter gibt. Nur der Status, nie der
            # Rumpf: der könnte alles Mögliche enthalten.
            raise ErasureUndelivered(f"HTTP {response.status_code}")
        return response
