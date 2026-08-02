"""Versionierte Boundary-DTOs für resume-service (ADR-0004 §1).

Monatsgenaue Daten als String `YYYY-MM`: ein `date` würde einen Tag erzwingen,
den niemand hat, und `null` für „läuft noch" ist eindeutiger als ein Datum in
der Zukunft.

Kein Sichtbarkeits- oder Empfängerfeld — wer den Lebenslauf sehen darf, steht
im Consent-Ledger (ADR-0020).
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "EducationV1",
    "PositionV1",
    "ResumeRequestV1",
    "ResumeV1",
    "SaveResumeV1",
]

_MONTH = Field(..., pattern=r"^\d{4}-(0[1-9]|1[0-2])$", description="Monat als YYYY-MM")


class PositionV1(BaseModel):
    employer: str = Field(..., min_length=1, max_length=160)
    title: str = Field(..., min_length=1, max_length=160)
    started_on: str = _MONTH
    #: `None` heißt „läuft noch" — nicht „unbekannt".
    ended_on: str | None = Field(default=None, pattern=r"^\d{4}-(0[1-9]|1[0-2])$")
    description: str = Field(default="", max_length=2000)


class EducationV1(BaseModel):
    institution: str = Field(..., min_length=1, max_length=160)
    qualification: str = Field(default="", max_length=160)
    started_on: str = _MONTH
    ended_on: str | None = Field(default=None, pattern=r"^\d{4}-(0[1-9]|1[0-2])$")


class SaveResumeV1(BaseModel):
    # Obergrenzen schon hier, damit ein absurd langer Body gar nicht erst in die
    # Domäne läuft. Sortiert und auf „höchstens eine laufende Station" geprüft
    # wird dort.
    positions: list[PositionV1] = Field(default_factory=list, max_length=40)
    education: list[EducationV1] = Field(default_factory=list, max_length=20)


class ResumeV1(BaseModel):
    subject_id: UUID
    positions: list[PositionV1]
    education: list[EducationV1]
    updated_at: datetime


class ResumeRequestV1(BaseModel):
    """Ein Anfragevorgang.

    `status` sagt, was geschehen ist. `active` sagt, was gilt — es kommt frisch
    aus dem Ledger und kann von `status` abweichen: nach einem Widerruf bleibt
    `GRANTED` stehen, während `active` auf `false` fällt. Genau diese Trennung
    ist der Grund, warum die Berechtigung nicht im Vorgang gespeichert wird.

    `active` ist nur für die betroffene Person gefüllt; das anfragende
    Unternehmen sieht `None`, denn es hat die Antwort schon in Form der Daten,
    die es bekommt oder nicht bekommt.
    """

    id: UUID
    subject_id: UUID
    tenant_id: UUID
    status: str
    created_at: datetime
    answered_at: datetime | None = None
    active: bool | None = None
