"""Identity-domain value objects."""

from __future__ import annotations

import re
from uuid import UUID

from worker_core import DomainError, ValueObject

__all__ = [
    "Email",
    "InvalidEmail",
    "PasswordHash",
    "TenantId",
    "UserId",
]

_EMAIL_RE = re.compile(r"^[^@\s]{1,254}@[^@\s]{1,254}\.[^@\s]{2,254}$")


class InvalidEmail(DomainError):
    def __init__(self, raw: str) -> None:
        super().__init__("invalid_email", f"Not a valid email: {raw!r}")


class Email(ValueObject):
    value: str

    def __init__(self, raw: str) -> None:
        if not isinstance(raw, str) or not _EMAIL_RE.match(raw):
            raise InvalidEmail(raw)
        object.__setattr__(self, "value", raw.lower())

    def __eq__(self, other: object) -> bool:
        return isinstance(other, Email) and self.value == other.value

    def __hash__(self) -> int:
        return hash(self.value)


class PasswordHash(ValueObject):
    value: str

    def __init__(self, value: str) -> None:
        object.__setattr__(self, "value", value)


class UserId(ValueObject):
    value: UUID

    def __init__(self, value: UUID) -> None:
        object.__setattr__(self, "value", value)


_NIL_UUID = UUID("00000000-0000-0000-0000-000000000000")


class TenantId(ValueObject):
    value: UUID

    def __init__(self, value: UUID) -> None:
        if value == _NIL_UUID:
            raise ValueError("TenantId must not be the nil UUID")
        object.__setattr__(self, "value", value)
