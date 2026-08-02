"""ConsentEvent construction and the PII allowlist."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from consent_service.domain.consent_event import (
    ConsentEvent,
    ConsentMetadataError,
    ReasonRequired,
)
from consent_service.domain.value_objects import (
    Capability,
    ConsentAction,
    ConsentEventId,
    Reason,
    SubjectId,
)

NOW = datetime(2026, 7, 31, 12, 0, tzinfo=UTC)


def _subject() -> SubjectId:
    return SubjectId(uuid4())


def _capability() -> Capability:
    return Capability("profile.visibility:public")


class TestConstruction:
    def test_grant_needs_no_reason(self) -> None:
        event = ConsentEvent.grant(subject_id=_subject(), capability=_capability(), recorded_at=NOW)
        assert event.action is ConsentAction.GRANT
        assert event.reason is None

    def test_revoke_requires_a_reason(self) -> None:
        # Withdrawing a capability must always be explainable.
        with pytest.raises(ReasonRequired):
            ConsentEvent(
                event_id=ConsentEventId(uuid4()),
                subject_id=_subject(),
                capability=_capability(),
                action=ConsentAction.REVOKE,
                recorded_at=NOW,
            )

    def test_delete_requires_a_reason(self) -> None:
        with pytest.raises(ReasonRequired):
            ConsentEvent(
                event_id=ConsentEventId(uuid4()),
                subject_id=_subject(),
                capability=_capability(),
                action=ConsentAction.DELETE,
                recorded_at=NOW,
            )

    def test_revoke_and_delete_carry_their_reason(self) -> None:
        reason = Reason("subject withdrew consent")
        for factory in (ConsentEvent.revoke, ConsentEvent.delete):
            event = factory(
                subject_id=_subject(),
                capability=_capability(),
                recorded_at=NOW,
                reason=reason,
            )
            assert event.reason == reason

    def test_actor_id_is_optional(self) -> None:
        assert (
            ConsentEvent.grant(
                subject_id=_subject(), capability=_capability(), recorded_at=NOW
            ).actor_id
            is None
        )

    def test_event_id_is_generated_when_absent(self) -> None:
        a = ConsentEvent.grant(subject_id=_subject(), capability=_capability(), recorded_at=NOW)
        b = ConsentEvent.grant(subject_id=_subject(), capability=_capability(), recorded_at=NOW)
        assert a.event_id != b.event_id

    def test_events_are_immutable(self) -> None:
        # Append-only is structural: there is no way to edit a recorded fact.
        event = ConsentEvent.grant(subject_id=_subject(), capability=_capability(), recorded_at=NOW)
        with pytest.raises(AttributeError):
            event.action = ConsentAction.REVOKE  # type: ignore[misc]


class TestMetadataAllowlist:
    def test_allowlisted_keys_pass(self) -> None:
        event = ConsentEvent.grant(
            subject_id=_subject(),
            capability=_capability(),
            recorded_at=NOW,
            metadata={"ip": "203.0.113.7", "user_agent": "curl/8", "reason": "opt-in"},
        )
        assert event.metadata["ip"] == "203.0.113.7"

    @pytest.mark.parametrize(
        "key",
        ["email", "password", "token", "access_token", "cv", "full_name", "anything_else"],
    )
    def test_pii_and_unknown_keys_are_rejected(self, key: str) -> None:
        # The ledger records *that* permission changed, never the personal data
        # the permission is about.
        with pytest.raises(ConsentMetadataError):
            ConsentEvent.grant(
                subject_id=_subject(),
                capability=_capability(),
                recorded_at=NOW,
                metadata={key: "value"},
            )

    def test_metadata_is_copied_not_aliased(self) -> None:
        source = {"ip": "203.0.113.7"}
        event = ConsentEvent.grant(
            subject_id=_subject(),
            capability=_capability(),
            recorded_at=NOW,
            metadata=source,
        )
        source["ip"] = "mutated"
        assert event.metadata["ip"] == "203.0.113.7"
