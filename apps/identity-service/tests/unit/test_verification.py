"""Token-Regeln: einmalig, befristet, nur als Hash gespeichert."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

from identity_service.domain.verification import (
    TokenPurpose,
    VerificationToken,
)
from identity_service.infrastructure.tokens import generate_token, hash_token

NOW = datetime(2026, 8, 1, 12, 0, tzinfo=UTC)


def _token(**overrides: object) -> VerificationToken:
    defaults: dict[str, object] = {
        "token_id": uuid4(),
        "user_id": uuid4(),
        "token_hash": "x" * 64,
        "purpose": TokenPurpose.EMAIL_VERIFY,
        "expires_at": NOW + timedelta(hours=24),
        "consumed_at": None,
    }
    defaults.update(overrides)
    return VerificationToken(**defaults)  # type: ignore[arg-type]


def test_generate_returns_plaintext_and_its_hash() -> None:
    raw, hashed = generate_token()

    assert len(raw) >= 32
    assert hashed == hash_token(raw)
    # Der Klartext darf nirgends aus dem Hash rekonstruierbar sein.
    assert raw not in hashed


def test_hash_is_stable_and_hex_sha256() -> None:
    assert hash_token("abc") == hash_token("abc")
    assert len(hash_token("abc")) == 64
    assert int(hash_token("abc"), 16) >= 0


def test_two_tokens_differ() -> None:
    assert generate_token()[0] != generate_token()[0]


def test_expiry_is_evaluated_against_the_given_moment() -> None:
    token = _token(expires_at=NOW + timedelta(seconds=1))

    assert token.is_expired(NOW) is False
    assert token.is_expired(NOW + timedelta(seconds=2)) is True


def test_consumed_token_reports_itself_as_used() -> None:
    assert _token().is_consumed() is False
    assert _token(consumed_at=NOW).is_consumed() is True
