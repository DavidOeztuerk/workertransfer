"""Message types for contracts."""

from uuid import UUID

from pydantic import BaseModel, ConfigDict


class Message(BaseModel):
    """Base message type."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    message_id: UUID
    message_type: str
    correlation_id: UUID | None = None
    causation_id: UUID | None = None


class Command(Message):
    """Command message - expects a response."""

    pass


class Query(Message):
    """Query message - expects a result."""

    pass


class Event(Message):
    """Event message - fire and forget."""

    pass
