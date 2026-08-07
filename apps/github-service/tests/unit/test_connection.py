"""Die Regeln der GitHub-Verbindung — und vor allem, was sie NICHT tut."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from github_service.domain.connection import (
    AlreadyVerified,
    GitHubConnection,
    InvalidLogin,
    NotVerified,
    Repository,
    challenge_description,
)

NOW = datetime(2026, 8, 3, 12, 0, tzinfo=UTC)


def _open(login: str = "anna") -> GitHubConnection:
    return GitHubConnection.open(subject_id=uuid4(), login=login)


def _repo(name: str, pushed: datetime | None, stars: int = 0) -> Repository:
    return Repository(
        name=name,
        description="",
        language="Python",
        stars=stars,
        url=f"https://github.com/anna/{name}",
        pushed_at=pushed,
    )


def test_it_starts_unverified_with_a_challenge() -> None:
    connection = _open()
    assert not connection.is_verified
    assert connection.challenge != ""


def test_two_connections_get_different_challenges() -> None:
    assert _open().challenge != _open().challenge


def test_the_description_is_what_has_to_stand_in_the_gist() -> None:
    assert challenge_description("abc") == "workertransfer-verify-abc"


@pytest.mark.parametrize(
    "login", ["", "   ", "a" * 40, "-anna", "anna-", "an--na", "an na", "an/na"]
)
def test_a_login_that_github_could_never_have_is_refused(login: str) -> None:
    # Streng zu prüfen erspart einen Abruf, der ohnehin nichts fände — und
    # verhindert, dass ein Pfadfragment in eine URL wandert.
    with pytest.raises(InvalidLogin):
        _open(login)


def test_an_at_sign_is_stripped_because_people_type_it() -> None:
    assert _open("@anna").login == "anna"


def test_nothing_is_stored_before_the_proof() -> None:
    """Sonst könnte jemand einen fremden Namen eintragen und dessen Arbeit zeigen."""
    connection = _open()
    with pytest.raises(NotVerified):
        connection.store([_repo("x", NOW)], now=NOW)


def test_a_verified_connection_stores_the_snapshot() -> None:
    connection = _open()
    connection.verify(now=NOW)
    connection.store([_repo("x", NOW)], now=NOW)

    assert [r.name for r in connection.repositories] == ["x"]
    assert connection.fetched_at == NOW


def test_verifying_twice_is_refused() -> None:
    connection = _open()
    connection.verify(now=NOW)
    with pytest.raises(AlreadyVerified):
        connection.verify(now=NOW)


def test_the_snapshot_is_sorted_by_last_change_not_by_stars() -> None:
    """Sterne messen Sichtbarkeit, nicht Arbeit — und eine Sortierung ist
    bereits eine Wertung."""
    connection = _open()
    connection.verify(now=NOW)
    old = datetime(2020, 1, 1, tzinfo=UTC)
    connection.store([_repo("beruehmt", old, stars=9000), _repo("frisch", NOW, stars=0)], now=NOW)

    assert [r.name for r in connection.repositories] == ["frisch", "beruehmt"]


def test_repositories_without_a_date_end_up_last_instead_of_crashing() -> None:
    connection = _open()
    connection.verify(now=NOW)
    connection.store([_repo("ohne", None), _repo("mit", NOW)], now=NOW)

    assert [r.name for r in connection.repositories] == ["mit", "ohne"]


def test_pointing_at_another_account_drops_the_proof() -> None:
    """Sonst wäre der Nachweis für ein anderes Konto erbracht worden.

    Der Weg wäre offen: eigenes Konto nachweisen, danach den Namen auf ein
    fremdes ändern — und dessen Arbeit steht unter dem eigenen Profil.
    """
    connection = _open("anna")
    connection.verify(now=NOW)
    connection.store([_repo("x", NOW)], now=NOW)

    connection.relink("jemand-anders")

    assert connection.login == "jemand-anders"
    assert not connection.is_verified
    assert connection.repositories == []
    assert connection.fetched_at is None


def test_the_same_account_in_different_case_keeps_the_proof() -> None:
    # GitHub-Namen sind nicht case-sensitiv; „Anna" und „anna" sind dasselbe
    # Konto, und ein erneuter Nachweis wäre Schikane.
    connection = _open("anna")
    connection.verify(now=NOW)

    connection.relink("Anna")

    assert connection.is_verified
    assert connection.login == "Anna"


def test_a_repository_carries_no_score() -> None:
    """ADR-0022: Belege mit Herkunft, keine Note.

    Der Test steht hier, damit ein späteres `score`- oder `rating`-Feld nicht
    unbemerkt hineinwächst.
    """
    fields = set(Repository.__dataclass_fields__)
    assert fields == {"name", "description", "language", "stars", "url", "pushed_at"}
