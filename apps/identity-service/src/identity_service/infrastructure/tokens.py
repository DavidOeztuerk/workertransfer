"""Token-Erzeugung: Klartext geht in die Mail, Hash in die Datenbank."""

from __future__ import annotations

import hashlib
import secrets

__all__ = ["generate_token", "hash_token"]

#: 32 Bytes urlsafe ≈ 43 Zeichen. Raten ist damit kein Angriffsweg.
_TOKEN_BYTES = 32


def hash_token(raw: str) -> str:
    """SHA-256 hex. Kein Salt: der Klartext ist bereits hochentropisch, und ein
    Salt würde die Suche über den Hash unmöglich machen."""
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def generate_token() -> tuple[str, str]:
    raw = secrets.token_urlsafe(_TOKEN_BYTES)
    return raw, hash_token(raw)
