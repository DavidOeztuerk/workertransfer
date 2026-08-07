"""Versionierte Boundary-DTOs für profile-service (ADR-0004 §1).

Kein Sichtbarkeits-Feld: ob ein Profil gezeigt werden darf, beantwortet der
Consent-Ledger, nicht das Profil selbst. Ein Flag hier wäre eine zweite
Wahrheit — und eine, die der Client sehen und womöglich senden könnte.
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "DraftProfileTextV1",
    "ProfilePageV1",
    "ProfileTextDraftV1",
    "ProfileV1",
    "SaveProfileV1",
]


class SaveProfileV1(BaseModel):
    headline: str = Field(..., min_length=1, max_length=120)
    bio: str = Field(default="", max_length=4000)
    location: str = Field(default="", max_length=120)
    remote_ok: bool = False
    # Obergrenze schon hier, damit ein absurd langer Body gar nicht erst in die
    # Domäne läuft. Entdoppelt und getrimmt wird dort.
    skills: list[str] = Field(default_factory=list, max_length=60)


class ProfileV1(BaseModel):
    subject_id: UUID
    headline: str
    bio: str
    location: str
    remote_ok: bool
    skills: list[str]
    updated_at: datetime


class ProfilePageV1(BaseModel):
    """Eine Seite Kandidaten.

    `items` kann weniger Einträge enthalten als angefragt: gefiltert wird nach
    der Einwilligung, und nachzuladen bis die Seite voll ist würde über die
    Anzahl der Runden verraten, wie viele Profile nicht freigegeben sind.
    `next_cursor` sagt, ob es weitergeht — die Anzahl sagt nichts über die
    Gesamtmenge.
    """

    items: list[ProfileV1]
    next_cursor: str | None = None


class DraftProfileTextV1(BaseModel):
    """Was die Person sich wünscht — mehr geht nicht in den Auftrag.

    Kein `subject_id`, kein Name, keine Adresse: der Rest des Zusammenhangs
    kommt aus dem gespeicherten Profil und aus dem Token. Was der Client nicht
    senden kann, kann er nicht in einen fremden Dienst schleusen.
    """

    wish: str = Field(default="", max_length=200)


class ProfileTextDraftV1(BaseModel):
    """Ein Vorschlag, kein Ergebnis.

    Nichts daran wird gespeichert — weder hier noch im Profil. Er lebt im
    Formular, bis die Person selbst speichert, und dann ist es ihr Text.
    """

    draft: str
