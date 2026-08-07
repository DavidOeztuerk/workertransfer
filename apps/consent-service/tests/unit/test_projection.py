"""project_state — the rules that decide whether a capability is granted."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import UUID, uuid4

from consent_service.domain.consent_event import ConsentEvent
from consent_service.domain.services import ConsentState, project_state
from consent_service.domain.value_objects import Capability, ConsentEventId, Reason, SubjectId

BASE = datetime(2026, 7, 31, 12, 0, tzinfo=UTC)
SUBJECT = SubjectId(uuid4())
CAPABILITY = Capability("profile.visibility:public")
WITHDRAWN = Reason("subject withdrew consent")


def _grant(offset: int = 0, event_id: UUID | None = None) -> ConsentEvent:
    return ConsentEvent.grant(
        subject_id=SUBJECT,
        capability=CAPABILITY,
        recorded_at=BASE + timedelta(seconds=offset),
        event_id=ConsentEventId(event_id) if event_id else None,
    )


def _revoke(offset: int = 0, event_id: UUID | None = None) -> ConsentEvent:
    return ConsentEvent.revoke(
        subject_id=SUBJECT,
        capability=CAPABILITY,
        recorded_at=BASE + timedelta(seconds=offset),
        reason=WITHDRAWN,
        event_id=ConsentEventId(event_id) if event_id else None,
    )


def _delete(offset: int = 0) -> ConsentEvent:
    return ConsentEvent.delete(
        subject_id=SUBJECT,
        capability=CAPABILITY,
        recorded_at=BASE + timedelta(seconds=offset),
        reason=Reason("erasure requested"),
    )


def test_no_events_is_not_granted() -> None:
    # Absence is a state, not an error: consumers must be able to ask about any
    # capability, including one nobody ever touched.
    state = project_state([])
    assert state == ConsentState(granted=False, reason="no consent event")


def test_grant_alone_is_granted() -> None:
    assert project_state([_grant()]).granted is True


def test_revoke_after_grant_withdraws_with_its_reason() -> None:
    state = project_state([_grant(0), _revoke(1)])
    assert state.granted is False
    assert state.reason == WITHDRAWN.value
    assert state.deleted is False


def test_re_consent_after_revoke_is_granted_again() -> None:
    # Withdrawing must never be a one-way door for the subject.
    assert project_state([_grant(0), _revoke(1), _grant(2)]).granted is True


def test_delete_after_grant_is_logically_deleted() -> None:
    state = project_state([_grant(0), _delete(1)])
    assert state.granted is False
    assert state.deleted is True


def test_input_order_does_not_matter() -> None:
    # The projection sorts by recorded_at; it must not depend on how the
    # repository happened to return the rows.
    events = [_revoke(1), _grant(0)]
    assert project_state(events).granted is False
    assert project_state(list(reversed(events))).granted is False


def test_same_timestamp_is_broken_by_event_id() -> None:
    # Two facts in the same clock tick must still resolve deterministically.
    low = UUID("00000000-0000-0000-0000-000000000001")
    high = UUID("ffffffff-ffff-ffff-ffff-ffffffffffff")
    assert project_state([_grant(0, low), _revoke(0, high)]).granted is False
    assert project_state([_revoke(0, low), _grant(0, high)]).granted is True


def test_duplicate_grants_stay_granted() -> None:
    assert project_state([_grant(0), _grant(1), _grant(2)]).granted is True


def test_recorded_at_dominates_over_list_position() -> None:
    # A late-arriving old fact must not override a newer one.
    assert project_state([_revoke(5), _grant(1)]).granted is False
