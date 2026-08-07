"""Der Transfer-Vorgang — drei Ja, jederzeit ein Nein.

Der ULTRAPLAN verlangt „beschäftigt → Firma muss mitwirken". Das lässt sich so
nicht bauen: **die Plattform weiß nicht, wo jemand arbeitet.** `employed` ist
ein Boolescher Wert, den die Person selbst setzt; es gibt keinen Datensatz
„Anna arbeitet bei X".

Und es soll keinen geben. Er wäre die Verbindung zwischen „arbeitet bei X" und
„hört zu" — genau die Auskunft, die jemanden den Arbeitsplatz kostet, in einer
einzigen Tabelle. Ihn anzulegen, damit die Plattform den Arbeitgeber
anschreiben kann, hieße, das größte Risiko des Systems zu erzeugen, um eine
Höflichkeit zu ermöglichen.

Stattdessen: der Vorgang trägt, dass eine Freigabe **nötig** ist, und die Person
selbst bestätigt, dass sie vorliegt. Das ist schwächer (niemand prüft es) und
sicherer. Zwischen einer Zusicherung, die niemand einlösen kann, und einer, die
niemanden gefährdet, ist die zweite die ehrlichere.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError

__all__ = [
    "MAX_TEXT",
    "InvalidOffer",
    "InvalidText",
    "NotYours",
    "Transfer",
    "TransferStatus",
    "TransitionNotAllowed",
]

MAX_TEXT = 2000


class TransferStatus(StrEnum):
    INTERESTED = "interested"
    TALKING = "talking"
    OFFERED = "offered"
    ACCEPTED = "accepted"
    COMPLETED = "completed"
    DECLINED = "declined"
    WITHDRAWN = "withdrawn"


#: Endgültig. Wer erneut will, beginnt einen neuen Vorgang — und braucht dafür
#: wieder eine gültige Freigabe des Marktstatus.
_FINAL = frozenset({TransferStatus.COMPLETED, TransferStatus.DECLINED, TransferStatus.WITHDRAWN})


class InvalidText(DomainError):
    def __init__(self, field: str) -> None:
        super().__init__("invalid_text", f"{field} exceeds {MAX_TEXT} characters")


class InvalidOffer(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("invalid_offer", detail)


class NotYours(DomainError):
    def __init__(self) -> None:
        super().__init__("not_yours", "This transfer belongs to someone else")


class TransitionNotAllowed(DomainError):
    def __init__(self, current: TransferStatus, what: str) -> None:
        super().__init__("transition_not_allowed", f"A {current} transfer cannot be {what}")


def _text(field: str, value: str) -> str:
    cleaned = value.strip()
    if len(cleaned) > MAX_TEXT:
        raise InvalidText(field)
    return cleaned


@dataclass(eq=False, slots=True)
class Transfer:
    id: UUID
    subject_id: UUID
    tenant_id: UUID
    status: TransferStatus
    #: Beim Anlegen aus dem Marktstatus kopiert, nicht bei jedem Lesezugriff neu
    #: geholt: wer während eines laufenden Gesprächs kündigt, ändert damit nicht
    #: rückwirkend die Bedingungen eines Angebots — und ein Unternehmen kann
    #: nicht darauf hoffen, dass sich die Regel noch ändert.
    requires_release: bool
    #: Von der PERSON bestätigt. Die Plattform prüft es nicht und kann es nicht.
    release_confirmed: bool
    message: str
    offer_note: str
    offer_start_on: str | None
    #: Festgehalten, nicht bewegt: die Plattform führt kein Geld. Es ist eine
    #: Zahl, auf die sich zwei Unternehmen einigen, und sie steht hier, damit
    #: beide Seiten dieselbe im Blick haben.
    offer_fee_cents: int | None
    created_at: datetime
    updated_at: datetime

    @classmethod
    def express_interest(
        cls,
        *,
        subject_id: UUID,
        tenant_id: UUID,
        requires_release: bool,
        message: str,
        now: datetime,
    ) -> Transfer:
        return cls(
            id=uuid4(),
            subject_id=subject_id,
            tenant_id=tenant_id,
            status=TransferStatus.INTERESTED,
            requires_release=requires_release,
            release_confirmed=False,
            message=_text("Message", message),
            offer_note="",
            offer_start_on=None,
            offer_fee_cents=None,
            created_at=now,
            updated_at=now,
        )

    # --- Die Person ------------------------------------------------------

    def accept_talk(self, *, by: UUID, now: datetime) -> None:
        self._assert_person(by)
        self._assert_status(TransferStatus.INTERESTED, "opened for talks")
        self._move(TransferStatus.TALKING, now)

    def accept_offer(self, *, by: UUID, now: datetime) -> None:
        self._assert_person(by)
        self._assert_status(TransferStatus.OFFERED, "accepted")
        self._move(TransferStatus.ACCEPTED, now)

    def confirm_release(self, *, by: UUID, now: datetime) -> None:
        """Die Person bestätigt, dass ihr aktueller Arbeitgeber sie gehen lässt.

        Niemand prüft das, und niemand kann es: die Plattform kennt weder den
        Arbeitgeber noch den Vertrag. Was der Schritt leistet, ist, die Frage zu
        stellen und die Antwort festzuhalten.
        """
        self._assert_person(by)
        self._assert_status(TransferStatus.ACCEPTED, "released")
        if not self.requires_release:
            raise TransitionNotAllowed(self.status, "released without needing a release")
        self.release_confirmed = True
        self._move(TransferStatus.COMPLETED, now)

    def decline(self, *, by: UUID, now: datetime) -> None:
        """Immer möglich, aus jedem laufenden Zustand.

        Ein Verfahren, aus dem man nicht aussteigen kann, ist kein Verfahren,
        sondern eine Falle.
        """
        self._assert_person(by)
        self._assert_running("declined")
        self._move(TransferStatus.DECLINED, now)

    # --- Das Unternehmen -------------------------------------------------

    def make_offer(
        self,
        *,
        note: str,
        start_on: str | None,
        fee_cents: int | None,
        now: datetime,
    ) -> None:
        self._assert_status(TransferStatus.TALKING, "offered")
        if fee_cents is not None and fee_cents < 0:
            raise InvalidOffer("A transfer fee cannot be negative")
        self.offer_note = _text("Offer", note)
        self.offer_start_on = start_on
        self.offer_fee_cents = fee_cents
        self._move(TransferStatus.OFFERED, now)

    def complete(self, *, now: datetime) -> None:
        """Der Abschluss ist die Aussage „wir stellen ein" — die trifft der
        Arbeitgeber. Die Person hat mit `accept_offer` bereits ja gesagt."""
        self._assert_status(TransferStatus.ACCEPTED, "completed")
        if self.requires_release:
            # Solange eine Freigabe nötig ist, schließt die Person ab — sie ist
            # die Einzige, die weiß, ob sie vorliegt.
            raise TransitionNotAllowed(self.status, "completed before the release is confirmed")
        self._move(TransferStatus.COMPLETED, now)

    def withdraw(self, *, now: datetime) -> None:
        self._assert_running("withdrawn")
        self._move(TransferStatus.WITHDRAWN, now)

    # --- Gemeinsames -----------------------------------------------------

    @property
    def is_running(self) -> bool:
        return self.status not in _FINAL

    def _assert_person(self, actor: UUID) -> None:
        if actor != self.subject_id:
            raise NotYours()

    def _assert_status(self, wanted: TransferStatus, what: str) -> None:
        if self.status is not wanted:
            raise TransitionNotAllowed(self.status, what)

    def _assert_running(self, what: str) -> None:
        if not self.is_running:
            raise TransitionNotAllowed(self.status, what)

    def _move(self, to: TransferStatus, now: datetime) -> None:
        self.status = to
        self.updated_at = now
