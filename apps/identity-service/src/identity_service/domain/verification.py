"""Einmal-Token für die E-Mail-Bestätigung.

Nur der Hash wird gespeichert. Eine geleakte Datenbankzeile darf keine
Kontoübernahme sein — mit dem Hash allein lässt sich kein Link bauen.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "TokenExpired",
    "TokenInvalid",
    "TokenPurpose",
    "VerificationToken",
]


class TokenPurpose(StrEnum):
    EMAIL_VERIFY = "email_verify"


class TokenInvalid(DomainError):
    def __init__(self) -> None:
        # Bewusst ohne Detail: unbekannt und bereits verbraucht sind von außen
        # nicht unterscheidbar, sonst wird der Endpunkt ein Orakel.
        super().__init__("token_invalid", "This confirmation link is not valid")


class TokenExpired(DomainError):
    def __init__(self) -> None:
        super().__init__("token_expired", "This confirmation link has expired")


@dataclass(frozen=True, slots=True)
class VerificationToken:
    token_id: UUID
    user_id: UUID
    token_hash: str
    purpose: TokenPurpose
    expires_at: datetime
    consumed_at: datetime | None

    def is_expired(self, now: datetime) -> bool:
        return self.expires_at <= now

    def is_consumed(self) -> bool:
        return self.consumed_at is not None
