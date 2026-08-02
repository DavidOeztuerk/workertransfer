"""Versionierte Boundary-DTOs für jobs-service (ADR-0004 §1).

Kein `tenant_id` im Anlegen-Vertrag: das Unternehmen steht im Token und wird
gegen die Mitgliedschaft geprüft. Was der Client nicht senden kann, kann er
nicht fälschen (ADR-0018).
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = ["JobPageV1", "JobV1", "SaveJobV1"]

#: Als Literale statt als freier String: ein Tippfehler soll an der Grenze
#: auffallen und nicht als unbekannter Wert in der Datenbank landen.
RemoteModeV1 = Literal["none", "hybrid", "full"]
EmploymentTypeV1 = Literal["full_time", "part_time", "contract", "internship"]


class SaveJobV1(BaseModel):
    title: str = Field(..., min_length=1, max_length=160)
    description: str = Field(..., min_length=1, max_length=20000)
    #: Leer heißt „nicht angegeben", nicht „überall".
    location: str = Field(default="", max_length=160)
    remote: RemoteModeV1 = "none"
    employment: EmploymentTypeV1 = "full_time"


class JobV1(BaseModel):
    id: UUID
    tenant_id: UUID
    title: str
    description: str
    location: str
    remote: RemoteModeV1
    employment: EmploymentTypeV1
    status: Literal["draft", "published", "closed"]
    published_at: datetime | None
    updated_at: datetime


class JobPageV1(BaseModel):
    items: list[JobV1]
    next_cursor: str | None = None
