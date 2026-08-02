"""Ports der Application-Schicht.

Der Consent-Ledger ist ein Port, kein Import: die Handler sollen wissen, DASS
sie fragen müssen, nicht WIE. Der HTTP-Adapter liegt in der Infrastruktur.
"""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from profile_service.domain.profile import Profile

__all__ = ["ConsentGate", "ProfileRepository", "tenant_capability"]

#: „Für alle Unternehmen" — der Schalter auf /profile.
VISIBILITY_CAPABILITY = "profile.visibility:public"


def tenant_capability(tenant_id: UUID) -> str:
    """„Für dieses eine Unternehmen" — entsteht mit einer Bewerbung (4.2).

    Die additive Verfeinerung, die ADR-0020 vorgesehen hat. `:public` bleibt,
    was es war; hier kommt eine zweite Möglichkeit dazu, nicht eine Lockerung
    der ersten.
    """
    return f"profile.visibility:tenant:{tenant_id}"


class ConsentGate(Protocol):
    """Darf der Aufrufer das Profil dieser Person sehen?

    Nimmt das Token des Aufrufers entgegen: der Service fragt in dessen Auftrag,
    nicht mit einem eigenen Konto. Wirft `ConsentUnavailable`, wenn der Ledger
    nicht antwortet — ein `False` wäre dann eine Aussage über die Person, die
    niemand treffen kann.
    """

    async def may_see(self, subject_id: UUID, *, tenant_id: UUID, bearer: str) -> bool: ...


class ProfileRepository(Protocol):
    async def get(self, subject_id: UUID) -> Profile | None: ...
    async def save(self, profile: Profile) -> None: ...
    async def page(self, *, limit: int, cursor: str | None) -> tuple[list[Profile], str | None]: ...
