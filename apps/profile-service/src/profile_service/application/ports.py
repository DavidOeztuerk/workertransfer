"""Ports der Application-Schicht.

Der Consent-Ledger ist ein Port, kein Import: die Handler sollen wissen, DASS
sie fragen müssen, nicht WIE. Der HTTP-Adapter liegt in der Infrastruktur.
"""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from profile_service.domain.profile import Profile

__all__ = ["ConsentGate", "ProfileRepository"]

#: Die eine Capability, die dieses Slice kennt (ADR-0013, Spec §5).
#: Feinere Abstufungen kämen additiv dazu, ohne diese zu brechen.
VISIBILITY_CAPABILITY = "profile.visibility:public"


class ConsentGate(Protocol):
    """Darf der Aufrufer das Profil dieser Person sehen?

    Nimmt das Token des Aufrufers entgegen: der Service fragt in dessen Auftrag,
    nicht mit einem eigenen Konto. Wirft `ConsentUnavailable`, wenn der Ledger
    nicht antwortet — ein `False` wäre dann eine Aussage über die Person, die
    niemand treffen kann.
    """

    async def may_see(self, subject_id: UUID, *, bearer: str) -> bool: ...


class ProfileRepository(Protocol):
    async def get(self, subject_id: UUID) -> Profile | None: ...
    async def save(self, profile: Profile) -> None: ...
    async def page(self, *, limit: int, cursor: str | None) -> tuple[list[Profile], str | None]: ...
