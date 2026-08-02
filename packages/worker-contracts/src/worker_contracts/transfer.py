"""Versionierte Boundary-DTOs für transfer-service (ADR-0004 §1).

`is_approachable` ist abgeleitet und wird mitgeschickt: die Frage „darf ich
diese Person ansprechen?" soll ein Aufrufer nicht aus dem Zustand
zusammenreimen müssen — dabei entstünde in jedem Client eine eigene Auslegung.
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = ["MarketStatusV1", "SaveMarketStatusV1"]

AvailabilityV1 = Literal["open", "listening", "unavailable"]


class SaveMarketStatusV1(BaseModel):
    availability: AvailabilityV1
    #: Arbeite ich gerade irgendwo? Kein Zustand, sondern eine Angabe: man kann
    #: beschäftigt UND offen sein.
    employed: bool = False
    note: str = Field(default="", max_length=500)


class MarketStatusV1(BaseModel):
    subject_id: UUID
    availability: AvailabilityV1
    employed: bool
    note: str
    is_approachable: bool
    updated_at: datetime
