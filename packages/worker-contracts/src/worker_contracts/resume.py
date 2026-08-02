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
