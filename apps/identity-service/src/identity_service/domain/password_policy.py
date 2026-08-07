"""Mini password policy (Phase 2). The readable-policy floor lives in the domain
until a shared policy module exists (worker-security was deleted in ADR-0014: it
only duplicated the platform's security-headers middleware)."""

from __future__ import annotations

from worker_core import DomainError

__all__ = ["PasswordPolicy", "WeakPassword"]

_MAX_BYTES = 72
_MIN_CHARS = 12


class WeakPassword(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__("weak_password", f"Password rejected: {reason}")


class PasswordPolicy:
    def validate(self, plain: str) -> None:
        if not plain:
            raise WeakPassword("must not be empty")
        if len(plain) < _MIN_CHARS:
            raise WeakPassword(f"must be at least {_MIN_CHARS} characters")
        if len(plain.encode("utf-8")) > _MAX_BYTES:
            raise WeakPassword("exceeds 72 bytes")
