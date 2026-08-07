"""Die Regeln des Lebenslaufs — bevor er sie hat."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import UUID, uuid4

import pytest
from resume_service.domain.resume import (
    Education,
    InvalidMonth,
    InvalidText,
    MonthDate,
    Position,
    Resume,
    TooManyEntries,
    TwoOpenPositions,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)


def month(value: str) -> MonthDate:
    return MonthDate.parse(value)


def position(
    employer: str = "Acme GmbH",
    title: str = "Backend-Entwicklerin",
    started: str = "2020-01",
    ended: str | None = "2023-06",
    description: str = "",
) -> Position:
    return Position(
        employer=employer,
        title=title,
        started_on=month(started),
        ended_on=None if ended is None else month(ended),
        description=description,
    )


class TestMonthDate:
    def test_parses_a_month(self) -> None:
        assert month("2020-01").year == 2020
        assert month("2020-01").month == 1

    @pytest.mark.parametrize("bad", ["2020", "2020-13", "2020-00", "20-01", "", "2020-1"])
    def test_rejects_what_is_not_a_month(self, bad: str) -> None:
        with pytest.raises(InvalidMonth):
            month(bad)

    def test_rejects_a_day(self) -> None:
        # Monatsgenau ist Absicht: kein Lebenslauf nennt den 14. März, und eine
        # Tagesangabe macht aus einer Lücke von drei Wochen einen Rechtfertigungsdruck.
        with pytest.raises(InvalidMonth):
            month("2020-01-14")

    def test_orders_chronologically(self) -> None:
        assert month("2019-12") < month("2020-01")
        assert month("2020-01") < month("2020-02")

    def test_round_trips_through_its_string_form(self) -> None:
        assert str(month("2020-07")) == "2020-07"


class TestPosition:
    def test_an_open_position_means_still_there(self) -> None:
        assert position(ended=None).is_current is True

    def test_rejects_an_end_before_the_start(self) -> None:
        with pytest.raises(InvalidMonth):
            position(started="2023-01", ended="2020-01")

    def test_accepts_start_and_end_in_the_same_month(self) -> None:
        # Ein Monat Probezeit ist kurz, aber keine Falscheingabe.
        assert position(started="2023-01", ended="2023-01").ended_on is not None

    @pytest.mark.parametrize("field", ["employer", "title"])
    def test_demands_employer_and_title(self, field: str) -> None:
        with pytest.raises(InvalidText):
            position(**{field: "   "})

    def test_trims_before_it_judges(self) -> None:
        assert position(employer="  Acme GmbH  ").employer == "Acme GmbH"


class TestResume:
    def test_starts_empty_and_that_is_a_state(self) -> None:
        resume = Resume.create(subject_id_stub(), positions=[], education=[], now=NOW)

        assert resume.positions == ()
        assert resume.education == ()

    def test_orders_positions_newest_first_regardless_of_input(self) -> None:
        # Reihenfolge kommt aus den Daten, nicht aus einer sort_order-Spalte, die
        # mit jeder Bearbeitung falsch werden kann.
        resume = Resume.create(
            subject_id_stub(),
            positions=[
                position(employer="Alt", started="2015-01", ended="2018-01"),
                position(employer="Neu", started="2020-01", ended="2023-01"),
            ],
            education=[],
            now=NOW,
        )

        assert [p.employer for p in resume.positions] == ["Neu", "Alt"]

    def test_puts_the_current_position_first(self) -> None:
        resume = Resume.create(
            subject_id_stub(),
            positions=[
                position(employer="Früher", started="2020-01", ended="2023-01"),
                position(employer="Jetzt", started="2023-02", ended=None),
            ],
            education=[],
            now=NOW,
        )

        assert resume.positions[0].employer == "Jetzt"

    def test_refuses_two_positions_that_never_ended(self) -> None:
        # Zwei gleichzeitig laufende Anstellungen sind selten; "ich habe das Ende
        # vergessen" ist häufig. Der Fehler wird abgefangen, nicht das Leben.
        with pytest.raises(TwoOpenPositions):
            Resume.create(
                subject_id_stub(),
                positions=[position(ended=None), position(employer="Zweite", ended=None)],
                education=[],
                now=NOW,
            )

    def test_caps_the_number_of_entries(self) -> None:
        with pytest.raises(TooManyEntries):
            Resume.create(
                subject_id_stub(),
                positions=[
                    position(started=f"20{i:02d}-01", ended=f"20{i:02d}-06") for i in range(41)
                ],
                education=[],
                now=NOW,
            )

    def test_update_validates_everything_before_it_writes_anything(self) -> None:
        resume = Resume.create(
            subject_id_stub(), positions=[position(employer="Bleibt")], education=[], now=NOW
        )

        with pytest.raises(TwoOpenPositions):
            resume.update(
                positions=[position(ended=None), position(employer="Zweite", ended=None)],
                education=[],
                now=NOW,
            )

        # Ein abgelehntes Formular darf kein halb geändertes Aggregat hinterlassen.
        assert [p.employer for p in resume.positions] == ["Bleibt"]

    def test_education_is_ordered_and_capped_the_same_way(self) -> None:
        resume = Resume.create(
            subject_id_stub(),
            positions=[],
            education=[
                Education(
                    institution="Schule",
                    qualification="Abitur",
                    started_on=month("2008-08"),
                    ended_on=month("2011-06"),
                ),
                Education(
                    institution="Uni",
                    qualification="B.Sc.",
                    started_on=month("2011-10"),
                    ended_on=month("2015-03"),
                ),
            ],
            now=NOW,
        )

        assert [e.institution for e in resume.education] == ["Uni", "Schule"]


def subject_id_stub() -> UUID:
    return uuid4()
