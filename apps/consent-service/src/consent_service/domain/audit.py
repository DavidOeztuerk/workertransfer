"""Audit-event domain model — service-owned and PII-free by construction.

Structurally the same shape as `identity_service.domain.audit`, and deliberately
*not* imported from it: audit payloads are service-specific (ADR-0012) and there
is no shared domain model across services (ADR-0004 §1). The actions differ —
this service records consent changes, not logins.
"""

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
    CONSENT_GRANT = "consent_grant"
    CONSENT_REVOKE = "consent_revoke"
    CONSENT_DELETE = "consent_delete"


# The capability name and a reason are permissible; the personal data the consent
# governs is not. An audit row must never become a second copy of the payload.
AUDIT_METADATA_ALLOWLIST: frozenset[str] = frozenset({"reason", "ip", "user_agent", "capability"})


class AuditMetadataError(DomainError):
    def __init__(self, key: str) -> None:
        super().__init__(
            "audit_metadata_not_allowlisted",
            f"Metadata key {key!r} is not in the audit PII allowlist",
        )


@dataclass(frozen=True, slots=True)
class AuditEvent(DomainEvent):
    # Keyword-only so the inherited base defaults (event_id/occurred_at) do not
    # violate the "non-default follows default" init ordering rule.
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
