"""Audit-event domain model — PII-free by construction."""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum
from uuid import UUID

from worker_core import DomainError, DomainEvent

__all__ = [
    "AUDIT_METADATA_ALLOWLIST",
    "AuditAction",
    "AuditEvent",
    "AuditMetadataError",
]


class AuditAction(StrEnum):
    REGISTER = "register"
    LOGIN_SUCCESS = "login_success"
    LOGIN_FAILURE = "login_failure"
    # S105 fires on the member NAME containing "TOKEN"; these values are
    # audit action labels, not credentials.
    TOKEN_REFRESH = "token_refresh"  # noqa: S105
    TOKEN_REVOKE = "token_revoke"  # noqa: S105


# Only non-PII technical metadata may be recorded. Passwords, emails,
# consent payloads, and tokens are forbidden by construction.
AUDIT_METADATA_ALLOWLIST: frozenset[str] = frozenset({"reason", "ip", "user_agent"})


class AuditMetadataError(DomainError):
    def __init__(self, key: str) -> None:
        super().__init__(
            "audit_metadata_not_allowlisted",
            f"Metadata key {key!r} is not in the audit PII allowlist",
        )


@dataclass(frozen=True, slots=True)
class AuditEvent(DomainEvent):
    # Required fields are keyword-only so the inherited base defaults
    # (event_id/occurred_at) don't violate the "non-default follows default"
    # init ordering rule — kw-only fields are excluded from positional order.
    actor_id: UUID | None = field(kw_only=True)
    tenant_id: UUID | None = field(kw_only=True)
    action: AuditAction = field(kw_only=True)
    target_id: UUID | None = field(kw_only=True)
    correlation_id: str | None = field(kw_only=True)
    metadata: dict[str, str] = field(kw_only=True)

    def __post_init__(self) -> None:
        for key in self.metadata:
            if key not in AUDIT_METADATA_ALLOWLIST:
                raise AuditMetadataError(key)
