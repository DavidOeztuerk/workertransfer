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

__all__ = [
    "ExpressInterestV1",
    "MakeOfferV1",
    "MarketRequestV1",
    "MarketStatusV1",
    "SaveMarketStatusV1",
    "TransferV1",
]

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


class ExpressInterestV1(BaseModel):
    subject_id: UUID
    message: str = Field(default="", max_length=2000)


class MakeOfferV1(BaseModel):
    note: str = Field(default="", max_length=2000)
    start_on: str | None = Field(default=None, pattern=r"^\d{4}-(0[1-9]|1[0-2])$")
    #: In Cent, und festgehalten statt bewegt: die Plattform führt kein Geld.
    fee_cents: int | None = Field(default=None, ge=0)


class TransferV1(BaseModel):
    id: UUID
    subject_id: UUID
    tenant_id: UUID
    status: Literal[
        "interested", "talking", "offered", "accepted", "completed", "declined", "withdrawn"
    ]
    #: Ob eine Freigabe des aktuellen Arbeitgebers nötig ist — beim Anlegen aus
    #: dem Marktstatus kopiert. Die Plattform kontaktiert diesen Arbeitgeber
    #: NICHT; sie weiß nicht, wer er ist, und soll es nicht wissen.
    requires_release: bool
    release_confirmed: bool
    message: str
    offer_note: str
    offer_start_on: str | None
    offer_fee_cents: int | None
    created_at: datetime
    updated_at: datetime


class MarketRequestV1(BaseModel):
    """Die Anfrage eines Unternehmens nach einem Marktstatus.

    `status` sagt, was geschehen ist. `active` sagt, was gilt — es kommt frisch
    aus dem Ledger und kann von `status` abweichen: nach einem Widerruf bleibt
    `GRANTED` stehen, während `active` auf `false` fällt. Genau diese Trennung
    ist der Grund, warum die Berechtigung nicht im Vorgang gespeichert wird.

    `active` ist nur für die betroffene Person gefüllt; das anfragende
    Unternehmen sieht `None` — es hat die Antwort schon in Form des Status, den
    es bekommt oder nicht bekommt.
    """

    id: UUID
    subject_id: UUID
    tenant_id: UUID
    status: Literal["PENDING", "GRANTED", "DECLINED"]
    created_at: datetime
    answered_at: datetime | None = None
    active: bool | None = None
