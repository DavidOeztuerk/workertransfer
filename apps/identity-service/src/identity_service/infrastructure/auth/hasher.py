"""Bcrypt adapter — bridges worker-auth BcryptPasswordHasher to the domain port."""

from __future__ import annotations

from worker_auth import BcryptPasswordHasher

from identity_service.domain.value_objects import PasswordHash


class BcryptPasswordAdapter:
    def __init__(self, *, rounds: int = 12) -> None:
        self._hasher = BcryptPasswordHasher(rounds=rounds)

    def hash(self, plain: str) -> PasswordHash:
        return PasswordHash(self._hasher.hash_password(plain))

    def verify(self, plain: str, hashed: PasswordHash) -> bool:
        return self._hasher.verify_password(plain, hashed.value)
