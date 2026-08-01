"""Versionierte Boundary-DTOs für identity-service (ADR-0004 §1).

`CreateCompanyV1` trägt bewusst **kein** Domain-Feld: die Firmendomain wird aus
der bestätigten Adresse des Erstellers abgeleitet. Was der Client nicht senden
kann, kann er nicht fälschen — dieselbe Regel, die ADR-0018 für `tenant_id`
durchgesetzt hat.
"""

from __future__ import annotations

from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "CompanyV1",
    "CreateCompanyV1",
    "MembershipV1",
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
