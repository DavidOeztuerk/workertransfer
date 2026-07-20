"""Event system: Domain/Integration/Application events, Event store, Outbox/Inbox pattern."""

from __future__ import annotations

from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any
from uuid import UUID, uuid4


@dataclass(frozen=True, slots=True)
class DomainEvent:
    """Base metadata for events raised inside an aggregate."""

    event_id: UUID = field(default_factory=uuid4)
    occurred_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    aggregate_id: UUID | None = None
    aggregate_type: str = ""
    version: int = 1

    def to_dict(self) -> dict[str, Any]:
        return {
            "event_id": str(self.event_id),
            "event_type": self.__class__.__name__,
            "occurred_at": self.occurred_at.isoformat(),
            "aggregate_id": str(self.aggregate_id) if self.aggregate_id else None,
            "aggregate_type": self.aggregate_type,
            "version": self.version,
        }


@dataclass(frozen=True, slots=True)
class IntegrationEvent(DomainEvent):
    """Event published to other services via message broker."""

    correlation_id: UUID = field(default_factory=uuid4)
    causation_id: UUID | None = None


class EventBus:
    """In-process event bus for domain events.

    The bus keys handlers by ``event_type.__name__`` and dispatches by
    ``event.__class__.__name__`` — it never reads event attributes beyond the
    class name. The ``event_type``/``event`` parameters are therefore typed
    ``type``/object rather than ``type[DomainEvent]``/``DomainEvent`` so a
    domain's own event hierarchy (e.g. ``worker_core.DomainEvent`` subclasses
    used by identity-service) can subscribe and publish without a nominal
    import dependency on ``worker_events.DomainEvent`` (ADR: platform bus is
    structurally name-keyed, not nominally event-typed).
    """

    def __init__(self) -> None:
        self._handlers: dict[str, list[Callable[..., Awaitable[None]]]] = {}

    def subscribe(self, event_type: type, handler: Callable[..., Awaitable[None]]) -> None:
        name = event_type.__name__
        if name not in self._handlers:
            self._handlers[name] = []
        self._handlers[name].append(handler)

    async def publish(self, event: object) -> None:
        handlers = self._handlers.get(event.__class__.__name__, [])
        for handler in handlers:
            await handler(event)

    async def publish_all(self, events: list[object]) -> None:
        for event in events:
            await self.publish(event)
