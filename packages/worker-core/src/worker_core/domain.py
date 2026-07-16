"""Small domain primitives with no transport, ORM, or framework dependency."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import UTC, datetime
from uuid import UUID, uuid4


class DomainError(Exception):
    """A business-rule failure that an application layer may translate."""

    def __init__(self, code: str, message: str) -> None:
        self.code = code
        self.message = message
        super().__init__(message)


@dataclass(eq=False, slots=True)
class Entity:
    """Identity-based base class for service-owned entities."""

    id: UUID = field(default_factory=uuid4)

    def __eq__(self, other: object) -> bool:
        return type(self) is type(other) and isinstance(other, Entity) and self.id == other.id

    def __hash__(self) -> int:
        return hash((type(self), self.id))


@dataclass(frozen=True, slots=True)
class ValueObject:
    """Marker base class for immutable, structural domain values."""


@dataclass(frozen=True, slots=True)
class DomainEvent:
    """Base metadata for events raised inside an aggregate."""

    event_id: UUID = field(default_factory=uuid4)
    occurred_at: datetime = field(default_factory=lambda: datetime.now(UTC))


@dataclass(frozen=True, slots=True)
class Result[TValue]:
    """Explicit result type for expected application outcomes."""

    _value: TValue | None = None
    _error: DomainError | None = None

    @classmethod
    def ok(cls, value: TValue) -> Result[TValue]:
        return cls(_value=value)

    @classmethod
    def fail(cls, error: DomainError) -> Result[TValue]:
        return cls(_error=error)

    @property
    def is_success(self) -> bool:
        return self._error is None

    @property
    def value(self) -> TValue:
        if self._error is not None:
            raise self._error
        if self._value is None:
            raise RuntimeError("A successful Result requires a value")
        return self._value

    @property
    def error(self) -> DomainError | None:
        return self._error
