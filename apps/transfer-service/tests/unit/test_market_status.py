"""Die Regeln des Marktstatus."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from transfer_service.domain.market_status import (
    Availability,
    InvalidNote,
    MarketStatus,
    tenant_capability,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)
LATER = NOW + timedelta(days=1)


class TestDefault:
    def test_saying_nothing_means_unavailable(self) -> None:
        """Die Voreinstellung darf nie zugunsten des Marktes ausfallen."""
        status = MarketStatus.default_for(uuid4(), now=NOW)

        assert status.availability is Availability.UNAVAILABLE
        assert status.is_approachable is False
        assert status.employed is False


class TestTransitions:
    @pytest.mark.parametrize("start", list(Availability))
    @pytest.mark.parametrize("target", list(Availability))
    def test_every_transition_is_allowed(self, start: Availability, target: Availability) -> None:
        # Es ist eine Aussage über den eigenen Willen, und der ändert sich ohne
        # Reihenfolge. Verbote wären hier Bevormundung.
        status = MarketStatus.create(uuid4(), availability=start, employed=False, note="", now=NOW)

        status.update(availability=target, employed=False, note="", now=LATER)

        assert status.availability is target

    def test_employed_and_open_at_the_same_time_is_the_normal_case(self) -> None:
        # Als Zustand modelliert wäre genau dieser Fall unmöglich.
        status = MarketStatus.create(
            uuid4(), availability=Availability.OPEN, employed=True, note="", now=NOW
        )

        assert status.employed is True
        assert status.is_approachable is True


class TestApproachability:
    @pytest.mark.parametrize(
        ("availability", "expected"),
        [
            (Availability.OPEN, True),
            (Availability.LISTENING, True),
            (Availability.UNAVAILABLE, False),
        ],
    )
    def test_unavailable_means_do_not_disturb(
        self, availability: Availability, expected: bool
    ) -> None:
        """Die Freigabe erlaubt zu sehen, nicht zu stören."""
        status = MarketStatus.create(
            uuid4(), availability=availability, employed=False, note="", now=NOW
        )

        assert status.is_approachable is expected


class TestNote:
    def test_it_is_trimmed(self) -> None:
        status = MarketStatus.create(
            uuid4(), availability=Availability.OPEN, employed=False, note="  Backend  ", now=NOW
        )

        assert status.note == "Backend"

    def test_an_endless_note_is_refused(self) -> None:
        with pytest.raises(InvalidNote):
            MarketStatus.create(
                uuid4(),
                availability=Availability.OPEN,
                employed=False,
                note="x" * 501,
                now=NOW,
            )

    def test_a_rejected_update_changes_nothing(self) -> None:
        status = MarketStatus.create(
            uuid4(), availability=Availability.OPEN, employed=True, note="Bleibt", now=NOW
        )

        with pytest.raises(InvalidNote):
            status.update(
                availability=Availability.UNAVAILABLE, employed=False, note="x" * 501, now=LATER
            )

        assert status.availability is Availability.OPEN
        assert status.employed is True
        assert status.note == "Bleibt"


class TestCapability:
    def test_it_always_names_a_recipient(self) -> None:
        """Ein `market.visibility:public` gibt es bewusst nicht.

        Beim Profil ist „für alle Unternehmen" eine sinnvolle Wahl; hier wäre
        sie ein Schalter, dessen Folgen niemand überblickt — darunter der eigene
        Arbeitgeber, der auf derselben Plattform ist.
        """
        tenant = uuid4()

        assert tenant_capability(tenant) == f"market.visibility:tenant:{tenant}"
        assert "public" not in tenant_capability(tenant)
