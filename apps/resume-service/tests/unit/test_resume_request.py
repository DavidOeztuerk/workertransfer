"""Die Anfrage als Vorgang — was sie zulässt und was nicht."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from resume_service.domain.request import (
    AlreadyAnswered,
    NotTheSubject,
    RequestStatus,
    ResumeRequest,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)
LATER = NOW + timedelta(hours=1)


def a_request() -> ResumeRequest:
    return ResumeRequest.open(subject_id=uuid4(), tenant_id=uuid4(), requested_by=uuid4(), now=NOW)


class TestOpening:
    def test_a_new_request_is_pending(self) -> None:
        assert a_request().status is RequestStatus.PENDING

    def test_it_remembers_who_asked_not_only_which_company(self) -> None:
        # Das Unternehmen ist die Berechtigung, die Person ist die Spur. Ohne
        # `requested_by` steht im Protokoll nur "irgendwer bei Acme".
        req = a_request()
        assert req.requested_by is not None
        assert req.answered_at is None


class TestAnswering:
    def test_the_subject_can_grant(self) -> None:
        req = a_request()

        req.grant(by=req.subject_id, now=LATER)

        assert req.status is RequestStatus.GRANTED
        assert req.answered_at == LATER

    def test_the_subject_can_decline(self) -> None:
        req = a_request()

        req.decline(by=req.subject_id, now=LATER)

        assert req.status is RequestStatus.DECLINED

    @pytest.mark.parametrize("action", ["grant", "decline"])
    def test_nobody_else_may_answer(self, action: str) -> None:
        req = a_request()

        with pytest.raises(NotTheSubject):
            getattr(req, action)(by=uuid4(), now=LATER)

        assert req.status is RequestStatus.PENDING

    def test_an_answered_request_is_not_answered_again(self) -> None:
        # Sonst könnte ein "grant" nach einem "decline" die Ablehnung stillschweigend
        # umdrehen — und der Widerruf gehört in den Ledger, nicht in diesen Vorgang.
        req = a_request()
        req.decline(by=req.subject_id, now=LATER)

        with pytest.raises(AlreadyAnswered):
            req.grant(by=req.subject_id, now=LATER)

    def test_granting_twice_is_also_refused(self) -> None:
        req = a_request()
        req.grant(by=req.subject_id, now=LATER)

        with pytest.raises(AlreadyAnswered):
            req.grant(by=req.subject_id, now=LATER)


class TestWhatItDoesNotDecide:
    def test_the_request_never_claims_access_is_active(self) -> None:
        """`GRANTED` heißt „wurde einmal erteilt", nicht „gilt gerade".

        Ob der Zugriff jetzt besteht, weiß nur der Ledger. Ein Feld hier wäre
        eine zweite Wahrheit — dieselbe Falle wie ein Sichtbarkeits-Flag am
        Profil (ADR-0020 §6). Nach einem Widerruf bleibt die Anfrage `GRANTED`
        und der Lesezugriff läuft trotzdem ins Leere.
        """
        req = a_request()
        req.grant(by=req.subject_id, now=LATER)

        assert not hasattr(req, "is_active")
        assert not hasattr(req, "revoked_at")
