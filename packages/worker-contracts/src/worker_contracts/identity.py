"""Versionierte Boundary-DTOs für identity-service (ADR-0004 §1).

`CreateCompanyV1` trägt bewusst **kein** Domain-Feld: die Firmendomain wird aus
der bestätigten Adresse des Erstellers abgeleitet. Was der Client nicht senden
kann, kann er nicht fälschen — dieselbe Regel, die ADR-0018 für `tenant_id`
durchgesetzt hat.
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "AcceptInvitationV1",
    "CompanyMemberV1",
    "CompanyV1",
    "CreateCompanyV1",
    "InvitationV1",
    "InviteMemberV1",
    "MembershipV1",
    "NotificationPreferencesV1",
    "NotifyV1",
    "RegisterUserV1",
    "ResendVerificationV1",
    "VerifyEmailV1",
]


class RegisterUserV1(BaseModel):
    # Kein tenant_id: registrieren ist der Akt einer natürlichen Person
    # (ADR-0017), und ein Unternehmen wird danach bewusst gewählt.
    email: str = Field(..., min_length=3, max_length=320)
    password: str = Field(..., min_length=1, max_length=1024)
    display_name: str = Field(..., min_length=1, max_length=255)


class VerifyEmailV1(BaseModel):
    token: str = Field(..., min_length=1, max_length=512)


class ResendVerificationV1(BaseModel):
    email: str = Field(..., min_length=3, max_length=320)


class CreateCompanyV1(BaseModel):
    name: str = Field(..., min_length=1, max_length=200)


class CompanyV1(BaseModel):
    id: UUID
    name: str
    domain: str


class MembershipV1(BaseModel):
    """Ein Unternehmen aus Sicht der Person, die dafür handeln darf."""

    id: UUID
    name: str
    domain: str
    role: str


class InviteMemberV1(BaseModel):
    """Wen einladen, und mit welcher Rolle.

    Kein Unternehmen im Body: es steht im Pfad und wird gegen die Mitgliedschaft
    des Aufrufers geprüft — nie gegen eine Angabe aus dem Body.
    """

    # `str`, nicht `EmailStr`: die Domäne hat mit `Email` bereits eine
    # Prüfung, und eine zweite an der Grenze wäre eine zweite Meinung, die
    # auseinanderlaufen kann — abgesehen von der zusätzlichen Abhängigkeit.
    email: str = Field(..., min_length=3, max_length=320)
    role: Literal["admin", "member"] = "member"


class InvitationV1(BaseModel):
    """Eine offene Einladung, wie ein Administrator sie sieht.

    Kein Token: der geht ausschließlich per Mail an die eingeladene Adresse.
    Stünde er in dieser Liste, könnte jeder Administrator jede Einladung selbst
    annehmen — und der Adressvergleich beim Annehmen wäre umgehbar.
    """

    id: UUID
    email: str
    role: str
    status: str
    created_at: datetime
    expires_at: datetime


class AcceptInvitationV1(BaseModel):
    token: str = Field(..., min_length=1, max_length=512)


class CompanyMemberV1(BaseModel):
    user_id: UUID
    display_name: str
    role: str


class NotifyV1(BaseModel):
    """„Sag dieser Person, dass es etwas Neues gibt."

    Bewusst OHNE Textfeld. Was die Mail sagt, entscheidet allein der
    identity-service, und sie sagt für jede Art dasselbe: eine Mail landet
    womöglich im Postfach beim aktuellen Arbeitgeber, und eine Zeile mit einem
    Firmennamen darin wäre genau die Auskunft, gegen die diese Plattform gebaut
    ist. Gäbe es hier ein `message`, wäre der Tag absehbar, an dem jemand „nur
    diese eine Zeile" mitschickt.
    """

    user_id: UUID
    kind: Literal["resume_request", "market_request", "application_update", "transfer_update"]


class NotificationPreferencesV1(BaseModel):
    """Vier Schalter, alle standardmäßig an.

    Die einzige Voreinstellung in diesem System, die nicht zurückhaltend ist —
    wer nicht erfährt, dass gefragt wurde, hat keine Wahl, sondern nur den
    Anschein einer.
    """

    resume_request: bool = True
    market_request: bool = True
    application_update: bool = True
    transfer_update: bool = True
