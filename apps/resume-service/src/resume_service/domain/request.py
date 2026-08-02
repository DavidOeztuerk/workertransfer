"""Die Anfrage eines Unternehmens nach einem Lebenslauf.

Ein Vorgang, keine Berechtigung. Der Unterschied ist die wichtigste Aussage
dieses Moduls: `GRANTED` heißt „wurde einmal erteilt", nicht „gilt gerade". Ob
der Zugriff jetzt besteht, beantwortet ausschließlich der Consent-Ledger, frisch
bei jedem Lesezugriff (ADR-0013).

Deshalb gibt es hier weder ein `is_active` noch ein `revoked_at`. Nach einem
Widerruf bleibt die Anfrage `GRANTED` und der Lesezugriff läuft trotzdem ins
Leere — das ist kein Widerspruch, sondern die Trennung zwischen dem, was
geschehen ist, und dem, was gilt.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError

__all__ = [
    "AlreadyAnswered",
    "NotTheSubject",
    "RequestStatus",
    "ResumeRequest",
]


class RequestStatus(StrEnum):
    PENDING = "PENDING"
    GRANTED = "GRANTED"
    DECLINED = "DECLINED"


class NotTheSubject(DomainError):
    def __init__(self) -> None:
        super().__init__("not_the_subject", "Only the person asked may answer")


class AlreadyAnswered(DomainError):
    def __init__(self, status: RequestStatus) -> None:
        super().__init__("already_answered", f"This request is already {status}")


@dataclass(eq=False, slots=True)
class ResumeRequest:
    id: UUID
    subject_id: UUID
    tenant_id: UUID
    #: Wer im Unternehmen gefragt hat. Das Unternehmen trägt die Berechtigung,
    #: die Person die Spur — ohne dieses Feld steht im Protokoll nur
    #: „irgendwer bei Acme".
    requested_by: UUID
    status: RequestStatus
    created_at: datetime
    answered_at: datetime | None

    @classmethod
    def open(
        cls, *, subject_id: UUID, tenant_id: UUID, requested_by: UUID, now: datetime
    ) -> ResumeRequest:
        return cls(
            id=uuid4(),
            subject_id=subject_id,
            tenant_id=tenant_id,
            requested_by=requested_by,
            status=RequestStatus.PENDING,
            created_at=now,
            answered_at=None,
        )

    def grant(self, *, by: UUID, now: datetime) -> None:
        self._assert_answerable_by(by)
        self.status = RequestStatus.GRANTED
        self.answered_at = now

    def decline(self, *, by: UUID, now: datetime) -> None:
        self._assert_answerable_by(by)
        self.status = RequestStatus.DECLINED
        self.answered_at = now

    def _assert_answerable_by(self, actor: UUID) -> None:
        if actor != self.subject_id:
            raise NotTheSubject()
        if self.status is not RequestStatus.PENDING:
            # Ein zweites „grant" nach einem „decline" würde die Ablehnung
            # stillschweigend umdrehen. Und ein Widerruf gehört in den Ledger,
            # nicht in diesen Vorgang.
            raise AlreadyAnswered(self.status)
