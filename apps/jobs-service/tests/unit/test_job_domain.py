"""Die Regeln der Ausschreibung."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from jobs_service.domain.job import (
    EmploymentType,
    InvalidText,
    Job,
    JobStatus,
    RemoteMode,
    TransitionNotAllowed,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)
LATER = NOW + timedelta(days=1)


def job(**overrides: object) -> Job:
    values: dict[str, object] = {
        "tenant_id": uuid4(),
        "title": "Backend-Entwicklerin",
        "description": "Was zu tun ist.",
        "location": "Berlin",
        "remote": RemoteMode.HYBRID,
        "employment": EmploymentType.FULL_TIME,
        "now": NOW,
    }
    values.update(overrides)
    return Job.draft(**values)  # type: ignore[arg-type]


class TestDrafting:
    def test_a_new_job_is_a_draft_and_not_public(self) -> None:
        created = job()

        assert created.status is JobStatus.DRAFT
        assert created.is_public is False
        assert created.published_at is None

    @pytest.mark.parametrize("field", ["title", "description"])
    def test_title_and_description_are_required(self, field: str) -> None:
        with pytest.raises(InvalidText):
            job(**{field: "   "})

    def test_an_empty_location_means_not_stated(self) -> None:
        # Nicht „überall" — das wäre eine Behauptung, die niemand gemacht hat.
        assert job(location="  ").location == ""

    def test_remote_is_a_choice_of_three_not_a_yes_or_no(self) -> None:
        assert {mode.value for mode in RemoteMode} == {"none", "hybrid", "full"}


class TestTransitions:
    def test_publishing_makes_it_public_and_stamps_the_time(self) -> None:
        created = job()

        created.publish(now=LATER)

        assert created.is_public is True
        assert created.published_at == LATER

    def test_publishing_twice_is_refused(self) -> None:
        created = job()
        created.publish(now=LATER)

        with pytest.raises(TransitionNotAllowed):
            created.publish(now=LATER)

    def test_a_closed_job_never_comes_back(self) -> None:
        """Wer erneut sucht, sucht etwas anderes — auch bei gleichem Titel.

        Ein Rückweg würde eine Bewerbungshistorie an eine Stelle hängen, die es
        so nicht mehr gibt.
        """
        created = job()
        created.publish(now=NOW)
        created.close(now=LATER)

        with pytest.raises(TransitionNotAllowed):
            created.publish(now=LATER)
        assert created.is_public is False

    def test_a_draft_can_be_closed_without_being_published_first(self) -> None:
        # Er war nie draußen; ihn nur über den Umweg der Veröffentlichung
        # loszuwerden wäre absurd.
        created = job()

        created.close(now=LATER)

        assert created.status is JobStatus.CLOSED

    def test_closing_twice_is_refused(self) -> None:
        created = job()
        created.close(now=LATER)

        with pytest.raises(TransitionNotAllowed):
            created.close(now=LATER)


class TestEditing:
    def test_a_published_job_may_still_be_corrected(self) -> None:
        """Zurückziehen und neu stellen würde Bewerbungen zerreißen."""
        created = job(title="Backend-Entwicklerin")
        created.publish(now=NOW)

        created.update(
            title="Senior Backend-Entwicklerin",
            description="Was zu tun ist.",
            location="Berlin",
            remote=RemoteMode.FULL,
            employment=EmploymentType.FULL_TIME,
            now=LATER,
        )

        assert created.title == "Senior Backend-Entwicklerin"
        assert created.is_public is True
        assert created.updated_at == LATER
        # Der Zeitpunkt der Veröffentlichung bleibt: die Stelle ist dieselbe.
        assert created.published_at == NOW

    def test_a_rejected_edit_changes_nothing(self) -> None:
        created = job(title="Bleibt")

        with pytest.raises(InvalidText):
            created.update(
                title="",
                description="Neu",
                location="Hamburg",
                remote=RemoteMode.NONE,
                employment=EmploymentType.CONTRACT,
                now=LATER,
            )

        assert created.title == "Bleibt"
        assert created.location == "Berlin"
