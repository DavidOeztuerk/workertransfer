"""Consent DTOs are a versioned wire contract (ADR-0004 §1).

Consumers pin V1. Renaming or removing a field here silently breaks every
consumer at runtime, so the field sets are asserted explicitly: a breaking change
must be a V2 next to this, not an edit to it.
"""

from __future__ import annotations

from uuid import uuid4

import pytest
from pydantic import ValidationError
from worker_contracts import (
    ConsentCheckResultV1,
    ConsentCheckV1,
    ConsentGrantV1,
    ConsentRevokeV1,
    ConsentStateV1,
)

SUBJECT = uuid4()
CAPABILITY = "profile.visibility:public"


def test_v1_field_sets_are_pinned() -> None:
    assert set(ConsentGrantV1.model_fields) == {"subject_id", "capability", "reason"}
    assert set(ConsentRevokeV1.model_fields) == {"subject_id", "capability", "reason"}
    assert set(ConsentCheckV1.model_fields) == {"subject_id", "capability"}
    assert set(ConsentStateV1.model_fields) == {
        "subject_id",
        "capability",
        "granted",
        "deleted",
        "reason",
    }


def test_grant_reason_is_optional() -> None:
    # Granting a permission needs no justification.
    assert ConsentGrantV1(subject_id=SUBJECT, capability=CAPABILITY).reason is None


def test_revoke_reason_is_mandatory() -> None:
    # Withdrawing a capability must always be explainable.
    with pytest.raises(ValidationError):
        ConsentRevokeV1(subject_id=SUBJECT, capability=CAPABILITY)  # type: ignore[call-arg]


@pytest.mark.parametrize("bad", ["", " " * 0])
def test_capability_must_not_be_empty(bad: str) -> None:
    with pytest.raises(ValidationError):
        ConsentCheckV1(subject_id=SUBJECT, capability=bad)


def test_state_defaults_to_not_deleted() -> None:
    state = ConsentStateV1(subject_id=SUBJECT, capability=CAPABILITY, granted=True)
    assert state.deleted is False
    assert state.reason is None


def test_absent_consent_is_a_state_not_an_error() -> None:
    state = ConsentStateV1(
        subject_id=SUBJECT, capability=CAPABILITY, granted=False, reason="no consent event"
    )
    assert state.granted is False


def test_dtos_carry_no_domain_types() -> None:
    """A consumer must never need to import consent_service to talk to it."""
    for model in (
        ConsentGrantV1,
        ConsentRevokeV1,
        ConsentCheckV1,
        ConsentStateV1,
        ConsentCheckResultV1,
    ):
        for field in model.model_fields.values():
            annotation = str(field.annotation)
            assert "consent_service" not in annotation, annotation


def test_check_result_has_no_reason_field() -> None:
    """The cross-subject read answers "may I?", never "why not".

    A withdrawal reason is up to 500 characters the subject wrote about itself.
    `/consent/check` is open to every authenticated caller for every subject, so
    the field must be absent from the model rather than blanked at the boundary —
    a blanked field gets un-blanked by the next refactor.
    """
    assert "reason" not in ConsentCheckResultV1.model_fields
    assert "reason" in ConsentStateV1.model_fields


def test_check_result_still_answers_the_question() -> None:
    result = ConsentCheckResultV1(subject_id=SUBJECT, capability=CAPABILITY, granted=False)

    assert result.granted is False
    assert result.deleted is False
