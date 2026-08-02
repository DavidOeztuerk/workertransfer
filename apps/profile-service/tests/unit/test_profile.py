"""Das Profil-Aggregat: Grenzen, Normalisierung, Änderungen."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from profile_service.domain.profile import (
    InvalidBio,
    InvalidHeadline,
    InvalidLocation,
    Profile,
    Skills,
    TooManySkills,
)

NOW = datetime(2026, 8, 2, 12, 0, tzinfo=UTC)


def _profile(**overrides: object) -> Profile:
    defaults: dict[str, object] = {
        "subject_id": uuid4(),
        "headline": "Senior Python Backend",
        "bio": "",
        "location": "",
        "remote_ok": False,
        "skills": Skills([]),
        "now": NOW,
    }
    defaults.update(overrides)
    return Profile.create(**defaults)  # type: ignore[arg-type]


class TestSkills:
    def test_empty_entries_are_dropped(self) -> None:
        assert Skills(["Python", "", "   ", "Rust"]).value == ("Python", "Rust")

    def test_entries_are_trimmed(self) -> None:
        assert Skills(["  Python  "]).value == ("Python",)

    def test_duplicates_are_removed_case_insensitively(self) -> None:
        # "Python" und "python" sind dieselbe Fähigkeit; die erste Schreibweise
        # gewinnt, weil die Person sie so eingetragen hat.
        assert Skills(["Python", "python", "PYTHON"]).value == ("Python",)

    def test_order_is_preserved(self) -> None:
        assert Skills(["Rust", "Python", "Go"]).value == ("Rust", "Python", "Go")

    def test_more_than_thirty_is_refused(self) -> None:
        with pytest.raises(TooManySkills):
            Skills([f"skill-{i}" for i in range(31)])

    def test_exactly_thirty_is_allowed(self) -> None:
        assert len(Skills([f"skill-{i}" for i in range(30)]).value) == 30

    def test_an_overlong_entry_is_refused(self) -> None:
        with pytest.raises(TooManySkills):
            Skills(["x" * 51])

    def test_deduplication_happens_before_the_limit(self) -> None:
        # Sonst würde jemand mit 31-mal "Python" abgewiesen, obwohl daraus eine
        # einzige Fähigkeit wird.
        assert Skills(["Python"] * 31).value == ("Python",)


class TestProfile:
    def test_headline_must_not_be_blank(self) -> None:
        with pytest.raises(InvalidHeadline):
            _profile(headline="   ")

    def test_headline_is_trimmed(self) -> None:
        assert _profile(headline="  Senior Dev  ").headline == "Senior Dev"

    def test_an_overlong_headline_is_refused(self) -> None:
        with pytest.raises(InvalidHeadline):
            _profile(headline="x" * 121)

    def test_bio_may_be_empty_but_not_endless(self) -> None:
        assert _profile(bio="").bio == ""
        with pytest.raises(InvalidBio):
            _profile(bio="x" * 4001)

    def test_an_overlong_location_is_refused(self) -> None:
        with pytest.raises(InvalidLocation):
            _profile(location="x" * 121)

    def test_creating_stamps_both_times(self) -> None:
        profile = _profile()

        assert profile.created_at == NOW
        assert profile.updated_at == NOW

    def test_updating_moves_only_updated_at(self) -> None:
        profile = _profile()
        later = datetime(2026, 8, 3, 9, 0, tzinfo=UTC)

        profile.update(
            headline="Staff Engineer",
            bio="Neu",
            location="Berlin",
            remote_ok=True,
            skills=Skills(["Python"]),
            now=later,
        )

        assert profile.headline == "Staff Engineer"
        assert profile.remote_ok is True
        assert profile.skills.value == ("Python",)
        assert profile.created_at == NOW, "created_at darf sich nie ändern"
        assert profile.updated_at == later

    def test_updating_validates_just_like_creating(self) -> None:
        profile = _profile()

        with pytest.raises(InvalidHeadline):
            profile.update(
                headline="",
                bio="",
                location="",
                remote_ok=False,
                skills=Skills([]),
                now=NOW,
            )

    def test_a_failed_update_leaves_the_aggregate_untouched(self) -> None:
        # Sonst stünde nach einem abgelehnten Formular ein halb geänderter
        # Zustand in der Sitzung.
        profile = _profile(headline="Original")

        with pytest.raises(InvalidHeadline):
            profile.update(
                headline="",
                bio="geändert",
                location="geändert",
                remote_ok=True,
                skills=Skills(["Python"]),
                now=NOW,
            )

        assert profile.headline == "Original"
        assert profile.bio == ""
        assert profile.remote_ok is False
