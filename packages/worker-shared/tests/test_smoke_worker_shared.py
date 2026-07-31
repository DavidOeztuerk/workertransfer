"""worker-shared primitives: pagination, cursors, money, UTC clock."""

from __future__ import annotations

from datetime import UTC

import pytest
from worker_shared import Cursor, InvalidCursor, Money, MoneyCurrencyMismatch, Page, utc_now


def test_utc_now_is_timezone_aware() -> None:
    # A naive datetime compares wrongly against timestamptz values from the DB.
    assert utc_now().tzinfo is UTC


class TestPage:
    def test_defaults_start_at_the_first_page(self) -> None:
        page = Page()
        assert (page.number, page.size, page.offset, page.limit) == (1, 20, 0, 20)

    def test_offset_follows_the_page_number(self) -> None:
        assert Page(number=3, size=25).offset == 50

    def test_clamps_instead_of_rejecting(self) -> None:
        assert Page(number=0).number == 1
        assert Page(number=-5).number == 1
        assert Page(size=0).size == 1
        assert Page(size=10_000).size == 100

    def test_max_size_is_configurable(self) -> None:
        assert Page(size=500, max_size=250).size == 250


class TestCursor:
    def test_roundtrip(self) -> None:
        assert Cursor.decode(Cursor("2026-07-31T10:00:00Z|42").encode()).value == (
            "2026-07-31T10:00:00Z|42"
        )

    def test_encoding_is_url_safe_and_unpadded(self) -> None:
        encoded = Cursor("a" * 10).encode()
        assert "=" not in encoded
        assert "+" not in encoded and "/" not in encoded

    def test_rejects_undecodable_input(self) -> None:
        with pytest.raises(InvalidCursor):
            Cursor.decode("!!!not-base64!!!")


class TestMoney:
    def test_normalises_the_currency_code(self) -> None:
        assert Money(1000, "eur").currency == "EUR"

    @pytest.mark.parametrize("bad", ["EURO", "E", "12", ""])
    def test_rejects_non_iso_currency_codes(self, bad: str) -> None:
        with pytest.raises(ValueError):
            Money(1, bad)

    def test_adds_and_subtracts_within_one_currency(self) -> None:
        assert Money(1000, "EUR") + Money(250, "EUR") == Money(1250, "EUR")
        assert Money(1000, "EUR") - Money(250, "EUR") == Money(750, "EUR")

    def test_refuses_to_mix_currencies(self) -> None:
        # Silently adding EUR to USD is how salary bugs reach production.
        with pytest.raises(MoneyCurrencyMismatch):
            Money(1000, "EUR") + Money(1000, "USD")

    def test_renders_minor_units_as_decimals(self) -> None:
        assert str(Money(123_456, "EUR")) == "1234.56 EUR"
        assert str(Money(5, "EUR")) == "0.05 EUR"
        assert str(Money(-250, "EUR")) == "-2.50 EUR"

    def test_is_orderable(self) -> None:
        assert sorted([Money(300, "EUR"), Money(100, "EUR")])[0] == Money(100, "EUR")
