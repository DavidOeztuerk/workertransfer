"""Session view and value objects for the session lifecycle.

Kept in the domain layer so the application layer ports
(SessionRepository.get_by_jti) can return a domain type instead
of leaking an ORM model across the Clean-Architecture boundary.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from uuid import UUID

__all__ = ["SessionView"]


@dataclass(frozen=True, slots=True)
class SessionView:
    user_id: UUID
    tenant_id: UUID
    refresh_jti: str
    expires_at: datetime
    revoked_at: datetime | None
