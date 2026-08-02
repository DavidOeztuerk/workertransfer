"""Domain-neutral primitives shared by every service.

Scope rule (docs/architecture.md): only technical, transport-independent,
non-business types live here. Pagination, cursors and money are shapes every
service repeats; profiles, jobs, transfers and consent are not — those stay in
the service that owns them.

Deliberately small. `kon.txt` lists a much longer wish list (constants, enums,
address, phone, email); those arrive when a service actually needs them, not
before. Until then this module stays stdlib-only.
"""

from __future__ import annotations

import base64
import binascii
from dataclasses import dataclass
from datetime import UTC, datetime

__all__ = [
    "Cursor",
    "InvalidCursor",
    "Money",
    "MoneyCurrencyMismatch",
    "Page",
    "utc_now",
]


def utc_now() -> datetime:
    """Timezone-aware UTC now.

    `datetime.utcnow()` returns a *naive* datetime, which compares and
    serialises wrongly against the timezone-aware values SQLAlchemy hands back
    for `timestamptz` columns. Services should route every "now" through here.
    """
    return datetime.now(UTC)


@dataclass(frozen=True, slots=True)
class Page:
    """Offset pagination request.

    Clamped rather than validated-and-rejected: a caller asking for page 0 or
    10_000 items means the nearest legal value, and a 422 on a query parameter
    is rarely what an API consumer wants.
    """

    number: int = 1
    size: int = 20
    max_size: int = 100

    def __post_init__(self) -> None:
        object.__setattr__(self, "number", max(1, self.number))
        object.__setattr__(self, "size", min(max(1, self.size), self.max_size))

    @property
    def offset(self) -> int:
        return (self.number - 1) * self.size

    @property
    def limit(self) -> int:
        return self.size


class InvalidCursor(ValueError):
    """Raised when a cursor value is not decodable."""


@dataclass(frozen=True, slots=True)
class Cursor:
    """Opaque keyset-pagination token.

    Base64 is encoding, not protection: the value is transparent to anyone who
    looks. Never put anything in a cursor that the caller may not see — the
    point is only to stop clients from constructing or arithmetically walking
    positions, so the server stays free to change the keyset later.
    """

    value: str

    def encode(self) -> str:
        return base64.urlsafe_b64encode(self.value.encode()).decode().rstrip("=")

    @classmethod
    def decode(cls, encoded: str) -> Cursor:
        padded = encoded + "=" * (-len(encoded) % 4)
        try:
            return cls(base64.urlsafe_b64decode(padded.encode()).decode())
        except (binascii.Error, UnicodeDecodeError, ValueError) as exc:
            raise InvalidCursor(f"cursor is not decodable: {encoded!r}") from exc


class MoneyCurrencyMismatch(ValueError):
    """Raised when two Money values of different currencies are combined."""


@dataclass(frozen=True, slots=True, order=True)
class Money:
    """An amount in minor units (cents) plus an ISO-4217 currency code.

    Minor units are integers on purpose: float arithmetic silently loses cents,
    which is unacceptable for salaries, transfer fees and contract values.
    """

    amount_minor: int
    currency: str

    def __post_init__(self) -> None:
        code = self.currency.upper()
        if len(code) != 3 or not code.isalpha():
            raise ValueError(f"currency must be a 3-letter ISO-4217 code, got {self.currency!r}")
        object.__setattr__(self, "currency", code)

    def _same_currency(self, other: Money) -> None:
        if self.currency != other.currency:
            raise MoneyCurrencyMismatch(
                f"cannot combine {self.currency} and {other.currency}; convert explicitly"
            )

    def __add__(self, other: Money) -> Money:
        self._same_currency(other)
        return Money(self.amount_minor + other.amount_minor, self.currency)

    def __sub__(self, other: Money) -> Money:
        self._same_currency(other)
        return Money(self.amount_minor - other.amount_minor, self.currency)

    def __str__(self) -> str:
        sign = "-" if self.amount_minor < 0 else ""
        units, minor = divmod(abs(self.amount_minor), 100)
        return f"{sign}{units}.{minor:02d} {self.currency}"
