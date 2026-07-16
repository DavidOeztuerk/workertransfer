"""Event definitions for contracts."""

from datetime import UTC, datetime
from typing import Any
from uuid import UUID

from pydantic import BaseModel, ConfigDict
from worker_core.domain import DomainEvent

__all__ = ["DomainEvent", "IntegrationEvent"]


class IntegrationEvent(BaseModel):
    """Base for integration events published to message broker."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    event_id: UUID
    event_type: str
    aggregate_id: UUID
    occurred_at: datetime = datetime.now(UTC)
    payload: dict[str, Any] = {}

    @classmethod
    def create(cls, aggregate_id: UUID, payload: dict[str, Any] | None = None) -> IntegrationEvent:
        return cls(
            event_id=UUID(int=0),
            event_type=cls.__name__,
            aggregate_id=aggregate_id,
            payload=payload or {},
        )
