"""Versionierte Boundary-DTOs für portfolio-service (ADR-0004 §1).

Kein Sichtbarkeitsfeld: ob ein Portfolio gezeigt werden darf, beantwortet der
Consent-Ledger (ADR-0020).
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = ["PortfolioItemV1", "PortfolioV1", "SavePortfolioV1"]


class PortfolioItemV1(BaseModel):
    title: str = Field(..., min_length=1, max_length=160)
    summary: str = Field(default="", max_length=1000)
    #: Optional. Dass nur http/https durchkommen, entscheidet die Domäne — ein
    #: zweites Muster hier wäre eine zweite Meinung, die auseinanderlaufen kann.
    url: str | None = Field(default=None, max_length=2000)
    role: str = Field(default="", max_length=160)
    year: int | None = Field(default=None, ge=1900, le=2200)


class SavePortfolioV1(BaseModel):
    # Obergrenze schon hier, damit ein absurd langer Body gar nicht erst in die
    # Domäne läuft.
    items: list[PortfolioItemV1] = Field(default_factory=list, max_length=30)


class PortfolioV1(BaseModel):
    subject_id: UUID
    items: list[PortfolioItemV1]
    updated_at: datetime
