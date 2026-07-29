"""Password hashing with bcrypt (direct, no passlib indirection)."""

from __future__ import annotations

import bcrypt

__all__ = [
    "BcryptPasswordHasher",
    "PasswordHashError",
    "PasswordTooLong",
    "hash_password",
    "verify_password",
]

_BCRYPT_MAX_BYTES = 72


class PasswordHashError(Exception):
    """Unexpected failure while hashing or verifying a password."""


class PasswordTooLong(PasswordHashError):
    """The password exceeds bcrypt's 72-byte input limit."""


class BcryptPasswordHasher:
    """Hashes passwords with bcrypt (cost 12) and verifies them in constant time."""

    def __init__(self, rounds: int = 12) -> None:
        self.rounds = rounds

    def hash_password(self, password: str) -> str:
        password_bytes = password.encode("utf-8")
        if len(password_bytes) > _BCRYPT_MAX_BYTES:
            raise PasswordTooLong(
                f"Password is {len(password_bytes)} bytes, bcrypt limit is {_BCRYPT_MAX_BYTES}"
            )
        try:
            salt = bcrypt.gensalt(rounds=self.rounds)
            hashed = bcrypt.hashpw(password_bytes, salt)
        except ValueError as exc:
            # belt-and-braces: bcrypt raises again if the check above missed an edge
            raise PasswordTooLong(str(exc)) from exc
        except Exception as exc:  # pragma: no cover - defensive
            raise PasswordHashError("Failed to hash password") from exc
        return hashed.decode("utf-8")

    def verify_password(self, plain: str, hashed: str) -> bool:
        try:
            hashed_bytes = hashed.encode("utf-8")
            plain_bytes = plain.encode("utf-8")
        except Exception as exc:  # pragma: no cover - defensive
            raise PasswordHashError("Failed to encode inputs") from exc
        try:
            return bcrypt.checkpw(plain_bytes, hashed_bytes)
        except ValueError:
            # Malformed hash string — treat as "does not match" rather than crash.
            return False


_default_hasher = BcryptPasswordHasher()


def hash_password(password: str) -> str:
    return _default_hasher.hash_password(password)


def verify_password(plain: str, hashed: str) -> bool:
    return _default_hasher.verify_password(plain, hashed)
