"""Versioned boundary DTOs for the Consent-Ledger (ADR-0004 §1).

These are the wire contract between consent-service and its consumers — never a
shared domain model. Consumers pin the V1 suffix; a breaking change becomes V2
next to it rather than an edit here, so an old consumer keeps working.

Deliberately free of domain types: a consumer must not need to import
`consent_service` to talk to it.
"""

from __future__ import annotations

from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "ConsentCheckV1",
    "ConsentGrantV1",
    "ConsentRevokeV1",
    "ConsentStateV1",
]

_CAPABILITY = Field(
    ...,
    min_length=1,
    max_length=255,
    description="Namespaced capability token, e.g. 'profile.visibility:public'",
)


class ConsentGrantV1(BaseModel):
    subject_id: UUID
    capability: str = _CAPABILITY
    # Optional: granting a permission needs no justification.
    reason: str | None = Field(default=None, max_length=500)


class ConsentRevokeV1(BaseModel):
    subject_id: UUID
    capability: str = _CAPABILITY
    # Mandatory: withdrawing a capability must always be explainable.
    reason: str = Field(..., min_length=1, max_length=500)


class ConsentCheckV1(BaseModel):
    subject_id: UUID
    capability: str = _CAPABILITY


class ConsentStateV1(BaseModel):
    """The effective state of one (subject, capability) pair.

    `granted: false` with `reason: "no consent event"` is the answer for a pair
    nobody ever touched — absence is a state, not a 404.
    """

    subject_id: UUID
    capability: str
    granted: bool
    deleted: bool = False
    reason: str | None = None
