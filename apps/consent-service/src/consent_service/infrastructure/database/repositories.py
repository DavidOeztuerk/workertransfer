"""SQLAlchemy implementations of the consent domain ports.

Note what is absent: neither repository offers `update` or `delete`. Append-only
is a property of the available API, not a rule someone has to remember.
"""

from __future__ import annotations

from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from consent_service.domain.audit import AuditEvent
from consent_service.domain.consent_event import ConsentEvent
from consent_service.domain.value_objects import (
    Capability,
    ConsentAction,
    ConsentEventId,
    Reason,
    SubjectId,
)
from consent_service.infrastructure.database.models import AuditEventModel, ConsentEventModel

__all__ = ["SqlAlchemyAuditRepository", "SqlAlchemyConsentEventRepository"]


def _to_domain(row: ConsentEventModel) -> ConsentEvent:
    return ConsentEvent(
        event_id=ConsentEventId(row.event_id),
        subject_id=SubjectId(row.subject_id),
        capability=Capability(row.capability),
        action=ConsentAction(row.action),
        recorded_at=row.recorded_at,
        actor_id=row.actor_id,
        reason=Reason(row.reason) if row.reason is not None else None,
        metadata=dict(row.meta),
    )


class SqlAlchemyConsentEventRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def append(self, event: ConsentEvent) -> None:
        self._session.add(
            ConsentEventModel(
                event_id=event.event_id.value,
                subject_id=event.subject_id.value,
                capability=event.capability.value,
                action=event.action.value,
                actor_id=event.actor_id,
                reason=event.reason.value if event.reason is not None else None,
                meta=dict(event.metadata),
                recorded_at=event.recorded_at,
            )
        )
        # Flush inside the UoW so a duplicate event_id raises here — inside the
        # transaction the caller controls — rather than at commit time.
        await self._session.flush()

    async def stream(self, subject_id: SubjectId) -> Sequence[ConsentEvent]:
        stmt = (
            select(ConsentEventModel)
            .where(ConsentEventModel.subject_id == subject_id.value)
            .order_by(ConsentEventModel.recorded_at, ConsentEventModel.event_id)
        )
        rows = (await self._session.execute(stmt)).scalars().all()
        return [_to_domain(row) for row in rows]

    async def latest_effective(
        self, subject_id: SubjectId, capability: Capability
    ) -> ConsentEvent | None:
        """The newest fact for one (subject, capability) pair.

        DISTINCT ON is the Postgres-native way to take one row per group without
        a window function or a self-join, and it matches ix_consent_events_lookup
        exactly. The ORDER BY must lead with the DISTINCT ON columns.
        """
        stmt = (
            select(ConsentEventModel)
            .where(
                ConsentEventModel.subject_id == subject_id.value,
                ConsentEventModel.capability == capability.value,
            )
            .distinct(ConsentEventModel.subject_id, ConsentEventModel.capability)
            .order_by(
                ConsentEventModel.subject_id,
                ConsentEventModel.capability,
                ConsentEventModel.recorded_at.desc(),
                ConsentEventModel.event_id.desc(),
            )
        )
        row = (await self._session.execute(stmt)).scalars().first()
        return _to_domain(row) if row is not None else None

    async def latest_per_capability(self, subject_id: SubjectId) -> Sequence[ConsentEvent]:
        """Wie `latest_effective`, nur ohne Einschränkung auf eine Fähigkeit.

        Die ORDER BY muss mit den DISTINCT-ON-Spalten beginnen und danach
        derselben Ordnung folgen wie `project_state` — `(recorded_at,
        event_id)`, absteigend. Weicht sie ab, sagt diese Liste bei zwei
        Ereignissen im selben Zeittakt etwas anderes als `/check`.
        """
        stmt = (
            select(ConsentEventModel)
            .where(ConsentEventModel.subject_id == subject_id.value)
            .distinct(ConsentEventModel.subject_id, ConsentEventModel.capability)
            .order_by(
                ConsentEventModel.subject_id,
                ConsentEventModel.capability,
                ConsentEventModel.recorded_at.desc(),
                ConsentEventModel.event_id.desc(),
            )
        )
        rows = (await self._session.execute(stmt)).scalars().all()
        return [_to_domain(row) for row in rows]


class SqlAlchemyAuditRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def append(self, event: AuditEvent) -> None:
        self._session.add(
            AuditEventModel(
                actor_id=event.actor_id,
                tenant_id=event.tenant_id,
                action=event.action,
                target_id=event.target_id,
                correlation_id=event.correlation_id,
                occurred_at=event.occurred_at,
                meta=dict(event.metadata),
            )
        )
        await self._session.flush()
