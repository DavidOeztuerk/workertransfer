"""Unternehmen als Identität — Name und bewiesene Domain, sonst nichts.

Das Employer-Profil (Kultur, Benefits, Team, Karriereseite) gehört in den
companies-service aus Phase 4. Hier liegt nur, was der Tenant-Wechsel synchron
braucht.

Ein Unternehmen entsteht ausschließlich bewiesen: die Domain stammt aus der
bestätigten Adresse des Erstellers, nie aus einem Request. Deshalb gibt es
keinen unverifizierten Zustand.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError

from identity_service.domain.value_objects import Email

__all__ = [
    "PUBLIC_EMAIL_DOMAINS",
    "AccountNotConfirmed",
    "Company",
    "DomainAlreadyClaimed",
    "EmailDomain",
    "InvalidCompanyName",
    "PublicEmailDomain",
    "TenantStatus",
]


class TenantStatus(StrEnum):
    """Zwei Zustände, und `DORMANT` ist kein „gelöscht".

    Löscht die einzige Person mit `role='admin'` ihr Konto, wird das Unternehmen
    **stillgelegt** und seine Anzeigen zurückgezogen (ADR-0027 §7). Nicht: die
    Löschung blockieren, bis jemand anderes Admin ist — ein persönliches Recht
    darf nicht an einer Organisationsfrage hängen.

    Und eine unbeaufsichtigte Stellenanzeige ist schlechter als keine:
    Bewerbungen liefen an niemanden.
    """

    ACTIVE = "active"
    DORMANT = "dormant"


#: Absichtlich kurz und erweiterbar. Vollständigkeit ist nicht erreichbar; die
#: Liste verhindert die offensichtlichen Fälle, in denen jemand einen
#: Massenanbieter als Unternehmen beansprucht.
PUBLIC_EMAIL_DOMAINS: frozenset[str] = frozenset(
    {
        "aol.com",
        "freenet.de",
        "gmail.com",
        "googlemail.com",
        "gmx.at",
        "gmx.ch",
        "gmx.de",
        "gmx.net",
        "hotmail.com",
        "icloud.com",
        "mail.com",
        "me.com",
        "outlook.com",
        "proton.me",
        "protonmail.com",
        "t-online.de",
        "web.de",
        "yahoo.com",
        "yahoo.de",
        "yandex.com",
        "zoho.com",
    }
)


class PublicEmailDomain(DomainError):
    def __init__(self, domain: str) -> None:
        super().__init__(
            "public_email_domain",
            f"{domain!r} is a public email provider and cannot be claimed as a company",
        )


class DomainAlreadyClaimed(DomainError):
    def __init__(self, domain: str) -> None:
        super().__init__("domain_already_claimed", f"{domain!r} already belongs to a company")


class AccountNotConfirmed(DomainError):
    def __init__(self) -> None:
        super().__init__(
            "account_not_confirmed",
            "Confirm your email address before creating a company",
        )


class InvalidCompanyName(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_company_name", "A company name must not be empty")


@dataclass(frozen=True, slots=True)
class EmailDomain:
    value: str

    def __init__(self, raw: str) -> None:
        object.__setattr__(self, "value", raw.strip().lower())

    @classmethod
    def from_email(cls, email: Email) -> EmailDomain:
        # Email normalisiert bereits auf Kleinschreibung und garantiert genau
        # ein '@' über sein Muster.
        return cls(email.value.split("@", 1)[1])

    def is_public(self) -> bool:
        return self.value in PUBLIC_EMAIL_DOMAINS


@dataclass(frozen=True, slots=True)
class Company:
    id: UUID
    name: str
    domain: EmailDomain

    @classmethod
    def create(cls, *, name: str, domain: EmailDomain) -> Company:
        cleaned = name.strip()
        if not cleaned:
            raise InvalidCompanyName()
        if domain.is_public():
            raise PublicEmailDomain(domain.value)
        return cls(id=uuid4(), name=cleaned, domain=domain)
