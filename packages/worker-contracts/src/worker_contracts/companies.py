"""Versionierte Boundary-DTOs für companies-service (ADR-0004 §1).

Kein `tenant_id` im Speichern-Vertrag: das Unternehmen steht im Token und wird
gegen die Mitgliedschaft geprüft. Was der Client nicht senden kann, kann er
nicht fälschen (ADR-0018).
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = ["CompanyProfileV1", "SaveCompanyProfileV1"]


class SaveCompanyProfileV1(BaseModel):
    display_name: str = Field(..., min_length=1, max_length=160)
    about: str = Field(default="", max_length=8000)
    #: Optional. Dass nur http/https durchkommen, entscheidet die Domäne — ein
    #: zweites Muster hier wäre eine zweite Meinung, die auseinanderlaufen kann.
    website: str | None = Field(default=None, max_length=2000)
    locations: list[str] = Field(default_factory=list, max_length=40)
    benefits: list[str] = Field(default_factory=list, max_length=40)


class CompanyProfileV1(BaseModel):
    tenant_id: UUID
    display_name: str
    about: str
    website: str | None
    locations: list[str]
    benefits: list[str]
    updated_at: datetime
