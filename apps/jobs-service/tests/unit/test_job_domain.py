"""Die Regeln der Ausschreibung."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from jobs_service.domain.job import (
    MAX_SKILL_LENGTH,
    MAX_SKILLS,
    EmploymentType,
    InvalidText,
    Job,
    JobStatus,
    RemoteMode,
    Skills,
    TooManySkills,
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
        "skills": Skills(["Python"]),
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
            skills=Skills(["Python", "Kubernetes"]),
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
                skills=Skills(["Rust"]),
                now=LATER,
            )

        assert created.title == "Bleibt"
        assert created.location == "Berlin"
        assert created.skills.value == ("Python",)


class TestSkills:
    """Was die Stelle verlangt — die Liste, gegen die im Browser abgeglichen wird.

    Sie steht hier und nicht im Fließtext, weil ein Text nicht abgleichbar ist,
    ohne ihn zu deuten. Eine Liste, die ein Mensch geschrieben hat, ist beides:
    lesbar und vergleichbar — und sie behauptet nichts, was nicht jemand
    hingeschrieben hat.
    """

    def test_entries_are_trimmed_and_blanks_dropped(self) -> None:
        assert Skills(["  Python  ", "   ", "Go"]).value == ("Python", "Go")

    def test_case_is_not_a_second_skill_and_the_first_spelling_wins(self) -> None:
        # Der Abgleich im Browser vergleicht ohne Rücksicht auf Groß- und
        # Kleinschreibung. Zwei Schreibweisen in EINER Liste wären deshalb ein
        # Treffer, der doppelt zählt.
        assert Skills(["Python", "python", "PYTHON"]).value == ("Python",)

    def test_a_skill_may_not_be_longer_than_a_profile_may_hold(self) -> None:
        with pytest.raises(TooManySkills):
            Skills(["x" * (MAX_SKILL_LENGTH + 1)])

    def test_duplicates_are_removed_before_counting(self) -> None:
        # Sonst würde eine Ausschreibung mit 21-mal „Python" abgewiesen,
        # obwohl daraus eine einzige Anforderung wird.
        assert Skills(["Python"] * (MAX_SKILLS + 1)).value == ("Python",)

    def test_too_many_distinct_skills_are_refused(self) -> None:
        with pytest.raises(TooManySkills):
            Skills([f"skill-{index}" for index in range(MAX_SKILLS + 1)])

    def test_a_job_without_skills_is_allowed(self) -> None:
        """Keine Pflicht: eine Stelle darf sagen, dass sie nichts aufzählt.

        Eine erzwungene Liste wäre eine Liste, die jemand ausfüllt, um das
        Formular loszuwerden — und der Abgleich verglich dann gegen Erfundenes.
        """
        assert job(skills=Skills([])).skills.value == ()

    def test_editing_replaces_the_whole_list(self) -> None:
        created = job(skills=Skills(["Python", "Go"]))

        created.update(
            title="Backend-Entwicklerin",
            description="Was zu tun ist.",
            location="Berlin",
            remote=RemoteMode.HYBRID,
            employment=EmploymentType.FULL_TIME,
            skills=Skills(["Python", "Kubernetes"]),
            now=LATER,
        )

        # Kein Zusammenführen: was gestrichen wurde, ist gestrichen.
        assert created.skills.value == ("Python", "Kubernetes")


class TestTheVocabulary:
    """Das Umbenennen (ADR-0023) sitzt IM Wertobjekt, nicht im Router.

    Damit gilt es überall, wo es überhaupt ein `Skills` gibt: beim Speichern,
    beim Lesen aus der Datenbank, in jedem Test. Läge es im Router, käme eine
    alte Zeile aus der Datenbank weiter mit „Postgres" zurück — und der
    Abgleich zeigte der Person eine Lücke, die es nicht gibt.
    """

    def test_a_known_spelling_becomes_the_known_name(self) -> None:
        assert Skills(["postgres", "k8s"]).value == ("PostgreSQL", "Kubernetes")

    def test_renaming_happens_before_deduplication(self) -> None:
        # Andersherum stünde zweimal derselbe Eintrag in der Liste, und die
        # Anzeige zählte eine Anforderung doppelt.
        assert Skills(["Postgres", "PostgreSQL", "psql"]).value == ("PostgreSQL",)

    def test_what_the_vocabulary_does_not_know_stays_as_typed(self) -> None:
        assert Skills(["Hufbeschlag"]).value == ("Hufbeschlag",)
