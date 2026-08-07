"""Die Marktstatus-Anfrage: ein Vorgang, keine Berechtigung."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from transfer_service.domain.request import (
    AlreadyAnswered,
    MarketRequest,
    NotTheSubject,
    RequestStatus,
)

NOW = datetime(2026, 8, 2, 12, 0, tzinfo=UTC)
LATER = datetime(2026, 8, 2, 13, 0, tzinfo=UTC)


def _open() -> MarketRequest:
    return MarketRequest.open(subject_id=uuid4(), tenant_id=uuid4(), requested_by=uuid4(), now=NOW)


def test_opens_pending_and_unanswered() -> None:
    request = _open()
    assert request.status is RequestStatus.PENDING
    assert request.answered_at is None


def test_the_person_grants() -> None:
    request = _open()
    request.grant(by=request.subject_id, now=LATER)
    assert request.status is RequestStatus.GRANTED
    assert request.answered_at == LATER


def test_the_person_declines() -> None:
    request = _open()
    request.decline(by=request.subject_id, now=LATER)
    assert request.status is RequestStatus.DECLINED


@pytest.mark.parametrize("action", ["grant", "decline"])
def test_nobody_else_may_answer(action: str) -> None:
    """Auch nicht das fragende Unternehmen — sonst erteilte es sich selbst."""
    request = _open()
    with pytest.raises(NotTheSubject):
        getattr(request, action)(by=request.tenant_id, now=LATER)


def test_a_declined_request_cannot_be_granted_later() -> None:
    """Sonst drehte ein zweiter Klick die Ablehnung stillschweigend um."""
    request = _open()
    request.decline(by=request.subject_id, now=LATER)
    with pytest.raises(AlreadyAnswered):
        request.grant(by=request.subject_id, now=LATER)


def test_a_granted_request_stays_granted_after_a_withdrawal() -> None:
    """`GRANTED` heißt „wurde einmal erteilt", nicht „gilt gerade".

    Der Widerruf lebt im Ledger. Ihn hier zu spiegeln hieße, zwei Wahrheiten
    über dieselbe Frage zu führen — und die zweite wäre die veraltete.
    """
    request = _open()
    request.grant(by=request.subject_id, now=LATER)
    assert not hasattr(request, "revoked_at")
    assert request.status is RequestStatus.GRANTED
