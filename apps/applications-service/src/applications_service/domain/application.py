"""Die Bewerbung — wo Person und Unternehmen sich treffen.

Bis hier lief alles auf einer von zwei Achsen: Profil, Lebenslauf und Portfolio
gehören einer Person und werden über den Consent-Ledger freigegeben; eine
Stellenausschreibung gehört einem Unternehmen, und der Ledger kommt dort nicht
vor, weil niemand betroffen ist, der einwilligen könnte.

Eine Bewerbung verbindet beide. Sie **kopiert keine Daten**: sie nennt eine
`subject_id`, und das Unternehmen holt Profil, Lebenslauf und Portfolio bei den
zuständigen Diensten — wo der Ledger greift. Eine Kopie ließe sich nicht
widerrufen, und ein Widerruf muss wirken (ADR-0013).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError

__all__ = [
    "Application",
    "ApplicationStatus",
    "InvalidMessage",
    "NotYours",
    "SharedArtifacts",
    "TransitionNotAllowed",
]

MAX_MESSAGE = 4000


class ApplicationStatus(StrEnum):
    SUBMITTED = "submitted"
    REVIEWING = "reviewing"
    REJECTED = "rejected"
    WITHDRAWN = "withdrawn"
    HIRED = "hired"


#: Zustände, aus denen es keinen Weg zurück in eine laufende Bewerbung gibt.
#: Nach einer Ablehnung erneut abzuschicken wäre Nachfassen gegen einen Willen,
#: der schon geäußert wurde — dieselbe Regel wie beim Lebenslauf.
_FINAL = frozenset({ApplicationStatus.REJECTED, ApplicationStatus.HIRED})


class InvalidMessage(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_message", f"The message exceeds {MAX_MESSAGE} characters")


class NotYours(DomainError):
    def __init__(self) -> None:
        super().__init__("not_yours", "This application belongs to someone else")


class TransitionNotAllowed(DomainError):
    def __init__(self, current: ApplicationStatus, wanted: ApplicationStatus) -> None:
        super().__init__(
            "transition_not_allowed", f"A {current} application cannot become {wanted}"
        )


@dataclass(frozen=True, slots=True)
class SharedArtifacts:
    """Was mitgeschickt wird.

    `profile` ist immer dabei: eine Bewerbung ohne jede Angabe zur Person ist
    keine, und „ich bewerbe mich, aber ihr dürft nichts von mir sehen" ist keine
    Wahl, die jemand ernsthaft trifft.
    """

    resume: bool = False
    portfolio: bool = False

    @property
    def profile(self) -> bool:
        return True


@dataclass(eq=False, slots=True)
class Application:
    id: UUID
    job_id: UUID
    #: Kopiert aus der Stelle, mit Absicht: ein Fremdschlüssel geht nicht (andere
    #: Datenbank, ADR-0004), und ein Round-Trip je Lesezugriff wäre teuer für
    #: eine Angabe, die sich nie ändert — eine Stelle wechselt nicht das
    #: Unternehmen. Eine Kopie ist nur gefährlich, wenn das Original sich ändern
    #: kann.
    tenant_id: UUID
    subject_id: UUID
    message: str
    shared: SharedArtifacts
    status: ApplicationStatus
    created_at: datetime
    updated_at: datetime
    answered_at: datetime | None = field(default=None)

    @classmethod
    def submit(
        cls,
        *,
        job_id: UUID,
        tenant_id: UUID,
        subject_id: UUID,
        message: str,
        shared: SharedArtifacts,
        now: datetime,
    ) -> Application:
        return cls(
            id=uuid4(),
            job_id=job_id,
            tenant_id=tenant_id,
            subject_id=subject_id,
            message=_message(message),
            shared=shared,
            status=ApplicationStatus.SUBMITTED,
            created_at=now,
            updated_at=now,
        )

    def resubmit(self, *, message: str, shared: SharedArtifacts, now: datetime) -> None:
        """Nach einem Rückzug erneut bewerben — eine neue Entscheidung.

        Nach einer Ablehnung nicht: das wäre Nachfassen gegen ein „nein", das
        schon gefallen ist.
        """
        if self.status is not ApplicationStatus.WITHDRAWN:
            raise TransitionNotAllowed(self.status, ApplicationStatus.SUBMITTED)
        self.message = _message(message)
        self.shared = shared
        self.status = ApplicationStatus.SUBMITTED
        self.answered_at = None
        self.updated_at = now

    def withdraw(self, *, by: UUID, now: datetime) -> None:
        """Immer möglich, solange die Bewerbung läuft.

        Auch aus `REVIEWING`: wer nicht mehr will, muss nicht warten, bis
        jemand anderes fertig ist.
        """
        if by != self.subject_id:
            raise NotYours()
        if self.status is ApplicationStatus.WITHDRAWN or self.status in _FINAL:
            raise TransitionNotAllowed(self.status, ApplicationStatus.WITHDRAWN)
        self.status = ApplicationStatus.WITHDRAWN
        self.updated_at = now

    def advance(self, *, to: ApplicationStatus, now: datetime) -> None:
        """Das Unternehmen bewegt die Bewerbung durch das Verfahren."""
        if to not in {
            ApplicationStatus.REVIEWING,
            ApplicationStatus.REJECTED,
            ApplicationStatus.HIRED,
        }:
            raise TransitionNotAllowed(self.status, to)
        # Eine zurückgezogene Bewerbung ist keine mehr; eine abgelehnte oder
        # angenommene ist entschieden.
        if self.status is ApplicationStatus.WITHDRAWN or self.status in _FINAL:
            raise TransitionNotAllowed(self.status, to)
        self.status = to
        if to in _FINAL:
            self.answered_at = now
        self.updated_at = now

    @property
    def is_live(self) -> bool:
        """Läuft die Bewerbung noch — und damit die Freigabe der Daten?"""
        return self.status in {ApplicationStatus.SUBMITTED, ApplicationStatus.REVIEWING}


def _message(value: str) -> str:
    cleaned = value.strip()
    if len(cleaned) > MAX_MESSAGE:
        raise InvalidMessage()
    return cleaned
