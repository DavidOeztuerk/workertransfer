"""Consent domain value objects — SubjectId, Capability, ConsentEventId, Reason."""

from __future__ import annotations

import re
from dataclasses import dataclass
from enum import StrEnum
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "Capability",
    "ConsentAction",
    "ConsentEventId",
    "InvalidCapability",
    "InvalidReason",
    "Reason",
    "SubjectId",
]


class ConsentAction(StrEnum):
    GRANT = "GRANT"
    REVOKE = "REVOKE"
    DELETE = "DELETE"


class InvalidCapability(DomainError):
    def __init__(self, value: str) -> None:
        super().__init__(
            "invalid_capability",
            f"Capability must match namespace pattern, got {value!r}",
        )


class InvalidReason(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("invalid_reason", detail)


@dataclass(frozen=True, slots=True)
class SubjectId:
    value: UUID


@dataclass(frozen=True, slots=True)
class ConsentEventId:
    value: UUID


_CAPABILITY_RE = re.compile(r"^[a-z][a-z_.]+(:\w+)?(:[{]?[\w-]+[}]?)?$")


@dataclass(frozen=True, slots=True)
class Capability:
    """Namespaced capability token, e.g. 'profile.visibility:public'."""

    value: str

    def __post_init__(self) -> None:
        if not self.value or not _CAPABILITY_RE.match(self.value):
            raise InvalidCapability(self.value)


@dataclass(frozen=True, slots=True)
class Reason:
    value: str

    MAX_LENGTH = 500

    def __post_init__(self) -> None:
        if not self.value or not self.value.strip():
            raise InvalidReason("reason must not be empty")
        if len(self.value) > self.MAX_LENGTH:
            raise InvalidReason(f"reason exceeds {self.MAX_LENGTH} characters")
