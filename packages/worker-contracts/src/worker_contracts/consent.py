"""Versioned boundary DTOs for the Consent-Ledger (ADR-0004 §1).

These are the wire contract between consent-service and its consumers — never a
shared domain model. Consumers pin the V1 suffix; a breaking change becomes V2
next to it rather than an edit here, so an old consumer keeps working.

Deliberately free of domain types: a consumer must not need to import
`consent_service` to talk to it.
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "ConsentCheckResultV1",
    "ConsentCheckV1",
    "ConsentGrantV1",
    "ConsentGrantedV1",
    "ConsentHistoryEntryV1",
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

    Carries `reason`, so it is only safe to return to the subject itself. The
    write endpoints qualify: they refuse an actor that is not the subject
    (`ConsentSubjectMismatch`). For the cross-subject read use
    `ConsentCheckResultV1`.
    """

    subject_id: UUID
    capability: str
    granted: bool
    deleted: bool = False
    reason: str | None = None


class ConsentCheckResultV1(BaseModel):
    """The answer to "may I?" — deliberately without the reason.

    `/consent/check` is open to any authenticated caller asking about any
    subject, because that is what makes the ledger usable as an enabler by
    consuming services. A withdrawal reason is up to 500 characters of free text
    the subject wrote about themselves, so it must not ride along on a query
    anyone may issue: the caller learns whether a capability is granted, never
    why it was withdrawn.

    Separate model rather than a nulled-out field on `ConsentStateV1`: a field
    that must be blanked at the boundary gets un-blanked by the next refactor.
    """

    subject_id: UUID
    capability: str
    granted: bool
    deleted: bool = False


class ConsentGrantedV1(BaseModel):
    """Eine derzeit wirksame Freigabe.

    Ohne `reason`: widerrufene Fähigkeiten stehen gar nicht in dieser Liste, und
    ein Grund hätte hier nichts zu suchen — er ist Freitext, den ein Mensch über
    sich selbst geschrieben hat.

    `granted_at` ist der Zeitpunkt der WIRKSAMEN Erteilung, nicht der ersten:
    wer widerruft und später erneut erteilt, hat seit dem zweiten Mal erteilt.
    """

    capability: str
    granted_at: datetime


class ConsentHistoryEntryV1(BaseModel):
    """Ein Ereignis aus der eigenen Geschichte.

    MIT `reason` — anders als `ConsentGrantedV1` und `ConsentCheckResultV1`. Der
    Widerrufsgrund ist Freitext, den die Person über sich selbst geschrieben
    hat; ihr gegenüber gibt es keinen Grund, ihn zurückzuhalten. Nach außen
    bleibt er verborgen. Das ist der Unterschied zwischen „gehört ihr" und
    „geht andere an".
    """

    capability: str
    action: Literal["GRANT", "REVOKE", "DELETE"]
    recorded_at: datetime
    reason: str | None = None
