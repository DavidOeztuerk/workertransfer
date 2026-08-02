"""Versionierte Boundary-DTOs für applications-service (ADR-0004 §1).

Die Bewerbung trägt **keine Profildaten** — nur eine `subject_id`. Wer Profil,
Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und dort
greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte einen
zweiten Filter, und der weicht irgendwann vom ersten ab.
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = ["AdvanceApplicationV1", "ApplicationV1", "SubmitApplicationV1"]


class SubmitApplicationV1(BaseModel):
    job_id: UUID
    message: str = Field(default="", max_length=4000)
    #: Das Profil ist immer dabei und steht deshalb nicht zur Wahl.
    shares_resume: bool = False
    shares_portfolio: bool = False


class ApplicationV1(BaseModel):
    id: UUID
    job_id: UUID
    tenant_id: UUID
    subject_id: UUID
    message: str
    shares_resume: bool
    shares_portfolio: bool
    status: Literal["submitted", "reviewing", "rejected", "withdrawn", "hired"]
    created_at: datetime
    updated_at: datetime


class AdvanceApplicationV1(BaseModel):
    #: Nur die drei, die dem Unternehmen gehören. `submitted` und `withdrawn`
    #: sind Handlungen der Person und stehen hier bewusst nicht.
    status: Literal["reviewing", "rejected", "hired"]
