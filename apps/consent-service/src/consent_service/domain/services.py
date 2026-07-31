"""Consent-state projection — a pure function over the event log.

The ledger keeps no status table. Current state is *computed* from the facts, so
there is exactly one place consent can be true and no second store to fall out of
sync. `SqlAlchemyConsentEventRepository.latest_effective` does the same reduction
in SQL for the hot read path; this function is the canonical definition of the
rules and is what the unit tests pin.

Never performs I/O.
"""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass

from consent_service.domain.consent_event import ConsentEvent
from consent_service.domain.value_objects import ConsentAction

__all__ = ["ConsentState", "project_state"]


@dataclass(frozen=True, slots=True)
class ConsentState:
    """The effective state of one (subject, capability) pair."""

    granted: bool
    deleted: bool = False
    reason: str | None = None

    @classmethod
    def absent(cls) -> ConsentState:
        # No event is a state, not an error: a capability nobody ever granted is
        # simply not granted. Consumers must be able to ask about anything.
        return cls(granted=False, reason="no consent event")


def project_state(events: Sequence[ConsentEvent]) -> ConsentState:
    """Reduce a stream of facts to the currently effective state.

    Ordering is `(recorded_at, event_id)`: two events written in the same clock
    tick still resolve deterministically instead of depending on insertion order.
    """
    if not events:
        return ConsentState.absent()

    latest = max(events, key=lambda event: (event.recorded_at, event.event_id.value))

    if latest.action is ConsentAction.GRANT:
        # A GRANT after a REVOKE is a valid re-consent — withdrawing permission
        # must never be a one-way door for the subject.
        return ConsentState(granted=True)
    if latest.action is ConsentAction.REVOKE:
        return ConsentState(
            granted=False,
            reason=latest.reason.value if latest.reason is not None else None,
        )
    return ConsentState(
        granted=False,
        deleted=True,
        reason=latest.reason.value if latest.reason is not None else None,
    )
