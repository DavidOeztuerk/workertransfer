"""Die Stellenausschreibung — die erste Sache, die einem Unternehmen gehört.

Profil, Lebenslauf und Portfolio gehören einer Person; ihre Schlüssel sind
`subject_id`, und ihre Sichtbarkeit beantwortet der Consent-Ledger. Eine
Ausschreibung gehört einem Unternehmen, und damit ist der Tenant hier die Achse
und kein Nebenattribut (ADR-0017).

Der Consent-Ledger kommt deshalb nicht vor: eine Ausschreibung ist eine Aussage
des Unternehmens über sich selbst, keine Information über eine Person — es gibt
niemanden, der einwilligen könnte. Er kehrt zurück, sobald sich jemand bewirbt.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError

__all__ = [
    "EmploymentType",
    "InvalidText",
    "Job",
    "JobStatus",
    "RemoteMode",
    "TransitionNotAllowed",
]

MAX_TITLE = 160
MAX_DESCRIPTION = 20000
MAX_LOCATION = 160


class RemoteMode(StrEnum):
    """Ein Aufzählungstyp, kein Boolescher Wert.

    „Remote möglich?" ist die Frage, die alle stellen, und „ja/nein" beantwortet
    sie falsch: hybrid ist der häufigste Fall und keine Zwischenstufe von wahr.
    """

    NONE = "none"
    HYBRID = "hybrid"
    FULL = "full"


class EmploymentType(StrEnum):
    FULL_TIME = "full_time"
    PART_TIME = "part_time"
    CONTRACT = "contract"
    INTERNSHIP = "internship"


class JobStatus(StrEnum):
    DRAFT = "draft"
    PUBLISHED = "published"
    CLOSED = "closed"


class InvalidText(DomainError):
    def __init__(self, field: str, detail: str) -> None:
        super().__init__("invalid_text", f"{field} {detail}")


class TransitionNotAllowed(DomainError):
    def __init__(self, current: JobStatus, wanted: JobStatus) -> None:
        super().__init__(
            "transition_not_allowed",
            f"A {current} job cannot become {wanted}",
        )


def _text(field: str, value: str, *, required: bool, limit: int) -> str:
    cleaned = value.strip()
    if required and not cleaned:
        raise InvalidText(field, "must not be empty")
    if len(cleaned) > limit:
        raise InvalidText(field, f"exceeds {limit} characters")
    return cleaned


@dataclass(eq=False, slots=True)
class Job:
    id: UUID
    tenant_id: UUID
    title: str
    description: str
    location: str
    remote: RemoteMode
    employment: EmploymentType
    status: JobStatus
    published_at: datetime | None
    created_at: datetime
    updated_at: datetime

    @classmethod
    def draft(
        cls,
        *,
        tenant_id: UUID,
        title: str,
        description: str,
        location: str,
        remote: RemoteMode,
        employment: EmploymentType,
        now: datetime,
    ) -> Job:
        checked_title, checked_description, checked_location = _validated(
            title, description, location
        )
        return cls(
            id=uuid4(),
            tenant_id=tenant_id,
            title=checked_title,
            description=checked_description,
            location=checked_location,
            remote=remote,
            employment=employment,
            status=JobStatus.DRAFT,
            published_at=None,
            created_at=now,
            updated_at=now,
        )

    def update(
        self,
        *,
        title: str,
        description: str,
        location: str,
        remote: RemoteMode,
        employment: EmploymentType,
        now: datetime,
    ) -> None:
        """Bearbeiten ist auch nach dem Veröffentlichen erlaubt.

        Eine Ausschreibung mit einem Tippfehler zurückzuziehen und neu zu
        stellen würde Bewerbungen zerreißen. Was sich ändert, ändert sich
        sichtbar (`updated_at`).
        """
        # Erst vollständig prüfen, dann schreiben.
        checked_title, checked_description, checked_location = _validated(
            title, description, location
        )
        self.title = checked_title
        self.description = checked_description
        self.location = checked_location
        self.remote = remote
        self.employment = employment
        self.updated_at = now

    def publish(self, *, now: datetime) -> None:
        if self.status is not JobStatus.DRAFT:
            raise TransitionNotAllowed(self.status, JobStatus.PUBLISHED)
        self.status = JobStatus.PUBLISHED
        self.published_at = now
        self.updated_at = now

    def close(self, *, now: datetime) -> None:
        # Aus einem Entwurf direkt zu schließen ist erlaubt: er war nie
        # draußen, und ihn nur über den Umweg der Veröffentlichung loszuwerden
        # wäre absurd.
        if self.status is JobStatus.CLOSED:
            raise TransitionNotAllowed(self.status, JobStatus.CLOSED)
        self.status = JobStatus.CLOSED
        self.updated_at = now

    @property
    def is_public(self) -> bool:
        """Nur veröffentlichte gibt es für die Öffentlichkeit.

        Eine geschlossene wird nicht wieder veröffentlicht: wer erneut sucht,
        sucht etwas anderes, auch wenn der Titel gleich lautet. Ein Rückweg
        würde eine Bewerbungshistorie an eine Stelle hängen, die es so nicht
        mehr gibt.
        """
        return self.status is JobStatus.PUBLISHED


def _validated(title: str, description: str, location: str) -> tuple[str, str, str]:
    """Gemeinsam, damit `draft` und `update` nicht auseinanderlaufen können."""
    return (
        _text("Title", title, required=True, limit=MAX_TITLE),
        _text("Description", description, required=True, limit=MAX_DESCRIPTION),
        # Leer heißt „nicht angegeben", nicht „überall" — das wäre eine
        # Behauptung, die das Unternehmen nicht gemacht hat.
        _text("Location", location, required=False, limit=MAX_LOCATION),
    )
