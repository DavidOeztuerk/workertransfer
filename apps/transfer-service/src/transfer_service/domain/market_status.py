"""Der Marktstatus einer Person — „bin ich ansprechbar?"

Die gefährlichste Angabe im ganzen System. Ein Lebenslauf verrät, wo jemand
war; der Marktstatus verrät, dass er weg will — und schon die **Existenz** der
Aussage kann jemanden den Arbeitsplatz kosten.

Deshalb gibt es hier bewusst **kein `:public`**: die Freigabe nennt immer einen
Empfänger. Beim Profil ist „für alle Unternehmen" eine sinnvolle Wahl; hier wäre
sie ein Schalter, dessen Folgen niemand überblickt — darunter der eigene
Arbeitgeber, der auf derselben Plattform ist.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID

from worker_core import DomainError

__all__ = ["Availability", "InvalidNote", "MarketStatus", "tenant_capability"]

MAX_NOTE = 500


class Availability(StrEnum):
    """Drei Zustände, alle Übergänge erlaubt.

    Es ist eine Aussage über den eigenen Willen, und der ändert sich ohne
    Reihenfolge. Eine Zustandsmaschine mit Verboten wäre hier Bevormundung:
    niemand muss erst „offen" gewesen sein, um „zuhörend" zu werden.
    """

    OPEN = "open"
    LISTENING = "listening"
    UNAVAILABLE = "unavailable"


def tenant_capability(tenant_id: UUID) -> str:
    """Immer empfängerbezogen. Ein `:public` gibt es hier nicht."""
    return f"market.visibility:tenant:{tenant_id}"


class InvalidNote(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_note", f"The note exceeds {MAX_NOTE} characters")


@dataclass(eq=False, slots=True)
class MarketStatus:
    subject_id: UUID
    availability: Availability
    #: Arbeite ich gerade irgendwo? Ein Feld, kein Zustand: es ist keine
    #: Absicht. Man kann beschäftigt UND offen sein — das ist der Normalfall auf
    #: einem Transfermarkt, und als Zustand wäre er unmöglich.
    employed: bool
    note: str
    created_at: datetime
    updated_at: datetime

    @classmethod
    def default_for(cls, subject_id: UUID, *, now: datetime) -> MarketStatus:
        """Wer nichts gesagt hat, hat nicht „ich höre zu" gesagt.

        Die Voreinstellung darf nie zugunsten des Marktes ausfallen.
        """
        return cls(
            subject_id=subject_id,
            availability=Availability.UNAVAILABLE,
            employed=False,
            note="",
            created_at=now,
            updated_at=now,
        )

    @classmethod
    def create(
        cls,
        subject_id: UUID,
        *,
        availability: Availability,
        employed: bool,
        note: str,
        now: datetime,
    ) -> MarketStatus:
        return cls(
            subject_id=subject_id,
            availability=availability,
            employed=employed,
            note=_note(note),
            created_at=now,
            updated_at=now,
        )

    def update(
        self, *, availability: Availability, employed: bool, note: str, now: datetime
    ) -> None:
        # Erst prüfen, dann schreiben: ein abgelehntes Formular darf kein halb
        # geändertes Aggregat hinterlassen.
        checked = _note(note)
        self.availability = availability
        self.employed = employed
        self.note = checked
        self.updated_at = now

    @property
    def is_approachable(self) -> bool:
        """Darf man diese Person ansprechen?

        `UNAVAILABLE` heißt nein — auch für ein Unternehmen mit Freigabe. Die
        Freigabe erlaubt zu *sehen*, nicht zu *stören*.
        """
        return self.availability in {Availability.OPEN, Availability.LISTENING}


def _note(value: str) -> str:
    cleaned = value.strip()
    if len(cleaned) > MAX_NOTE:
        raise InvalidNote()
    return cleaned
