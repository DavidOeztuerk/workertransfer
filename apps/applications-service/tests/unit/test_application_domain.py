"""Die Regeln der Bewerbung."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from applications_service.domain.application import (
    Application,
    ApplicationStatus,
    InvalidMessage,
    NotYours,
    SharedArtifacts,
    TransitionNotAllowed,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)
LATER = NOW + timedelta(days=1)


def application(**overrides: object) -> Application:
    values: dict[str, object] = {
        "job_id": uuid4(),
        "tenant_id": uuid4(),
        "subject_id": uuid4(),
        "message": "Ich passe dazu.",
        "shared": SharedArtifacts(resume=True),
        "now": NOW,
    }
    values.update(overrides)
    return Application.submit(**values)  # type: ignore[arg-type]


class TestSubmitting:
    def test_a_new_application_is_live(self) -> None:
        created = application()

        assert created.status is ApplicationStatus.SUBMITTED
        assert created.is_live is True

    def test_the_profile_is_always_shared(self) -> None:
        # „Ich bewerbe mich, aber ihr dürft nichts von mir sehen" ist keine
        # Wahl, die jemand ernsthaft trifft.
        assert SharedArtifacts().profile is True
        assert SharedArtifacts(resume=True, portfolio=True).profile is True

    def test_an_endless_message_is_refused(self) -> None:
        with pytest.raises(InvalidMessage):
            application(message="x" * 4001)


class TestWithdrawing:
    def test_the_applicant_may_withdraw_while_it_runs(self) -> None:
        created = application()

        created.withdraw(by=created.subject_id, now=LATER)

        assert created.status is ApplicationStatus.WITHDRAWN
        assert created.is_live is False

    def test_withdrawing_works_even_while_it_is_being_reviewed(self) -> None:
        # Wer nicht mehr will, muss nicht warten, bis jemand anderes fertig ist.
        created = application()
        created.advance(to=ApplicationStatus.REVIEWING, now=NOW)

        created.withdraw(by=created.subject_id, now=LATER)

        assert created.status is ApplicationStatus.WITHDRAWN

    def test_nobody_else_may_withdraw(self) -> None:
        created = application()

        with pytest.raises(NotYours):
            created.withdraw(by=uuid4(), now=LATER)

    def test_a_decided_application_cannot_be_withdrawn(self) -> None:
        created = application()
        created.advance(to=ApplicationStatus.REJECTED, now=NOW)

        with pytest.raises(TransitionNotAllowed):
            created.withdraw(by=created.subject_id, now=LATER)


class TestResubmitting:
    def test_after_a_withdrawal_one_may_apply_again(self) -> None:
        """Das ist eine neue Entscheidung, kein Nachfassen."""
        created = application()
        created.withdraw(by=created.subject_id, now=NOW)

        created.resubmit(message="Doch.", shared=SharedArtifacts(portfolio=True), now=LATER)

        assert created.status is ApplicationStatus.SUBMITTED
        assert created.shared.portfolio is True
        assert created.is_live is True

    def test_after_a_rejection_one_may_not(self) -> None:
        # Nachfassen gegen ein „nein", das schon gefallen ist — dieselbe Regel
        # wie beim Lebenslauf.
        created = application()
        created.advance(to=ApplicationStatus.REJECTED, now=NOW)

        with pytest.raises(TransitionNotAllowed):
            created.resubmit(message="Bitte doch", shared=SharedArtifacts(), now=LATER)

    def test_a_running_application_is_not_resubmitted(self) -> None:
        created = application()

        with pytest.raises(TransitionNotAllowed):
            created.resubmit(message="Nochmal", shared=SharedArtifacts(), now=LATER)


class TestTheCompanySide:
    def test_it_moves_through_the_process(self) -> None:
        created = application()

        created.advance(to=ApplicationStatus.REVIEWING, now=LATER)
        assert created.is_live is True

        created.advance(to=ApplicationStatus.HIRED, now=LATER)
        assert created.is_live is False
        assert created.answered_at == LATER

    def test_a_withdrawn_application_is_no_longer_theirs_to_decide(self) -> None:
        created = application()
        created.withdraw(by=created.subject_id, now=NOW)

        with pytest.raises(TransitionNotAllowed):
            created.advance(to=ApplicationStatus.REVIEWING, now=LATER)

    def test_a_decision_is_final(self) -> None:
        created = application()
        created.advance(to=ApplicationStatus.HIRED, now=NOW)

        with pytest.raises(TransitionNotAllowed):
            created.advance(to=ApplicationStatus.REJECTED, now=LATER)

    @pytest.mark.parametrize(
        "forbidden", [ApplicationStatus.SUBMITTED, ApplicationStatus.WITHDRAWN]
    )
    def test_the_company_cannot_submit_or_withdraw_for_someone(
        self, forbidden: ApplicationStatus
    ) -> None:
        created = application()

        with pytest.raises(TransitionNotAllowed):
            created.advance(to=forbidden, now=LATER)
