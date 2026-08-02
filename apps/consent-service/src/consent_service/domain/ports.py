"""Domain ports — the interfaces infrastructure must satisfy.

`ConsentEventRepository` deliberately offers no `update` or `delete`. Append-only
is enforced by the shape of the port, not by a rule in a document: there is no
method to call that would rewrite a fact.
"""

from __future__ import annotations

from collections.abc import Sequence
from datetime import datetime
from typing import Protocol

from consent_service.domain.audit import AuditEvent
from consent_service.domain.consent_event import ConsentEvent
from consent_service.domain.value_objects import Capability, SubjectId

__all__ = ["AuditRepository", "Clock", "ConsentEventRepository"]


class ConsentEventRepository(Protocol):
    async def append(self, event: ConsentEvent) -> None:
        """Insert a new fact. Never updates an existing row."""
        ...

    async def stream(self, subject_id: SubjectId) -> Sequence[ConsentEvent]:
        """Every fact recorded for a subject, oldest first (audit/export)."""
        ...

    async def latest_effective(
        self, subject_id: SubjectId, capability: Capability
    ) -> ConsentEvent | None:
        """The single most recent fact for one (subject, capability) pair."""
        ...

    async def latest_per_capability(self, subject_id: SubjectId) -> Sequence[ConsentEvent]:
        """The newest fact for EVERY capability of one subject.

        Dieselbe Reduktion wie `latest_effective`, nur über alle Fähigkeiten
        statt über eine. Bewusst dieselbe Ordnung — zwei Wege an dieselbe
        Auskunft, die sich uneinig werden können, sind schlimmer als kein
        zweiter Weg.
        """
        ...


class AuditRepository(Protocol):
    async def append(self, event: AuditEvent) -> None: ...


class Clock(Protocol):
    def now(self) -> datetime:
        """Timezone-aware UTC. Injected so tests can pin ordering exactly."""
        ...
