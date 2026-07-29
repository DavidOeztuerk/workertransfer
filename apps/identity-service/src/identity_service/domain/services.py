"""Domain service ports (interfaces) — no transport/ORM/JWT imports."""

from __future__ import annotations

from datetime import datetime
from typing import Protocol

from identity_service.domain.value_objects import PasswordHash

__all__ = ["Clock", "PasswordHashing", "TokenService"]


class PasswordHashing(Protocol):
    def hash(self, plain: str) -> PasswordHash: ...
    def verify(self, plain: str, hashed: PasswordHash) -> bool: ...


class Clock(Protocol):
    def now(self) -> datetime: ...


class TokenService(Protocol):
    # Minimal surface the application layer needs from a token service.
    # The infrastructure adapter (JwTokenService, Sub-step 2.4) implements this.
    def issue_access_token(
        self,
        user_id: object,
        tenant_id: object,
        roles: list[str],
        permissions: list[str],
    ) -> str: ...

    def issue_refresh_token(
        self, user_id: object, tenant_id: object, *, session_jti: str
    ) -> str: ...
