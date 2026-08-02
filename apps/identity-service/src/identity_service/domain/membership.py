"""Company membership — the link between a natural person and a tenant.

A tenant is a company and a natural person has none by default (ADR-0017), so
membership is its own aggregate rather than a column on `User`. That is not
bookkeeping preference: one person may act for several companies (a recruiter
holding multiple mandates, an employee who changes employer without losing their
account), and the set of companies changes over an account's life while the
account itself does not.

Granting a membership is deliberately not exposed over HTTP in this slice —
that belongs to the company-service that does not exist yet. What exists here is
the aggregate, its repository port, and the read the tenant switch needs.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID

from worker_core import DomainError

from identity_service.domain.value_objects import TenantId, UserId

__all__ = [
    "LastAdminMayNotLeave",
    "MembershipRole",
    "MembershipView",
    "NotAMember",
    "OnlyAdminsMayRemove",
    "TenantMembership",
]


class NotAMember(DomainError):
    def __init__(self) -> None:
        # Deliberately does not say whether the tenant exists: a caller must not
        # be able to enumerate companies by probing this endpoint.
        super().__init__("not_a_member", "The user is not a member of this tenant")


class MembershipRole(StrEnum):
    """Durchgesetzt wird der Unterschied erst in Scheibe C (Einladungen); wer
    ein Unternehmen anlegt, ist ab jetzt aber bereits als ADMIN vermerkt."""

    ADMIN = "admin"
    MEMBER = "member"


@dataclass(frozen=True, slots=True)
class TenantMembership:
    user_id: UserId
    tenant_id: TenantId
    granted_at: datetime
    role: MembershipRole


@dataclass(frozen=True, slots=True)
class MembershipView:
    """Read model for the tenant-switch API: plain types, not value objects.

    Deliberately distinct from ``TenantMembership`` — this is what a caller
    needs to render a company picker (name, domain, role), not the aggregate
    the domain reasons about.
    """

    tenant_id: UUID
    name: str
    domain: str
    role: MembershipRole


class OnlyAdminsMayRemove(DomainError):
    def __init__(self) -> None:
        super().__init__("only_admins_may_remove", "Only an administrator may remove members")


class LastAdminMayNotLeave(DomainError):
    """Ein Unternehmen ohne Administrator kann niemanden mehr hereinlassen.

    Es wäre nicht gelöscht, sondern verwaist: die Domain bleibt beansprucht
    (ADR-0019), die Daten bleiben, aber niemand kann mehr einladen oder
    entfernen. Diese Sackgasse entsteht mit einem einzigen Klick und lässt sich
    danach nur noch von Hand in der Datenbank auflösen — deshalb wird sie
    verhindert, nicht später repariert.
    """

    def __init__(self) -> None:
        super().__init__(
            "last_admin_may_not_leave",
            "A company needs at least one administrator; promote someone first",
        )
