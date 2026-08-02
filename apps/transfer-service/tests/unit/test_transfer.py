"""Die Regeln des Transfer-Vorgangs."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from transfer_service.domain.transfer import (
    InvalidOffer,
    NotYours,
    Transfer,
    TransferStatus,
    TransitionNotAllowed,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)
LATER = NOW + timedelta(days=1)


def transfer(*, requires_release: bool = False) -> Transfer:
    return Transfer.express_interest(
        subject_id=uuid4(),
        tenant_id=uuid4(),
        requires_release=requires_release,
        message="Wir hätten Interesse.",
        now=NOW,
    )


def _through_to_accepted(t: Transfer) -> None:
    t.accept_talk(by=t.subject_id, now=LATER)
    t.make_offer(note="Ein Angebot.", start_on="2026-11", fee_cents=None, now=LATER)
    t.accept_offer(by=t.subject_id, now=LATER)


class TestTheHappyPathWithoutARelease:
    def test_the_company_closes_it(self) -> None:
        """Der Abschluss ist die Aussage „wir stellen ein" — die trifft der
        Arbeitgeber. Die Person hat mit accept_offer bereits ja gesagt."""
        t = transfer()

        _through_to_accepted(t)
        t.complete(now=LATER)

        assert t.status is TransferStatus.COMPLETED
        assert t.is_running is False


class TestTheHappyPathWithARelease:
    def test_the_person_confirms_and_that_closes_it(self) -> None:
        t = transfer(requires_release=True)
        _through_to_accepted(t)

        t.confirm_release(by=t.subject_id, now=LATER)

        assert t.release_confirmed is True
        assert t.status is TransferStatus.COMPLETED

    def test_the_company_cannot_close_it_before_the_release(self) -> None:
        # Nur die Person weiß, ob ihr Arbeitgeber sie gehen lässt.
        t = transfer(requires_release=True)
        _through_to_accepted(t)

        with pytest.raises(TransitionNotAllowed):
            t.complete(now=LATER)

    def test_confirming_a_release_nobody_needs_is_refused(self) -> None:
        t = transfer(requires_release=False)
        _through_to_accepted(t)

        with pytest.raises(TransitionNotAllowed):
            t.confirm_release(by=t.subject_id, now=LATER)


class TestSayingNo:
    @pytest.mark.parametrize("upto", ["interested", "talking", "offered", "accepted"])
    def test_the_person_may_decline_from_any_running_state(self, upto: str) -> None:
        """Ein Verfahren, aus dem man nicht aussteigen kann, ist kein
        Verfahren, sondern eine Falle."""
        t = transfer()
        if upto in {"talking", "offered", "accepted"}:
            t.accept_talk(by=t.subject_id, now=LATER)
        if upto in {"offered", "accepted"}:
            t.make_offer(note="", start_on=None, fee_cents=None, now=LATER)
        if upto == "accepted":
            t.accept_offer(by=t.subject_id, now=LATER)

        t.decline(by=t.subject_id, now=LATER)

        assert t.status is TransferStatus.DECLINED

    def test_the_company_may_withdraw_from_any_running_state(self) -> None:
        t = transfer()
        t.accept_talk(by=t.subject_id, now=LATER)

        t.withdraw(now=LATER)

        assert t.status is TransferStatus.WITHDRAWN

    def test_a_finished_transfer_stays_finished(self) -> None:
        t = transfer()
        t.decline(by=t.subject_id, now=LATER)

        with pytest.raises(TransitionNotAllowed):
            t.accept_talk(by=t.subject_id, now=LATER)
        with pytest.raises(TransitionNotAllowed):
            t.withdraw(now=LATER)


class TestWhoMayDoWhat:
    @pytest.mark.parametrize("action", ["accept_talk", "decline"])
    def test_only_the_person_answers_for_the_person(self, action: str) -> None:
        t = transfer()

        with pytest.raises(NotYours):
            getattr(t, action)(by=uuid4(), now=LATER)

        assert t.status is TransferStatus.INTERESTED

    def test_an_offer_needs_a_conversation_first(self) -> None:
        # Ein Angebot an jemanden, der noch nicht zugestimmt hat zu reden, wäre
        # genau die Belästigung, gegen die der ganze Fluss gebaut ist.
        t = transfer()

        with pytest.raises(TransitionNotAllowed):
            t.make_offer(note="", start_on=None, fee_cents=None, now=LATER)


class TestTheOffer:
    def test_it_records_the_terms(self) -> None:
        t = transfer()
        t.accept_talk(by=t.subject_id, now=LATER)

        t.make_offer(note="Guter Vertrag.", start_on="2026-11", fee_cents=250000, now=LATER)

        assert t.offer_start_on == "2026-11"
        # Festgehalten, nicht bewegt: die Plattform führt kein Geld.
        assert t.offer_fee_cents == 250000

    def test_a_negative_fee_is_refused(self) -> None:
        t = transfer()
        t.accept_talk(by=t.subject_id, now=LATER)

        with pytest.raises(InvalidOffer):
            t.make_offer(note="", start_on=None, fee_cents=-1, now=LATER)

    def test_a_fee_is_optional(self) -> None:
        # Nicht jeder Wechsel hat eine Ablöse.
        t = transfer()
        t.accept_talk(by=t.subject_id, now=LATER)

        t.make_offer(note="", start_on=None, fee_cents=None, now=LATER)

        assert t.offer_fee_cents is None
