"""Die Regeln des Portfolios — bevor es sie hat."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import UUID, uuid4

import pytest
from portfolio_service.domain.portfolio import (
    MAX_ITEMS,
    InvalidText,
    InvalidUrl,
    InvalidYear,
    Portfolio,
    PortfolioItem,
    TooManyItems,
)

NOW = datetime(2026, 8, 2, tzinfo=UTC)


def item(**overrides: object) -> PortfolioItem:
    values: dict[str, object] = {
        "title": "Ein Werkzeug",
        "summary": "Was es tut.",
        "url": "https://example.org/werkzeug",
        "role": "Entwicklung",
        "year": 2024,
    }
    values.update(overrides)
    return PortfolioItem(**values)  # type: ignore[arg-type]


def subject() -> UUID:
    return uuid4()


class TestItem:
    def test_demands_a_title(self) -> None:
        with pytest.raises(InvalidText):
            item(title="   ")

    def test_trims_before_it_judges(self) -> None:
        assert item(title="  Ein Werkzeug  ").title == "Ein Werkzeug"

    def test_url_is_optional(self) -> None:
        assert item(url=None).url is None
        assert item(url="").url is None

    @pytest.mark.parametrize(
        "hostile",
        [
            "javascript:alert(1)",
            "JavaScript:alert(1)",
            "data:text/html;base64,PHNjcmlwdD4=",
            "file:///etc/passwd",
            "ftp://example.org/x",
        ],
    )
    def test_only_http_and_https_are_allowed(self, hostile: str) -> None:
        # Ein Portfolio-Link wird von fremden Menschen angeklickt. javascript:
        # und data: sind in einem Feld, das später in einem Browser landet, kein
        # exotischer Randfall, sondern der Normalfall eines Angriffs.
        with pytest.raises(InvalidUrl):
            item(url=hostile)

    def test_http_and_https_pass(self) -> None:
        assert item(url="http://example.org").url == "http://example.org"
        assert item(url="https://example.org").url == "https://example.org"

    def test_a_year_may_be_the_next_one(self) -> None:
        # Etwas kann gerade erscheinen.
        assert item(year=NOW.year + 1).year == NOW.year + 1

    def test_a_year_before_1900_is_a_typo(self) -> None:
        with pytest.raises(InvalidYear):
            item(year=1899)

    def test_the_upper_bound_needs_the_clock_and_lives_in_the_aggregate(self) -> None:
        """Ein Eintrag allein weiß nicht, welches Jahr gerade ist.

        Ihm eine Uhr zu geben wäre eine versteckte Abhängigkeit in einem
        Wertobjekt; sich `datetime.now()` zu holen wäre dasselbe, nur
        unsichtbarer. Der Eintrag prüft deshalb, was er wissen kann (nicht vor
        1900), das Aggregat den Rest — und das ist der einzige Weg, auf dem ein
        Eintrag in ein Portfolio gelangt.
        """
        assert item(year=2100).year == 2100

        with pytest.raises(InvalidYear):
            Portfolio.create(subject(), items=[item(year=2100)], now=NOW)

    def test_year_is_optional(self) -> None:
        assert item(year=None).year is None


class TestPortfolio:
    def test_starts_empty_and_that_is_a_state(self) -> None:
        assert Portfolio.create(subject(), items=[], now=NOW).items == ()

    def test_keeps_the_order_it_was_given(self) -> None:
        # Anders als der Lebenslauf hat ein Portfolio keine natürliche Ordnung:
        # "das hier zuerst" ist eine Entscheidung der Person.
        portfolio = Portfolio.create(
            subject(),
            items=[item(title="Zuerst"), item(title="Dann"), item(title="Zuletzt")],
            now=NOW,
        )

        assert [entry.title for entry in portfolio.items] == ["Zuerst", "Dann", "Zuletzt"]

    def test_caps_the_number_of_items(self) -> None:
        with pytest.raises(TooManyItems):
            Portfolio.create(
                subject(), items=[item(title=f"Nr {i}") for i in range(MAX_ITEMS + 1)], now=NOW
            )

    def test_update_validates_everything_before_it_writes_anything(self) -> None:
        portfolio = Portfolio.create(subject(), items=[item(title="Bleibt")], now=NOW)

        with pytest.raises(InvalidUrl):
            portfolio.update(items=[item(title="Gut"), item(url="javascript:alert(1)")], now=NOW)

        assert [entry.title for entry in portfolio.items] == ["Bleibt"]
