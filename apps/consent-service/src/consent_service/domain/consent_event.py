"""ConsentEvent — the append-only fact recorded in the ledger.

There is no mutable consent aggregate. GRANT, REVOKE and DELETE are *new facts*,
never edits to an existing row. That makes audit correctness structural rather
than a convention someone has to remember: there is no code path that can rewrite
history because none is offered.

The metadata allowlist mirrors `identity_service.domain.audit` deliberately by
*copying* rather than importing it — audit payloads are service-owned (ADR-0012),
and a shared consent/audit model would be exactly the cross-service domain
coupling ADR-0004 forbids.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from uuid import UUID, uuid4

from worker_core import DomainError

from consent_service.domain.value_objects import (
    Capability,
    ConsentAction,
    ConsentEventId,
    Reason,
    SubjectId,
)

__all__ = [
    "CONSENT_METADATA_ALLOWLIST",
    "ConsentEvent",
    "ConsentMetadataError",
    "ReasonRequired",
]

# Only non-PII technical metadata may be recorded. A consent event describes
# *that* permission changed, never the personal data the permission is about.
CONSENT_METADATA_ALLOWLIST: frozenset[str] = frozenset({"reason", "ip", "user_agent", "actor_id"})


class ConsentMetadataError(DomainError):
    def __init__(self, key: str) -> None:
        super().__init__(
            "consent_metadata_not_allowlisted",
            f"Metadata key {key!r} is not in the consent PII allowlist",
        )


class ReasonRequired(DomainError):
    def __init__(self, action: ConsentAction) -> None:
        super().__init__(
            "consent_reason_required",
            f"A reason is mandatory for {action.value}",
        )


@dataclass(frozen=True, slots=True)
class ConsentEvent:
    """One immutable fact about one (subject, capability) pair."""

    event_id: ConsentEventId
    subject_id: SubjectId
    capability: Capability
    action: ConsentAction
    recorded_at: datetime
    actor_id: UUID | None = None
    reason: Reason | None = None
    metadata: dict[str, str] = field(default_factory=dict)

    def __post_init__(self) -> None:
        for key in self.metadata:
            if key not in CONSENT_METADATA_ALLOWLIST:
                raise ConsentMetadataError(key)
        # Withdrawing a capability must always be explainable; granting one does
        # not need a justification.
        #
        # DELETE ausdrücklich nicht mehr (ADR-0027 §1): seit die Kontolöschung
        # sein einziger Erzeuger ist, hieße „Grund verpflichtend", von einem
        # Menschen, der sein Konto löschen will, eine Begründung zu verlangen —
        # ein Hebel gegen ihn. Und der Freitext wäre ausgerechnet das Einzige,
        # das §5 danach wieder entfernen müsste.
        if self.action is ConsentAction.REVOKE and self.reason is None:
            raise ReasonRequired(self.action)

    @classmethod
    def grant(
        cls,
        *,
        subject_id: SubjectId,
        capability: Capability,
        recorded_at: datetime,
        actor_id: UUID | None = None,
        reason: Reason | None = None,
        metadata: dict[str, str] | None = None,
        event_id: ConsentEventId | None = None,
    ) -> ConsentEvent:
        return cls(
            event_id=event_id or ConsentEventId(uuid4()),
            subject_id=subject_id,
            capability=capability,
            action=ConsentAction.GRANT,
            recorded_at=recorded_at,
            actor_id=actor_id,
            reason=reason,
            metadata=dict(metadata or {}),
        )

    @classmethod
    def revoke(
        cls,
        *,
        subject_id: SubjectId,
        capability: Capability,
        recorded_at: datetime,
        reason: Reason,
        actor_id: UUID | None = None,
        metadata: dict[str, str] | None = None,
        event_id: ConsentEventId | None = None,
    ) -> ConsentEvent:
        return cls(
            event_id=event_id or ConsentEventId(uuid4()),
            subject_id=subject_id,
            capability=capability,
            action=ConsentAction.REVOKE,
            recorded_at=recorded_at,
            actor_id=actor_id,
            reason=reason,
            metadata=dict(metadata or {}),
        )

    @classmethod
    def delete(
        cls,
        *,
        subject_id: SubjectId,
        capability: Capability,
        recorded_at: datetime,
        reason: Reason | None = None,
        actor_id: UUID | None = None,
        metadata: dict[str, str] | None = None,
        event_id: ConsentEventId | None = None,
    ) -> ConsentEvent:
        return cls(
            event_id=event_id or ConsentEventId(uuid4()),
            subject_id=subject_id,
            capability=capability,
            action=ConsentAction.DELETE,
            recorded_at=recorded_at,
            actor_id=actor_id,
            reason=reason,
            metadata=dict(metadata or {}),
        )
