"""Was mit einer Löschzeile passiert, wenn sie an der Reihe ist (ADR-0027 §6).

Ein `Delivery` für die Outbox, das drei verschiedene Dinge tut — je nachdem,
welche Art die Zeile trägt:

* `erasure:<dienst>` → HTTP an den Empfänger. Scheitert er, scheitert die Zeile.
* `erasure:final-notice` → die Abschlussnachricht, **erst wenn alle sieben
  quittiert haben**. Vorher: zurückgestellt, kein verbrauchter Versuch.
* `erasure:identity` → `users` fällt, **erst wenn die Nachricht draußen ist**.
* `company:withdrawal` → die Anzeigen eines stillgelegten Unternehmens (§7).

**Die Reihenfolge steht hier und nirgends sonst.** Sie über `created_at` zu
regeln wäre eine Verabredung, die beim ersten Zeitsprung bricht; sie über einen
zweiten Dauerläufer zu regeln wäre ein zweiter Ort, an dem sie steht. Hier ist
sie eine Vorbedingung, die jede Zeile selbst prüft: `Deferred` heißt „noch nicht
dran", und die Zeile kommt beim nächsten Takt wieder.

Jede Vorbedingung wird gegen **festgeschriebenen** Zustand geprüft — eine eigene
Sitzung, nicht die des Zustellers. Sonst könnte die Abschlussnachricht auf
Quittungen bauen, die im selben Durchlauf noch zurückgerollt werden. Der Preis
ist ein Takt Verzögerung je Stufe, und der ist in §6 ausdrücklich benannt.
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from typing import Any
from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession
from worker_outbox import Deferred

from identity_service.application.erasure import (
    KIND_COMPANY_WITHDRAWAL,
    KIND_FINAL_NOTICE,
    KIND_IDENTITY,
    KIND_PREFIX,
    finish_erasure,
    open_kinds,
)
from identity_service.infrastructure.database.models import UserModel
from identity_service.infrastructure.erasure import ErasureUndelivered, HttpErasureDelivery

__all__ = ["FINAL_NOTICE_BODY", "FINAL_NOTICE_SUBJECT", "ErasureDispatch"]

_logger = logging.getLogger("workertransfer.identity.erasure")

FINAL_NOTICE_SUBJECT = "Ihr Konto wurde gelöscht"

#: **Dass** es fertig ist — mehr nicht.
#:
#: Ausdrücklich KEINE Aufstellung dessen, was die Person hatte. Eine
#: Abschiedsmail, die auflistet, was gerade gelöscht wurde, ist eine Kopie der
#: Daten in einem Postfach, das womöglich nicht nur ihr gehört — dieselbe
#: Überlegung, aus der der Datenexport nicht per Mail zugestellt wird.
#:
#: In der Voreinstellung ist das der ganze Text: es bleibt nichts, über das zu
#: berichten wäre. Erst ein umgelegter Aufbewahrungsschalter (§3) brächte einen
#: Zusatz — und der kommt mit dem Commit, der ihn umlegt.
FINAL_NOTICE_BODY = (
    "Ihr Konto bei WorkerTransfer ist gelöscht.\n\n"
    "Alle Daten, die wir über Sie gespeichert hatten, sind entfernt. "
    "Es bleibt nichts stehen.\n\n"
    "Diese Nachricht ist die letzte, die Sie von uns bekommen.\n"
)


class ErasureDispatch:
    def __init__(
        self,
        *,
        session_factory: Callable[[], AsyncSession],
        delivery: HttpErasureDelivery,
        mailer: Any,
        clock: Any,
    ) -> None:
        self._session_factory = session_factory
        self._delivery = delivery
        self._mailer = mailer
        self._clock = clock

    async def notify(self, user_id: UUID, kind: str) -> None:
        if kind == KIND_COMPANY_WITHDRAWAL:
            # `user_id` trägt hier die tenant_id — die Tabelle hat nur diese
            # eine Kennungsspalte.
            await self._delivery.withdraw_company(user_id)
            return
        if kind == KIND_FINAL_NOTICE:
            await self._send_final_notice(user_id)
            return
        if kind == KIND_IDENTITY:
            await self._drop_the_account(user_id)
            return
        if kind.startswith(KIND_PREFIX):
            retained = await self._delivery.send(kind[len(KIND_PREFIX) :], user_id)
            if retained:
                # Ausgesetzt ist nicht übersprungen (§3.4). In der
                # Voreinstellung passiert das nie; wenn doch, soll es nicht
                # nur in einer Tabelle stehen.
                _logger.warning(
                    "Empfänger %s hat %s Zeilen aufbewahrt (Aufbewahrungsschalter)",
                    kind,
                    retained,
                )
            return
        raise ErasureUndelivered(f"unbekannte Absicht: {kind}")

    async def _still_open(self, user_id: UUID, *, ignoring: set[str]) -> list[str]:
        async with self._session_factory() as session:
            return [kind for kind in await open_kinds(session, user_id) if kind not in ignoring]

    async def _send_final_notice(self, user_id: UUID) -> None:
        outstanding = await self._still_open(user_id, ignoring={KIND_FINAL_NOTICE, KIND_IDENTITY})
        if outstanding:
            raise Deferred(f"noch offen: {', '.join(outstanding)}")

        async with self._session_factory() as session:
            user = await session.get(UserModel, user_id)
            if user is None:
                # Das Konto ist schon weg — also war die Nachricht schon
                # einmal draußen und nur die Quittung ging verloren.
                # „Mindestens einmal" heißt hier: lieber gar keine zweite
                # Mail als eine an eine Adresse, die wir nicht mehr kennen.
                return
            address = user.email

        await self._mailer.send(to=address, subject=FINAL_NOTICE_SUBJECT, body=FINAL_NOTICE_BODY)

    async def _drop_the_account(self, user_id: UUID) -> None:
        outstanding = await self._still_open(user_id, ignoring={KIND_IDENTITY})
        if outstanding:
            # Auch die Abschlussnachricht zählt dazu: die Adresse liegt in der
            # Zeile, die gleich fällt.
            raise Deferred(f"noch offen: {', '.join(outstanding)}")

        async with self._session_factory() as session:
            await finish_erasure(session, user_id=user_id, now=self._clock.now())
            await session.commit()
