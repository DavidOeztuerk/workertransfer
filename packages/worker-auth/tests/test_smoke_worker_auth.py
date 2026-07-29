"""worker-auth smoke + unit tests (Phase 2).

Exercises real bcrypt (BcryptPasswordHasher) and real HS256 JWT roundtrips
(TokenManager on PyJWT). The Phase-1 passlib/jose blockers are resolved.
"""

import time
from uuid import uuid4

import pytest
from worker_auth import (
    BcryptPasswordHasher,
    ExpiredToken,
    InvalidToken,
    PasswordTooLong,
    TokenManager,
    TokenPayload,
    hash_password,
    verify_password,
)


def test_access_token_roundtrip() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()

    token = manager.create_access_token(user_id, tenant_id, roles=["user"], permissions=["read"])
    decoded = manager.verify_token(token, expected_type="access")

    assert decoded.sub == user_id
    assert decoded.tenant_id == tenant_id
    assert decoded.roles == ["user"]
    assert decoded.permissions == ["read"]
    assert decoded.type == "access"
    assert isinstance(decoded.jti, str) and len(decoded.jti) > 0


def test_refresh_token_has_distinct_type_and_jti() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()

    token = manager.create_refresh_token(user_id, tenant_id, session_jti="session-jti-123")
    decoded = manager.verify_token(token, expected_type="refresh")

    assert decoded.type == "refresh"
    assert decoded.jti == "session-jti-123"


def test_wrong_expected_type_rejects() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()

    access = manager.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    with pytest.raises(InvalidToken):
        manager.verify_token(access, expected_type="refresh")


def test_expired_token_raises_expired() -> None:
    secret = "a" * 40
    user_id, tenant_id = uuid4(), uuid4()
    zero_min = TokenManager(secret=secret, access_token_expire_minutes=0)
    expired = zero_min.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    import time as _t

    _t.sleep(1)  # ensure exp (now) is in the past by >=1s
    manager = TokenManager(secret=secret)
    with pytest.raises(ExpiredToken):
        manager.verify_token(expired, expected_type="access")


def test_tampered_signature_rejected() -> None:
    secret = "a" * 40
    manager = TokenManager(secret=secret)
    user_id, tenant_id = uuid4(), uuid4()
    token = manager.create_access_token(user_id, tenant_id, roles=[], permissions=[])
    tampered = token[:-4] + "aaaa"
    with pytest.raises(InvalidToken):
        manager.verify_token(tampered, expected_type="access")


def test_tokenpayload_model_fields() -> None:
    now = int(time.time())
    payload = TokenPayload(
        sub=uuid4(),
        tenant_id=uuid4(),
        roles=["admin"],
        permissions=["*"],
        exp=now + 60,
        iat=now,
        type="access",
        jti="j",
    )
    assert payload.type == "access"
    assert payload.roles == ["admin"]


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
