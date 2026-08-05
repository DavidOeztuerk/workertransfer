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

__all__ = ["DraftJobTextV1", "JobPageV1", "JobTextDraftV1", "JobV1", "SaveJobV1"]

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
    #: Was die Stelle verlangt. Voreinstellung leer: eine Ausschreibung darf
    #: sagen, dass sie nichts aufzählt. Die Grenzen prüft die Domäne — hier
    #: steht nur ein Deckel gegen eine Liste, die nie ankommen sollte.
    skills: list[str] = Field(default_factory=list, max_length=200)


class JobV1(BaseModel):
    id: UUID
    tenant_id: UUID
    title: str
    description: str
    location: str
    remote: RemoteModeV1
    employment: EmploymentTypeV1
    #: Öffentlich wie die ganze Stelle: wer sucht, darf sagen, was er sucht.
    skills: list[str] = Field(default_factory=list)
    status: Literal["draft", "published", "closed"]
    published_at: datetime | None
    updated_at: datetime


class JobPageV1(BaseModel):
    items: list[JobV1]
    next_cursor: str | None = None


class DraftJobTextV1(BaseModel):
    """Der Auftrag: die Anzeige, wie sie im Formular steht, plus ein Wunsch.

    Anders als beim Profil kommt der Zusammenhang HIER aus dem Request — die
    Anzeige gibt es beim Schreiben ja noch nicht in der Datenbank. Das ist
    unbedenklich: es sind Angaben des Unternehmens über sich selbst, keine
    Daten über eine Person.
    """

    title: str = Field(default="", max_length=160)
    description: str = Field(default="", max_length=20000)
    location: str = Field(default="", max_length=160)
    skills: list[str] = Field(default_factory=list, max_length=200)
    wish: str = Field(default="", max_length=200)


class JobTextDraftV1(BaseModel):
    draft: str
