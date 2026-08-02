"""Die Regeln des Arbeitgeberprofils."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from companies_service.domain.company_profile import (
    MAX_ENTRIES,
    CompanyProfile,
    InvalidText,
    InvalidUrl,
    TooManyEntries,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)


def profile(**overrides: object) -> CompanyProfile:
    values: dict[str, object] = {
        "display_name": "Muster",
        "about": "Wer wir sind.",
        "website": "https://muster.example",
        "locations": ["Berlin", "Hamburg"],
        "benefits": ["Homeoffice"],
        "now": NOW,
    }
    values.update(overrides)
    return CompanyProfile.create(uuid4(), **values)  # type: ignore[arg-type]


class TestName:
    def test_a_display_name_is_required(self) -> None:
        with pytest.raises(InvalidText):
            profile(display_name="   ")

    def test_it_is_not_the_account_name(self) -> None:
        """Kontoname und Marke dürfen auseinandergehen — sie beschreiben
        verschiedene Dinge, und keiner wird aus dem anderen abgeleitet."""
        assert profile(display_name="Muster").display_name == "Muster"


class TestWebsite:
    def test_it_is_optional(self) -> None:
        assert profile(website=None).website is None
        assert profile(website="  ").website is None

    @pytest.mark.parametrize(
        "hostile", ["javascript:alert(1)", "data:text/html,x", "ftp://x.example", "keinhost"]
    )
    def test_only_http_and_https(self, hostile: str) -> None:
        # Ein Link wird von fremden Menschen angeklickt.
        with pytest.raises(InvalidUrl):
            profile(website=hostile)


class TestEntries:
    def test_duplicates_collapse_case_insensitively(self) -> None:
        assert profile(benefits=["Homeoffice", "homeoffice", "HOMEOFFICE"]).benefits == (
            "Homeoffice",
        )

    def test_blanks_fall_away(self) -> None:
        assert profile(locations=["Berlin", "  ", ""]).locations == ("Berlin",)

    def test_the_order_is_kept(self) -> None:
        assert profile(locations=["Hamburg", "Berlin"]).locations == ("Hamburg", "Berlin")

    def test_deduplication_happens_before_counting(self) -> None:
        # Sonst würde jemand mit einundzwanzigmal „Homeoffice" abgewiesen,
        # obwohl daraus ein Eintrag wird.
        assert profile(benefits=["Homeoffice"] * (MAX_ENTRIES + 1)).benefits == ("Homeoffice",)

    def test_too_many_distinct_entries_are_refused(self) -> None:
        with pytest.raises(TooManyEntries):
            profile(benefits=[f"Nr {i}" for i in range(MAX_ENTRIES + 1)])


class TestUpdating:
    def test_a_rejected_change_leaves_everything_alone(self) -> None:
        existing = profile(display_name="Bleibt", locations=["Berlin"])

        with pytest.raises(InvalidUrl):
            existing.update(
                display_name="Neu",
                about="Neu",
                website="javascript:alert(1)",
                locations=["Hamburg"],
                benefits=[],
                now=NOW,
            )

        assert existing.display_name == "Bleibt"
        assert existing.locations == ("Berlin",)
