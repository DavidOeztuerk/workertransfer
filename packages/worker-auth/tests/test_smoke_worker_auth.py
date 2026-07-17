"""Smoke tests for worker-auth (Phase 1.5).

Exercises the ``TokenManager`` constructor (pure field storage) and the
``TokenPayload`` pydantic model. ``hash_password``/``verify_password`` are NOT
called — they trigger passlib's bcrypt backend, which is broken in the current
venv (passlib/bcrypt version skew), and run real crypto. ``create_access_token``
needs a real RSA key and is also avoided.
"""

import time
from uuid import uuid4

from worker_auth import TokenManager, TokenPayload


def test_smoke_token_manager_and_payload() -> None:
    manager = TokenManager(
        private_key="priv",
        public_key="pub",
        algorithm="HS256",
        access_token_expire_minutes=5,
    )
    now = int(time.time())
    payload = TokenPayload(
        sub=uuid4(),
        tenant_id=uuid4(),
        exp=now + 60,
        iat=now,
        type="access",
        jti="j",
    )

    assert manager.algorithm == "HS256"
    assert manager.private_key == "priv"
    assert payload.type == "access"
    assert payload.jti == "j"


import pytest  # noqa: E402
from worker_auth import (  # noqa: E402
    BcryptPasswordHasher,
    PasswordTooLong,
    hash_password,
    verify_password,
)


def test_hash_and_verify_roundtrip() -> None:
    hasher = BcryptPasswordHasher()
    hashed = hasher.hash_password("correct horse battery staple")
    assert hashed != "correct horse battery staple"
    assert hashed.startswith("$2b$") or hashed.startswith("$2y$")
    assert hasher.verify_password("correct horse battery staple", hashed) is True
    assert hasher.verify_password("wrong password", hashed) is False


def test_module_functions_backed_by_default_hasher() -> None:
    hashed = hash_password("hunter2")
    assert hashed.startswith("$2")
    assert verify_password("hunter2", hashed) is True
    assert verify_password("nope", hashed) is False


def test_password_longer_than_72_bytes_raises_password_too_long() -> None:
    hasher = BcryptPasswordHasher()
    long_password = "a" * 73  # 73 ASCII bytes > 72
    with pytest.raises(PasswordTooLong):
        hasher.hash_password(long_password)


def test_verify_with_malformed_hash_returns_false() -> None:
    hasher = BcryptPasswordHasher()
    assert hasher.verify_password("anything", "not-a-bcrypt-hash") is False
